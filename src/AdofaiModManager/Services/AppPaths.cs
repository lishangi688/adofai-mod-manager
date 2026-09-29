using System.IO;

namespace AdofaiModManager.Services;

/// <summary>
/// 统一的路径管理。
///
/// 注意：安装到 Program Files 后，程序目录是**只读**的（非管理员写不进去），
/// 所以日志一律写到用户目录，配置/收藏等数据也都在用户目录。
/// </summary>
public static class AppPaths
{
    private const string FolderName = "AdofaiModManager";

    /// <summary>漫游数据目录（配置、收藏、更新源、内核缓存）</summary>
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), FolderName);

    /// <summary>本机数据目录（日志）</summary>
    public static string LocalDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName);

    public static string LogDirectory { get; } = Path.Combine(LocalDirectory, "logs");

    /// <summary>缓存根目录（图标 / 接口响应 / 已下载的 mod 包）</summary>
    public static string CacheDirectory { get; } = Path.Combine(LocalDirectory, "cache");

    public static string IconCacheDirectory { get; } = Path.Combine(CacheDirectory, "icons");

    public static string ApiCacheDirectory { get; } = Path.Combine(CacheDirectory, "api");

    public static string ModCacheDirectory { get; } = Path.Combine(CacheDirectory, "mods");

    public static string CrashLogPath { get; } = Path.Combine(LogDirectory, "crash.log");

    public static string DebugLogPath { get; } = Path.Combine(LogDirectory, "debug.log");

    /// <summary>调试日志开关（环境变量 AMM_SCROLL_DEBUG=1 时开启）</summary>
    public static bool DebugLogEnabled =>
        Environment.GetEnvironmentVariable("AMM_SCROLL_DEBUG") == "1";

    public static void AppendDebugLog(string message)
    {
        if (!DebugLogEnabled)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(LogDirectory);
            File.AppendAllText(DebugLogPath, message + Environment.NewLine);
        }
        catch
        {
            // 忽略
        }
    }
}
