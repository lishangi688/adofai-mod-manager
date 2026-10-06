// 多资源站可行性探测（独立小工具，不属于应用本体）
//
// 目的：在动手写适配器之前，用真实数据确认三件事：
//   ① 两个新源（modlist.org / TUF）的公开 API 能不能匿名拉到 ADOFAI mod 列表；
//   ② 能不能解析出「下载直链」（它们都是 302 跳转，不是直接给 zip）；
//   ③ 这些 mod 与 adofaitools 资源站的重叠度（决定「同 mod 多源」的去重策略）。
//
// 运行：dotnet run --project tools/SourceProbe
// 说明：走系统代理（和应用一致），网络不通的源会跳过并标注。

using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

Console.OutputEncoding = Encoding.UTF8;

var handler = new HttpClientHandler
{
    UseProxy = true,
    Proxy = WebRequest.DefaultWebProxy,
    // 关键：不要自动跟随 302，否则看不到真实的下载直链
    AllowAutoRedirect = false,
};
using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (AMM-SourceProbe)");

const string ModlistBase = "https://modlist.org";
const string TufBase = "https://api.tuforums.com";

var findings = new List<string>();

// ---------------------------------------------------------------- modlist.org
Console.WriteLine("===== 1) modlist.org =====");
var modlist = new List<(string Name, string Slug, string Version, string GameVersion, string Categories, string SourceUrl)>();
try
{
    var sw = Stopwatch.StartNew();
    using var doc = await GetJson($"{ModlistBase}/api/mods?game=adofai&limit=100");
    sw.Stop();

    var root = doc.RootElement;
    var total = root.TryGetProperty("pagination", out var pg) && pg.TryGetProperty("total", out var t) ? t.GetInt32() : -1;
    Console.WriteLine($"  GET /api/mods?game=adofai  →  200  ({sw.ElapsedMilliseconds} ms)   total={total}");

    foreach (var m in root.GetProperty("mods").EnumerateArray())
    {
        var version = m.TryGetProperty("latestVersion", out var lv) && lv.ValueKind == JsonValueKind.Object
            ? Str(lv, "version") : "";
        var gameVersion = m.TryGetProperty("latestVersion", out var lv2) && lv2.ValueKind == JsonValueKind.Object
            ? Str(lv2, "gameVersion") : "";

        modlist.Add((
            Str(m, "name"),
            Str(m, "slug"),
            version,
            gameVersion,
            m.TryGetProperty("categories", out var c) && c.ValueKind == JsonValueKind.Array
                ? string.Join("/", c.EnumerateArray().Select(x => x.GetString()))
                : "",
            Str(m, "sourceUrl")));
    }

    Console.WriteLine($"  列表解析: {modlist.Count} 个");
    foreach (var m in modlist)
    {
        Console.WriteLine($"    - {m.Name,-28} v{m.Version,-12} 游戏版本={m.GameVersion,-14} 分类={m.Categories}");
    }

    // 下载链路：多试几个，TUF 上有些 mod 的 ZIP 没登记（会跳到项目主页）
    Console.WriteLine();
    Console.WriteLine("  下载链路探测（最多试 3 个）:");
    foreach (var probe in modlist.Where(m => m.Slug.Length > 0).Take(3))
    {
        Console.WriteLine($"    · {probe.Name} (slug={probe.Slug})");
        var url = await ResolveRedirect($"{ModlistBase}/api/mods/{probe.Slug}/download?platform=windows", "modlist.org");
        if (url is not null)
        {
            Console.WriteLine($"      302 → {Shorten(url)}");
            Console.WriteLine($"      直链: {await HeadInfo(url)}");
        }
    }

    findings.Add($"modlist.org：{modlist.Count} 个 ADOFAI mod，公开 API 免 key，结构化（分类 + gameVersion）");
}
catch (Exception ex)
{
    Console.WriteLine($"  ❌ 失败：{ex.Message}");
    findings.Add($"modlist.org：探测失败（{ex.Message}）");
}

// ---------------------------------------------------------------------- TUF
Console.WriteLine();
Console.WriteLine("===== 2) TUF (tuforums.com) =====");
var tuf = new List<(string Name, string Slug, string Version, string Tags)>();
try
{
    var sw = Stopwatch.StartNew();
    using var doc = await GetJson($"{TufBase}/v2/mods?limit=100");
    sw.Stop();

    var root = doc.RootElement;
    var total = root.TryGetProperty("total", out var t) ? t.GetInt32() : -1;
    Console.WriteLine($"  GET /v2/mods  →  200  ({sw.ElapsedMilliseconds} ms)   total={total}");

    foreach (var m in root.GetProperty("mods").EnumerateArray())
    {
        var tags = m.TryGetProperty("tags", out var tg) && tg.ValueKind == JsonValueKind.Array
            ? string.Join("/", tg.EnumerateArray().Select(x => x.TryGetProperty("name", out var n) ? n.GetString() : ""))
            : "";

        tuf.Add((Str(m, "name"), Str(m, "slug"), Str(m, "version"), tags));
    }

    Console.WriteLine($"  列表解析: {tuf.Count} 个（total={total}，看是否需要翻页）");
    foreach (var m in tuf.Take(10))
    {
        Console.WriteLine($"    - {m.Name,-28} v{m.Version,-12} 标签={m.Tags}");
    }

    if (tuf.Count > 0)
    {
        var probe = tuf.FirstOrDefault(m => m.Slug.Length > 0);
        Console.WriteLine();
        Console.WriteLine($"  详情探测: GET /v2/mods/{probe.Slug}");
        using (var detail = await GetJson($"{TufBase}/v2/mods/{probe.Slug}"))
        {
            var props = detail.RootElement.ValueKind == JsonValueKind.Object
                ? string.Join(", ", detail.RootElement.EnumerateObject().Select(p => p.Name).Take(14))
                : "(非对象)";
            Console.WriteLine($"    字段: {props}");
        }

        Console.WriteLine($"  下载链路探测（最多试 3 个）:");
        foreach (var tufItem in tuf.Where(x => x.Slug.Length > 0).Take(3))
        {
            Console.WriteLine($"    · {tufItem.Name} (slug={tufItem.Slug})");
            var url = await ResolveRedirect($"{TufBase}/v2/mods/{tufItem.Slug}/download?platform=windows", "TUF");
            if (url is not null)
            {
                Console.WriteLine($"      302 → {Shorten(url)}");
                Console.WriteLine($"      直链: {await HeadInfo(url)}");
            }
        }
    }

    findings.Add($"TUF：{total} 个 mod（内容大头），官方 REST + Swagger，免 key，支持 platform=windows|macos|linux");
}
catch (Exception ex)
{
    Console.WriteLine($"  ❌ 失败：{ex.Message}");
    findings.Add($"TUF：探测失败（{ex.Message}）");
}

// ------------------------------------------------- 与 adofaitools 资源站重叠度
Console.WriteLine();
Console.WriteLine("===== 3) 与 adofaitools 资源站的重叠（去重策略依据）=====");
var siteNames = new List<string>();
try
{
    var settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AdofaiModManager", "settings.json");
    using var settings = JsonDocument.Parse(File.ReadAllText(settingsPath, Encoding.UTF8));
    var baseUrl = settings.RootElement.TryGetProperty("ApiBaseUrl", out var b) ? b.GetString() : null;
    var key = settings.RootElement.TryGetProperty("ApiKey", out var k) ? k.GetString() : null;

    if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(key))
    {
        Console.WriteLine("  跳过：settings.json 里没有站点地址或 API key");
    }
    else
    {
        for (var page = 1; page <= 5; page++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl!.TrimEnd('/')}/api/mods?page={page}&pageSize=100&sort=updated");
            req.Headers.Authorization = new("Bearer", key);
            using var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                Console.WriteLine($"  ❌ 站点返回 {(int)resp.StatusCode}");
                break;
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var items = doc.RootElement.GetProperty("items");
            var count = items.GetArrayLength();
            foreach (var m in items.EnumerateArray())
            {
                siteNames.Add(Str(m, "displayName"));
            }

            var total = doc.RootElement.TryGetProperty("total", out var t) ? t.GetInt32() : 0;
            Console.WriteLine($"  资源站第 {page} 页：{count} 条（total={total}）");
            if (count == 0 || siteNames.Count >= total) break;
        }

        var siteSet = siteNames.Select(Norm).Where(s => s.Length > 0).ToHashSet();
        Console.WriteLine($"  资源站共 {siteSet.Count} 个（规范化后）");

        var modlistHit = modlist.Count(m => siteSet.Contains(Norm(m.Name)));
        var tufHit = tuf.Count(m => siteSet.Contains(Norm(m.Name)));
        Console.WriteLine($"  与 modlist.org 同名: {modlistHit} / {modlist.Count}");
        Console.WriteLine($"  与 TUF 同名:        {tufHit} / {tuf.Count}（只比了首页 {tuf.Count} 条）");
        findings.Add($"重叠：modlist.org {modlistHit}/{modlist.Count}、TUF {tufHit}/{tuf.Count} 与资源站同名 → 需要按名字归一化去重");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"  ❌ 失败：{ex.Message}");
}

// ------------------------------------------------------------------------ 汇总
Console.WriteLine();
Console.WriteLine("===== 结论 =====");
foreach (var f in findings)
{
    Console.WriteLine($"  · {f}");
}

return 0;

// -------------------------------------------------------------------- helpers
async Task<JsonDocument> GetJson(string url)
{
    using var resp = await http.GetAsync(url);
    var body = await resp.Content.ReadAsStringAsync();
    if (!resp.IsSuccessStatusCode)
    {
        throw new Exception($"HTTP {(int)resp.StatusCode} {url}  {body[..Math.Min(160, body.Length)]}");
    }

    return JsonDocument.Parse(body);
}

// 不自动跟随重定向，直接读出 302 的目标地址
async Task<string?> ResolveRedirect(string url, string label)
{
    using var req = new HttpRequestMessage(HttpMethod.Get, url);
    using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);

    if (resp.StatusCode is HttpStatusCode.Found or HttpStatusCode.MovedPermanently or HttpStatusCode.TemporaryRedirect
        or HttpStatusCode.PermanentRedirect)
    {
        return resp.Headers.Location?.ToString();
    }

    Console.WriteLine($"    ({label} 返回 {(int)resp.StatusCode}，不是 302 —— 可能直接是文件或需要登录)");
    return null;
}

// 只取开头几个字节，确认这是真实可下的文件（这里要跟随 302 才能拿到最终大小）
async Task<string> HeadInfo(string url)
{
    try
    {
        using var follow = new HttpClient(new HttpClientHandler
        {
            UseProxy = true,
            Proxy = WebRequest.DefaultWebProxy,
        })
        { Timeout = TimeSpan.FromSeconds(60) };
        follow.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (AMM-SourceProbe)");

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
        using var resp = await follow.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);

        var len = resp.Content.Headers.ContentRange?.Length
                  ?? resp.Content.Headers.ContentLength
                  ?? 0;

        return $"HTTP {(int)resp.StatusCode}，{len / 1048576.0:0.#} MB，{resp.Content.Headers.ContentType?.MediaType}";
    }
    catch (Exception ex)
    {
        return $"取不到（{ex.Message}）";
    }
}

static string Str(JsonElement e, string name) =>
    e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

static string Shorten(string url) => url.Length <= 110 ? url : url[..110] + "…";

// 与 UpdateCenter.NormalizeName 同思路：只留字母数字，去掉括号说明与标点
static string Norm(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return "";
    }

    var cut = value;
    var open = cut.IndexOfAny(['(', '（', '[', '【']);
    if (open > 0)
    {
        cut = cut[..open];
    }

    return new string(cut.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}
