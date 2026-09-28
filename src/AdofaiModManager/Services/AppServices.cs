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

        if (string.IsNullOrWhiteSpace(Settings.Settings.GamePath) ||
            !Directory.Exists(Settings.Settings.GamePath))
        {
            var best = GameLocator.FindBest();
            if (best is not null)
            {
                Settings.Settings.GamePath = best;
                Settings.Save();
            }
        }
    }
}
