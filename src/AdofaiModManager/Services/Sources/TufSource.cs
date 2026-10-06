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

/// <summary>
/// TUF（The Universal Forums，api.tuforums.com）适配器。
///
/// 特点：官方 REST v2 + Swagger 文档，读接口免 key，ADOFAI mod 160 个左右（内容大头）。
/// 注意：详情是 <c>{ "mod": { ... } }</c> 包了一层；版本里直接带 downloadUrl。
/// </summary>
public sealed class TufSource : IRemoteSource
{
    private const string BaseUrl = "https://api.tuforums.com";

    private readonly HttpClient _http;

    public TufSource(HttpClient? http = null) => _http = http ?? SourceHttp.CreateClient();

    public string Id => "tuf";

    public string DisplayName => "TUF";

    public bool RequiresKey => false;

    /// <summary>TUF 的标签（用来填界面上的「类型」筛选）。</summary>
    public IReadOnlyList<string> Categories { get; } =
        ["Gameplay", "Quality Of Life", "Editor", "Jokes", "Overlay", "Dependency"];

    public async Task<RemoteModPage> GetModsAsync(RemoteModQuery query, CancellationToken ct = default)
    {
        // 实测：TUF 的列表接口只认 q（搜索）和 limit/offset；
        // sort / sortBy / orderBy / tag / tags / category 这些参数一律被忽略。
        // 所以一次把全部拉下来（当前 160 个左右），在本地排序 + 按标签筛选，
        // 然后整页返回 —— 界面会显示「已全部加载」，也不会反复请求。
        var all = await GetAllAsync(query.Search, ct);

        var filtered = string.IsNullOrWhiteSpace(query.Category)
            ? all
            : all.Where(m => m.Categories.Any(c => c.Equals(query.Category, StringComparison.OrdinalIgnoreCase)))
                 .ToList();

        var sorted = query.Sort switch
        {
            RemoteSort.Downloads => filtered.OrderByDescending(m => m.Downloads),
            RemoteSort.Favorites => filtered.OrderByDescending(m => m.Likes),
            RemoteSort.Name => filtered.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => filtered.OrderByDescending(m => m.UpdatedAt ?? DateTime.MinValue),
        };

        var items = sorted.ToList();
        return new RemoteModPage(items, items.Count, 1, Math.Max(1, items.Count));
    }

    private async Task<List<RemoteMod>> GetAllAsync(string? search, CancellationToken ct)
    {
        const int pageSize = 100;
        const int cap = 500;

        var result = new List<RemoteMod>();

        for (var offset = 0; offset < cap; offset += pageSize)
        {
            var url = $"{BaseUrl}/v2/mods?limit={pageSize}&offset={offset}";
            if (!string.IsNullOrWhiteSpace(search))
            {
                // 注意：参数名是 q，不是 search（实测 search= 会被忽略）
                url += "&q=" + Uri.EscapeDataString(search.Trim());
            }

            using var doc = await GetJsonAsync(url, ct);
            var root = doc.RootElement;

            var batch = 0;
            if (root.TryGetProperty("mods", out var mods) && mods.ValueKind == JsonValueKind.Array)
            {
                foreach (var mod in mods.EnumerateArray())
                {
                    result.Add(MapMod(mod, Id));
                    batch++;
                }
            }

            var total = root.TryGetProperty("total", out var totalElement) && totalElement.TryGetInt32(out var totalValue)
                ? totalValue
                : 0;

            if (batch == 0 || result.Count >= total)
            {
                break;
            }
        }

        return result;
    }

    public async Task<RemoteModDetail?> GetModDetailAsync(string slug, CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync($"{BaseUrl}/v2/mods/{Uri.EscapeDataString(slug)}", ct);

        if (!doc.RootElement.TryGetProperty("mod", out var mod) || mod.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var versions = new List<RemoteVersion>();
        if (mod.TryGetProperty("versions", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var version in list.EnumerateArray())
            {
                versions.Add(MapVersion(version));
            }
        }

        // 版本列表有时为空，但 latestVersion 一定有 → 兜一个进去
        if (versions.Count == 0 && mod.TryGetProperty("latestVersion", out var latest) && latest.ValueKind == JsonValueKind.Object)
        {
            versions.Add(MapVersion(latest));
        }

        return new RemoteModDetail(MapMod(mod, Id), Json.Text(mod, "description"), versions);
    }

    public async Task<RemoteDownload?> ResolveDownloadAsync(
        string slug,
        RemoteVersion version,
        string platform,
        CancellationToken ct = default)
    {
        // 细节：platformDownloadUrls 里可能有按平台分的链接，优先用它
        try
        {
            using var doc = await GetJsonAsync($"{BaseUrl}/v2/mods/{Uri.EscapeDataString(slug)}", ct);
            if (doc.RootElement.TryGetProperty("mod", out var mod)
                && mod.TryGetProperty("versions", out var list)
                && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    if (!string.Equals(Json.Text(item, "version"), version.VersionId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (item.TryGetProperty("platformDownloadUrls", out var platforms)
                        && platforms.ValueKind == JsonValueKind.Object
                        && platforms.TryGetProperty(platform, out var platformUrl)
                        && platformUrl.ValueKind == JsonValueKind.String)
                    {
                        return RemoteDownload.Unverified(platformUrl.GetString()!, $"{slug}-{version.VersionId}.zip");
                    }
                }
            }
        }
        catch
        {
            // 拿不到就退回下面的 downloadUrl / 下载端点
        }

        if (!string.IsNullOrWhiteSpace(version.DirectUrl))
        {
            return RemoteDownload.Unverified(version.DirectUrl, $"{slug}-{version.VersionId}.zip");
        }

        // 走官方的下载端点（TUF 会 302 到真实文件，并统计下载量）
        return RemoteDownload.Unverified(
            $"{BaseUrl}/v2/mods/{Uri.EscapeDataString(slug)}/download?platform={Uri.EscapeDataString(platform)}",
            $"{slug}-{version.VersionId}.zip");
    }

    private static RemoteMod MapMod(JsonElement mod, string sourceId)
    {
        var name = Json.Text(mod, "name");
        var description = Json.Text(mod, "description");

        return new RemoteMod(
            SourceId: sourceId,
            Id: Json.Text(mod, "id"),
            Slug: Json.Text(mod, "slug"),
            Name: name,
            Authors: Json.Text(mod, "creatorUsername"),
            Summary: Json.FirstLine(description),
            IconUrl: Json.Text(mod, "imageUrl"),
            Categories: Json.Strings(mod, "tags", "name"),
            LatestVersion: Json.Text(mod, "version"),
            GameVersion: null,
            Downloads: Json.Long(mod, "downloadCount"),
            Likes: Json.Long(mod, "likes"),
            HomepageUrl: Json.Text(mod, "projectUrl"),
            UpdatedAt: Json.DateTime(mod, "sourceUploadedAt"),
            Loader: ModLoaderHeuristics.Guess(name, null, description));
    }

    private static RemoteVersion MapVersion(JsonElement version)
    {
        var platforms = new List<string>();
        if (version.TryGetProperty("platformDownloadUrls", out var list) && list.ValueKind == JsonValueKind.Object)
        {
            foreach (var item in list.EnumerateObject())
            {
                if (item.Value.ValueKind == JsonValueKind.String)
                {
                    platforms.Add(item.Name);
                }
            }
        }

        return new RemoteVersion(
            VersionId: Json.Text(version, "version"),
            Changelog: Json.Text(version, "notes"),
            GameVersion: null,
            IsBeta: false,
            Platforms: platforms,
            DirectUrl: Json.Text(version, "downloadUrl"));
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"TUF 返回 {(int)response.StatusCode}：{url}");
        }

        return JsonDocument.Parse(body);
    }
}
