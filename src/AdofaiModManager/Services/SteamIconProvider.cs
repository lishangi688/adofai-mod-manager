using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using System.Windows.Media.Imaging;

namespace AdofaiModManager.Services;

/// <summary>
/// 从本机的 <c>steam.exe</c> 里取出 Steam 图标，并转成**单色**（跟随主题文字色），
/// 给「Steam 启动」按钮用 —— 这样和界面里其它 Fluent 图标风格一致，也不会分发有版权的素材。
///
/// 做法：把图标按亮度分成"圆盘 / 活塞"两块（实测是干净的双峰分布），
/// 圆盘填成当前主题的文字色、活塞镂空，得到实心圆盘版单色图标。
/// 取不到 Steam 就返回 null，调用方保留默认的播放图标。
/// </summary>
public static class SteamIconProvider
{
    /// <summary>256×256 的原始图标只取一次。</summary>
    private static bool _rawLoaded;

    private static Bitmap? _raw;

    /// <summary>按颜色缓存成品（深色/浅色主题各一份）。</summary>
    private static readonly Dictionary<uint, ImageSource> Cache = [];

    /// <summary>取与主题色匹配的单色 Steam 图标（取不到返回 null）。</summary>
    public static ImageSource? Get(MediaColor tint)
    {
        var key = ((uint)tint.A << 24) | ((uint)tint.R << 16) | ((uint)tint.G << 8) | tint.B;

        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var image = Build(tint);
        Cache[key] = image;
        return image;
    }

    private static ImageSource? Build(MediaColor tint)
    {
        try
        {
            var source = GetRawBitmap();
            if (source is null)
            {
                return null;
            }

            var width = source.Width;
            var height = source.Height;
            var data = source.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            try
            {
                var stride = data.Stride;
                var input = new byte[stride * height];
                Marshal.Copy(data.Scan0, input, 0, input.Length);

                var output = new byte[input.Length];

                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        var i = (y * stride) + (x * 4);

                        double blue = input[i];
                        double green = input[i + 1];
                        double red = input[i + 2];
                        double alpha = input[i + 3];

                        var luminance = ((0.299 * red) + (0.587 * green) + (0.114 * blue)) / 255.0;

                        // 实心圆盘版：暗的圆盘部分填成主题色，亮的活塞部分镂空。
                        //
                        // 阈值怎么定的：实测这个图标 256×256 下不透明像素的亮度分布是干净的双峰——
                        //   圆盘 0.1~0.4（约 70%）、活塞 0.9~1.0（约 26%），中间几乎没有。
                        // 所以在 0.45~0.75 之间做一段柔化过渡，既能把两者分开，又保留边缘抗锯齿。
                        var piston = Math.Clamp((luminance - 0.45) / 0.30, 0.0, 1.0);
                        var coverage = 1.0 - piston;

                        var outAlpha = (int)Math.Round(coverage * (alpha / 255.0) * 255);

                        output[i] = tint.B;
                        output[i + 1] = tint.G;
                        output[i + 2] = tint.R;
                        output[i + 3] = (byte)Math.Clamp(outAlpha, 0, 255);
                    }
                }

                var result = BitmapSource.Create(
                    width,
                    height,
                    96,
                    96,
                    System.Windows.Media.PixelFormats.Bgra32,
                    null,
                    output,
                    stride);

                result.Freeze();
                return result;
            }
            finally
            {
                source.UnlockBits(data);
            }
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? GetRawBitmap()
    {
        if (_rawLoaded)
        {
            return _raw;
        }

        _rawLoaded = true;

        try
        {
            var exe = SteamGameLocator.FindSteamExe();
            if (exe is null || !File.Exists(exe))
            {
                return null;
            }

            // 优先要 256px 大图（缩放到按钮尺寸时更清晰）
            var handle = ExtractLargeIcon(exe);
            if (handle != IntPtr.Zero)
            {
                try
                {
                    using var large = Icon.FromHandle(handle);
                    _raw = (Bitmap)large.ToBitmap().Clone();
                    return _raw;
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }

            // 退回到 exe 的关联图标（一般 32×32）
            using var fallback = Icon.ExtractAssociatedIcon(exe);
            if (fallback is not null)
            {
                _raw = (Bitmap)fallback.ToBitmap().Clone();
            }
        }
        catch
        {
            _raw = null;
        }

        return _raw;
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
}
