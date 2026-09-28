using System.Net;
using System.Net.Http;
using System.Text.Json;
using AdofaiModManager.Models;

namespace AdofaiModManager.Services;

/// <summary>
/// GitHub 更新通道：
///  1) 优先读 mod 自带的 Repository 字段（UMM 标准 Repository.json）；
///  2) 没有则用 GitHub Releases（取最新 Release 里的 Repository.json 或 zip）。
/// </summary>
public sealed class GitHubUpdateService
{
    private static readonly HttpClient Http = CreateHttpClient();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly UpdateSourceStore _store;

    public GitHubUpdateService(UpdateSourceStore store) => _store = store;

    /// <summary>解析某个 mod 的更新来源（手动指定优先）。</summary>
    public UpdateSource ResolveSource(InstalledMod mod)
    {
        var manual = _store.GetOverride(mod.Id);
        if (!string.IsNullOrWhiteSpace(manual))
        {
            var source = ParseUrl(manual, true);
            if (source.Kind != UpdateSourceKind.None)
            {
                return source;
            }
        }

        if (!string.IsNullOrWhiteSpace(mod.Repository))
        {
            var source = ParseUrl(mod.Repository, false);
            if (source.Kind != UpdateSourceKind.None)
            {
                return source;
            }
        }

        if (!string.IsNullOrWhiteSpace(mod.HomePage))
        {
            var repo = TryParseGitHubRepo(mod.HomePage);
            if (repo is not null)
            {
                return new UpdateSource
                {
                    Kind = UpdateSourceKind.GitHubRepo,
                    Owner = repo.Value.Owner,
                    Repo = repo.Value.Repo,
                };
            }
        }

        return new UpdateSource { Kind = UpdateSourceKind.None };
    }

    public async Task<UpdateCheckResult> CheckAsync(UpdateSource source, string modId, string? localVersion, CancellationToken ct = default)
    {
        try
        {
            return source.Kind switch
            {
                UpdateSourceKind.RepositoryJson => await CheckRepositoryAsync(source.Url!, modId, localVersion, ct),
                UpdateSourceKind.GitHubRepo => await CheckGitHubAsync(source.Owner!, source.Repo!, modId, localVersion, ct),
                _ => new UpdateCheckResult { Success = false, Message = "该 mod 没有可用的更新源。" },
            };
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult { Success = false, Message = $"检查失败：{ex.Message}" };
        }
    }

    private async Task<UpdateCheckResult> CheckRepositoryAsync(string url, string modId, string? localVersion, CancellationToken ct)
    {
        var json = await GetStringAsync(url, ct);
        var repository = JsonSerializer.Deserialize<UmmRepositoryFile>(json, JsonOptions);

        if (repository?.Releases is null || repository.Releases.Count == 0)
        {
            return new UpdateCheckResult { Success = false, Message = "Repository.json 里没有版本信息。" };
        }

        var release = repository.Releases.FirstOrDefault(r =>
                          string.Equals(r.Id, modId, StringComparison.OrdinalIgnoreCase))
                      ?? repository.Releases[0];

        if (string.IsNullOrWhiteSpace(release.DownloadUrl))
        {
            return new UpdateCheckResult { Success = false, Message = "Repository.json 里没有下载地址。" };
        }

        var remote = release.Version;
        var available = IsNewer(remote, localVersion);

        return new UpdateCheckResult
        {
            Success = true,
            UpdateAvailable = available,
            LocalVersion = localVersion,
            RemoteVersion = remote,
            DownloadUrl = release.DownloadUrl,
            Message = available ? $"发现新版本 {remote}" : $"已是最新（{localVersion ?? "未知"}）",
        };
    }

    private async Task<UpdateCheckResult> CheckGitHubAsync(string owner, string repo, string modId, string? localVersion, CancellationToken ct)
    {
        var api = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
        var json = await GetStringAsync(api, ct, gitHubApi: true);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
        tag = tag?.TrimStart('v', 'V');

        string? repositoryJsonUrl = null;
        string? zipUrl = null;
        string? zipName = null;

        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;
                var url = asset.TryGetProperty("browser_download_url", out var urlElement) ? urlElement.GetString() : null;

                if (url is null)
                {
                    continue;
                }

                if (name.Equals("Repository.json", StringComparison.OrdinalIgnoreCase))
                {
                    repositoryJsonUrl ??= url;
                }
                else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    var better = zipUrl is null || name.Contains(modId, StringComparison.OrdinalIgnoreCase);
                    if (better)
                    {
                        zipUrl = url;
                        zipName = name;
                    }
                }
            }
        }

        if (repositoryJsonUrl is not null)
        {
            var result = await CheckRepositoryAsync(repositoryJsonUrl, modId, localVersion, ct);
            if (result.Success)
            {
                return result;
            }
        }

        if (zipUrl is null)
        {
            return new UpdateCheckResult
            {
                Success = false,
                Message = $"仓库 {owner}/{repo} 的最新 Release 里没有找到可下载的 zip。",
            };
        }

        var available = IsNewer(tag, localVersion);

        return new UpdateCheckResult
        {
            Success = true,
            UpdateAvailable = available,
            LocalVersion = localVersion,
            RemoteVersion = tag,
            DownloadUrl = zipUrl,
            FileName = zipName,
            Message = available
                ? $"发现新版本 {tag}（{zipName}）"
                : $"已是最新（{localVersion ?? "未知"}）",
        };
    }

    private static bool IsNewer(string? remote, string? local)
    {
        if (string.IsNullOrWhiteSpace(remote))
        {
            return false;
        }

        return local is null || KernelService.CompareVersions(remote, local) > 0;
    }

    private static async Task<string> GetStringAsync(string url, CancellationToken ct, bool gitHubApi = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Accept", gitHubApi ? "application/vnd.github+json" : "application/json");

        using var response = await Http.SendAsync(request, ct);

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            throw new InvalidOperationException("GitHub 接口访问受限（可能是请求太频繁），请稍后再试。");
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException("地址不存在（404）。请检查更新源地址是否正确。");
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    private static UpdateSource ParseUrl(string url, bool manual)
    {
        var trimmed = url.Trim();

        var repo = TryParseGitHubRepo(trimmed);
        if (repo is not null &&
            !trimmed.Contains("/raw/", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return new UpdateSource
            {
                Kind = UpdateSourceKind.GitHubRepo,
                Owner = repo.Value.Owner,
                Repo = repo.Value.Repo,
                IsManual = manual,
            };
        }

        var repositoryUrl = trimmed.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed.TrimEnd('/') + "/Repository.json";

        return new UpdateSource
        {
            Kind = UpdateSourceKind.RepositoryJson,
            Url = repositoryUrl,
            IsManual = manual,
        };
    }

    private static (string Owner, string Repo)? TryParseGitHubRepo(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return null;
        }

        return (parts[0], parts[1].Replace(".git", string.Empty, StringComparison.OrdinalIgnoreCase));
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AdofaiModManager/0.1");
        return client;
    }

    private sealed class UmmRepositoryFile
    {
        public List<UmmRepositoryRelease> Releases { get; set; } = [];
    }

    private sealed class UmmRepositoryRelease
    {
        public string? Id { get; set; }

        public string? Version { get; set; }

        public string? DownloadUrl { get; set; }
    }
}
