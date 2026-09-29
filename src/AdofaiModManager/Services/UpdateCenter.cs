using AdofaiModManager.Models;

namespace AdofaiModManager.Services;

/// <summary>
/// 全局的"更新中心"：保存各 mod 的更新检查结果，供启动检查、角标提醒和各页面共用。
///
/// 更新来源分两类，都参与判断：
///  ① 资源站（只要配置了站点）—— 与「在线 Mod」页看到的一致；
///  ② mod 自带的 GitHub 更新源（Repository.json / Releases）。
/// </summary>
public sealed class UpdateCenter
{
    private static readonly TimeSpan SiteMapTtl = TimeSpan.FromMinutes(10);

    private readonly Dictionary<string, UpdateCheckResult> _results = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, SiteModMatch> _siteMap = new(StringComparer.OrdinalIgnoreCase);

    private DateTime _siteMapTime = DateTime.MinValue;

    public IReadOnlyDictionary<string, UpdateCheckResult> Results => _results;

    /// <summary>资源站 mod 映射（键：UMM Id 或显示名）</summary>
    public IReadOnlyDictionary<string, SiteModMatch> SiteMap => _siteMap;

    public int UpdatableCount { get; private set; }

    public bool HasChecked { get; private set; }

    /// <summary>结果变化（检查完成 / 清除 / 站点映射刷新）时触发。</summary>
    public event Action? Changed;

    public UpdateCheckResult? Get(string modId) =>
        _results.TryGetValue(modId, out var result) ? result : null;

    /// <summary>在资源站上找与某个已安装 mod 对应的条目。</summary>
    public SiteModMatch? FindSiteMod(string modId) =>
        _siteMap.TryGetValue(modId, out var match) ? match : null;

    public void Set(string modId, UpdateCheckResult result)
    {
        _results[modId] = result;
        Recount();
        Changed?.Invoke();
    }

    public void Clear(string modId)
    {
        if (_results.Remove(modId))
        {
            Recount();
            Changed?.Invoke();
        }
    }

    public void ClearAll()
    {
        _results.Clear();
        Recount();
        Changed?.Invoke();
    }

    /// <summary>
    /// 拉取/刷新「资源站 mod 映射」。10 分钟内重复调用直接返回。
    /// 映射更新后会触发 Changed，让页面刷新（显示更新源与图标）。
    /// </summary>
    public async Task EnsureSiteMapAsync(bool force = false, CancellationToken ct = default)
    {
        if (!force &&
            _siteMap.Count > 0 &&
            DateTime.UtcNow - _siteMapTime < SiteMapTtl)
        {
            return;
        }

        var client = BuildSiteClient();
        if (client is null)
        {
            return;
        }

        var map = await TryFetchSiteVersionsAsync(client, ct);
        if (map is null || map.Count == 0)
        {
            return;
        }

        _siteMap.Clear();
        foreach (var pair in map)
        {
            _siteMap[pair.Key] = pair.Value;
        }

        _siteMapTime = DateTime.UtcNow;
        Changed?.Invoke();
    }

    /// <summary>检查全部已安装 mod 的更新。</summary>
    public async Task CheckAllAsync(
        IReadOnlyList<InstalledMod> mods,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var github = new GitHubUpdateService(AppServices.UpdateSources);

        // 资源站：一次性取出全部 mod 的最新版本
        await EnsureSiteMapAsync(ct: ct);

        foreach (var mod in mods)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(mod.DisplayName);

            // ① 资源站
            if (FindSiteMod(mod.Id) is { } site)
            {
                var newer = KernelService.CompareVersions(site.Version, mod.Version) > 0;

                _results[mod.Id] = new UpdateCheckResult
                {
                    Success = true,
                    UpdateAvailable = newer,
                    LocalVersion = mod.Version,
                    RemoteVersion = site.Version,
                    SiteSlug = site.Slug,
                    SiteResourceType = site.ResourceType,
                    SourceLabel = "资源站",
                    Message = newer ? $"资源站有新版 {site.Version}" : $"资源站已是最新（{site.Version}）",
                };

                continue;
            }

            // ② GitHub
            var source = github.ResolveSource(mod);
            if (source.Kind == UpdateSourceKind.None)
            {
                continue;
            }

            var result = await github.CheckAsync(source, mod.Id, mod.Version, ct);
            result.SourceLabel = "GitHub";
            _results[mod.Id] = result;
        }

        HasChecked = true;
        Recount();
        Changed?.Invoke();
    }

    private static AdofaiToolsClient? BuildSiteClient() => AppServices.CreateApiClient();

    /// <summary>
    /// 拉取资源站全部 mod，建立 "UMM Id / 显示名 → 站点条目" 的映射。
    /// 一次拉 100 条（接口上限），翻页取完。
    /// </summary>
    private static async Task<Dictionary<string, SiteModMatch>?> TryFetchSiteVersionsAsync(
        AdofaiToolsClient client,
        CancellationToken ct)
    {
        try
        {
            var map = new Dictionary<string, SiteModMatch>(StringComparer.OrdinalIgnoreCase);
            var page = 1;

            while (true)
            {
                var result = await client.GetModsAsync(page, 100, null, null, null, "updated", null, ct);

                foreach (var item in result.Items)
                {
                    var version = item.LatestVersion?.VersionId;
                    if (string.IsNullOrWhiteSpace(version))
                    {
                        continue;
                    }

                    var match = new SiteModMatch(
                        version,
                        item.Slug,
                        item.ResourceType ?? "MOD",
                        item.IconUrl,
                        item.DisplayName);

                    // 显示名做键（多数 mod 的 displayName 就是 UMM Id）
                    map[item.DisplayName] = match;

                    // 装过的走精确映射
                    var ummId = AppServices.InstallMap.GetUmmId(item.Id);
                    if (!string.IsNullOrWhiteSpace(ummId))
                    {
                        map[ummId] = match;
                    }
                }

                if (result.Items.Count == 0 || page * result.PageSize >= result.Total)
                {
                    break;
                }

                page++;
            }

            return map;
        }
        catch (AdofaiToolsException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void Recount() =>
        UpdatableCount = _results.Values.Count(r => r.Success && r.UpdateAvailable);
}
