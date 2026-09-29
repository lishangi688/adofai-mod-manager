namespace AdofaiModManager.Models;

/// <summary>
/// 资源站上与某个 mod 的匹配结果。
/// 用于「已安装」页：判断更新源、以及同步站点图标。
/// </summary>
public sealed record SiteModMatch(
    string Version,
    string Slug,
    string ResourceType,
    string? IconUrl,
    string DisplayName);
