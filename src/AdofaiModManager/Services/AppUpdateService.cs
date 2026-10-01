using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using AdofaiModManager.Models;
using Microsoft.Win32;

namespace AdofaiModManager.Services;

/// <summary>AMM 自身的分发形态：影响更新时推荐下载哪种文件。</summary>
public enum AppDistribution
{
    /// <summary>绿色版（解压即用）</summary>
    Portable,

    /// <summary>安装版（Inno Setup 装到 Program Files 或用户目录）</summary>
    Installed,
}

/// <summary>
/// AMM 自身的新版本信息。
/// </summary>
/// <param name="Version">新版本号</param>
/// <param name="SourceLabel">来源（资源站 / GitHub）</param>
/// <param name="PageUrl">人看的页面（资源站首页 / GitHub 发布页）</param>
/// <param name="DownloadUrl">可直接下载的地址（资源站会返回免鉴权签名直链；GitHub 通常为空）</param>
/// <param name="FileName">下载文件名（若有）</param>
public sealed record AppUpdateInfo(
    string Version,
    string SourceLabel,
    string PageUrl,
    string? DownloadUrl = null,
    string? FileName = null);

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

        var tasks = new List<Task<AppUpdateInfo?>>();

        // 用户关掉了「同时查询 GitHub」就跳过（与 mod 的更新检查保持一致）
        if (AppServices.Settings.Settings.CheckGitHubUpdates)
        {
            tasks.Add(CheckGitHubAsync(ct));
        }

        if (AppServices.CreateApiClient() is { } client)
        {
            tasks.Add(CheckSiteAsync(client, ct));
        }

        if (tasks.Count == 0)
        {
            return null;
        }

        var found = await Task.WhenAll(tasks);

        return found
            .Where(info => info is not null)
            .Select(info => info!)
            .Where(info => KernelService.CompareVersions(info.Version, current) > 0)
            .OrderByDescending(info => info.Version, Comparer<string>.Create(KernelService.CompareVersions))
            // 版本号相同时优先资源站：国内更快，而且能拿到免鉴权的直链下载地址
            .ThenByDescending(info => info.SourceLabel == "资源站" ? 1 : 0)
            .FirstOrDefault();
    }

    /// <summary>
    /// 当前是「安装版」还是「绿色版」。
    /// 影响更新引导：安装版建议下载安装包；绿色版直接下载 zip 覆盖即可。
    /// </summary>
    public static AppDistribution Distribution => DetectDistribution();

    /// <summary>给用户的更新建议（一句话）。</summary>
    public static string DistributionHint => Distribution == AppDistribution.Installed
        ? "你是「安装版」：建议下载安装包（Setup .exe）直接覆盖安装。"
        : "你是「绿色版」：下载 zip 后解压覆盖到当前目录即可。";

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

    /// <summary>资源站：在「工具库」里找 AMM 自己，并尽量取到一个可直链下载的文件。</summary>
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

            // 资源站不允许直接外链文件（需要鉴权），但可以申请一个「下载意图」，
            // 拿到一个 24 小时有效的签名直链，浏览器无需 key 即可下载。
            string? downloadUrl = null;
            string? fileName = null;

            var newest = detail.Versions
                .Where(v => !string.IsNullOrWhiteSpace(v.VersionId))
                .OrderByDescending(v => v.VersionId!, Comparer<string>.Create(KernelService.CompareVersions))
                .FirstOrDefault();

            if (newest?.File is { } file && !string.IsNullOrWhiteSpace(file.Id))
            {
                try
                {
                    var intent = await client.CreateToolDownloadIntentAsync(file.Id, ct);
                    if (!string.IsNullOrWhiteSpace(intent.Url))
                    {
                        downloadUrl = intent.Url;
                        fileName = string.IsNullOrWhiteSpace(intent.FileName) ? file.Name : intent.FileName;
                    }
                }
                catch
                {
                    // 拿不到直链也没关系，退化成"打开资源站"
                }
            }

            return new AppUpdateInfo(version, "资源站", baseUrl, downloadUrl, fileName);
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

        var compact = name.Replace(" ", string.Empty);

        return name.Contains("ADOFAI Mod Manager", StringComparison.OrdinalIgnoreCase)
               || compact.Equals("AMM", StringComparison.OrdinalIgnoreCase)
               // 站点上常见的写法：AMM 模组管理器 / AMM管理器
               || compact.StartsWith("AMM", StringComparison.OrdinalIgnoreCase)
               || slug.Contains("adofai-mod-manager", StringComparison.OrdinalIgnoreCase)
               || slug.Equals("amm", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 判断当前跑的是安装版还是绿色版：
    /// Inno Setup 安装时会在注册表里写卸载项（含 InstallLocation），
    /// 如果该项指向的目录正好是当前程序所在目录，就说明是安装版。
    /// </summary>
    private static AppDistribution DetectDistribution()
    {
        try
        {
            var exeDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using var uninstall = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null)
                {
                    continue;
                }

                foreach (var sub in uninstall.GetSubKeyNames())
                {
                    using var key = uninstall.OpenSubKey(sub);
                    if (key?.GetValue("DisplayName") is not string displayName)
                    {
                        continue;
                    }

                    if (!displayName.Contains("ADOFAI Mod Manager", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var location = key.GetValue("InstallLocation") as string;
                    if (string.IsNullOrWhiteSpace(location))
                    {
                        // 退而求其次：从卸载命令里把目录抠出来
                        var command = key.GetValue("UninstallString") as string;
                        if (!string.IsNullOrWhiteSpace(command))
                        {
                            location = Path.GetDirectoryName(command.Trim().Trim('"'));
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(location) &&
                        string.Equals(
                            location!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                            exeDir,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return AppDistribution.Installed;
                    }
                }
            }
        }
        catch
        {
            // 读注册表失败就当绿色版（更保守：不会给出"覆盖安装"的错误建议）
        }

        return AppDistribution.Portable;
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
