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
        }
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
