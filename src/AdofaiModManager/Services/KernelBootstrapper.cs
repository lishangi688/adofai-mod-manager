using AdofaiModManager.Models;

namespace AdofaiModManager.Services;

/// <summary>
/// 内核安装的"编排"层：把「本地内核管理」（KernelService）和「资源站查最新版」组合起来。
///
/// 单独拆出来的原因：KernelService 不应该依赖资源站客户端
/// （这样它保持独立、也能被隔离的测试项目单独编译）。
/// </summary>
public sealed class KernelBootstrapper(LoaderService loader)
{
    public KernelService Kernel { get; } = new(loader);

    /// <summary>
    /// 装/修加载器的最佳路径：优先用资源站上更新的内核，否则用内置内核。
    /// （首次向导和「UMM 环境」页共用）
    /// </summary>
    public async Task<InstallResult> InstallBestAsync(
        AdofaiToolsClient? client,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var bundled = Kernel.GetBundled();
        if (bundled is null)
        {
            return new InstallResult(false, "内置内核缺失，请重新安装本软件。");
        }

        if (client is not null)
        {
            try
            {
                progress?.Report("正在从资源站检查最新内核…");
                var site = await FindLatestSiteKernelAsync(client, ct);

                if (site is not null &&
                    KernelService.CompareVersions(site.Value.VersionId, bundled.Version) > 0)
                {
                    progress?.Report($"资源站有更新的内核 {site.Value.VersionId}，正在下载…");
                    var intent = await client.CreateToolDownloadIntentAsync(site.Value.FileId, ct);
                    var (imported, message) = await Kernel.ImportAsync(intent.Url, site.Value.VersionId, ct);

                    return imported is not null
                        ? Kernel.Deploy(imported)
                        : new InstallResult(false, message);
                }
            }
            catch (AdofaiToolsException)
            {
                // 资源站不可用 → 走内置内核
            }
            catch (Exception)
            {
                // 同上
            }
        }

        progress?.Report($"正在使用内置内核 {bundled.Version}…");
        return Kernel.Deploy(bundled);
    }

    /// <summary>强制从资源站下载并部署最新内核（资源站没有则失败）。</summary>
    public async Task<InstallResult> InstallFromSiteAsync(
        AdofaiToolsClient client,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var site = await FindLatestSiteKernelAsync(client, ct);
        if (site is null)
        {
            return new InstallResult(false, "资源站上没有找到 UnityModManager，可改用内置内核或导入本地 zip。");
        }

        progress?.Report($"正在下载资源站内核 {site.Value.VersionId}…");
        var intent = await client.CreateToolDownloadIntentAsync(site.Value.FileId, ct);
        var (imported, message) = await Kernel.ImportAsync(intent.Url, site.Value.VersionId, ct);

        return imported is not null ? Kernel.Deploy(imported) : new InstallResult(false, message);
    }

    /// <summary>只部署 AMM 内置内核（离线可用）。</summary>
    public InstallResult InstallBundled()
    {
        var bundled = Kernel.GetBundled();
        return bundled is null
            ? new InstallResult(false, "内置内核缺失，请重新安装本软件。")
            : Kernel.Deploy(bundled);
    }

    /// <summary>从资源站「工具库」找最新的 UnityModManager 版本与文件。</summary>
    public static async Task<(string VersionId, string FileId)?> FindLatestSiteKernelAsync(
        AdofaiToolsClient client,
        CancellationToken ct = default)
    {
        var list = await client.GetToolsAsync("UnityModManager", 1, 20, ct);
        var tool = list.Items.FirstOrDefault(t =>
            t.DisplayName.Contains("UnityModManager", StringComparison.OrdinalIgnoreCase));

        if (tool is null)
        {
            return null;
        }

        var detail = await client.GetToolDetailAsync(tool.Slug, ct);
        var latestId = tool.LatestVersion?.VersionId;

        // 注意：接口返回的 versions 不一定按新到旧排序
        var version = detail.Versions.FirstOrDefault(v =>
                          !string.IsNullOrWhiteSpace(latestId) &&
                          string.Equals(v.VersionId, latestId, StringComparison.OrdinalIgnoreCase))
                      ?? detail.Versions
                          .Where(v => !string.IsNullOrWhiteSpace(v.VersionId))
                          .OrderByDescending(v => v.VersionId!,
                              Comparer<string>.Create((a, b) => KernelService.CompareVersions(a, b)))
                          .FirstOrDefault();

        return version?.File is null || string.IsNullOrWhiteSpace(version.VersionId)
            ? null
            : (version.VersionId, version.File.Id);
    }
}
