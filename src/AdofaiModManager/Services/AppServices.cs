using System.IO;

namespace AdofaiModManager.Services;

/// <summary>
/// 极简的全局服务容器（后续会替换为正式依赖注入）。
/// </summary>
public static class AppServices
{
    public static SettingsService Settings { get; private set; } = null!;

    public static SteamGameLocator GameLocator { get; private set; } = null!;

    public static InstallMapService InstallMap { get; private set; } = null!;

    public static UpdateSourceStore UpdateSources { get; private set; } = null!;

    public static UpdateCenter Updates { get; private set; } = null!;

    public static FavoritesStore Favorites { get; private set; } = null!;

    /// <summary>当前游戏版本：优先用户手动填写，否则尝试自动读取。</summary>
    public static string? GameVersion
    {
        get
        {
            var manual = Settings.Settings.GameVersionOverride;
            if (!string.IsNullOrWhiteSpace(manual))
            {
                return manual.Trim();
            }

            var path = Settings.Settings.GamePath;
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return null;
            }

            return GameVersionReader.TryRead(path!);
        }
    }

    /// <summary>
    /// 按当前设置创建资源站客户端。
    /// API key 允许为空 —— 有些站点可能不要求鉴权。
    /// </summary>
    public static AdofaiToolsClient? CreateApiClient()
    {
        var settings = Settings.Settings;
        if (string.IsNullOrWhiteSpace(settings.ApiBaseUrl))
        {
            return null;
        }

        return new AdofaiToolsClient(settings.ApiBaseUrl, settings.ApiKey);
    }

    public static void Initialize()
    {
        Settings = new SettingsService();
        Settings.Load();

        GameLocator = new SteamGameLocator();
        InstallMap = new InstallMapService();
        UpdateSources = new UpdateSourceStore();
        Updates = new UpdateCenter();
        Favorites = new FavoritesStore();

        // 游戏目录：没设置过、或当前这个明显不如自动检测的结果时，换成更好的那个
        // （机器上可能有旧的游戏备份，靠打分区分）
        var configured = Settings.Settings.GamePath;
        var best = GameLocator.FindBest();

        var configuredScore = string.IsNullOrWhiteSpace(configured) || !Directory.Exists(configured)
            ? int.MinValue
            : GameLocator.Score(configured!);

        if (best is not null &&
            !string.Equals(best, configured, StringComparison.OrdinalIgnoreCase) &&
            GameLocator.Score(best) > configuredScore)
        {
            Settings.Settings.GamePath = best;
            Settings.Save();
        }
    }
}
