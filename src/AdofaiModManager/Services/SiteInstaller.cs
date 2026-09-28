using AdofaiModManager.Models;

namespace AdofaiModManager.Services;

/// <summary>从资源站安装一个 mod 的公共流程（供「在线 Mod」和「收藏」共用）。</summary>
public sealed class SiteInstaller(AdofaiToolsClient client, ModService modService)
{
    public Task<InstallResult> InstallAsync(
        ModDetail detail,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        return InstallAsync(detail, detail.PreferredFile, progress, ct);
    }

    public async Task<InstallResult> InstallAsync(
        ModDetail detail,
        ModVersionFile? file,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        if (file is null)
        {
            return new InstallResult(false, "该 mod 没有可下载的文件。");
        }

        var intent = await client.CreateDownloadIntentAsync(file.Id, ct);
        var result = await modService.InstallFromUrlAsync(intent.Url, progress, ct);

        if (result.Success && result.ModId is not null)
        {
            AppServices.InstallMap.Set(detail.Id, result.ModId);

            var gitHub = detail.HomepageUrl ?? detail.SourceUrl;
            if (!string.IsNullOrWhiteSpace(gitHub) &&
                gitHub.Contains("github.com", StringComparison.OrdinalIgnoreCase))
            {
                AppServices.UpdateSources.SetOverride(result.ModId, gitHub);
            }
        }

        return result;
    }
}
