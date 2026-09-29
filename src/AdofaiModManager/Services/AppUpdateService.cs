using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using AdofaiModManager.Models;

namespace AdofaiModManager.Services;

/// <summary>AMM 自身的新版本信息。</summary>
public sealed record AppUpdateInfo(string Version, string SourceLabel, string PageUrl);

/// <summary>
/// AMM 自身的更新检查（两条通道，思路和 mod 一致）：
///   ① **GitHub Releases** —— 作者首发，国内可能需要代理；
///   ② **资源站「工具库」** —— 站长会把 AMM 也放到资源站上。
///
/// 两边都查，取版本号更高的那个；都查不到就当作"已是最新"（不打扰用户）。
///
/// 这里只做「提示 + 打开下载页」，**不自动替换正在运行的程序**：
/// 安装版装在 Program Files 下需要管理员权限，交给用户手动更新更稳妥。
/// </summary>
public static class AppUpdateService
{
    public const string GitHubOwner = "lishangi688";

    public const string GitHubRepo = "adofai-mod-manager";

    private static readonly HttpClient Http = CreateHttpClient();

    /// <summary>当前版本，例如 "0.1"。</summary>
    public static string CurrentVersion =>
        FormatVersion(Assembly.GetExecutingAssembly().GetName().Version);

    /// <summary>发布页（GitHub Releases）。</summary>
    public static string ReleasesPageUrl =>
        $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases";

    /// <summary>仓库地址。</summary>
    public static string RepoUrl => $"https://github.com/{GitHubOwner}/{GitHubRepo}";

    /// <summary>检查新版本；没有新版本或检查失败都返回 null。</summary>
    public static async Task<AppUpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        var current = CurrentVersion;

        var tasks = new List<Task<AppUpdateInfo?>> { CheckGitHubAsync(ct) };

        if (AppServices.CreateApiClient() is { } client)
        {
            tasks.Add(CheckSiteAsync(client, ct));
        }

        var found = await Task.WhenAll(tasks);

        return found
            .Where(info => info is not null)
            .Select(info => info!)
            .Where(info => KernelService.CompareVersions(info.Version, current) > 0)
            .OrderByDescending(info => info.Version, Comparer<string>.Create(KernelService.CompareVersions))
            .FirstOrDefault();
    }

    /// <summary>GitHub：取最新 Release 的 tag。</summary>
    private static async Task<AppUpdateInfo?> CheckGitHubAsync(CancellationToken ct)
    {
        try
        {
            var api = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

            using var request = new HttpRequestMessage(HttpMethod.Get, api);
            request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");

            using var response = await Http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                // 404 通常表示还没发过 Release —— 属于正常情况
                return null;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = document.RootElement;

            var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
            var page = root.TryGetProperty("html_url", out var pageElement) ? pageElement.GetString() : null;

            var version = tag?.TrimStart('v', 'V');
            if (string.IsNullOrWhiteSpace(version))
            {
                return null;
            }

            return new AppUpdateInfo(version, "GitHub", page ?? ReleasesPageUrl);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>资源站：在「工具库」里找 AMM 自己。</summary>
    private static async Task<AppUpdateInfo?> CheckSiteAsync(AdofaiToolsClient client, CancellationToken ct)
    {
        try
        {
            var list = await client.GetToolsAsync(null, 1, 100, ct);
            var tool = list.Items.FirstOrDefault(IsSelf);

            if (tool is null)
            {
                return null;
            }

            var detail = await client.GetToolDetailAsync(tool.Slug, ct);
            var version = PickNewestVersion(detail) ?? tool.LatestVersion?.VersionId;

            if (string.IsNullOrWhiteSpace(version))
            {
                return null;
            }

            var baseUrl = AppServices.Settings.Settings.ApiBaseUrl.TrimEnd('/');
            return new AppUpdateInfo(version, "资源站", $"{baseUrl}/tools/{tool.Slug}");
        }
        catch
        {
            return null;
        }
    }

    /// <summary>在工具库里认出 AMM 自己（站点上的名字/短名/别名都认）。</summary>
    private static bool IsSelf(ToolListItem tool)
    {
        var name = tool.DisplayName ?? string.Empty;
        var slug = tool.Slug ?? string.Empty;

        return name.Contains("ADOFAI Mod Manager", StringComparison.OrdinalIgnoreCase)
               || name.Replace(" ", string.Empty).Equals("AMM", StringComparison.OrdinalIgnoreCase)
               || slug.Contains("adofai-mod-manager", StringComparison.OrdinalIgnoreCase)
               || slug.Equals("amm", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>接口返回的 versions 不一定按新到旧排序，这里自己挑最新的。</summary>
    private static string? PickNewestVersion(ToolDetail detail) =>
        detail.Versions
            .Where(v => !string.IsNullOrWhiteSpace(v.VersionId))
            .OrderByDescending(v => v.VersionId!, Comparer<string>.Create(KernelService.CompareVersions))
            .FirstOrDefault()?.VersionId;

    private static string FormatVersion(Version? version)
    {
        if (version is null)
        {
            return "0.0";
        }

        var text = $"{version.Major}.{version.Minor}";
        if (version.Build > 0)
        {
            text += $".{version.Build}";
        }

        if (version.Revision > 0)
        {
            text += $".{version.Revision}";
        }

        return text;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AdofaiModManager/0.1");
        return client;
    }
}
