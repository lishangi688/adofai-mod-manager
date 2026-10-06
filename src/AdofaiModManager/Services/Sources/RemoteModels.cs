using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AdofaiModManager.Services.Sources;

/// <summary>mod 需要哪种加载器（ADOFAI 现在不止 UMM 一种了）。</summary>
public enum ModLoader
{
    /// <summary>认不出来 —— 不猜，装包时再按内容确认。</summary>
    Unknown,

    Umm,

    MelonLoader,
}

/// <summary>排序方式（与具体站点无关，由各适配器翻译成自己的参数）。</summary>
public enum RemoteSort
{
    Updated,
    Downloads,
    Favorites,
    Name,
}

/// <summary>归一化后的版本（各站字段名不同，统一成这个）。</summary>
public sealed record RemoteVersion(
    string VersionId,
    string? Changelog,
    string? GameVersion,
    bool IsBeta,
    IReadOnlyList<string> Platforms,
    /// <summary>源直接给了链接时用它（TUF）；为 null 表示要再调接口拿（modlist）。</summary>
    string? DirectUrl);

/// <summary>归一化后的 mod（列表用）。</summary>
public sealed record RemoteMod(
    string SourceId,
    string Id,
    string Slug,
    string Name,
    string? Authors,
    string? Summary,
    string? IconUrl,
    IReadOnlyList<string> Categories,
    string? LatestVersion,
    string? GameVersion,
    long Downloads,
    long Likes,
    string? HomepageUrl,
    DateTime? UpdatedAt,
    ModLoader Loader);

public sealed record RemoteModPage(IReadOnlyList<RemoteMod> Items, int Total, int Page, int PageSize);

public sealed record RemoteModDetail(RemoteMod Mod, string? Description, IReadOnlyList<RemoteVersion> Versions);

/// <summary>已解析出的下载文件（后续要过一遍 <see cref="SourceHttp.ProbeAsync"/> 校验）。</summary>
public sealed record RemoteDownload(string Url, string FileName, long Size, string? ContentType, bool IsArchive)
{
    /// <summary>没有校验信息时就当"未知/可疑"，让调用方自己去判断。</summary>
    public static RemoteDownload Unverified(string url, string fileName) => new(url, fileName, 0, null, false);
}

public sealed record RemoteModQuery(
    int Page = 1,
    int PageSize = 30,
    string? Search = null,
    string? Category = null,
    RemoteSort Sort = RemoteSort.Updated);

/// <summary>
/// 统一的「远端 mod 源」。各站接口形状/鉴权/下载方式都不同，
/// 由各自的适配器归一化成上面的模型，上层（UI / 更新检查 / 安装）只认这个接口。
/// </summary>
public interface IRemoteSource
{
    /// <summary>短标识（tuf / modlist / adofaitools / github）。</summary>
    string Id { get; }

    string DisplayName { get; }

    /// <summary>是否需要 API key（免 key 的源开箱可用）。</summary>
    bool RequiresKey { get; }

    /// <summary>这个源自己能提供的分类 / 标签（用来填界面上的"类型"筛选；没有就返回空）。</summary>
    IReadOnlyList<string> Categories { get; }

    /// <summary>这个 mod 在源站上的页面地址（用于署名/回链；没有就返回 null）。</summary>
    string? ModPageUrl(string slug);

    Task<RemoteModPage> GetModsAsync(RemoteModQuery query, CancellationToken ct = default);

    Task<RemoteModDetail?> GetModDetailAsync(string slug, CancellationToken ct = default);

    /// <summary>解析出「可以下载的地址」（可能是源自己的 download 端点，会 302 到真实文件）。</summary>
    Task<RemoteDownload?> ResolveDownloadAsync(string slug, RemoteVersion version, string platform, CancellationToken ct = default);
}

/// <summary>从元数据里猜加载器（描述里常写 "requires MelonLoader"）。</summary>
public static class ModLoaderHeuristics
{
    public static ModLoader Guess(string? name, string? summary, string? description)
    {
        var text = $"{name} {summary} {description}";

        if (text.Contains("MelonLoader", StringComparison.OrdinalIgnoreCase))
        {
            return ModLoader.MelonLoader;
        }

        return ModLoader.Unknown;
    }
}

/// <summary>源适配器共用的 HTTP 与健全性检查。</summary>
public static class SourceHttp
{
    /// <summary>列表类接口的缓存时长（10 分钟）。</summary>
    public static readonly TimeSpan ListTtl = TimeSpan.FromMinutes(10);

    /// <summary>详情类接口的缓存时长（30 分钟）。</summary>
    public static readonly TimeSpan DetailTtl = TimeSpan.FromMinutes(30);

    /// <summary>
    /// 带缓存的 GET（JSON）。
    /// 目的：切来源 / 反复搜索时不再重复打第三方站，界面也不会卡一下。
    /// 缓存分两层：进程内（一定有）+ 应用磁盘缓存（由 AppServices 挂上来）。
    /// </summary>
    public static async Task<JsonDocument> GetJsonCachedAsync(
        HttpClient http,
        string url,
        TimeSpan ttl,
        CancellationToken ct)
    {
        if (SourceCache.TryGet(url, ttl, out var cached) && !string.IsNullOrWhiteSpace(cached))
        {
            return JsonDocument.Parse(cached);
        }

        try
        {
            using var response = await http.GetAsync(url, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"HTTP {(int)response.StatusCode}：{url}");
            }

            SourceCache.Set(url, body, ttl);
            return JsonDocument.Parse(body);
        }
        catch
        {
            // 网络失败时用过期缓存兜底（总比什么都没有强）
            if (SourceCache.TryGetStale(url, out var stale) && !string.IsNullOrWhiteSpace(stale))
            {
                return JsonDocument.Parse(stale);
            }

            throw;
        }
    }

    public static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            AllowAutoRedirect = true,
            UseProxy = true,   // 跟随系统代理（和主程序一致）
        };

        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "AdofaiModManager/0.1.5 (+https://github.com/lishangi688/adofai-mod-manager)");
        return client;
    }

    /// <summary>
    /// 校验一个下载地址：跟随跳转、取最终地址与大小，并**如实判断是不是压缩包**。
    /// 返回 null 表示下不动；<c>IsArchive=false</c> 表示"下到的不是压缩包"（例如跳到了项目主页）。
    /// </summary>
    public static async Task<RemoteDownload?> ProbeAsync(string url, string fallbackName, CancellationToken ct = default)
    {
        try
        {
            using var client = CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(0, 0);

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;
            var finalName = Path.GetFileName(response.RequestMessage?.RequestUri?.AbsolutePath ?? string.Empty);
            var size = response.Content.Headers.ContentRange?.Length
                       ?? response.Content.Headers.ContentLength
                       ?? 0;
            var contentType = response.Content.Headers.ContentType?.MediaType;

            // 判断"是不是压缩包"只看证据：Content-Type + 最终 URL 的后缀。
            // 注意：不能用兜底文件名来判断，否则会把跳到网页的情况误判成压缩包。
            var isArchive =
                (contentType is not null
                 && (contentType.Contains("zip", StringComparison.OrdinalIgnoreCase)
                     || contentType.Contains("octet-stream", StringComparison.OrdinalIgnoreCase)
                     || contentType.Contains("compressed", StringComparison.OrdinalIgnoreCase)))
                || finalName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

            var displayName = finalName.Contains('.') ? finalName : fallbackName;
            return new RemoteDownload(finalUrl, displayName, size, contentType, isArchive);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// 源接口响应的缓存（按 URL 缓存原始 JSON）。
/// 两层：进程内字典（一定有）+ 可选的应用磁盘缓存（启动时由 AppServices 挂上，
/// 这样独立的小工具（冒烟测试）不依赖应用的路径/配置也能跑）。
/// </summary>
public static class SourceCache
{
    private static readonly Dictionary<string, (DateTime Expires, string Body)> Memory = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Stale = new(StringComparer.Ordinal);

    /// <summary>可选的磁盘缓存读（返回 null 表示没有）。</summary>
    public static Func<string, TimeSpan, string?>? DiskRead { get; set; }

    /// <summary>可选的磁盘缓存写。</summary>
    public static Action<string, string>? DiskWrite { get; set; }

    public static bool TryGet(string key, TimeSpan ttl, out string body)
    {
        lock (Memory)
        {
            if (Memory.TryGetValue(key, out var hit) && hit.Expires > DateTime.UtcNow)
            {
                body = hit.Body;
                return true;
            }
        }

        if (DiskRead?.Invoke(key, ttl) is { Length: > 0 } fromDisk)
        {
            Remember(key, fromDisk, ttl);
            body = fromDisk;
            return true;
        }

        body = string.Empty;
        return false;
    }

    public static bool TryGetStale(string key, out string body)
    {
        lock (Memory)
        {
            if (Stale.TryGetValue(key, out var stale))
            {
                body = stale;
                return true;
            }
        }

        body = string.Empty;
        return false;
    }

    public static void Set(string key, string body, TimeSpan ttl)
    {
        Remember(key, body, ttl);
        DiskWrite?.Invoke(key, body);
    }

    private static void Remember(string key, string body, TimeSpan ttl)
    {
        lock (Memory)
        {
            Memory[key] = (DateTime.UtcNow.Add(ttl), body);
            Stale[key] = body;

            // 简单的容量保护，避免长时间运行后无限增长
            if (Memory.Count > 400)
            {
                foreach (var old in Memory.Where(kv => kv.Value.Expires <= DateTime.UtcNow).Select(kv => kv.Key).ToList())
                {
                    Memory.Remove(old);
                }
            }
        }
    }
}

/// <summary>容错的 JSON 取值（各站字段时有时无、类型不一）。</summary>
public static class Json
{
    public static string Text(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            _ => string.Empty,
        };
    }

    public static long Long(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : 0;

    public static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    /// <summary>取时间字段（各站都是 ISO 字符串）。</summary>
    public static DateTime? DateTime(JsonElement element, string name)
    {
        var text = Text(element, name);
        return System.DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    public static List<string> Strings(JsonElement element, string arrayName, string fieldName)
    {
        var result = new List<string>();
        if (!element.TryGetProperty(arrayName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in array.EnumerateArray())
        {
            var text = item.ValueKind == JsonValueKind.String
                ? item.GetString()
                : Text(item, fieldName);

            if (!string.IsNullOrWhiteSpace(text))
            {
                result.Add(text);
            }
        }

        return result;
    }

    /// <summary>取描述的第一行当摘要（TUF 只给一段 markdown）。</summary>
    public static string FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart('#', '-', '*', ' ', '>');
            if (line.Length > 0)
            {
                return line.Length > 160 ? line[..160] : line;
            }
        }

        return string.Empty;
    }
}
