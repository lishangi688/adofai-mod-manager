using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AdofaiModManager.Services;
using AdofaiModManager.Views.Dialogs;
using Wpf.Ui.Controls;

namespace AdofaiModManager.Views.Pages;

public partial class LoaderPage : Page
{
    private bool _busy;

    public LoaderPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            PageScrollFix.DisableOuterPageScrolling(this);
            ApplySteamIcon();
            Refresh();
        };
    }

    /// <summary>
    /// 能取到本机 Steam 图标就换到按钮上（取不到就保留默认的播放图标）。
    /// 图标会转成单色并用当前主题的文字色，和界面其它图标保持一致。
    /// </summary>
    private void ApplySteamIcon()
    {
        var tint = (TryFindResource("TextFillColorPrimaryBrush") as SolidColorBrush)?.Color
                   ?? Colors.White;

        if (SteamIconProvider.Get(tint) is not { } steamIcon)
        {
            return;
        }

        var element = new ImageIcon
        {
            Source = steamIcon,
            Width = 18,
            Height = 18,
        };

        // 大图缩小到 18px 时用高质量缩放，边缘更干净
        RenderOptions.SetBitmapScalingMode(element, BitmapScalingMode.HighQuality);

        LaunchSteamButton.Icon = element;
    }

    private LoaderService? CreateLoader()
    {
        var gamePath = AppServices.Settings.Settings.GamePath;
        if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
        {
            return null;
        }

        return new LoaderService(gamePath);
    }

    private void Refresh()
    {
        var loader = CreateLoader();
        if (loader is null)
        {
            StateText.Text = "⚠ 尚未设置游戏目录";
            DetailText.Text = "请到「设置」里指定《冰与火之舞》的安装目录。";
            InstallButton.IsEnabled = false;
            UninstallButton.IsEnabled = false;
            RollbackButton.IsEnabled = false;
            CheckUpdateButton.IsEnabled = false;
            return;
        }

        var status = loader.Detect();
        var kernel = new KernelService(loader);
        var deployed = kernel.GetDeployedVersion();
        var kernels = kernel.GetLocalKernels();
        var bundled = kernel.GetBundled();

        if (!status.GameExists)
        {
            StateText.Text = "⚠ 找不到游戏主程序";
            DetailText.Text = $"路径：{loader.ExecutablePath}";
            InstallButton.IsEnabled = false;
            UninstallButton.IsEnabled = false;
            RollbackButton.IsEnabled = false;
            CheckUpdateButton.IsEnabled = false;
            return;
        }

        var lines = new List<string>
        {
            $"游戏目录：{loader.GamePath}",
            $"游戏架构：{(status.Is64BitGame ? "64 位" : "32 位")}",
            $"Mods 目录：{(Directory.Exists(Path.Combine(loader.GamePath, "Mods")) ? "存在" : "不存在")}",
            string.Empty,
            $"内置内核：{(bundled is null ? "缺失" : bundled.Version)}",
            $"已部署内核：{deployed ?? "未部署"}",
        };

        if (kernels.Count > 1)
        {
            lines.Add("本地内核包：" + string.Join("、", kernels.Select(k => $"{k.Version}({k.Source})")));
        }

        if (status.IsInstalled)
        {
            StateText.Text = "✓ 加载器已安装（DoorstopProxy）";
            lines.Insert(3, $"注入方式：UnityDoorstop / DoorstopProxy    winhttp.dll：已部署    doorstop_config.ini：已部署");
        }
        else if (status.IsPartial)
        {
            StateText.Text = "⚠ 加载器不完整";
            lines.Insert(1, "检测到部分加载器文件，建议点「安装 / 修复加载器」补齐。");
        }
        else
        {
            StateText.Text = "○ 尚未安装加载器";
            lines.Insert(1, "点击「安装 / 修复加载器」即可为游戏装上 mod 支持。");
        }

        if (status.LegacyBackups.Length > 0)
        {
            lines.Add(string.Empty);
            lines.Add($"⚠ 检测到 {status.LegacyBackups.Length} 个旧版注入残留（Assembly 方式）："
                + string.Join("、", status.LegacyBackups.Select(Path.GetFileName)));
            lines.Add("建议先卸载加载器清理，再重新安装。");
        }

        DetailText.Text = string.Join(Environment.NewLine, lines);

        var hasAnything = status.LoaderFolderExists || status.WinhttpExists || status.DoorstopConfigExists;

        InstallButton.IsEnabled = !_busy;
        UninstallButton.IsEnabled = !_busy && hasAnything;
        RollbackButton.IsEnabled = !_busy && kernels.Count > 1;
        CheckUpdateButton.IsEnabled = !_busy;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        InstallButton.IsEnabled = !busy;
        UninstallButton.IsEnabled = !busy;
        RollbackButton.IsEnabled = !busy;
        CheckUpdateButton.IsEnabled = !busy;
    }

    private void Report(InstallResult result)
    {
        StatusBar.Severity = result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        StatusBar.Title = result.Success ? "操作成功" : "操作失败";
        StatusBar.Message = result.Message;
        StatusBar.IsOpen = true;
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        var loader = CreateLoader();
        if (loader is null)
        {
            Report(false, "请先在「设置」里指定游戏目录。");
            return;
        }

        var choice = new KernelSourceDialog { Owner = Window.GetWindow(this) };
        if (choice.ShowDialog() != true)
        {
            return;
        }

        if (choice.Choice == KernelSourceChoice.ImportZip)
        {
            await ImportKernelZipAsync(loader);
        }
        else if (choice.Choice == KernelSourceChoice.UseBundled)
        {
            SetBusy(true);
            try
            {
                var result = new KernelBootstrapper(loader).InstallBundled();
                Report(result.Success, result.Message);
            }
            finally
            {
                SetBusy(false);
                Refresh();
            }
        }
        else
        {
            await FetchOrBundledAsync(loader);
        }
    }

    /// <summary>从资源站取最新内核；没有更新或失败时用内置内核。</summary>
    private async Task FetchOrBundledAsync(LoaderService loader)
    {
        SetBusy(true);
        try
        {
            // 优先用资源站上更新的内核，否则用内置内核
            var client = BuildClient(out _);
            var progress = new Progress<string>(message => Report(true, message));

            var result = await new KernelBootstrapper(loader).InstallBestAsync(client, progress);
            Report(result.Success, result.Message);
        }
        finally
        {
            SetBusy(false);
            Refresh();
        }
    }

    /// <summary>从资源站检查内核是否有更新（只查询，不改动）。</summary>
    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        var loader = CreateLoader();
        if (loader is null)
        {
            Report(false, "请先在「设置」里指定游戏目录。");
            return;
        }

        var client = BuildClient(out var error);
        if (client is null)
        {
            Report(false, error);
            return;
        }

        var kernel = new KernelService(loader);
        SetBusy(true);

        try
        {
            var current = kernel.GetDeployedVersion() ?? kernel.GetBundled()?.Version;

            Report(true, "正在从资源站检查内核更新…");
            var site = await KernelBootstrapper.FindLatestSiteKernelAsync(client);

            if (site is null)
            {
                Report(false, "资源站上没有找到 UnityModManager。");
                return;
            }

            if (current is not null && KernelService.CompareVersions(site.Value.VersionId, current) <= 0)
            {
                Report(true, $"内核已是最新：当前 {current}，资源站最新 {site.Value.VersionId}。");
                return;
            }

            var confirm = System.Windows.MessageBox.Show(
                $"资源站有更新的内核 {site.Value.VersionId}（当前：{current ?? "未部署"}）。\n\n是否下载并更新？\n（升级前会自动备份当前内核，可回滚）",
                "内核更新",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (confirm != System.Windows.MessageBoxResult.Yes)
            {
                return;
            }

            var intent = await client.CreateToolDownloadIntentAsync(site.Value.FileId);
            var (imported, message) = await kernel.ImportAsync(intent.Url, site.Value.VersionId);
            if (imported is null)
            {
                Report(false, message);
                return;
            }

            Report(kernel.Deploy(imported));
        }
        catch (AdofaiToolsException ex)
        {
            Report(false, ex.Message);
        }
        finally
        {
            SetBusy(false);
            Refresh();
        }
    }

    private void Rollback_Click(object sender, RoutedEventArgs e)
    {
        var loader = CreateLoader();
        if (loader is null)
        {
            return;
        }

        var kernel = new KernelService(loader);
        var current = kernel.GetDeployedVersion();

        var confirm = System.Windows.MessageBox.Show(
            $"确定要回滚内核吗？\n当前已部署：{current ?? "未部署"}\n\n会切换到本地保存的上一个内核版本（同样会先备份当前版本）。",
            "回滚内核",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (confirm != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        Report(kernel.Rollback());
        Refresh();
    }

    private void Report(bool success, string message)
    {
        StatusBar.Severity = success ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        StatusBar.Title = success ? "提示" : "出错了";
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }

    private static AdofaiToolsClient? BuildClient(out string error)
    {
        error = string.Empty;
        var client = AppServices.CreateApiClient();

        if (client is null)
        {
            error = "尚未配置资源站地址。请到「设置」里填写。";
        }

        return client;
    }

    /// <summary>导入本地 UnityModManager 压缩包（例如从 Nexus Mods 下载的）。</summary>
    private async Task ImportKernelZipAsync(LoaderService loader)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 UnityModManager 压缩包（可从 Nexus Mods 下载）",
            Filter = "压缩包 (*.zip)|*.zip|所有文件 (*.*)|*.*",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var kernel = new KernelService(loader);
        SetBusy(true);

        try
        {
            Report(true, "正在导入内核…");
            var (imported, message) = await kernel.ImportAsync(dialog.FileName);
            if (imported is null)
            {
                Report(false, message);
                return;
            }

            Report(kernel.Deploy(imported));
        }
        finally
        {
            SetBusy(false);
            Refresh();
        }
    }

    private void Uninstall_Click(object sender, RoutedEventArgs e)    {
        var loader = CreateLoader();
        if (loader is null)
        {
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            "确定要卸载游戏加载器吗？\n这会移除 winhttp.dll、doorstop_config.ini 和 UnityModManager 目录。\n（Mods 文件夹里的 mod 不会被删除）",
            "确认卸载加载器",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (confirm != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        Report(loader.Uninstall());
        Refresh();
    }

    private void OpenGameFolder_Click(object sender, RoutedEventArgs e)
    {
        var loader = CreateLoader();
        if (loader is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{loader.GamePath}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Report(new InstallResult(false, $"打开目录失败：{ex.Message}"));
        }
    }

    private void Launch_Click(object sender, RoutedEventArgs e)
    {
        var loader = CreateLoader();
        if (loader is null)
        {
            Report(new InstallResult(false, "请先在「设置」里指定游戏目录。"));
            return;
        }

        try
        {
            loader.LaunchGame();
            Report(new InstallResult(true, "已请求通过 Steam 启动《冰与火之舞》，请稍候。"));
        }
        catch (Exception ex)
        {
            Report(new InstallResult(false, $"启动失败：{ex.Message}"));
        }
    }

    /// <summary>直接运行游戏 exe（不经 Steam）：更快，但成就/云存档可能不同步。</summary>
    private void LaunchDirect_Click(object sender, RoutedEventArgs e)
    {
        var loader = CreateLoader();
        if (loader is null)
        {
            Report(new InstallResult(false, "请先在「设置」里指定游戏目录。"));
            return;
        }

        try
        {
            loader.LaunchGameDirectly();
            Report(new InstallResult(true, "已直接启动游戏（未经 Steam）。成就 / 云存档可能不同步。"));
        }
        catch (Exception ex)
        {
            Report(new InstallResult(false, $"启动失败：{ex.Message}"));
        }
    }
}
