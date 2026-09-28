using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AdofaiModManager.Models;
using AdofaiModManager.Services;
using Wpf.Ui.Controls;

namespace AdofaiModManager.Views.Pages;

public partial class OnlineModsPage : Page
{
    private readonly ObservableCollection<ModListItem> _mods = [];

    private readonly HashSet<string> _installedIds = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, string> _installedVersions = new(StringComparer.OrdinalIgnoreCase);

    private readonly bool _ready;

    private bool _busy;

    private int _page = 1;

    private int _total;

    private readonly int _pageSize = 20;

    private ModDetail? _detail;

    public OnlineModsPage()
    {
        InitializeComponent();

        ModList.ItemsSource = _mods;
        SortCombo.SelectedIndex = 0;
        TypeCombo.SelectedIndex = 0;

        _ready = true;

        if (AppPaths.DebugLogEnabled)
        {
            ModList.PreviewMouseLeftButtonDown += (_, _) => DebugLog("ListBox 收到 PreviewMouseLeftButtonDown");
            PreviewMouseLeftButtonDown += (_, e) =>
            {
                var p = e.GetPosition(this);
                var sp = PointToScreen(p);
                DebugLog($"Page 点击：页面逻辑=({p.X:0},{p.Y:0}) 屏幕=({sp.X:0},{sp.Y:0}) 源={e.OriginalSource?.GetType().Name}");
            };

            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    try
                    {
                        var tl = ModList.PointToScreen(new Point(0, 0));
                        DebugLog($"ListBox 屏幕范围 左上=({tl.X:0},{tl.Y:0}) 尺寸={ModList.ActualWidth:0}x{ModList.ActualHeight:0}");
                    }
                    catch
                    {
                        // 忽略
                    }
                }),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        Loaded += (_, _) =>
        {
            PageScrollFix.DisableOuterPageScrolling(this);
            if (_mods.Count == 0)
            {
                _ = SearchAsync(1);
            }
        };
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

    private static ModService? BuildModService()
    {
        var gamePath = AppServices.Settings.Settings.GamePath;
        if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
        {
            return null;
        }

        return new ModService(gamePath);
    }

    private void LoadInstalled()
    {
        _installedIds.Clear();
        _installedVersions.Clear();

        var service = BuildModService();
        if (service is null)
        {
            return;
        }

        foreach (var mod in service.Scan())
        {
            _installedIds.Add(mod.Id);
            if (!string.IsNullOrWhiteSpace(mod.Version))
            {
                _installedVersions[mod.Id] = mod.Version!;
            }
        }
    }

    private void ApplyLocalState(ModListItem item)
    {
        var ummId = AppServices.InstallMap.GetUmmId(item.Id);

        string? matched = null;
        if (ummId is not null && _installedIds.Contains(ummId))
        {
            matched = ummId;
        }
        else if (_installedIds.Contains(item.DisplayName))
        {
            matched = item.DisplayName;
        }

        if (matched is null)
        {
            item.LocalState = null;
            return;
        }

        var local = _installedVersions.TryGetValue(matched, out var version) ? version : null;
        var remote = item.LatestVersion?.VersionId;

        item.LocalState = local is not null && remote is not null &&
                          KernelService.CompareVersions(remote, local) > 0
            ? "可更新"
            : "已安装";
    }

    private async Task SearchAsync(int page)
    {
        if (_busy)
        {
            return;
        }

        var client = BuildClient(out var error);
        if (client is null)
        {
            Report(false, error);
            SubtitleText.Text = error;
            _mods.Clear();
            UpdatePager();
            return;
        }

        SetBusy(true);
        try
        {
            LoadInstalled();

            var sort = (SortCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            var type = (TypeCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            var search = SearchBox.Text;

            var result = await client.GetModsAsync(page, _pageSize, search, type, null, sort);

            _page = result.Page <= 0 ? page : result.Page;
            _total = result.Total;

            _mods.Clear();
            foreach (var item in result.Items)
            {
                ApplyLocalState(item);
            }

            // 可更新的置顶（组内保持接口原顺序）
            foreach (var item in result.Items.OrderByDescending(i => i.LocalState == "可更新" ? 1 : 0))
            {
                _mods.Add(item);
            }

            UpdatePager();
            _ = LoadIconsAsync(_mods.ToList());

            if (_mods.Count > 0)
            {
                ModList.SelectedIndex = 0;
            }

            SubtitleText.Text = _mods.Count == 0
                ? "没有找到匹配的 mod。"
                : $"资源站共 {result.Total} 个资源。";
        }
        catch (AdofaiToolsException ex)
        {
            Report(false, ex.Message);
            SubtitleText.Text = ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static async Task LoadIconsAsync(IReadOnlyCollection<ModListItem> items)
    {
        var tasks = items.Select(async item =>
        {
            var source = await ImageLoader.LoadAsync(item.IconUrl);
            if (source is not null)
            {
                item.IconSource = source;
            }
        });

        await Task.WhenAll(tasks);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        UpdatePager();
    }

    private void UpdatePager()
    {
        var pages = _pageSize > 0 ? (int)Math.Ceiling(_total / (double)_pageSize) : 1;
        if (pages < 1)
        {
            pages = 1;
        }

        PageText.Text = $"第 {_page} / {pages} 页（共 {_total} 个）";
        PrevButton.IsEnabled = !_busy && _page > 1;
        NextButton.IsEnabled = !_busy && _page < pages;
    }

    private void Report(bool success, string message)
    {
        StatusBar.Severity = success ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        StatusBar.Title = success ? "提示" : "出错了";
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }

    private int _detailRequestId;

    private static void DebugLog(string message) => AppPaths.AppendDebugLog("[detail] " + message);

    private async void ModList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModList.SelectedItem is not ModListItem item)
        {
            DebugLog("SelectionChanged: 选中项不是 ModListItem");
            return;
        }

        var requestId = ++_detailRequestId;
        DebugLog($"SelectionChanged -> {item.DisplayName} (req={requestId}, type={item.ResourceType}, slug={item.Slug})");

        var client = BuildClient(out var error);
        if (client is null)
        {
            Report(false, error);
            return;
        }

        try
        {
            var detail = await client.GetModDetailAsync(item.ResourceType ?? "MOD", item.Slug);

            if (requestId != _detailRequestId)
            {
                DebugLog($"忽略过期请求 req={requestId}（当前 {_detailRequestId}）");
                return;
            }

            _detail = detail;
            DetailPanel.DataContext = detail;
            DetailEmpty.Visibility = Visibility.Collapsed;
            DetailScroll.Visibility = Visibility.Visible;

            DetailIcon.Source = item.IconSource ?? await ImageLoader.LoadAsync(detail.IconUrl);

            PopulateVersions(detail);

            InstallStatusText.Text = string.Empty;
            InstallButton.IsEnabled = true;
            InstallButton.Content = item.LocalState switch
            {
                "可更新" => "更新",
                "已安装" => "重新安装",
                _ => "安装",
            };
            UpdateFavoriteButton(detail.Id);
            DebugLog($"详情已加载 -> {detail.DisplayName}");
        }
        catch (AdofaiToolsException ex)
        {
            DebugLog("AdofaiToolsException: " + ex.Message);
            Report(false, ex.Message);
        }
        catch (Exception ex)
        {
            DebugLog("Exception: " + ex);
            Report(false, $"加载详情失败：{ex.Message}");
        }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_detail is null)
        {
            return;
        }

        var client = BuildClient(out var error);
        if (client is null)
        {
            Report(false, error);
            return;
        }

        var service = BuildModService();
        if (service is null)
        {
            Report(false, "请先在「设置」里指定游戏目录。");
            return;
        }

        var file = PreferredFileOf(VersionCombo.SelectedItem as ModVersion) ?? _detail.PreferredFile;
        if (file is null)
        {
            Report(false, "该 mod 没有可下载的文件。");
            return;
        }

        SetBusy(true);
        InstallButton.IsEnabled = false;
        InstallProgress.Visibility = Visibility.Visible;
        InstallProgress.Value = 0;

        try
        {
            InstallStatusText.Text = "正在获取下载地址…";

            var progress = new Progress<int>(percent =>
            {
                InstallProgress.Value = percent;
                InstallStatusText.Text = $"正在下载… {percent}%";
            });

            var installer = new SiteInstaller(client, service);
            var result = await installer.InstallAsync(_detail, file, progress);

            Report(result.Success, result.Message);
            InstallStatusText.Text = result.Message;

            LoadInstalled();
            foreach (var item in _mods)
            {
                ApplyLocalState(item);
            }

            InstallButton.Content = result.Success ? "重新安装" : "安装";
        }
        catch (AdofaiToolsException ex)
        {
            Report(false, ex.Message);
            InstallStatusText.Text = ex.Message;
        }
        finally
        {
            InstallProgress.Visibility = Visibility.Collapsed;
            InstallButton.IsEnabled = true;
            SetBusy(false);
        }
    }

    private void UpdateFavoriteButton(string siteId)
    {
        FavoriteButton.Content = AppServices.Favorites.Contains(siteId) ? "已收藏" : "收藏";
    }

    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (_detail is null)
        {
            return;
        }

        if (AppServices.Favorites.Contains(_detail.Id))
        {
            AppServices.Favorites.Remove(_detail.Id);
            Report(true, $"已取消收藏「{_detail.DisplayName}」。");
        }
        else
        {
            AppServices.Favorites.Add(new FavoriteMod
            {
                SiteId = _detail.Id,
                ResourceType = _detail.ResourceType,
                Slug = _detail.Slug,
                DisplayName = _detail.DisplayName,
                Summary = _detail.Summary,
                IconUrl = _detail.IconUrl,
                AuthorsLabel = _detail.AuthorsLabel,
                VersionLabel = string.IsNullOrWhiteSpace(_detail.LatestVersion?.VersionId)
                    ? null
                    : "v" + _detail.LatestVersion!.VersionId,
                HomepageUrl = _detail.HomepageUrl,
                SourceUrl = _detail.SourceUrl,
                AddedAt = DateTime.Now,
            });

            Report(true, $"已收藏「{_detail.DisplayName}」，可在左侧「收藏」页查看。");
        }

        UpdateFavoriteButton(_detail.Id);
    }

    private void PopulateVersions(ModDetail detail)
    {
        var ordered = detail.Versions
            .Where(v => !string.IsNullOrWhiteSpace(v.VersionId))
            .OrderByDescending(v => v.VersionId!, Comparer<string>.Create(KernelService.CompareVersions))
            .ToList();

        VersionCombo.ItemsSource = ordered;
        VersionCombo.SelectedIndex = ordered.Count > 0 ? 0 : -1;
    }

    /// <summary>取某个版本里优先安装的文件（优先 unitymodmanager，其次第一个）。</summary>
    private static ModVersionFile? PreferredFileOf(ModVersion? version)
    {
        if (version is null)
        {
            return null;
        }

        return version.Files.FirstOrDefault(f =>
                   f.Loaders.Any(l => l.Equals("unitymodmanager", StringComparison.OrdinalIgnoreCase)))
               ?? version.Files.FirstOrDefault();
    }

    private void Search_Click(object sender, RoutedEventArgs e) => _ = SearchAsync(1);

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = SearchAsync(_page);

    private void Prev_Click(object sender, RoutedEventArgs e) => _ = SearchAsync(Math.Max(1, _page - 1));

    private void Next_Click(object sender, RoutedEventArgs e) => _ = SearchAsync(_page + 1);

    private void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }

        _ = SearchAsync(1);
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _ = SearchAsync(1);
        }
    }
}
