using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AdofaiModManager.Models;
using AdofaiModManager.Services;
using AdofaiModManager.Views.Dialogs;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace AdofaiModManager.Views.Pages;

public partial class InstalledModsPage : Page
{
    private readonly ObservableCollection<InstalledMod> _mods = [];

    private bool _busy;

    public InstalledModsPage()
    {
        InitializeComponent();
        ModsList.ItemsSource = _mods;
        Loaded += (_, _) =>
        {
            PageScrollFix.DisableOuterPageScrolling(this);
            Reload();
        };

        // 更新检查（含启动时的后台检查）完成后，列表同步刷新
        AppServices.Updates.Changed += OnUpdatesChanged;
        Unloaded += (_, _) => AppServices.Updates.Changed -= OnUpdatesChanged;
    }

    private void OnUpdatesChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(Reload);
            return;
        }

        Reload();
    }

    private static ModService? CreateService()
    {
        var gamePath = AppServices.Settings.Settings.GamePath;
        if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
        {
            return null;
        }

        return new ModService(gamePath!);
    }

    private void Reload()
    {
        _mods.Clear();

        var service = CreateService();
        if (service is null)
        {
            SummaryText.Text = "尚未设置游戏目录";
            ShowHint("还没有设置游戏目录。\n请到左侧「设置」里自动检测或手动选择《冰与火之舞》的安装目录。");
            return;
        }

        if (!Directory.Exists(service.ModsPath))
        {
            SummaryText.Text = $"没有找到 Mods 文件夹：{service.ModsPath}";
            ShowHint("游戏目录里还没有 Mods 文件夹。\n装过至少一个 mod（或运行过一次游戏）后就会出现。");
            return;
        }

        var updateService = new GitHubUpdateService(AppServices.UpdateSources);
        var gameVersion = AppServices.GameVersion;
        var hasCompatibilityInfo = false;

        var scanned = new List<InstalledMod>();

        foreach (var mod in service.Scan())
        {
            var source = updateService.ResolveSource(mod);
            mod.UpdateSourceLabel = source.Kind == UpdateSourceKind.None
                ? "更新源：未设置（可点「绑定 GitHub」手动指定）"
                : $"更新源：{source.Description}{(source.IsManual ? "（手动）" : string.Empty)}";

            if (AppServices.Updates.Get(mod.Id) is { } result)
            {
                var via = string.IsNullOrWhiteSpace(result.SourceLabel) ? string.Empty : $"（{result.SourceLabel}）";
                mod.UpdateStatus = result.Success
                    ? (result.UpdateAvailable
                        ? $"可更新 → {result.RemoteVersion}{via}"
                        : $"已是最新{via}")
                    : $"检查失败{via}";
                mod.HasUpdate = result.Success && result.UpdateAvailable;
                mod.RemoteVersion = result.RemoteVersion;
                mod.UpdateDownloadUrl = result.DownloadUrl;
                mod.UpdateFileName = result.FileName;
            }

            if (!string.IsNullOrWhiteSpace(mod.GameVersion) &&
                !string.IsNullOrWhiteSpace(gameVersion) &&
                !VersionsMatch(mod.GameVersion!, gameVersion!))
            {
                mod.CompatibilityWarning = $"⚠ 该 mod 要求游戏 {mod.GameVersion}，当前检测为 {gameVersion}";
                hasCompatibilityInfo = true;
            }

            scanned.Add(mod);
        }

        // 可更新的置顶（组内保持原顺序）
        foreach (var mod in scanned.OrderByDescending(m => m.HasUpdate))
        {
            _mods.Add(mod);
        }

        var enabled = _mods.Count(m => m.IsEnabled);
        var updatable = _mods.Count(m => m.HasUpdate);
        SummaryText.Text = $"共 {_mods.Count} 个 mod（{enabled} 个已启用"
            + (updatable > 0 ? $"，{updatable} 个可更新" : string.Empty)
            + "）"
            + (string.IsNullOrWhiteSpace(gameVersion) ? string.Empty : $"　·　游戏版本 {gameVersion}")
            + (service.IsLoaderInstalled ? string.Empty : "　·　⚠ 未检测到 UMM loader");

        if (_mods.Count == 0)
        {
            ShowHint("Mods 文件夹是空的。\n点右上角「安装本地 zip」安装一个 mod。");
        }
        else
        {
            HideHint();
        }

        if (hasCompatibilityInfo)
        {
            StatusBar.Severity = InfoBarSeverity.Warning;
            StatusBar.Title = "版本兼容提醒";
            StatusBar.Message = "有 mod 声明的游戏版本与当前检测不一致，详情见各条目的红色提示。";
            StatusBar.IsOpen = true;
        }

        if (Environment.GetEnvironmentVariable("AMM_SCROLL_DEBUG") == "1")
        {
            Dispatcher.BeginInvoke(
                new Action(() => File.AppendAllText(
                    Path.Combine(AppContext.BaseDirectory, "scroll-debug.log"),
                    $"Extent={ModsScroller.ExtentHeight} Viewport={ModsScroller.ViewportHeight} Scrollable={ModsScroller.ScrollableHeight}{Environment.NewLine}")),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private static bool VersionsMatch(string a, string b) =>
        KernelService.CompareVersions(a, b) == 0;

    private void ShowHint(string text)
    {
        EmptyHint.Text = text;
        EmptyHint.Visibility = Visibility.Visible;
    }

    private void HideHint() => EmptyHint.Visibility = Visibility.Collapsed;

    private void Report(bool success, string message)
    {
        StatusBar.Severity = success ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        StatusBar.Title = success ? "提示" : "出错了";
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    private void InstallZip_Click(object sender, RoutedEventArgs e)
    {
        var service = CreateService();
        if (service is null)
        {
            Report(false, "请先在「设置」里指定游戏目录。");
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "选择 UMM 格式的 mod 压缩包",
            Filter = "Mod 压缩包 (*.zip)|*.zip|所有文件 (*.*)|*.*",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var result = service.InstallFromZip(dialog.FileName);
        Report(result.Success, result.Message);
        Reload();
    }

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not InstalledMod mod)
        {
            return;
        }

        var service = CreateService();
        if (service is null)
        {
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"确定要卸载「{mod.DisplayName}」吗？\n这会删除文件夹：{mod.FolderPath}",
            "确认卸载",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (confirm != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        var result = service.Uninstall(mod);
        Report(result.Success, result.Message);
        AppServices.Updates.Clear(mod.Id);
        Reload();
    }

    private void ToggleEnabled_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox || checkBox.DataContext is not InstalledMod mod)
        {
            return;
        }

        var service = CreateService();
        if (service is null)
        {
            return;
        }

        var result = service.SetEnabled(mod, checkBox.IsChecked == true);
        Report(result.Success, result.Message);
        Reload();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not InstalledMod mod)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{mod.FolderPath}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Report(false, $"打开文件夹失败：{ex.Message}");
        }
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not InstalledMod mod)
        {
            return;
        }

        var updateService = new GitHubUpdateService(AppServices.UpdateSources);
        var source = updateService.ResolveSource(mod);

        if (source.Kind == UpdateSourceKind.None)
        {
            Report(false, $"{mod.DisplayName} 没有可用的更新源，请先「绑定 GitHub」。");
            return;
        }

        var result = await updateService.CheckAsync(source, mod.Id, mod.Version);
        AppServices.Updates.Set(mod.Id, result);
        Report(result.Success, result.Message);
        Reload();
    }

    private async void CheckAll_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        var service = CreateService();
        if (service is null)
        {
            Report(false, "请先在「设置」里指定游戏目录。");
            return;
        }

        var mods = service.Scan();

        _busy = true;
        CheckAllButton.IsEnabled = false;

        try
        {
            var progress = new Progress<string>(name =>
            {
                StatusBar.Severity = InfoBarSeverity.Informational;
                StatusBar.Title = "正在检查更新";
                StatusBar.Message = name;
                StatusBar.IsOpen = true;
            });

            await AppServices.Updates.CheckAllAsync(mods, progress);

            var updatable = mods.Count(m => AppServices.Updates.Get(m.Id) is { Success: true, UpdateAvailable: true });
            Report(true, $"检查完成：{mods.Count} 个 mod，{updatable} 个可更新。");
        }
        finally
        {
            _busy = false;
            CheckAllButton.IsEnabled = true;
            Reload();
        }
    }

    private static AdofaiToolsClient? BuildSiteClient()
    {
        return AppServices.CreateApiClient();
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not InstalledMod mod)
        {
            return;
        }

        var result = AppServices.Updates.Get(mod.Id);
        if (result is null || !result.UpdateAvailable)
        {
            Report(false, "请先「检查更新」。");
            return;
        }

        var service = CreateService();
        if (service is null)
        {
            Report(false, "请先在「设置」里指定游戏目录。");
            return;
        }

        _busy = true;
        StatusBar.Severity = InfoBarSeverity.Informational;
        StatusBar.Title = "正在更新";
        StatusBar.Message = $"{mod.DisplayName} → v{result.RemoteVersion}";
        StatusBar.IsOpen = true;

        try
        {
            var progress = new Progress<int>(percent =>
                StatusBar.Message = $"{mod.DisplayName} → v{result.RemoteVersion}　下载中 {percent}%");

            InstallResult install;

            if (!string.IsNullOrWhiteSpace(result.DownloadUrl))
            {
                install = await service.InstallFromUrlAsync(result.DownloadUrl, progress);
            }
            else if (!string.IsNullOrWhiteSpace(result.SiteSlug))
            {
                var client = BuildSiteClient();
                if (client is null)
                {
                    Report(false, "该更新来自资源站，但未配置 API key。");
                    return;
                }

                var detail = await client.GetModDetailAsync(result.SiteResourceType ?? "MOD", result.SiteSlug!);
                install = await new SiteInstaller(client, service).InstallAsync(detail, progress);
            }
            else
            {
                Report(false, "该更新没有可用的下载来源。");
                return;
            }

            Report(install.Success, install.Message);
            AppServices.Updates.Clear(mod.Id);
        }
        catch (AdofaiToolsException ex)
        {
            Report(false, ex.Message);
        }
        finally
        {
            _busy = false;
            Reload();
        }
    }

    private void BindGitHub_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not InstalledMod mod)
        {
            return;
        }

        var current = AppServices.UpdateSources.GetOverride(mod.Id)
                      ?? mod.Repository
                      ?? mod.HomePage
                      ?? string.Empty;

        var dialog = new TextInputDialog(
            "绑定 GitHub 更新源",
            $"为「{mod.DisplayName}」指定 GitHub 仓库地址，或 Repository.json 的直链。\n"
            + "例如：https://github.com/作者/仓库\n"
            + "留空并确定可清除手动设置，恢复自动识别。",
            current)
        {
            Owner = Window.GetWindow(this),
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        AppServices.UpdateSources.SetOverride(mod.Id, dialog.InputText);
        AppServices.Updates.Clear(mod.Id);
        Reload();
    }
}
