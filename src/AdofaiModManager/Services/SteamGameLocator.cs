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

    /// <summary>返回最合适的游戏目录：优先包含游戏主程序的，其次包含 Mods 目录的。</summary>
    public string? FindBest()
    {
        var candidates = FindCandidates();

        return candidates.FirstOrDefault(c => File.Exists(Path.Combine(c, GameExeName)))
            ?? candidates.FirstOrDefault(c => Directory.Exists(Path.Combine(c, "Mods")))
            ?? candidates.FirstOrDefault();
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
