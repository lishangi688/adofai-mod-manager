using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AdofaiModManager.Services;

/// <summary>
/// 通过 Steam 的 libraryfolders.vdf 定位《冰与火之舞》安装目录。
/// </summary>
public sealed class SteamGameLocator
{
    public const int AppId = 977950;
    public const string GameFolderName = "A Dance of Fire and Ice";
    public const string GameExeName = "A Dance of Fire and Ice.exe";

    /// <summary>返回所有可能的游戏目录（去重）。</summary>
    public IReadOnlyList<string> FindCandidates()
    {
        var result = new List<string>();

        foreach (var libraryRoot in FindSteamLibraries())
        {
            var candidate = Path.Combine(libraryRoot, "steamapps", "common", GameFolderName);
            if (Directory.Exists(candidate) && !result.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(candidate);
            }
        }

        return result;
    }

    /// <summary>
    /// 返回最合适的游戏目录。
    /// 用打分而不是"取第一个"：机器上可能存在旧的游戏副本
    /// （例如 xxx old 备份），靠主程序 / Mods / 加载器文件来区分。
    /// </summary>
    public string? FindBest()
    {
        var candidates = FindCandidates();
        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates
            .OrderByDescending(Score)
            .ThenBy(c => c, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    /// <summary>给一个游戏目录打分（越高越像"常玩的那个真安装"）。</summary>
    public int Score(string path)
    {
        var score = 0;

        if (File.Exists(Path.Combine(path, GameExeName)))
        {
            score += 100; // 有游戏主程序最重要
        }

        if (Directory.Exists(Path.Combine(path, "Mods")))
        {
            score += 10;
        }

        if (File.Exists(Path.Combine(path, "winhttp.dll")) ||
            File.Exists(Path.Combine(path, "doorstop_config.ini")))
        {
            score += 5; // 已经装过加载器，多半就是常玩的那个
        }

        return score;
    }

    /// <summary>枚举所有 Steam 库目录（含 Steam 安装目录本身）。</summary>
    public IReadOnlyList<string> FindSteamLibraries()
    {
        var libraries = new List<string>();

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var normalized = path.Trim().TrimEnd('\\', '/');
            if (Directory.Exists(normalized) && !libraries.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                libraries.Add(normalized);
            }
        }

        Add(Environment.GetEnvironmentVariable("SteamPath"));
        Add(GetSteamInstallPathFromRegistry());

        // 常见默认位置
        Add(@"C:\Program Files (x86)\Steam");
        Add(@"C:\Program Files\Steam");
        Add(@"D:\Steam");
        Add(@"D:\SteamLibrary");

        // 解析每个候选 Steam 根目录下的 libraryfolders.vdf
        foreach (var root in libraries.ToList())
        {
            foreach (var vdf in new[]
            {
                Path.Combine(root, "steamapps", "libraryfolders.vdf"),
                Path.Combine(root, "config", "libraryfolders.vdf"),
            })
            {
                if (File.Exists(vdf))
                {
                    foreach (var lib in ParseLibraryFolders(vdf))
                    {
                        Add(lib);
                    }
                }
            }
        }

        return libraries;
    }

    /// <summary>
    /// 找到本机的 steam.exe（用于「Steam 启动」按钮取图标等）。
    /// 找不到返回 null。
    /// </summary>
    public static string? FindSteamExe()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamExe") is string fromRegistry &&
                !string.IsNullOrWhiteSpace(fromRegistry) &&
                File.Exists(fromRegistry))
            {
                return fromRegistry;
            }
        }
        catch
        {
            // 忽略
        }

        try
        {
            if (GetSteamInstallPathFromRegistry() is { } installPath)
            {
                var candidate = Path.Combine(installPath, "steam.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        catch
        {
            // 忽略
        }

        foreach (var directory in new[] { @"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam" })
        {
            var candidate = Path.Combine(directory, "steam.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? GetSteamInstallPathFromRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamPath") is string currentUserPath && !string.IsNullOrWhiteSpace(currentUserPath))
            {
                return currentUserPath;
            }
        }
        catch
        {
            // 忽略
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")
                ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
            if (key?.GetValue("InstallPath") is string installPath && !string.IsNullOrWhiteSpace(installPath))
            {
                return installPath;
            }
        }
        catch
        {
            // 忽略
        }

        return null;
    }

    private static IEnumerable<string> ParseLibraryFolders(string vdfPath)
    {
        string text;
        try
        {
            text = File.ReadAllText(vdfPath);
        }
        catch
        {
            yield break;
        }

        foreach (Match match in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\""))
        {
            var raw = match.Groups[1].Value.Replace("\\\\", "\\");
            if (!string.IsNullOrWhiteSpace(raw))
            {
                yield return raw;
            }
        }
    }
}
