using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AdofaiModManager.Services;

/// <summary>
/// 安全地下载并解码网络图片。失败一律返回 null，绝不抛出（避免打崩 UI）。
/// </summary>
public static class ImageLoader
{
    private static readonly HttpClient Http = CreateHttpClient();

    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new();

    public static async Task<ImageSource?> LoadAsync(string? url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        if (Cache.TryGetValue(url, out var cached))
        {
            return cached;
        }

        ImageSource? image = null;
        try
        {
            var bytes = await Http.GetByteArrayAsync(uri, ct);
            image = Decode(bytes);
        }
        catch
        {
            image = null;
        }

        Cache[url] = image;
        return image;
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
