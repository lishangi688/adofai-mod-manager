using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AdofaiModManager.Services;

/// <summary>
/// WPF-UI 的 NavigationView 会把页面放进它自己的 ScrollViewer 里，
/// 于是页面会被赋予“无限高度”，导致页面内部的 ScrollViewer / ListBox 拿不到可滚动高度
/// （表现：滚不动，或者整个页面一起滚）。
///
/// 解决办法：把外层的滚动条设为 Disabled —— 这样外层不再无限测量页面，
/// 页面高度被约束到可视区，内部各区域就能各自独立滚动。
/// </summary>
public static class PageScrollFix
{
    public static void DisableOuterPageScrolling(DependencyObject page)
    {
        try
        {
            var node = page;
            for (var depth = 0; depth < 20 && node is not null; depth++)
            {
                node = VisualTreeHelper.GetParent(node);

                if (node is ScrollViewer scrollViewer)
                {
                    scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                    scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                    return;
                }
            }
        }
        catch
        {
            // 失败也不影响使用
        }
    }
}
