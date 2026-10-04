using System.Text.RegularExpressions;

namespace AdofaiModManager.Services;

/// <summary>
/// 版本号"写法"的判断 —— 用来处理作者在两边（mod 内的 Info.json / 资源站 / GitHub）
/// 用了不同格式的版本号时的更新判断，避免误报"有更新"。
///
/// 例：本地 Info.json 写的是 26w40c（年份+周），资源站上填的是 26.5.1（三段数字）。
/// 这种直接逐段比较会得出"资源站更新"的假结论。
/// </summary>
public static class VersionScheme
{
    /// <summary>两边是否是同一套写法（判断"字母是否紧贴数字"）。</summary>
    public static bool Same(string? a, string? b) => HasGluedDigitLetter(a) == HasGluedDigitLetter(b);

    /// <summary>字母紧贴数字：26w40c → true；26.5.1、26.5 Alpha、1.0.0-beta、2.0.r125 → false。</summary>
    public static bool HasGluedDigitLetter(string? version) =>
        !string.IsNullOrWhiteSpace(version) && Regex.IsMatch(version, @"[0-9][A-Za-z]");

    /// <summary>只比较第一个数字段（写法不同时的兜底，宁可少报也不误报）。</summary>
    public static int CompareFirstNumber(string? a, string? b)
    {
        static int FirstNumber(string? version)
        {
            var match = Regex.Match(version ?? string.Empty, @"\d+");
            return match.Success && int.TryParse(match.Value, out var value) ? value : 0;
        }

        return FirstNumber(a).CompareTo(FirstNumber(b));
    }

    /// <summary>
    /// 判断"远端是否比本地新"（纯字符串判断，不发网络请求，适合列表里批量使用）。
    /// 写法一致 → 逐段比较；写法不同 → 只比第一个数字段（宁可少报也不误报）。
    /// </summary>
    public static bool IsRemoteNewer(string? local, string? remote)
    {
        if (string.IsNullOrWhiteSpace(local) || string.IsNullOrWhiteSpace(remote))
        {
            return false;
        }

        return Same(local, remote)
            ? KernelService.CompareVersions(remote, local) > 0
            : CompareFirstNumber(remote, local) > 0;
    }
}
