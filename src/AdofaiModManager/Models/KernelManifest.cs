namespace AdofaiModManager.Models;

/// <summary>清单里的一条内核版本（用于从资源站/本地导入时描述一个内核包）。</summary>
public sealed class KernelRelease
{
    public string Version { get; set; } = string.Empty;

    public string? ReleaseDate { get; set; }

    public string? DownloadUrl { get; set; }

    public string? Sha256 { get; set; }

    public string? Upstream { get; set; }

    public string? Notes { get; set; }
}

/// <summary>本地可用的一个内核包。</summary>
public sealed class KernelInfo
{
    public required string Version { get; init; }

    public required string Directory { get; init; }

    /// <summary>bundled / downloaded / snapshot</summary>
    public string Source { get; init; } = "local";

    public string? Notes { get; init; }
}
