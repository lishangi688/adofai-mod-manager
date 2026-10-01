using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AdofaiModManager.Services;

/// <summary>
/// 从本机的 <c>steam.exe</c> 里取出 Steam 图标，给「Steam 启动」按钮用。
///
/// 为什么这样做：Steam 的 logo 有版权，直接从用户自己电脑上的 Steam 程序里读图标，
/// 既不额外分发素材，也能保证和用户桌面上的 Steam 图标一致。
/// 取不到就返回 null，调用方保留默认的播放图标即可。
/// </summary>
public static class SteamIconProvider
{
    private static bool _loaded;

    private static ImageSource? _cached;

    /// <summary>取 Steam 图标（结果会缓存；取不到返回 null）。</summary>
    public static ImageSource? Get()
    {
        if (_loaded)
        {
            return _cached;
        }

        _loaded = true;
        _cached = Load();
        return _cached;
    }

    private static ImageSource? Load()
    {
        try
        {
            var exe = SteamGameLocator.FindSteamExe();
            if (exe is null || !File.Exists(exe))
            {
                return null;
            }

            // 优先取大尺寸图标：缩到按钮尺寸时比 32×32 清晰得多
            var handle = ExtractLargeIcon(exe);
            if (handle != IntPtr.Zero)
            {
                try
                {
                    using var large = Icon.FromHandle(handle);
                    return ToSource(large);
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }

            // 退回到 exe 的关联图标（一般是 32×32）
            using var fallback = Icon.ExtractAssociatedIcon(exe);
            return fallback is null ? null : ToSource(fallback);
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource ToSource(Icon icon)
    {
        using var bitmap = icon.ToBitmap();
        var handle = bitmap.GetHbitmap();

        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(
                handle,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            // 冻结后才能安全缓存 / 跨线程使用
            source.Freeze();
            return source;
        }
        finally
        {
            DeleteObject(handle);
        }
    }

    /// <summary>用 Shell 接口要一个大尺寸图标（256，取不到就给 exe 里最大的那个）。</summary>
    private static IntPtr ExtractLargeIcon(string exe)
    {
        try
        {
            // nIconSize：低字 = 大图标尺寸，高字 = 小图标尺寸
            var size = (uint)((16 << 16) | 256);

            var hr = SHDefExtractIcon(exe, 0, 0, out var large, out var small, size);

            if (small != IntPtr.Zero)
            {
                DestroyIcon(small);
            }

            return hr == 0 ? large : IntPtr.Zero;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHDefExtractIcon(
        string pszIconFile,
        int iIndex,
        uint uFlags,
        out IntPtr phiconLarge,
        out IntPtr phiconSmall,
        uint nIconSize);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
