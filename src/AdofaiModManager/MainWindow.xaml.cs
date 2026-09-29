using System.IO;
using System.Windows;
using AdofaiModManager.Services;
using AdofaiModManager.Views.Pages;
using Wpf.Ui.Controls;

namespace AdofaiModManager;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();

        RootNavigation.SetPageProviderService(
            new NavigationViewPageProvider(type => Activator.CreateInstance(type)));

        AppServices.Updates.Changed += OnUpdatesChanged;
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;

        var page = AppServices.Settings.Settings.LastPage switch
        {
            nameof(InstalledModsPage) => typeof(InstalledModsPage),
            nameof(FavoritesPage) => typeof(FavoritesPage),
            nameof(LoaderPage) => typeof(LoaderPage),
            nameof(SettingsPage) => typeof(SettingsPage),
            _ => typeof(OnlineModsPage),
        };

        RootNavigation.Navigate(page, null);

        if (AppServices.Settings.Settings.CheckUpdatesOnStartup)
        {
            await RunStartupUpdateCheckAsync();
            await RunStartupAppUpdateCheckAsync();
        }
    }

    private AppUpdateInfo? _appUpdate;

    /// <summary>启动时静默检查 AMM 自身有没有新版本（有新版本才显示提示条）。</summary>
    private async Task RunStartupAppUpdateCheckAsync()
    {
        try
        {
            var info = await AppUpdateService.CheckAsync();
            if (info is null)
            {
                return;
            }

            // 用户点过「忽略此版本」就不再提示（换新版本还会提示）
            if (string.Equals(
                    AppServices.Settings.Settings.SkippedAppVersion,
                    info.Version,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _appUpdate = info;
            AppUpdateText.Text =
                $"新版本 v{info.Version}（来源：{info.SourceLabel}）　·　当前 v{AppUpdateService.CurrentVersion}";
            AppUpdateBar.IsOpen = true;
        }
        catch
        {
            // 自身更新检查失败不打扰用户
        }
    }

    private void OpenAppRelease_Click(object sender, RoutedEventArgs e)
    {
        if (_appUpdate is null)
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(_appUpdate.PageUrl) { UseShellExecute = true });
        }
        catch
        {
            // 打开失败就算了
        }

        AppUpdateBar.IsOpen = false;
    }

    private void SkipAppVersion_Click(object sender, RoutedEventArgs e)
    {
        if (_appUpdate is not null)
        {
            AppServices.Settings.Settings.SkippedAppVersion = _appUpdate.Version;
            AppServices.Settings.Save();
        }

        AppUpdateBar.IsOpen = false;
    }

    private async Task RunStartupUpdateCheckAsync()
    {
        try
        {
            var gamePath = AppServices.Settings.Settings.GamePath;
            if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
            {
                return;
            }

            var mods = new ModService(gamePath).Scan();
            if (mods.Count == 0)
            {
                return;
            }

            await AppServices.Updates.CheckAllAsync(mods);
        }
        catch
        {
            // 启动检查失败不影响使用
        }
    }

    private void OnUpdatesChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(UpdateBadge);
            return;
        }

        UpdateBadge();
    }

    private void UpdateBadge()
    {
        var count = AppServices.Updates.UpdatableCount;

        InstalledNavItem.InfoBadge = count > 0
            ? new InfoBadge { Value = count.ToString(), Severity = InfoBadgeSeverity.Attention }
            : null;
    }

    private void RootNavigation_SelectionChanged(NavigationView sender, RoutedEventArgs args)
    {
        if (sender.SelectedItem is INavigationViewItem item && item.TargetPageType is { } pageType)
        {
            sender.Navigate(pageType, null);

            AppServices.Settings.Settings.LastPage = pageType.Name;
            AppServices.Settings.Save();
        }
    }
}
