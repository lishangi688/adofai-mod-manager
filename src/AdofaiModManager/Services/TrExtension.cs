using System.Windows.Data;
using System.Windows.Markup;

namespace AdofaiModManager.Services;

/// <summary>
/// XAML 里取多语言文案：<c>Text="{loc:Tr Settings_Title}"</c>。
///
/// 返回的是绑定（而不是当场取好的字符串），所以切换语言时界面会**实时刷新**。
/// key 只允许字母、数字、下划线（绑定路径里更稳当）。
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object? ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            return string.Empty;
        }

        var binding = new Binding($"[{Key}]")
        {
            Source = Loc.Instance,
            Mode = BindingMode.OneWay,
        };

        return binding.ProvideValue(serviceProvider);
    }
}
