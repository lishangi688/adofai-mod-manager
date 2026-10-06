using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AdofaiModManager.Services;
using Microsoft.Win32;

namespace AdofaiModManager.Views.Pages;

public partial class SettingsPage : Page
{
    /// <summary>
    /// 是否在设置里显示「重新运行首次使用向导」。
    ///
    /// 公开发布版本**默认不显示**（普通用户不需要，向导只在首次启动出现）。
    /// 开发调试或录制素材时，设置环境变量 <c>AMM_SHOW_WIZARD_BUTTON=1</c> 启动即可打开，
    /// 这样就不需要改代码、也不会把调试开关误提交进仓库。
    /// </summary>
    private static readonly bool ShowRerunWizard =
        string.Equals(
            Environment.GetEnvironmentVariable("AMM_SHOW_WIZARD_BUTTON"),
            "1",
            StringComparison.Ordinal);

    private readonly bool _ready;

    public SettingsPage()
    {
        InitializeComponent();

        var settings = AppServices.Settings.Settings;

        GamePathBox.Text = settings.GamePath ?? string.Empty;
        GameVersionBox.Text = settings.GameVersionOverride ?? string.Empty;
        ApiBaseUrlBox.Text = settings.ApiBaseUrl;
        ApiKeyBox.Password = settings.ApiKey ?? string.Empty;
        ConfigPathText.Text = $"配置文件：{AppServices.Settings.ConfigFilePath}";

        switch ((settings.Theme ?? ThemeService.System).ToLowerInvariant())
        {
            case "light":
                ThemeLight.IsChecked = true;
                break;
            case "dark":
                ThemeDark.IsChecked = true;
                break;
            default:
                ThemeSystem.IsChecked = true;
                break;
        }

        RerunWizardPanel.Visibility = ShowRerunWizard ? Visibility.Visible : Visibility.Collapsed;

        AppVersionText.Text = $"AMM 版本 v{AppUpdateService.CurrentVersion}";

        CheckOnStartupBox.IsChecked = settings.CheckUpdatesOnStartup;
        CheckGitHubBox.IsChecked = settings.CheckGitHubUpdates;

        UpdateGameStatus();
        UpdateGameVersionStatus();

        _ready = true;

        Loaded += (_, _) => PageScrollFix.DisableOuterPageScrolling(this);
    }

    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready || sender is not RadioButton radio || radio.Tag is not string tag)
        {
            return;
        }

        AppServices.Settings.Settings.Theme = tag;
        AppServices.Settings.Save();
        ThemeService.Apply(tag, Window.GetWindow(this));
    }

    // ---------------- 游戏目录 / 版本 ----------------

    private void GamePathBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }

        AppServices.Settings.Settings.GamePath = GamePathBox.Text.Trim();
        AppServices.Settings.Save();
        UpdateGameStatus();
        UpdateGameVersionStatus();
    }

    private void GameVersionBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }

        AppServices.Settings.Settings.GameVersionOverride = GameVersionBox.Text.Trim();
        AppServices.Settings.Save();
        UpdateGameVersionStatus();
    }

    private void AutoDetectButton_Click(object sender, RoutedEventArgs e)
    {
        var best = AppServices.GameLocator.FindBest();
        if (best is null)
        {
            GameStatusText.Text = "未能在 Steam 库中找到《冰与火之舞》，请手动选择游戏目录。";
            return;
        }

        GamePathBox.Text = best;
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择《冰与火之舞》游戏目录" };
        if (dialog.ShowDialog() == true)
        {
            GamePathBox.Text = dialog.FolderName;
        }
    }

    private void UpdateGameStatus()
    {
        var path = AppServices.Settings.Settings.GamePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            GameStatusText.Text = "尚未设置游戏目录。";
            return;
        }

        if (!Directory.Exists(path))
        {
            GameStatusText.Text = "⚠ 该目录不存在。";
            return;
        }

        var hasExe = File.Exists(Path.Combine(path, SteamGameLocator.GameExeName));
        var hasMods = Directory.Exists(Path.Combine(path, "Mods"));

        GameStatusText.Text = hasExe
            ? $"✓ 已找到游戏主程序。Mods 目录：{(hasMods ? "存在" : "不存在（可能尚未安装过 mod）")}"
            : "⚠ 该目录里没找到游戏主程序，请确认路径是否正确。";
    }

    private void UpdateGameVersionStatus()
    {
        var path = AppServices.Settings.Settings.GamePath;
        var manual = AppServices.Settings.Settings.GameVersionOverride;

        var detected = string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)
            ? null
            : GameVersionReader.TryRead(path!);

        if (!string.IsNullOrWhiteSpace(manual))
        {
            GameVersionStatusText.Text = detected is null
                ? $"✓ 当前使用：{manual}（手动指定）"
                : $"✓ 当前使用：{manual}（手动指定）　·　自动检测为 {detected}";
            return;
        }

        GameVersionStatusText.Text = detected is null
            ? "未能自动识别游戏版本（不影响使用，也可以在上面手动填写）。"
            : $"✓ 已识别游戏版本：{detected}（自动检测）";
    }

    // ---------------- 资源站 ----------------

    private void ApiBaseUrlBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }

        AppServices.Settings.Settings.ApiBaseUrl = ApiBaseUrlBox.Text.Trim();
        AppServices.Settings.Save();
    }

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }

        AppServices.Settings.Settings.ApiKey = ApiKeyBox.Password.Trim();
        AppServices.Settings.Save();
    }

    private async void TestApi_Click(object sender, RoutedEventArgs e)
    {
        var client = AppServices.CreateApiClient();
        if (client is null)
        {
            ApiStatusText.Text = "✗ 请先填写站点地址。";
            return;
        }

        ApiStatusText.Text = "正在测试连接…";

        try
        {
            var page = await client.GetModsAsync(1, 1);
            ApiStatusText.Text = $"✓ 连接成功，ADOFAITools 共有 {page.Total} 个资源。";
        }
        catch (AdofaiToolsException ex)
        {
            ApiStatusText.Text = "✗ " + ex.Message;
        }
    }

    private void OpenSite_Click(object sender, RoutedEventArgs e)
    {
        var url = AppServices.Settings.Settings.ApiBaseUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ApiStatusText.Text = "✗ 打开链接失败：" + ex.Message;
        }
    }

    // ---------------- 更新检查 ----------------

    private void CheckOnStartup_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }

        AppServices.Settings.Settings.CheckUpdatesOnStartup = CheckOnStartupBox.IsChecked == true;
        AppServices.Settings.Save();
    }

    private void CheckGitHub_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }

        AppServices.Settings.Settings.CheckGitHubUpdates = CheckGitHubBox.IsChecked == true;
        AppServices.Settings.Save();
    }

    // ---------------- 关于 ----------------

    private AppUpdateInfo? _pendingUpdate;

    private async void CheckAppUpdate_Click(object sender, RoutedEventArgs e)
    {
        AppUpdateStatusText.Text = "正在检查…";

        var info = await AppUpdateService.CheckAsync();

        if (info is null)
        {
            _pendingUpdate = null;
            AppUpdateNowButton.Visibility = Visibility.Collapsed;
            AppUpdateStatusText.Text = $"✓ 已是最新版本（v{AppUpdateService.CurrentVersion}）";
            return;
        }

        _pendingUpdate = info;
        AppUpdateNowButton.Visibility = Visibility.Visible;
        AppUpdateStatusText.Text = $"发现新版本 v{info.Version}（来源：{info.SourceLabel}）。";

        // 资源站能给出免鉴权直链时，优先引导走资源站（国内速度稳定）
        var canDownloadFromSite = info.UrlFor(AppUpdateService.Distribution) is not null;

        var text =
            $"发现 AMM 新版本 v{info.Version}（来源：{info.SourceLabel}）\n"
            + $"当前版本：v{AppUpdateService.CurrentVersion}\n\n"
            + AppUpdateService.DistributionHint + "\n\n";

        if (canDownloadFromSite)
        {
            text += "· 「是」= 立即更新（自动下载；绿色版自动替换并重启，安装版会打开安装程序）\n"
                    + "· 「否」= 打开发布页，自己下载\n"
                    + "· 「取消」= 稍后再更新";
        }
        else
        {
            text += "是否打开下载页？\n"
                    + "（安装版直接运行安装包覆盖安装；绿色版解压替换即可。）";
        }

        var choice = System.Windows.MessageBox.Show(
            text,
            "发现新版本",
            canDownloadFromSite
                ? System.Windows.MessageBoxButton.YesNoCancel
                : System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Information);

        if (choice == System.Windows.MessageBoxResult.Yes && canDownloadFromSite)
        {
            AppUpdateNow_Click(sender, e);
            return;
        }

        var target = choice == System.Windows.MessageBoxResult.Yes
            ? info.PageUrl
            : choice == System.Windows.MessageBoxResult.No && canDownloadFromSite
                ? AppUpdateService.ReleasesPageUrl
                : null;

        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            AppUpdateStatusText.Text = $"已打开下载页，请下载 v{info.Version}。";
        }
        catch (Exception ex)
        {
            AppUpdateStatusText.Text = "✗ 打开链接失败：" + ex.Message;
        }
    }

    /// <summary>下载新版本并自动替换（绿色版解压覆盖；安装版调用安装程序）。</summary>
    private async void AppUpdateNow_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate is null)
        {
            AppUpdateStatusText.Text = "请先点「检查更新」。";
            return;
        }

        AppUpdateNowButton.IsEnabled = false;
        AppUpdateProgress.Visibility = Visibility.Visible;
        AppUpdateProgress.IsIndeterminate = true;

        try
        {
            var progress = new InlineProgress<string>(
                text => AppUpdateStatusText.Text = text,
                Dispatcher);

            var result = await AppSelfUpdater.ApplyAsync(
                _pendingUpdate,
                AppUpdateService.Distribution,
                progress);

            AppUpdateProgress.IsIndeterminate = false;
            AppUpdateStatusText.Text = result.Message;

            if (result.ShouldExit)
            {
                AppUpdateProgress.Value = 100;
                await Task.Delay(1500);

                // 退出程序，把"替换文件"交给更新脚本 / 安装程序
                Application.Current.Shutdown();
                return;
            }
        }
        finally
        {
            AppUpdateNowButton.IsEnabled = true;
        }
    }

    // ---------------- 其它 ----------------

    private void RerunWizard_Click(object sender, RoutedEventArgs e)
    {
        var wizard = new Views.Dialogs.FirstRunWizard { Owner = Window.GetWindow(this) };
        wizard.ShowDialog();

        // 向导里可能改了游戏目录 / 资源站，刷新一下界面
        var settings = AppServices.Settings.Settings;
        GamePathBox.Text = settings.GamePath ?? string.Empty;
        ApiBaseUrlBox.Text = settings.ApiBaseUrl;
        ApiKeyBox.Password = settings.ApiKey ?? string.Empty;

        UpdateGameStatus();
        UpdateGameVersionStatus();
    }
}
