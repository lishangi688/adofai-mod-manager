using AdofaiModManager.Models;

namespace AdofaiModManager.Services;

/// <summary>
/// 全局的"更新中心"：保存各 mod 的更新检查结果，供启动检查、角标提醒和各页面共用。
///
/// 更新来源分两类，都参与判断：
///  ① 资源站（只要填了 API key）—— 与「在线 Mod」页看到的一致；
///  ② mod 自带的 GitHub 更新源（Repository.json / Releases）。
/// </summary>
public sealed class UpdateCenter
{
    private readonly Dictionary<string, UpdateCheckResult> _results = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, UpdateCheckResult> Results => _results;

    public int UpdatableCount { get; private set; }

    public bool HasChecked { get; private set; }

    /// <summary>结果变化（检查完成 / 清除）时触发。</summary>
    public event Action? Changed;

    public UpdateCheckResult? Get(string modId) =>
        _results.TryGetValue(modId, out var result) ? result : null;

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

    /// <summary>检查全部已安装 mod 的更新。</summary>
    public async Task CheckAllAsync(
        IReadOnlyList<InstalledMod> mods,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var github = new GitHubUpdateService(AppServices.UpdateSources);

        // 资源站：一次性取出全部 mod 的最新版本
        var siteClient = BuildSiteClient();
        var siteVersions = siteClient is null ? null : await TryFetchSiteVersionsAsync(siteClient, ct);

        foreach (var mod in mods)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(mod.DisplayName);

            // ① 资源站
            if (siteVersions is not null && siteVersions.TryGetValue(mod.Id, out var entry))
            {
                var newer = KernelService.CompareVersions(entry.Version, mod.Version) > 0;

                _results[mod.Id] = new UpdateCheckResult
                {
                    Success = true,
                    UpdateAvailable = newer,
                    LocalVersion = mod.Version,
                    RemoteVersion = entry.Version,
                    SiteSlug = entry.Slug,
                    SiteResourceType = entry.ResourceType,
                    SourceLabel = "资源站",
                    Message = newer ? $"资源站有新版 {entry.Version}" : $"资源站已是最新（{entry.Version}）",
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

    private static AdofaiToolsClient? BuildSiteClient()
    {
        return AppServices.CreateApiClient();
    }

    /// <summary>拉取资源站全部 mod 的最新版本，建立 "UMM Id / 显示名 → 版本" 的映射。</summary>
    private static async Task<Dictionary<string, SiteEntry>?> TryFetchSiteVersionsAsync(
        AdofaiToolsClient client,
        CancellationToken ct)
    {
        try
        {
            var map = new Dictionary<string, SiteEntry>(StringComparer.OrdinalIgnoreCase);
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

                    var entry = new SiteEntry(version, item.Slug, item.ResourceType ?? "MOD");

                    // 显示名做键（多数 mod 的 displayName 就是 UMM Id）
                    map[item.DisplayName] = entry;

                    // 装过的走精确映射
                    var ummId = AppServices.InstallMap.GetUmmId(item.Id);
                    if (!string.IsNullOrWhiteSpace(ummId))
                    {
                        map[ummId] = entry;
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

    private sealed record SiteEntry(string Version, string Slug, string ResourceType);
}
