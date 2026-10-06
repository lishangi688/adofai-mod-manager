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
/// modlist.org 适配器。
///
/// 特点：Nuxt + D1 的公开 JSON API，免 key，ADOFAI 只有 10 个 mod，
/// 但**结构化最好**（5 类分类、每个版本带 gameVersion / isBeta / 按平台分的下载）。
/// 注意：它们的接口会**剥掉下载链接**（stripDownloadUrls），必须走 /download 端点拿 302。
/// </summary>
public sealed class ModlistSource : IRemoteSource
{
    private const string BaseUrl = "https://modlist.org";
    private const string Game = "adofai";

    private readonly HttpClient _http;

    public ModlistSource(HttpClient? http = null) => _http = http ?? SourceHttp.CreateClient();

    public string Id => "modlist";

    public string DisplayName => "modlist.org";

    public bool RequiresKey => false;

    /// <summary>modlist.org 的固定分类（用来填界面上的「类型」筛选）。</summary>
    public IReadOnlyList<string> Categories { get; } =
        ["ui", "gameplay", "utility", "visuals", "library"];

    public async Task<RemoteModPage> GetModsAsync(RemoteModQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 100);

        // 实测：sortBy / search / categories 都支持
        var sortBy = query.Sort switch
        {
            RemoteSort.Downloads => "downloads_desc",
            RemoteSort.Favorites => "downloads_desc",   // 它没有收藏排序，退化成下载量
            RemoteSort.Name => "name_asc",
            _ => "created",                             // 最近更新 ≈ 最近创建
        };

        var url = $"{BaseUrl}/api/mods?game={Game}&page={page}&limit={size}&sortBy={sortBy}";
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            url += "&search=" + Uri.EscapeDataString(query.Search.Trim());
        }

        if (!string.IsNullOrWhiteSpace(query.Category) && query.Category != "all")
        {
            url += "&categories=" + Uri.EscapeDataString(query.Category);
        }

        using var doc = await SourceHttp.GetJsonCachedAsync(
            _http, url, SourceHttp.ListTtl, ct);
        var root = doc.RootElement;

        var total = 0;
        if (root.TryGetProperty("pagination", out var pagination) && pagination.TryGetProperty("total", out var totalElement))
        {
            totalElement.TryGetInt32(out total);
        }

        var items = new List<RemoteMod>();
        if (root.TryGetProperty("mods", out var mods) && mods.ValueKind == JsonValueKind.Array)
        {
            foreach (var mod in mods.EnumerateArray())
            {
                items.Add(MapMod(mod, Id));
            }
        }

        return new RemoteModPage(items, total, page, size);
    }

    public async Task<RemoteModDetail?> GetModDetailAsync(string slug, CancellationToken ct = default)
    {
        using var doc = await SourceHttp.GetJsonCachedAsync(
            _http, $"{BaseUrl}/api/mods/{Uri.EscapeDataString(slug)}", SourceHttp.DetailTtl, ct);
        var root = doc.RootElement;

        if (!root.TryGetProperty("mod", out var mod) || mod.ValueKind != JsonValueKind.Object)
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

        return new RemoteModDetail(MapMod(mod, Id), Json.Text(mod, "description"), versions);
    }

    public Task<RemoteDownload?> ResolveDownloadAsync(
        string slug,
        RemoteVersion version,
        string platform,
        CancellationToken ct = default)
    {
        // modlist.org 的接口不带下载链接，只能走它自己的 /download 端点（会 302 到真实文件）
        var url = $"{BaseUrl}/api/mods/{Uri.EscapeDataString(slug)}/download?platform={Uri.EscapeDataString(platform)}";
        if (!string.IsNullOrWhiteSpace(version.VersionId))
        {
            url += "&version=" + Uri.EscapeDataString(version.VersionId);
        }

        return Task.FromResult<RemoteDownload?>(
            RemoteDownload.Unverified(url, $"{slug}-{version.VersionId}.zip"));
    }

    /// <summary>列表/详情里的 latestVersion 是个对象（{version, gameVersion, isBeta, ...}）。</summary>
    private static string LatestVersionOf(JsonElement mod)
    {
        if (!mod.TryGetProperty("latestVersion", out var latest))
        {
            return string.Empty;
        }

        return latest.ValueKind switch
        {
            JsonValueKind.Object => Json.Text(latest, "version"),
            JsonValueKind.String => latest.GetString() ?? string.Empty,
            _ => string.Empty,
        };
    }

    private static RemoteMod MapMod(JsonElement mod, string sourceId)
    {
        var name = Json.Text(mod, "name");
        var description = Json.Text(mod, "description");
        var summary = Json.Text(mod, "summary");

        // logo 是相对路径（/logos/xxx.png）
        var logo = Json.Text(mod, "logo");
        var iconUrl = logo.StartsWith('/') ? BaseUrl + logo : logo;

        return new RemoteMod(
            SourceId: sourceId,
            Id: Json.Text(mod, "_id"),
            Slug: Json.Text(mod, "slug"),
            Name: name,
            Authors: AuthorName(mod),
            Summary: summary.Length > 0 ? summary : Json.FirstLine(description),
            IconUrl: iconUrl.Length > 0 ? iconUrl : null,
            Categories: Json.Strings(mod, "categories", "name"),
            LatestVersion: LatestVersionOf(mod),
            GameVersion: null,
            Downloads: Json.Long(mod, "downloads"),
            Likes: 0,
            HomepageUrl: Json.Text(mod, "sourceUrl"),
            UpdatedAt: Json.DateTime(mod, "updatedAt"),
            Loader: ModLoaderHeuristics.Guess(name, summary, description));
    }

    private static RemoteVersion MapVersion(JsonElement version) =>
        new(
            VersionId: Json.Text(version, "version"),
            Changelog: Json.Text(version, "changelog"),
            GameVersion: Json.Text(version, "gameVersion"),
            IsBeta: Json.Bool(version, "isBeta"),
            Platforms: Json.Strings(version, "availablePlatforms", "name"),
            DirectUrl: null);   // 被接口剥掉了，只能走 /download

    private static string AuthorName(JsonElement mod)
    {
        if (!mod.TryGetProperty("authorId", out var author) || author.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        var globalName = Json.Text(author, "globalName");
        return globalName.Length > 0 ? globalName : Json.Text(author, "username");
    }
}
