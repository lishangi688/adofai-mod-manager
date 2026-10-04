using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    /// <summary>是否已经把所有页都加载完了（用于滚动加载）</summary>
    private bool _allLoaded;

    private ScrollViewer? _listScroller;

    private bool _scrollHooked;

    private readonly int _pageSize = 30;

    private ModDetail? _detail;

    public OnlineModsPage()
    {
        InitializeComponent();

        ModList.ItemsSource = _mods;
        SortCombo.SelectedIndex = 0;

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
            HookListScroll();

            if (_mods.Count == 0)
            {
                _ = SearchAsync(1);
            }
        };
    }

    /// <summary>
    /// 给 mod 列表内部的滚动条挂上事件，滚到接近底部时自动加载下一页
    /// （取代原来的「上一页 / 下一页」分页按钮）。
    /// </summary>
    private void HookListScroll()
    {
        if (_scrollHooked)
        {
            return;
        }

        _listScroller ??= FindScrollViewer(ModList);
        if (_listScroller is null)
        {
            return;
        }

        _listScroller.ScrollChanged += OnListScrollChanged;
        _scrollHooked = true;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer)
        {
            return viewer;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private void OnListScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_busy || _allLoaded || e.ExtentHeight <= 0)
        {
            return;
        }

        // 距离底部还剩不到一屏时，提前把下一页拉进来
        if (e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 300)
        {
            _ = LoadMoreAsync();
        }
    }

    /// <summary>
    /// 记录当前列表是基于哪一组筛选条件加载的。
    /// 作用：筛选条件改了但还没重新搜索时，禁止"继续加载下一页"——
    /// 否则会拿新筛选词 + 旧页码去请求，出现"共 1 个资源却已加载 30 个"这种自相矛盾的状态。
    /// </summary>
    private string _loadedFilter = string.Empty;

    private string CurrentFilterKey()
    {
        var sort = (SortCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;
        var type = (TypeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;
        return $"{SearchBox.Text.Trim()}|{sort}|{type}";
    }

    /// <summary>加载下一页并追加到列表末尾（筛选条件必须和已加载的一致）。</summary>
    private Task LoadMoreAsync()
    {
        if (CurrentFilterKey() != _loadedFilter)
        {
            return Task.CompletedTask;
        }

        return FetchAsync(_page + 1, append: true);
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

    private Task SearchAsync(int page) => FetchAsync(page, append: false);

    /// <param name="page">要请求的页码</param>
    /// <param name="append">true = 追加到列表末尾（滚动加载）；false = 重新搜索（清空重来）</param>
    private async Task FetchAsync(int page, bool append)
    {
        if (_busy || (append && _allLoaded))
        {
            return;
        }

        var client = BuildClient(out var error);
        if (client is null)
        {
            Report(false, error);
            SubtitleText.Text = error;

            if (!append)
            {
                _mods.Clear();
                UpdateFooter();
            }

            return;
        }

        SetBusy(true);
        try
        {
            if (!append)
            {
                LoadInstalled();
            }

            // 这次请求用的筛选条件（请求过程中用户可能又改了输入框，所以先固定下来）
            var filterKey = CurrentFilterKey();

            var sort = (SortCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            var type = (TypeCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            var search = SearchBox.Text;

            var result = await client.GetModsAsync(page, _pageSize, search, type, null, sort);

            if (!append)
            {
                _mods.Clear();
                _page = 1;
                _allLoaded = false;
            }

            _loadedFilter = filterKey;
            _page = result.Page <= 0 ? page : result.Page;
            _total = result.Total;

            var known = new HashSet<string>(_mods.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);

            // 可更新的置顶（保持接口原始顺序，每页内部这样排）
            foreach (var item in result.Items.OrderByDescending(i => i.LocalState == "可更新" ? 1 : 0))
            {
                ApplyLocalState(item);

                if (known.Add(item.Id))
                {
                    _mods.Add(item);
                }
            }

            _allLoaded = result.Items.Count == 0 || (_total > 0 && _mods.Count >= _total);

            if (!append)
            {
                UpdateTypeFilter(result.Items);
            }

            UpdateFooter();
            _ = LoadIconsAsync(_mods.ToList());

            if (!append && _mods.Count > 0)
            {
                ModList.SelectedIndex = 0;
            }

            SubtitleText.Text = _mods.Count == 0
                ? "没有找到匹配的 mod。"
                : $"资源站共 {result.Total} 个资源，已加载 {_mods.Count} 个。";
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

    private bool _populatingTypes;

    /// <summary>
    /// 类型筛选是"自适应"的：默认不显示。
    /// 只有当资源站真的返回了多种类型时才出现（对第三方站友好）；
    /// 因为 adofaitools 目前所有资源都是 MOD，这里通常就是隐藏的。
    /// </summary>
    private void UpdateTypeFilter(IReadOnlyList<ModListItem> items)
    {
        var selectedType = (TypeCombo.SelectedItem as ComboBoxItem)?.Tag as string;

        // 已经在按类型筛选：保持显示，避免把用户"困"在筛选里
        if (!string.IsNullOrWhiteSpace(selectedType))
        {
            TypeCombo.Visibility = Visibility.Visible;
            return;
        }

        var types = items
            .Select(i => i.ResourceType)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (types.Count <= 1)
        {
            TypeCombo.Visibility = Visibility.Collapsed;
            return;
        }

        if (TypeCombo.Items.Count == 0)
        {
            _populatingTypes = true;
            TypeCombo.Items.Add(new ComboBoxItem { Content = "全部类型", Tag = string.Empty });
            foreach (var type in types)
            {
                TypeCombo.Items.Add(new ComboBoxItem { Content = TypeLabel(type), Tag = type });
            }

            TypeCombo.SelectedIndex = 0;
            _populatingTypes = false;
        }

        TypeCombo.Visibility = Visibility.Visible;
    }

    private static string TypeLabel(string type) => type.ToUpperInvariant() switch
    {
        "MOD" => "Mod",
        "RESOURCEPACK" => "资源包",
        "SHADER" => "着色器",
        "PLUGIN" => "插件",
        "LIBRARY" => "库",
        "TOOL" => "工具",
        _ => "其它",
    };

    private void TypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _populatingTypes)
        {
            return;
        }

        _ = SearchAsync(1);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        UpdateFooter();
    }

    private void UpdateFooter()
    {
        LoadMoreButton.IsEnabled = !_busy;

        if (_total <= 0)
        {
            PageText.Text = string.Empty;
            LoadMoreButton.Visibility = Visibility.Collapsed;
            return;
        }

        if (_allLoaded)
        {
            PageText.Text = $"已全部加载（共 {_total} 个）";
            LoadMoreButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            PageText.Text = $"已加载 {_mods.Count} / {_total}　（继续向下滚动会自动加载）";
            LoadMoreButton.Visibility = Visibility.Visible;
        }
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

        var selected = VersionCombo.SelectedItem as ModVersion;
        var file = PreferredFileOf(selected) ?? _detail.PreferredFile;
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

            var progress = new InlineProgress<int>(percent =>
            {
                InstallProgress.Value = percent;
                InstallStatusText.Text = $"正在下载… {percent}%";
            }, Dispatcher);

            var installer = new SiteInstaller(client, service);
            var selectedVersion = selected?.VersionId;
            var result = await installer.InstallAsync(_detail, file, selectedVersion, progress);

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

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = SearchAsync(1);

    private void LoadMore_Click(object sender, RoutedEventArgs e) => _ = LoadMoreAsync();

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
