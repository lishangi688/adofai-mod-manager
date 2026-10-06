// 多资源站冒烟测试：用真正的适配器跑「列表 → 详情 → 下载解析 → 下载校验」。
//
// 运行：dotnet run --project tools/SourceSmokeTest
// 说明：走系统代理（和主程序一致）。

using System.Text;
using AdofaiModManager.Services.Sources;

Console.OutputEncoding = Encoding.UTF8;

var sources = new IRemoteSource[] { new TufSource(), new ModlistSource() };
var problems = 0;

foreach (var source in sources)
{
    Console.WriteLine(new string('=', 78));
    Console.WriteLine($"源：{source.DisplayName}   (id={source.Id}，需要 key={source.RequiresKey})");
    Console.WriteLine(new string('=', 78));

    RemoteModPage page;
    try
    {
        page = await source.GetModsAsync(new RemoteModQuery(Page: 1, PageSize: 10));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ❌ 列表失败：{ex.Message}");
        problems++;
        Console.WriteLine();
        continue;
    }

    Console.WriteLine($"  [列表] 共 {page.Total} 个，本页 {page.Items.Count} 个");
    foreach (var mod in page.Items.Take(6))
    {
        Console.WriteLine($"    - {mod.Name,-30} v{Trim(mod.LatestVersion),-11} 加载器={mod.Loader,-11} 分类={string.Join("/", mod.Categories)}");
    }

    // 翻页是否正确（第 2 页应该换一批）
    if (page.Total > page.Items.Count)
    {
        var second = await source.GetModsAsync(new RemoteModQuery(Page: 2, PageSize: 10));
        var firstSlug = page.Items.FirstOrDefault()?.Slug;
        var secondSlug = second.Items.FirstOrDefault()?.Slug;
        Console.WriteLine($"  [翻页] 第 2 页首条 = {secondSlug}（和第 1 页不同 = {firstSlug != secondSlug}）");
        if (firstSlug == secondSlug)
        {
            Console.WriteLine("         ⚠ 第 2 页没有换批 —— 这个源的分页参数需要再确认");
        }
    }

    Console.WriteLine();
    Console.WriteLine("  [详情 + 下载校验]");

    foreach (var candidate in page.Items.Take(3))
    {
        RemoteModDetail? detail;
        try
        {
            detail = await source.GetModDetailAsync(candidate.Slug);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    ❌ {candidate.Slug} 详情失败：{ex.Message}");
            problems++;
            continue;
        }

        if (detail is null)
        {
            Console.WriteLine($"    ❌ {candidate.Slug} 详情为空");
            problems++;
            continue;
        }

        var latest = detail.Versions.FirstOrDefault(v => !v.IsBeta) ?? detail.Versions.FirstOrDefault();
        Console.WriteLine($"    · {detail.Mod.Name}：{detail.Versions.Count} 个版本，摘要={Trunc(detail.Mod.Summary, 46)}");

        if (latest is null)
        {
            Console.WriteLine("        ⚠ 没有版本信息（无法安装）");
            problems++;
            continue;
        }

        var download = await source.ResolveDownloadAsync(candidate.Slug, latest, "windows");
        if (download is null)
        {
            Console.WriteLine($"        ❌ v{latest.VersionId} 解析不到下载地址");
            problems++;
            continue;
        }

        var probe = await SourceHttp.ProbeAsync(download.Url, download.FileName);
        if (probe is null)
        {
            Console.WriteLine($"        ❌ v{latest.VersionId} 下载地址不通：{Short(download.Url)}");
            problems++;
            continue;
        }

        var flag = probe.IsArchive ? "✅" : "⚠ 不是压缩包";
        Console.WriteLine($"        v{latest.VersionId} 游戏版本={Trim(latest.GameVersion, 12)} → {Short(probe.Url)}");
        Console.WriteLine($"        {flag}  {probe.Size / 1048576.0:0.#} MB   类型={probe.ContentType}   文件名={probe.FileName}");

        if (!probe.IsArchive)
        {
            problems++;
        }
    }

    Console.WriteLine();
}

Console.WriteLine(new string('=', 78));
Console.WriteLine(problems == 0
    ? "全部通过 ✅"
    : $"有 {problems} 处需要处理（上面标了 ❌ / ⚠）");

return problems == 0 ? 0 : 1;

static string Short(string? text) { text ??= ""; return text.Length <= 84 ? text : text[..84] + "…"; }

static string Trunc(string? text, int max) { text ??= ""; return text.Length <= max ? text : text[..max] + "…"; }

static string Trim(string? text, int max = 10) { text ??= ""; return text.Length <= max ? text : text[..max] + "…"; }
