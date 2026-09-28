namespace AdofaiModManager.Models;

/// <summary>
/// 「已安装」列表里的一项。
/// </summary>
public sealed class InstalledMod
{
    public required string Id { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string? Version { get; init; }

    public string? Author { get; init; }

    public string? GameVersion { get; init; }

    public string? ManagerVersion { get; init; }

    public string[]? Requirements { get; init; }

    public string? HomePage { get; init; }

    public string? Repository { get; init; }

    public string FolderPath { get; init; } = string.Empty;

    public string FolderName { get; init; } = string.Empty;

    public bool IsEnabled { get; set; }

    /// <summary>更新源说明（页面填入）</summary>
    public string? UpdateSourceLabel { get; set; }

    /// <summary>更新状态文本（检查后填入）</summary>
    public string? UpdateStatus { get; set; }

    public bool HasUpdate { get; set; }

    public string? RemoteVersion { get; set; }

    public string? UpdateDownloadUrl { get; set; }

    public string? UpdateFileName { get; set; }

    /// <summary>依赖（Info.json 的 Requirements）</summary>
    public string RequirementsLabel => Requirements is { Length: > 0 }
        ? "依赖：" + string.Join("、", Requirements)
        : string.Empty;

    /// <summary>游戏版本兼容性警告（页面填入）</summary>
    public string? CompatibilityWarning { get; set; }

    /// <summary>图标占位字符。</summary>
    public string Initial =>
        string.IsNullOrWhiteSpace(DisplayName) ? "?" : DisplayName.Trim()[..1].ToUpperInvariant();

    public string Subtitle =>
        string.IsNullOrWhiteSpace(Version) ? Id : $"{Id}  ·  v{Version}";

    public string ExtraInfo
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Author))
            {
                parts.Add($"作者 {Author}");
            }

            if (!string.IsNullOrWhiteSpace(GameVersion))
            {
                parts.Add($"游戏 {GameVersion}");
            }

            if (!string.IsNullOrWhiteSpace(ManagerVersion))
            {
                parts.Add($"UMM {ManagerVersion}");
            }

            return string.Join("   ·   ", parts);
        }
    }
}
