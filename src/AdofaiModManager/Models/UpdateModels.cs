namespace AdofaiModManager.Models;

public enum UpdateSourceKind
{
    None,
    RepositoryJson,
    GitHubRepo,
}

/// <summary>某个 mod 的更新来源。</summary>
public sealed class UpdateSource
{
    public UpdateSourceKind Kind { get; init; }

    public string? Url { get; init; }

    public string? Owner { get; init; }

    public string? Repo { get; init; }

    public bool IsManual { get; init; }

    public string Description => Kind switch
    {
        UpdateSourceKind.RepositoryJson => Url ?? string.Empty,
        UpdateSourceKind.GitHubRepo => $"GitHub：{Owner}/{Repo}",
        _ => "未设置",
    };
}

/// <summary>一次更新检查的结果。</summary>
public sealed class UpdateCheckResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public bool UpdateAvailable { get; init; }

    public string? LocalVersion { get; init; }

    public string? RemoteVersion { get; init; }

    public string? DownloadUrl { get; set; }

    public string? FileName { get; set; }

    /// <summary>来源标签：资源站 / GitHub</summary>
    public string? SourceLabel { get; set; }

    /// <summary>资源站 mod 的标识（若更新来自资源站）</summary>
    public string? SiteSlug { get; set; }

    public string? SiteResourceType { get; set; }

    /// <summary>
    /// 次要来源的说明，例如「资源站 2.5.0」或「GitHub 连不上」。
    /// 用于让用户知道"另一个来源是什么情况"。
    /// </summary>
    public string? SecondaryNote { get; set; }

    /// <summary>
    /// 两边版本号"写法"不同（无法逐段比较）。
    /// 例：本地 26w40（年份+周）vs 资源站 26.5.1（三段数字）。
    /// </summary>
    public bool VersionSchemeMismatch { get; set; }

    /// <summary>是否同时存在可用于下载的资源站版本（GitHub 下载失败时兜底用）</summary>
    public bool HasSiteFallback => !string.IsNullOrWhiteSpace(SiteSlug);

    public string ShortStatus => !Success
        ? "检查失败"
        : UpdateAvailable
            ? $"可更新 → {RemoteVersion}"
            : "已是最新";
}
