using System.IO;
using System.Windows;
using System.Windows.Controls;
using AdofaiModManager.Services;
using Microsoft.Win32;

namespace AdofaiModManager.Views.Pages;

public partial class SettingsPage : Page
{
    private readonly bool _ready;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => PageScrollFix.DisableOuterPageScrolling(this);

        var settings = AppServices.Settings.Settings;
        GamePathBox.Text = settings.GamePath ?? string.Empty;
        ApiBaseUrlBox.Text = settings.ApiBaseUrl;
        ApiKeyBox.Text = settings.ApiKey ?? string.Empty;
        GameVersionBox.Text = settings.GameVersionOverride ?? string.Empty;
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

        UpdateGameStatus();
        _ready = true;
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

    private void GamePathBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }

        AppServices.Settings.Settings.GamePath = GamePathBox.Text.Trim();
        AppServices.Settings.Save();
        UpdateGameStatus();
    }

    private void ApiBaseUrlBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }

        AppServices.Settings.Settings.ApiBaseUrl = ApiBaseUrlBox.Text.Trim();
        AppServices.Settings.Save();
    }

    private void ApiKeyBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }

        AppServices.Settings.Settings.ApiKey = ApiKeyBox.Text.Trim();
        AppServices.Settings.Save();
    }

    private void GameVersionBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }

        AppServices.Settings.Settings.GameVersionOverride = GameVersionBox.Text.Trim();
        AppServices.Settings.Save();
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
}
