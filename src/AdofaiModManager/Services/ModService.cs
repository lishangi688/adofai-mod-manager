using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using AdofaiModManager.Models;

namespace AdofaiModManager.Services;

/// <summary>
/// 本地 mod 管理：扫描 Mods 文件夹、安装本地 zip、卸载、启用/禁用。
/// </summary>
public sealed class ModService
{
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public string GamePath { get; }

    public string ModsPath { get; }

    public string LoaderPath { get; }

    public UmmParamsService Params { get; }

    public ModService(string gamePath)
    {
        GamePath = gamePath;
        ModsPath = Path.Combine(gamePath, "Mods");

        var dataFolder = Path.GetFileNameWithoutExtension(SteamGameLocator.GameExeName) + "_Data";
        LoaderPath = Path.Combine(gamePath, dataFolder, "Managed", "UnityModManager");

        Params = new UmmParamsService(Path.Combine(LoaderPath, "Params.xml"));
    }

    /// <summary>是否已经装了 UMM loader（有 winhttp.dll 与 Params.xml/loader 目录）。</summary>
    public bool IsLoaderInstalled =>
        Directory.Exists(LoaderPath) &&
        File.Exists(Path.Combine(LoaderPath, "UnityModManager.dll"));

    public IReadOnlyList<InstalledMod> Scan()
    {
        var result = new List<InstalledMod>();

        if (!Directory.Exists(ModsPath))
        {
            return result;
        }

        foreach (var dir in Directory.GetDirectories(ModsPath))
        {
            var infoPath = Path.Combine(dir, "Info.json");
            if (!File.Exists(infoPath))
            {
                infoPath = Path.Combine(dir, "info.json");
            }

            if (!File.Exists(infoPath))
            {
                continue;
            }

            try
            {
                var info = JsonSerializer.Deserialize<ModInfoJson>(File.ReadAllText(infoPath), JsonOptions);
                if (info is null || string.IsNullOrWhiteSpace(info.Id))
                {
                    continue;
                }

                result.Add(new InstalledMod
                {
                    Id = info.Id,
                    DisplayName = string.IsNullOrWhiteSpace(info.DisplayName) ? info.Id : info.DisplayName,
                    Version = info.Version,
                    Author = info.Author,
                    GameVersion = info.GameVersion,
                    ManagerVersion = info.ManagerVersion,
                    Requirements = info.Requirements,
                    HomePage = info.HomePage,
                    Repository = info.Repository,
                    FolderPath = dir,
                    FolderName = Path.GetFileName(dir),
                    IsEnabled = Params.IsEnabled(info.Id),
                });
            }
            catch
            {
                // 单个 mod 解析失败不影响其它 mod
            }
        }

        result.Sort((a, b) =>
            string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase));

        return result;
    }

    public InstallResult InstallFromZip(string zipPath)
    {
        if (!File.Exists(zipPath))
        {
            return new InstallResult(false, "文件不存在。");
        }

        try
        {
            using var archive = ZipFile.OpenRead(zipPath);

            var infoEntry = archive.Entries.FirstOrDefault(e =>
                Path.GetFileName(e.FullName).Equals("Info.json", StringComparison.OrdinalIgnoreCase));

            if (infoEntry is null)
            {
                return new InstallResult(false, "这不是 UMM 格式的 mod：压缩包里没有找到 Info.json。");
            }

            ModInfoJson? info;
            using (var stream = infoEntry.Open())
            {
                info = JsonSerializer.Deserialize<ModInfoJson>(stream, JsonOptions);
            }

            if (info is null || string.IsNullOrWhiteSpace(info.Id))
            {
                return new InstallResult(false, "Info.json 无效或缺少 Id 字段。");
            }

            var prefix = infoEntry.FullName[..^"Info.json".Length];
            var targetDir = Path.Combine(ModsPath, info.Id);
            var targetRoot = Path.GetFullPath(targetDir) + Path.DirectorySeparatorChar;

            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, true);
            }

            Directory.CreateDirectory(targetDir);

            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue; // 目录条目
                }

                var relative = entry.FullName.Replace('\\', '/');

                if (prefix.Length > 0)
                {
                    if (!relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue; // mod 目录之外的文件，跳过
                    }

                    relative = relative[prefix.Length..];
                }

                if (string.IsNullOrEmpty(relative))
                {
                    continue;
                }

                var destination = Path.Combine(targetDir, relative.Replace('/', Path.DirectorySeparatorChar));
                var fullDestination = Path.GetFullPath(destination);

                // 防 Zip Slip
                if (!fullDestination.StartsWith(targetRoot, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var parent = Path.GetDirectoryName(fullDestination);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                entry.ExtractToFile(fullDestination, true);
            }

            return new InstallResult(true, $"已安装 {info.DisplayName ?? info.Id} v{info.Version}。", info.Id);
        }
        catch (Exception ex)
        {
            return new InstallResult(false, $"安装失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 从 URL 下载 zip 并安装（progress 为 0-100）。
    /// cacheName 不为空时，安装成功后会把这个 zip 留在本地缓存里（离线可重装）。
    /// </summary>
    public async Task<InstallResult> InstallFromUrlAsync(
        string url,
        IProgress<int>? progress = null,
        CancellationToken ct = default,
        string? cacheName = null)
    {
        string? temp = null;
        try
        {
            temp = Path.Combine(Path.GetTempPath(), $"amm-mod-{Guid.NewGuid():N}.zip");

            using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? -1L;
                await using var source = await response.Content.ReadAsStreamAsync(ct);
                await using var target = File.Create(temp);

                var buffer = new byte[81920];
                long read = 0;
                int count;
                while ((count = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, count), ct);
                    read += count;
                    if (total > 0)
                    {
                        progress?.Report((int)(read * 100 / total));
                    }
                }
            }

            var result = InstallFromZip(temp);

            if (result.Success && !string.IsNullOrWhiteSpace(cacheName))
            {
                TrySaveToCache(temp, cacheName!);
            }

            return result;
        }
        catch (Exception ex)
        {
            return new InstallResult(false, $"下载安装失败：{ex.Message}");
        }
        finally
        {
            try
            {
                if (temp is not null && File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch
            {
                // 忽略
            }
        }
    }

    /// <summary>查本地缓存的 mod 包（离线重装用）。</summary>
    public static string? GetCachedZip(string cacheName)
    {
        try
        {
            var path = Path.Combine(AppPaths.ModCacheDirectory, SanitizeFileName(cacheName) + ".zip");
            return File.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    private static void TrySaveToCache(string sourceZip, string cacheName)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.ModCacheDirectory);
            var target = Path.Combine(AppPaths.ModCacheDirectory, SanitizeFileName(cacheName) + ".zip");
            File.Copy(sourceZip, target, true);
            TrimZipCache();
        }
        catch
        {
            // 缓存失败不影响安装
        }
    }

    /// <summary>限制缓存大小：最多 15 个包 / 600MB，超出的按最旧优先删除。</summary>
    private static void TrimZipCache(int maxCount = 15, long maxBytes = 600L * 1024 * 1024)
    {
        try
        {
            var files = new DirectoryInfo(AppPaths.ModCacheDirectory)
                .GetFiles("*.zip")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            long total = 0;
            for (var i = 0; i < files.Count; i++)
            {
                total += files[i].Length;
                if (i >= maxCount || total > maxBytes)
                {
                    files[i].Delete();
                }
            }
        }
        catch
        {
            // 忽略
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    public InstallResult Uninstall(InstalledMod mod)
    {
        try
        {
            if (Directory.Exists(mod.FolderPath))
            {
                Directory.Delete(mod.FolderPath, true);
            }

            return new InstallResult(true, $"已卸载 {mod.DisplayName}。", mod.Id);
        }
        catch (Exception ex)
        {
            return new InstallResult(false, $"卸载失败：{ex.Message}");
        }
    }

    public InstallResult SetEnabled(InstalledMod mod, bool enabled)
    {
        try
        {
            Params.SetEnabled(mod.Id, enabled);
            return new InstallResult(true, enabled ? $"已启用 {mod.DisplayName}。" : $"已禁用 {mod.DisplayName}。", mod.Id);
        }
        catch (Exception ex)
        {
            return new InstallResult(false, $"修改启用状态失败：{ex.Message}");
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AdofaiModManager/0.1");
        return client;
    }
}
