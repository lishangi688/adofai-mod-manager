using System.IO;
using System.Text.Json;
using AdofaiModManager.Models;

namespace AdofaiModManager.Services;

/// <summary>
/// 读写本地配置。
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = true,
    };

    public string ConfigDirectory { get; }

    public string ConfigFilePath { get; }

    public AppSettings Settings { get; private set; } = new();

    public SettingsService()
    {
        ConfigDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AdofaiModManager");
        ConfigFilePath = Path.Combine(ConfigDirectory, "settings.json");
    }

    public void Load()
    {
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                var json = File.ReadAllText(ConfigFilePath);
                Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch
        {
            Settings = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            File.WriteAllText(ConfigFilePath, JsonSerializer.Serialize(Settings, JsonWriteOptions));
        }
        catch
        {
            // 配置保存失败不应导致程序崩溃
        }
    }
}
