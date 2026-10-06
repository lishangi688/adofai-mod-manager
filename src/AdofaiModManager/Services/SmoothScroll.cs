using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AdofaiModManager.Services;

/// <summary>
/// 让滚轮滚动变成平滑动画（WPF 原生是"一格一跳"）。
///
/// 关键点：
/// 1) 用「目标值 + 逐帧逼近」，滚轮只抬高目标值，不重启动画 → 快速滚动不抖动；
/// 2) 用 CompositionTarget.Rendering（与显示器刷新同步）而不是固定 16ms 定时器
///    → 在高刷新率屏幕上同样丝滑；
/// 3) 逼近速度按时间计算（与帧率无关），不同刷新率手感一致。
///
/// 用法：在任意元素上设置 services:SmoothScroll.IsEnabled="True"。
/// </summary>
public static class SmoothScroll
{
    /// <summary>每格滚轮滚动的像素数。</summary>
    private const double PixelsPerNotch = 90;

    /// <summary>指数逼近的时间常数（秒），越小越"跟手"。</summary>
    private const double TimeConstant = 0.075;

    /// <summary>小于该距离就直接到位。</summary>
    private const double MinStep = 0.5;

    /// <summary>
    /// 在程序集加载时给所有 ComboBox 挂一个类级处理器。
    /// 背景：ComboBox 的下拉列表活在独立的弹层树里，窗口/控件上的 PreviewMouseWheel 收不到它的
    /// 滚轮事件；只有在下拉打开后，把平滑滚动挂到弹层自己的 ScrollViewer 上，下拉才会同样丝滑。
    /// （DropDownOpened 不是 public 的 RoutedEvent，无法直接做类级注册，所以在 Loaded 时挂 CLR 事件。）
    /// </summary>
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(ComboBox),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnComboBoxLoaded));
    }

    private static void OnComboBoxLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox combo)
        {
            combo.DropDownOpened -= OnComboBoxDropDownOpened;
            combo.DropDownOpened += OnComboBoxDropDownOpened;
        }
    }

    private static void OnComboBoxDropDownOpened(object? sender, EventArgs e)
    {
        if (sender is not ComboBox combo)
        {
            return;
        }

        // WPF-UI 的 ComboBox 模板里，下拉是个视觉树上的 Popup（名字不是 PART_Popup），
        // 所以直接遍历找 Popup，再在它的内容里找 ScrollViewer。
        if (FindVisualChild<Popup>(combo)?.Child is not DependencyObject popupContent)
        {
            return;
        }

        if (FindScrollViewer(popupContent) is not { } scrollViewer)
        {
            return;
        }

        // 下拉默认是"按项滚动"（CanContentScroll=true），那样动画只能整项跳、不丝滑；
        // 改成按像素滚动，平滑滚动才能逐帧逼近。
        if (scrollViewer.CanContentScroll)
        {
            scrollViewer.CanContentScroll = false;
        }

        if (!GetIsEnabled(scrollViewer))
        {
            SetIsEnabled(scrollViewer, true);
            AppPaths.AppendDebugLog($"[SmoothScroll] 已为下拉弹层启用平滑滚动（CanContentScroll={scrollViewer.CanContentScroll}）");
        }
    }

    private static T? FindVisualChild<T>(DependencyObject? element) where T : DependencyObject
    {
        if (element is null)
        {
            return null;
        }

        var count = VisualTreeHelper.GetChildrenCount(element);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            if (child is T typed)
            {
                return typed;
            }

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(SmoothScroll),
            new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached(
            "State",
            typeof(ScrollState),
            typeof(SmoothScroll),
            new PropertyMetadata(null));

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        element.PreviewMouseWheel -= OnPreviewMouseWheel;

        if (e.NewValue is true)
        {
            element.PreviewMouseWheel += OnPreviewMouseWheel;
        }
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta == 0)
        {
            return;
        }

        // 鼠标其实悬在下拉弹层上时，滚轮却会落到这里 —— 因为弹层是独立窗口，
        // 而 Windows 的 WM_MOUSEWHEEL 发给"有键盘焦点的窗口"（＝主窗口），不是鼠标下的窗口。
        // 结果就是"下拉划不动、背后的详情却在滚"。这种情况要把滚动转交给弹层自己的 ScrollViewer。
        if (FindPopupScrollViewerUnderMouse(out var popupScroll) &&
            !ReferenceEquals(popupScroll, sender as ScrollViewer))
        {
            if (popupScroll is not null)
            {
                SmoothScrollBy(popupScroll, e.Delta);
            }

            // 无论弹层能不能滚（比如项目太少），都别让背后的详情跟着动。
            e.Handled = true;
            return;
        }

        var target = FindScrollViewer(sender as DependencyObject);
        if (target is null)
        {
            return;
        }

        if (SmoothScrollBy(target, e.Delta))
        {
            e.Handled = true;
        }
    }

    private static bool SmoothScrollBy(ScrollViewer target, int delta)
    {
        var max = target.ScrollableHeight;
        if (max <= MinStep)
        {
            return false;
        }

        var state = GetState(target);

        if (!state.Running)
        {
            state.Target = target.VerticalOffset;
        }

        // 按项滚动的控件（CanContentScroll=true）偏移量单位是"项"，不能按像素算。
        var step = target.CanContentScroll ? 1.0 : PixelsPerNotch;

        state.Target = Math.Clamp(state.Target - delta / 120.0 * step, 0, max);

        StartAnimation(target, state);
        return true;
    }

    /// <summary>
    /// 鼠标当前是否悬在下拉弹层（Popup）上；是的话顺带取出弹层里的 ScrollViewer。
    /// PopupRoot 是 WPF 内部类型，无法直接引用，所以从鼠标元素往上找、按类型名判断。
    /// </summary>
    private static bool FindPopupScrollViewerUnderMouse(out ScrollViewer? scrollViewer)
    {
        scrollViewer = null;

        for (var node = Mouse.DirectlyOver as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node.GetType().Name == "PopupRoot")
            {
                scrollViewer = FindScrollViewer(node);
                return true;
            }
        }

        return false;
    }

    private static void StartAnimation(ScrollViewer scrollViewer, ScrollState state)
    {
        if (state.Running)
        {
            return;
        }

        state.Running = true;
        state.LastTime = 0;

        void OnRendering(object? sender, EventArgs args)
        {
            var now = args is RenderingEventArgs rendering ? rendering.RenderingTime.TotalSeconds : 0;
            var dt = state.LastTime <= 0 ? 0.016 : now - state.LastTime;
            state.LastTime = now;

            // 帧间隔异常（切后台等）时不跳变
            dt = Math.Clamp(dt, 0.001, 0.05);

            var current = scrollViewer.VerticalOffset;
            var diff = state.Target - current;

            if (Math.Abs(diff) <= MinStep)
            {
                scrollViewer.ScrollToVerticalOffset(state.Target);
                CompositionTarget.Rendering -= state.Handler!;
                state.Handler = null;
                state.Running = false;
                state.LastTime = 0;
                return;
            }

            // 与帧率无关的指数逼近
            var factor = 1 - Math.Exp(-dt / TimeConstant);
            scrollViewer.ScrollToVerticalOffset(current + diff * factor);
        }

        state.Handler = OnRendering;
        CompositionTarget.Rendering += OnRendering;
    }

    private static ScrollState GetState(ScrollViewer scrollViewer)
    {
        if (scrollViewer.GetValue(StateProperty) is ScrollState state)
        {
            return state;
        }

        state = new ScrollState();
        scrollViewer.SetValue(StateProperty, state);
        return state;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject? element)
    {
        if (element is null)
        {
            return null;
        }

        if (element is ScrollViewer scrollViewer)
        {
            return scrollViewer;
        }

        var count = VisualTreeHelper.GetChildrenCount(element);
        for (var i = 0; i < count; i++)
        {
            var found = FindScrollViewer(VisualTreeHelper.GetChild(element, i));
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private sealed class ScrollState
    {
        public double Target;

        public double LastTime;

        public bool Running;

        public EventHandler? Handler;
    }
}
