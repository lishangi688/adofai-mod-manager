using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AdofaiModManager.Services;

/// <summary>
/// 安全地加载网络图片（图标、头像）。
///
/// 缓存策略：内存 → 磁盘（7 天）→ 网络。
/// 这样**每次启动不会再从资源站重复拉一遍图标**，减轻站长压力；
/// 7 天后会重新获取，兼顾"图标可能更新"。
/// 失败时回退到过期缓存，最后才返回 null（绝不抛出，避免打崩 UI）。
/// </summary>
public static class ImageLoader
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(7);

    private static readonly HttpClient Http = CreateHttpClient();

    private static readonly ConcurrentDictionary<string, ImageSource?> Memory = new();

    public static async Task<ImageSource?> LoadAsync(string? url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        if (Memory.TryGetValue(url, out var cached))
        {
            return cached;
        }

        var file = Path.Combine(AppPaths.IconCacheDirectory, ApiCache.Hash(url) + ".img");

        // 1) 磁盘缓存（未过期）
        if (File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < CacheTtl)
        {
            var bytes = await TryReadAsync(file, ct);
            if (bytes is not null)
            {
                var fromDisk = Decode(bytes);
                if (fromDisk is not null)
                {
                    Memory[url] = fromDisk;
                    return fromDisk;
                }
            }
        }

        // 2) 下载
        try
        {
            var data = await Http.GetByteArrayAsync(uri, ct);
            var image = Decode(data);
            if (image is not null)
            {
                await TryWriteAsync(file, data, ct);
                Memory[url] = image;
                return image;
            }
        }
        catch
        {
            // 走下面的过期缓存回退
        }

        // 3) 下载失败 → 用过期缓存顶一下
        if (File.Exists(file))
        {
            var bytes = await TryReadAsync(file, ct);
            if (bytes is not null)
            {
                var stale = Decode(bytes);
                Memory[url] = stale;
                return stale;
            }
        }

        Memory[url] = null;
        return null;
    }

    private static async Task<byte[]?> TryReadAsync(string path, CancellationToken ct)
    {
        try
        {
            return await File.ReadAllBytesAsync(path, ct);
        }
        catch
        {
            return null;
        }
    }

    private static async Task TryWriteAsync(string path, byte[] data, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.IconCacheDirectory);
            var temp = path + ".tmp";
            await File.WriteAllBytesAsync(temp, data, ct);
            File.Move(temp, path, true);
        }
        catch
        {
            // 缓存失败不影响显示
        }
    }

    private static ImageSource? Decode(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            // 忽略颜色配置，避免个别图片在取 ColorContexts 时抛异常
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AdofaiModManager/0.1");
        return client;
    }
}
