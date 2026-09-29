using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AdofaiModManager.Services;
using Wpf.Ui.Controls;

namespace AdofaiModManager.Views.Dialogs;

/// <summary>
/// 首次使用向导：① 游戏目录 → ② UMM 加载器 → ③ 资源站（可选）。
/// 每一步都可以跳过；完成后设置 HasCompletedSetup，之后可在「设置」里重新打开。
/// </summary>
public partial class FirstRunWizard : FluentWindow
{
    private int _step = 1;

    public FirstRunWizard()
    {
        InitializeComponent();

        var settings = AppServices.Settings.Settings;

        // 游戏目录：没设置过就自动检测
        if (string.IsNullOrWhiteSpace(settings.GamePath))
        {
            var best = AppServices.GameLocator.FindBest();
            if (best is not null)
            {
                settings.GamePath = best;
                AppServices.Settings.Save();
            }
        }

        GamePathBox.Text = settings.GamePath ?? string.Empty;
        ApiBaseBox.Text = settings.ApiBaseUrl;
        ApiKeyBox.Text = settings.ApiKey ?? string.Empty;

        SetStep(1);
    }

    private static LoaderService? CreateLoader()
    {
        var gamePath = AppServices.Settings.Settings.GamePath;
        if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
        {
            return null;
        }

        return new LoaderService(gamePath!);
    }

    private void SetStep(int step)
    {
        _step = step;

        Step1Panel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3Panel.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;

        Step1Label.Opacity = step == 1 ? 1 : 0.4;
        Step2Label.Opacity = step == 2 ? 1 : 0.4;
        Step3Label.Opacity = step == 3 ? 1 : 0.4;

        Step1Label.FontWeight = step == 1 ? FontWeights.SemiBold : FontWeights.Normal;
        Step2Label.FontWeight = step == 2 ? FontWeights.SemiBold : FontWeights.Normal;
        Step3Label.FontWeight = step == 3 ? FontWeights.SemiBold : FontWeights.Normal;

        BackButton.IsEnabled = step > 1;
        NextButton.Visibility = step < 3 ? Visibility.Visible : Visibility.Collapsed;
        FinishButton.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        SkipStepButton.Visibility = step < 3 ? Visibility.Visible : Visibility.Collapsed;

        StatusBar.IsOpen = false;

        if (step == 1)
        {
            UpdateGameStatus();
        }
        else if (step == 2)
        {
            RefreshLoaderStatus();
        }
    }

    private void Report(bool success, string message)
    {
        StatusBar.Severity = success ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        StatusBar.Title = success ? "提示" : "出错了";
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }

    // ---------------- 第 1 步：游戏目录 ----------------

    private void GamePathBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        AppServices.Settings.Settings.GamePath = GamePathBox.Text.Trim();
        AppServices.Settings.Save();
        UpdateGameStatus();
    }

    private void AutoDetect_Click(object sender, RoutedEventArgs e)
    {
        var best = AppServices.GameLocator.FindBest();
        if (best is null)
        {
            Report(false, "未能在 Steam 库中找到《冰与火之舞》，请手动选择游戏目录。");
            return;
        }

        GamePathBox.Text = best;
        Report(true, "已自动检测到游戏目录。");
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择《冰与火之舞》游戏目录" };
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
            ? $"✓ 已找到游戏主程序。Mods 目录：{(hasMods ? "存在" : "不存在（装过 mod 后会出现）")}"
            : "⚠ 该目录里没找到游戏主程序，请确认路径是否正确。";
    }

    // ---------------- 第 2 步：加载器 ----------------

    private void RefreshLoaderStatus()
    {
        var loader = CreateLoader();
        if (loader is null)
        {
            LoaderStatusText.Text = "⚠ 尚未设置游戏目录，请回到第 1 步。";
            InstallLoaderButton.IsEnabled = false;
            return;
        }

        var status = loader.Detect();
        if (!status.GameExists)
        {
            LoaderStatusText.Text = "⚠ 找不到游戏主程序，请回到第 1 步检查目录。";
            InstallLoaderButton.IsEnabled = false;
            return;
        }

        InstallLoaderButton.IsEnabled = true;

        if (status.IsInstalled)
        {
            LoaderStatusText.Text =
                "✓ 加载器已就绪，可以直接下一步。\n"
                + $"加载器版本：{status.LoaderVersion ?? "未知"}\n"
                + "注入方式：UnityDoorstop / DoorstopProxy";
        }
        else if (status.IsPartial)
        {
            LoaderStatusText.Text = "⚠ 检测到加载器不完整，建议点下面的按钮修复。";
        }
        else
        {
            LoaderStatusText.Text = "○ 尚未安装加载器。点下面的按钮即可装好（使用 amm 内置内核，无需联网）。";
        }
    }

    private async void InstallLoader_Click(object sender, RoutedEventArgs e)
    {
        var loader = CreateLoader();
        if (loader is null)
        {
            Report(false, "请先在第 1 步设置游戏目录。");
            return;
        }

        InstallLoaderButton.IsEnabled = false;
        try
        {
            var client = AppServices.CreateApiClient();
            var progress = new Progress<string>(message => Report(true, message));

            var result = await new KernelBootstrapper(loader).InstallBestAsync(client, progress);
            Report(result.Success, result.Message);
        }
        finally
        {
            InstallLoaderButton.IsEnabled = true;
            RefreshLoaderStatus();
        }
    }

    // ---------------- 第 3 步：资源站（可选） ----------------

    private void ApiBaseBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        AppServices.Settings.Settings.ApiBaseUrl = ApiBaseBox.Text.Trim();
        AppServices.Settings.Save();
    }

    private void ApiKeyBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        AppServices.Settings.Settings.ApiKey = ApiKeyBox.Text.Trim();
        AppServices.Settings.Save();
    }

    private async void TestApi_Click(object sender, RoutedEventArgs e)
    {
        var client = AppServices.CreateApiClient();
        if (client is null)
        {
            ApiStatusText.Text = "✗ 请先填写资源站地址。";
            return;
        }

        ApiStatusText.Text = "正在测试连接…";

        try
        {
            var page = await client.GetModsAsync(1, 1);
            ApiStatusText.Text = $"✓ 连接成功，资源站共有 {page.Total} 个资源。";
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
        catch
        {
            // 忽略
        }
    }

    // ---------------- 导航 ----------------

    private void Back_Click(object sender, RoutedEventArgs e) => SetStep(Math.Max(1, _step - 1));

    private void Next_Click(object sender, RoutedEventArgs e) => SetStep(Math.Min(3, _step + 1));

    private void SkipStep_Click(object sender, RoutedEventArgs e)
    {
        if (_step < 3)
        {
            SetStep(_step + 1);
        }
    }

    private void Finish_Click(object sender, RoutedEventArgs e)
    {
        AppServices.Settings.Settings.HasCompletedSetup = true;
        AppServices.Settings.Save();
        DialogResult = true;
    }
}
