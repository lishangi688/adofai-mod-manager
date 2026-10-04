using System.Windows.Threading;

namespace AdofaiModManager.Services;

/// <summary>
/// 同步执行的进度回调。
///
/// 为什么不用 <see cref="Progress{T}"/>：
/// <c>Progress&lt;T&gt;</c> 会把每次回调**异步** post 到 UI 线程队列。
/// 于是"检查全部更新"会出现这样的顺序问题：
/// <list type="number">
///   <item>循环里报告「正在检查更新 TUFHelperLite」→ 排入 UI 队列</item>
///   <item>检查结束，界面已经显示「检查完成：11 个 mod，1 个可更新。」</item>
///   <item>第 1 步排队的回调这时才执行，把提示又改回「正在检查更新 TUFHelperLite」</item>
/// </list>
/// 结果就是状态栏一直停在最后一个 mod 名上，看起来像卡死，其实早就完成了。
///
/// 这里改成同步执行（必要时同步 Invoke 到 UI 线程），
/// 让提示的先后顺序与调用顺序完全一致，完成提示不会再被覆盖。
/// </summary>
public sealed class InlineProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    private readonly Dispatcher? _dispatcher;

    public InlineProgress(Action<T> handler, Dispatcher? dispatcher = null)
    {
        _handler = handler;
        _dispatcher = dispatcher;
    }

    public void Report(T value)
    {
        if (_dispatcher is null || _dispatcher.CheckAccess())
        {
            _handler(value);
            return;
        }

        // 同步等待 UI 线程执行完，保证顺序不会乱
        _dispatcher.Invoke(() => _handler(value));
    }
}
