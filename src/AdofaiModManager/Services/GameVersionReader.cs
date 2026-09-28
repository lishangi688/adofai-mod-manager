using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AdofaiModManager.Services;

/// <summary>
/// 尝试从 Unity 的 globalgamemanagers 里读出游戏版本（bundleVersion）。
/// 读不到也没关系，用户可以在设置里手动填写。
/// </summary>
public static class GameVersionReader
{
    public static string? TryRead(string gamePath)
    {
        try
        {
            var dataFolder = Path.GetFileNameWithoutExtension(SteamGameLocator.GameExeName) + "_Data";
            var file = Path.Combine(gamePath, dataFolder, "globalgamemanagers");
            if (!File.Exists(file))
            {
                return null;
            }

            var text = Encoding.ASCII.GetString(File.ReadAllBytes(file));

            // 组件限制为 1~2 位，可自动排除 Unity 版本（6000.x）与年份
            foreach (Match match in Regex.Matches(text, @"(?:[^0-9])(\d{1,2})\.(\d{1,2})\.(\d{1,2})(?:[^0-9]|$)"))
            {
                return $"{match.Groups[1].Value}.{match.Groups[2].Value}.{match.Groups[3].Value}";
            }
        }
        catch
        {
            // 忽略
        }

        return null;
    }
}
