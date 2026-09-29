using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AdofaiModManager.Services;

/// <summary>
/// 接口响应的磁盘缓存。
///
/// 目的：减少对资源站的重复请求（图标、mod 列表、详情都不必每次启动重新拉）。
/// 策略：写入 JSON 文件，用文件的最后写入时间判断是否过期。
/// </summary>
public static class ApiCache
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>取缓存（未过期才算命中）。</summary>
    public static bool TryGet<T>(string key, TimeSpan ttl, out T? value) =>
        TryGetCore(key, ttl, out value);

    /// <summary>取缓存（忽略过期时间，用于请求失败时回退）。</summary>
    public static bool TryGetStale<T>(string key, out T? value) =>
        TryGetCore(key, null, out value);

    public static void Set<T>(string key, T value)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.ApiCacheDirectory);
            var path = PathFor(key);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value));
            File.Move(temp, path, true);
        }
        catch
        {
            // 缓存失败不影响功能
        }
    }

    /// <summary>按参数生成一个稳定的缓存键。</summary>
    public static string Key(string prefix, params string?[] parts) =>
        prefix + "-" + Hash(string.Join('|', parts));

    private static bool TryGetCore<T>(string key, TimeSpan? ttl, out T? value)
    {
        value = default;

        try
        {
            var path = PathFor(key);
            if (!File.Exists(path))
            {
                return false;
            }

            if (ttl is not null && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > ttl)
            {
                return false;
            }

            value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
            return value is not null;
        }
        catch
        {
            return false;
        }
    }

    private static string PathFor(string key) =>
        Path.Combine(AppPaths.ApiCacheDirectory, key + ".json");

    public static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..32].ToLowerInvariant();
    }
}
