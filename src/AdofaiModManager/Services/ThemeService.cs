using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace AdofaiModManager.Services;

/// <summary>
/// 主题（浅色 / 深色 / 跟随系统）的应用逻辑。
/// </summary>
public static class ThemeService
{
    public const string System = "System";
    public const string Light = "Light";
    public const string Dark = "Dark";

    public static bool IsSystem(string? setting) =>
        string.IsNullOrWhiteSpace(setting) ||
        setting.Equals(System, StringComparison.OrdinalIgnoreCase);

    public static ApplicationTheme Resolve(string? setting)
    {
        if (!IsSystem(setting))
        {
            return setting!.Equals(Light, StringComparison.OrdinalIgnoreCase)
                ? ApplicationTheme.Light
                : ApplicationTheme.Dark;
        }

        return FromSystemTheme(ApplicationThemeManager.GetSystemTheme());
    }

    public static void Apply(string? setting, Window? window = null)
    {
        var theme = Resolve(setting);
        ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, true);
        ApplyCustomBrushes(theme);

        if (window is null)
        {
            return;
        }

        try
        {
            if (IsSystem(setting))
            {
                SystemThemeWatcher.Watch(window, WindowBackdropType.Mica, true);
            }
            else
            {
                SystemThemeWatcher.UnWatch(window);
            }
        }
        catch
        {
            // 跟随系统失败不应影响启动
        }
    }

    /// <summary>
    /// 自定义一套卡片配色。WPF-UI 自带的填充色在浅色主题下过于透明
    /// （白底上的半透明白 ≈ 看不见），所以这里浅色/深色各给一套明确的颜色。
    /// </summary>
    private static void ApplyCustomBrushes(ApplicationTheme theme)
    {
        var resources = Application.Current?.Resources;
        if (resources is null)
        {
            return;
        }

        var light = theme != ApplicationTheme.Dark;

        resources["AmmCardBackgroundBrush"] = Solid(light, 0xEF, 0xEF, 0xEF, 0xFF, 0x26, 0x26, 0x26);
        resources["AmmCardBorderBrush"] = Solid(light, 0xDA, 0xDA, 0xDA, 0xFF, 0x3A, 0x3A, 0x3A);
        resources["AmmCardHoverBrush"] = Solid(light, 0xE6, 0xE6, 0xE6, 0xFF, 0x33, 0x33, 0x33);
        resources["AmmCardSelectedBrush"] = Solid(light, 0xCF, 0xCF, 0xCF, 0xFF, 0x45, 0x45, 0x45);

        // 悬停"变亮"的浮层：浅色用黑色半透明压暗、深色用白色半透明提亮
        resources["AmmHoverOverlayBrush"] = Overlay(light, 0x12, 0x24);
    }

    private static SolidColorBrush Overlay(bool light, byte lightAlpha, byte darkAlpha)
    {
        var brush = light
            ? new SolidColorBrush(Color.FromArgb(lightAlpha, 0, 0, 0))
            : new SolidColorBrush(Color.FromArgb(darkAlpha, 255, 255, 255));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush Solid(bool light, byte lr, byte lg, byte lb, byte da, byte dr, byte dg, byte db)
    {
        var brush = light
            ? new SolidColorBrush(Color.FromRgb(lr, lg, lb))
            : new SolidColorBrush(Color.FromArgb(da, dr, dg, db));
        brush.Freeze();
        return brush;
    }

    private static ApplicationTheme FromSystemTheme(SystemTheme theme) => theme switch
    {
        SystemTheme.Light or SystemTheme.HCWhite => ApplicationTheme.Light,
        _ => ApplicationTheme.Dark,
    };
}
