using System.Windows;
using System.Windows.Controls;
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

        // 鼠标在「弹层」里（例如 ComboBox 的下拉列表）时不要接管：
        // 那种滚动应该由弹层自己的 ScrollViewer 处理，和它抢会把下拉滚不动。
        if (Mouse.DirectlyOver is Visual over && IsInsidePopup(over))
        {
            return;
        }

        var target = FindScrollViewer(sender as DependencyObject);
        if (target is null)
        {
            return;
        }

        var max = target.ScrollableHeight;
        if (max <= MinStep)
        {
            return;
        }

        e.Handled = true;

        var state = GetState(target);

        if (!state.Running)
        {
            state.Target = target.VerticalOffset;
        }

        state.Target = Math.Clamp(state.Target - e.Delta / 120.0 * PixelsPerNotch, 0, max);

        StartAnimation(target, state);
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

    /// <summary>
    /// 鼠标所在元素是不是在弹层里。
    /// PopupRoot 是 WPF 的内部类型（无法直接引用），所以一路走到视觉树根再按类型名判断。
    /// </summary>
    private static bool IsInsidePopup(Visual visual)
    {
        var root = visual;

        while (VisualTreeHelper.GetParent(root) is { } parent)
        {
            if (parent is Visual parentVisual)
            {
                root = parentVisual;
            }
            else
            {
                break;
            }
        }

        return root.GetType().Name == "PopupRoot";
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
