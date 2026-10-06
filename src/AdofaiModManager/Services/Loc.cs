using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace AdofaiModManager.Services;

/// <summary>
/// 界面多语言（PoC）。
///
/// 文案放在程序目录的 Locales/{语言}.json；缺 key 时自动回退到简体中文。
/// 语言切换会触发 PropertyChanged("Item[]")，XAML 里用 {loc:Tr Key} 写的绑定会实时刷新，
/// 不需要重启程序。
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    /// <summary>支持的语言（顺序即设置里下拉框的顺序）。</summary>
    public static readonly (string Code, string NativeName)[] Supported =
    [
        ("zh-Hans", "简体中文"),
        ("zh-Hant", "繁體中文"),
        ("ja", "日本語"),
        ("ko", "한국어"),
        ("en", "English"),
    ];

    public const string System = "system";
    private const string Fallback = "zh-Hans";

    private readonly Dictionary<string, Dictionary<string, string>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _map = [];
    private Dictionary<string, string> _fallbackMap = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>当前语言代码（zh-Hans / zh-Hant / ja / ko / en）。</summary>
    public string Language { get; private set; } = Fallback;

    /// <summary>XAML 绑定入口：Text="{loc:Tr Settings_Title}" 实际绑到这里。</summary>
    public string this[string key] => T(key);

    /// <summary>取文案；给了 args 就走 string.Format。</summary>
    public string T(string key, params object?[] args)
    {
        var text = _map.TryGetValue(key, out var value) ? value
                 : _fallbackMap.TryGetValue(key, out var fallback) ? fallback
                 : key;

        return args.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, args);
    }

    /// <summary>
    /// 当前语言的「字体链」：首选该系统字体，后面是跨语言兜底
    /// （例如韩语界面里出现中文 mod 简介时，也能找到中文字体）。
    /// </summary>
    public string FontChain => Language switch
    {
        "zh-Hant" => "Microsoft JhengHei UI, Microsoft YaHei UI, Segoe UI",
        "ja" => "Yu Gothic UI, Meiryo, Microsoft YaHei UI, Segoe UI",
        "ko" => "Malgun Gothic, Microsoft YaHei UI, Yu Gothic UI, Segoe UI",
        "en" => "Segoe UI, Microsoft YaHei UI",
        _ => "Microsoft YaHei UI, Segoe UI",
    };

    /// <summary>
    /// 给 WPF 的 XmlLanguage：决定「汉字字形变体」。
    /// 日文/韩文/繁体用的汉字写法不同，靠这个才能显示对。
    /// </summary>
    public string XmlLang => Language switch
    {
        "zh-Hant" => "zh-TW",
        "ja" => "ja-JP",
        "ko" => "ko-KR",
        "en" => "en-US",
        _ => "zh-CN",
    };

    /// <summary>切换语言；<paramref name="code"/> 允许传 "system"（跟随系统）。</summary>
    public void SetLanguage(string? code, bool notify = true)
    {
        var target = Normalize(code);

        if (string.Equals(target, Language, StringComparison.OrdinalIgnoreCase) && _map.Count > 0)
        {
            return;
        }

        Language = target;
        _map = Load(target);
        _fallbackMap = Load(Fallback);

        if (notify)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }
    }

    /// <summary>把「跟随系统」或任意输入归一化成受支持的语言代码。</summary>
    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Equals(System, StringComparison.OrdinalIgnoreCase))
        {
            code = CultureInfo.CurrentUICulture.Name;
        }

        if (code.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return code.Contains("TW", StringComparison.OrdinalIgnoreCase)
                   || code.Contains("HK", StringComparison.OrdinalIgnoreCase)
                   || code.Contains("MO", StringComparison.OrdinalIgnoreCase)
                   || code.Contains("Hant", StringComparison.OrdinalIgnoreCase)
                ? "zh-Hant"
                : "zh-Hans";
        }

        if (code.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return "ja";
        if (code.StartsWith("ko", StringComparison.OrdinalIgnoreCase)) return "ko";
        if (code.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return "en";

        return Fallback;
    }

    /// <summary>把当前语言的字体与字形语言应用到窗口（子元素会继承）。</summary>
    public void ApplyTypography(Control root)
    {
        root.FontFamily = new FontFamily(FontChain);
        root.Language = XmlLanguage.GetLanguage(XmlLang);
    }

    private Dictionary<string, string> Load(string code)
    {
        if (_cache.TryGetValue(code, out var cached))
        {
            return cached;
        }

        Dictionary<string, string> map;

        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Locales", code + ".json");
            map = File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path, Encoding.UTF8)) ?? []
                : [];
        }
        catch
        {
            map = [];
        }

        _cache[code] = map;
        return map;
    }
}
