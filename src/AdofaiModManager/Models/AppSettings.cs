namespace AdofaiModManager.Models;

/// <summary>
/// 用户配置，保存在 %AppData%\AdofaiModManager\settings.json
/// </summary>
public sealed class AppSettings
{
    /// <summary>游戏安装目录（含 A Dance of Fire and Ice.exe）</summary>
    public string? GamePath { get; set; }

    /// <summary>资源站接口地址</summary>
    public string ApiBaseUrl { get; set; } = "https://www.adofaitools.top";

    /// <summary>用户填写的 API key（仅本地保存）</summary>
    public string? ApiKey { get; set; }

    /// <summary>主题：System（跟随系统） / Light / Dark</summary>
    public string Theme { get; set; } = "System";

    /// <summary>记住上次停留的页面</summary>
    public string LastPage { get; set; } = "OnlineModsPage";

    /// <summary>手动指定的游戏版本（留空则自动检测）</summary>
    public string? GameVersionOverride { get; set; }

    /// <summary>是否已完成首次使用向导</summary>
    public bool HasCompletedSetup { get; set; }

    /// <summary>打开软件时检查更新</summary>
    public bool CheckUpdatesOnStartup { get; set; } = true;
}
