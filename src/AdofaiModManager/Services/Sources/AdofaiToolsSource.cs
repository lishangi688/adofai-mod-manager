using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AdofaiModManager.Models;

namespace AdofaiModManager.Services.Sources;

/// <summary>
/// 把现有的 adofaitools 客户端也套进 <see cref="IRemoteSource"/>。
///
/// 目的：证明抽象站得住（连"下载要申请签名直链"这种站点也能归一化），
/// 为以后把「在线 Mod」页统一到同一个接口铺路。
/// 注意：「在线 Mod」页目前仍然走原有代码路径，本类不影响现有行为。
/// </summary>
public sealed class AdofaiToolsSource(AdofaiToolsClient client) : IRemoteSource
{
    public string Id => "adofaitools";

    public string DisplayName => "资源站";

    public bool RequiresKey => true;

    public async Task<RemoteModPage> GetModsAsync(RemoteModQuery query, CancellationToken ct = default)
    {
        var page = await client.GetModsAsync(
            Math.Max(1, query.Page),
            Math.Clamp(query.PageSize, 1, 100),
            query.Search,
            query.Category,   // resourceType
            null,             // loader
            query.Sort,
            null,             // featured
            ct);

        var items = page.Items.Select(ToRemoteMod).ToList();
        return new RemoteModPage(items, page.Total, page.Page, page.PageSize);
    }

    public async Task<RemoteModDetail?> GetModDetailAsync(string slug, CancellationToken ct = default)
    {
        var detail = await client.GetModDetailAsync("MOD", slug, ct);

        var versions = detail.Versions
            .Where(v => !string.IsNullOrWhiteSpace(v.VersionId))
            .Select(v => new RemoteVersion(
                VersionId: v.VersionId!,
                Changelog: v.Changelog,
                GameVersion: v.Files
                    .SelectMany(f => f.SupportedGames)
                    .Select(g => g.Version)
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
                IsBeta: v.VersionType?.Contains("beta", StringComparison.OrdinalIgnoreCase) ?? false,
                Platforms: ["windows"],
                DirectUrl: null))   // 资源站要申请签名直链，现场解析
            .ToList();

        var mod = ToRemoteMod(new ModListItem
        {
            Id = detail.Id,
            ResourceType = detail.ResourceType,
            Slug = detail.Slug,
            DisplayName = detail.DisplayName,
            Summary = detail.Summary,
            Authors = detail.Authors,
            IconUrl = detail.IconUrl,
            DownloadCount = detail.DownloadCount,
            FavoriteCount = detail.FavoriteCount,
            LatestVersion = detail.LatestVersion,
        });

        return new RemoteModDetail(mod, detail.Description, versions);
    }

    public async Task<RemoteDownload?> ResolveDownloadAsync(
        string slug,
        RemoteVersion version,
        string platform,
        CancellationToken ct = default)
    {
        // 资源站不直接给外链：要先按版本找到文件，再申请一个 24 小时有效的签名直链
        var detail = await client.GetModDetailAsync("MOD", slug, ct);

        var file = detail.Versions
            .FirstOrDefault(v => string.Equals(v.VersionId, version.VersionId, StringComparison.OrdinalIgnoreCase))
            ?.Files.FirstOrDefault(f => f.Loaders.Any(l => l.Equals("unitymodmanager", StringComparison.OrdinalIgnoreCase)))
            ?? detail.Versions
                .FirstOrDefault(v => string.Equals(v.VersionId, version.VersionId, StringComparison.OrdinalIgnoreCase))
                ?.Files.FirstOrDefault()
            ?? detail.PreferredFile;

        if (file is null || string.IsNullOrWhiteSpace(file.Id))
        {
            return null;
        }

        var intent = await client.CreateDownloadIntentAsync(file.Id, ct);
        return new RemoteDownload(
            intent.Url,
            intent.FileName ?? file.Name ?? $"{slug}-{version.VersionId}.zip",
            file.Size,
            file.MimeType,
            IsArchive: true);   // 资源站的文件是服务端管着的，不需要像第三方源那样防"跳到网页"
    }

    private static RemoteMod ToRemoteMod(ModListItem item) => new(
        SourceId: "adofaitools",
        Id: item.Id,
        Slug: item.Slug,
        Name: item.DisplayName,
        Authors: string.Join("、", item.Authors
            .Select(a => a.Name ?? a.Nickname ?? a.Username ?? string.Empty)
            .Where(x => x.Length > 0)),
        Summary: item.Summary,
        IconUrl: item.IconUrl,
        Categories: item.ResourceType is { Length: > 0 } type ? [type] : [],
        LatestVersion: item.LatestVersion?.VersionId,
        GameVersion: null,
        Downloads: item.DownloadCount,
        Likes: item.FavoriteCount,
        HomepageUrl: null,
        Loader: ModLoader.Umm);
}
