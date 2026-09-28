using Wpf.Ui.Abstractions;

namespace AdofaiModManager.Services;

/// <summary>
/// 让 WPF-UI 的 NavigationView 能够创建页面实例。
/// </summary>
public sealed class NavigationViewPageProvider(Func<Type, object?> resolver) : INavigationViewPageProvider
{
    public object? GetPage(Type pageType) => resolver(pageType);
}
