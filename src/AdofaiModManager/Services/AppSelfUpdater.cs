using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AdofaiModManager.Services;

/// <summary>自动更新准备/执行的结果。</summary>
public sealed record SelfUpdateResult(bool Success, string Message)
{
    /// <summary>成功时：调用方应当退出程序，把替换工作交给更新脚本/安装包。</summary>
    public bool ShouldExit => Success;
}

/// <summary>
/// AMM 自身更新：下载 → 校验 → 落地 → 交给外部完成替换。
///
/// 为什么要"退出后再替换"：
/// 正在运行的 exe/dll 被系统占用，没法直接覆盖自己。所以要么
///   · 绿色版：解压到临时目录，然后由一个小脚本等 AMM 退出后复制过去并重启；
///   · 安装版：下载安装包后用 Inno Setup 的静默参数运行，由安装器负责覆盖与重启。
/// </summary>
public static class AppSelfUpdater
{
    private static readonly HttpClient Http = CreateHttpClient();

    /// <summary>
    /// 下载并准备更新。返回成功时调用方应立即退出（<see cref="SelfUpdateResult.ShouldExit"/>）。
    /// </summary>
    /// <param name="info">更新信息</param>
    /// <param name="distribution">当前是安装版还是绿色版</param>
    /// <param name="progress">进度文本（可直接显示给用户）</param>
    public static async Task<SelfUpdateResult> ApplyAsync(
        AppUpdateInfo info,
        AppDistribution distribution,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            var url = info.UrlFor(distribution);

            if (string.IsNullOrWhiteSpace(url))
            {
                return new SelfUpdateResult(false, "没有找到适合当前版本（安装版 / 绿色版）的下载文件，请手动下载。");
            }

            var workRoot = Path.Combine(Path.GetTempPath(), $"amm-update-{info.Version}");
            var packageDir = Path.Combine(workRoot, "package");
            var payloadDir = Path.Combine(workRoot, "payload");

            ResetDirectory(workRoot);
            Directory.CreateDirectory(packageDir);
            Directory.CreateDirectory(payloadDir);

            var isInstaller = distribution == AppDistribution.Installed;

            // 安装版优先直接下安装包（GitHub 侧就是 .exe）；否则下压缩包
            var fileName = string.IsNullOrWhiteSpace(info.FileName)
                ? Path.GetFileName(new Uri(url).LocalPath)
                : info.FileName!;

            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = isInstaller ? "setup.exe" : "update.zip";
            }

            var packagePath = Path.Combine(packageDir, SanitizeFileName(fileName));

            if (File.Exists(url))
            {
                // 本地文件（开发测试用）
                progress?.Report($"正在读取本地更新包 v{info.Version} …");
                File.Copy(url, packagePath, true);
            }
            else
            {
                progress?.Report($"正在下载 v{info.Version} …　0%");
                await DownloadAsync(url, packagePath, progress, info.Version, ct);
            }

            // ---- 安装包：直接静默运行，交给安装器覆盖 ----
            if (packagePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                progress?.Report("下载完成，正在启动安装程序…");
                return LaunchInstaller(packagePath);
            }

            // ---- 压缩包：解压出绿色版文件 ----
            progress?.Report("正在解压…");
            var extracted = ExtractPackage(packagePath, payloadDir, out var manifestVersion, out var setupInside);

            if (!string.IsNullOrWhiteSpace(manifestVersion) &&
                KernelService.CompareVersions(manifestVersion, info.Version) != 0)
            {
                return new SelfUpdateResult(
                    false,
                    $"压缩包里的版本（{manifestVersion}）与要更新的版本（{info.Version}）不一致，已中止。");
            }

            if (isInstaller)
            {
                // 安装版 + 只有合并包：把包里的安装器取出来跑
                if (setupInside is null)
                {
                    return new SelfUpdateResult(false, "压缩包里没有找到安装程序，请手动下载安装。");
                }

                progress?.Report("正在启动安装程序…");
                return LaunchInstaller(setupInside);
            }

            if (!extracted)
            {
                return new SelfUpdateResult(false, "压缩包里没有找到程序文件，已中止。");
            }

            if (AppPaths.DebugLogEnabled)
            {
                var top = Directory.Exists(payloadDir)
                    ? string.Join(", ", Directory.GetFiles(payloadDir).Take(6).Select(Path.GetFileName))
                    : "(无)";
                AppPaths.AppendDebugLog($"[selfupdate] 解压到 {payloadDir}，顶层文件示例：{top}");
            }

            // ---- 绿色版：写更新脚本，等 AMM 退出后替换并重启 ----
            progress?.Report("准备替换文件…");
            return LaunchPortableUpdater(payloadDir, AppContext.BaseDirectory, workRoot);
        }
        catch (OperationCanceledException)
        {
            return new SelfUpdateResult(false, "更新已取消。");
        }
        catch (Exception ex)
        {
            return new SelfUpdateResult(false, $"更新失败：{ex.Message}");
        }
    }

    private static async Task DownloadAsync(
        string url,
        string targetPath,
        IProgress<string>? progress,
        string version,
        CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1L;

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var target = File.Create(targetPath);

        var buffer = new byte[81920];
        long read = 0;
        int count;

        while ((count = await source.ReadAsync(buffer, ct)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, count), ct);
            read += count;

            if (total > 0)
            {
                var percent = (int)(read * 100 / total);
                progress?.Report($"正在下载 v{version} …　{percent}%（{read / 1048576.0:0.#} / {total / 1048576.0:0.#} MB）");
            }
            else
            {
                progress?.Report($"正在下载 v{version} …　{read / 1048576.0:0.#} MB");
            }
        }
    }

    /// <summary>
    /// 解压压缩包里的程序文件到 payloadDir。
    /// 支持两种布局：
    ///   · 合并包（资源站）：绿色版文件夹（名字由 update.json 指定）+ Setup.exe + update.json
    ///   · 绿色版包（GitHub）：程序文件直接在根目录
    /// </summary>
    private static bool ExtractPackage(
        string packagePath,
        string payloadDir,
        out string? manifestVersion,
        out string? setupInside)
    {
        manifestVersion = null;
        setupInside = null;

        using var archive = ZipFile.OpenRead(packagePath);

        // 注意：不同打包工具生成的 zip，内部路径分隔符可能是 "/" 也可能是 "\"
        //（PowerShell 5.1 的 Compress-Archive 就是反斜杠），这里统一成 "/" 再判断。
        static string Normalize(string fullName) => fullName.Replace('\\', '/');

        // 先读 update.json：里面有版本号，以及"绿色版文件夹叫什么"
        // （合并包里那个文件夹刻意用产品全名，方便用户直接拖拽）
        var portableFolder = "portable";

        var manifestEntry = archive.Entries.FirstOrDefault(e =>
            Normalize(e.FullName).Equals("update.json", StringComparison.OrdinalIgnoreCase));

        if (manifestEntry is not null)
        {
            try
            {
                using var stream = manifestEntry.Open();
                using var document = JsonDocument.Parse(stream);

                if (document.RootElement.TryGetProperty("version", out var versionElement))
                {
                    manifestVersion = versionElement.GetString();
                }

                if (document.RootElement.TryGetProperty("portable", out var portableElement) &&
                    !string.IsNullOrWhiteSpace(portableElement.GetString()))
                {
                    portableFolder = portableElement.GetString()!.Trim().TrimEnd('/');
                }
            }
            catch
            {
                // 元信息读不出来就按默认布局处理
            }
        }

        // 合并包里的安装器
        var setupEntry = archive.Entries.FirstOrDefault(e =>
            e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
            e.Name.Contains("Setup", StringComparison.OrdinalIgnoreCase));

        if (setupEntry is not null)
        {
            setupInside = Path.Combine(Path.GetDirectoryName(payloadDir)!, setupEntry.Name);
            setupEntry.ExtractToFile(setupInside, true);
        }

        // 合并包：只取「绿色版文件夹」下的文件；绿色版包：程序文件直接在根目录
        var portablePrefix = portableFolder + "/";

        var hasPortableFolder = archive.Entries.Any(e =>
            Normalize(e.FullName).StartsWith(portablePrefix, StringComparison.OrdinalIgnoreCase));

        var copied = 0;

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue; // 目录条目
            }

            var fullName = Normalize(entry.FullName);
            string relative;

            if (hasPortableFolder)
            {
                if (!fullName.StartsWith(portablePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                relative = fullName[portablePrefix.Length..];
            }
            else
            {
                // 绿色版包：程序文件直接在根目录
                relative = fullName;
            }

            if (string.IsNullOrEmpty(relative))
            {
                continue;
            }

            var destination = Path.GetFullPath(Path.Combine(payloadDir, relative.Replace('/', Path.DirectorySeparatorChar)));
            var payloadRoot = Path.GetFullPath(payloadDir) + Path.DirectorySeparatorChar;

            // 防 Zip Slip
            if (!destination.StartsWith(payloadRoot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            entry.ExtractToFile(destination, true);
            copied++;
        }

        return copied > 0;
    }

    private static SelfUpdateResult LaunchInstaller(string installerPath)
    {
        try
        {
            // Inno Setup 静默参数：不显示界面、自动关闭占用文件的程序、装完自动重启
            Process.Start(new ProcessStartInfo(installerPath)
            {
                UseShellExecute = true,
                Arguments = "/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /NORESTART",
            });

            return new SelfUpdateResult(true, "安装程序已启动，AMM 即将退出并自动完成更新。");
        }
        catch (Exception ex)
        {
            return new SelfUpdateResult(false, $"启动安装程序失败：{ex.Message}");
        }
    }

    private static SelfUpdateResult LaunchPortableUpdater(
        string payloadDir,
        string targetDir,
        string workRoot)
    {
        try
        {
            var target = targetDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var exe = Path.Combine(target, "AdofaiModManager.exe");
            var log = Path.Combine(AppPaths.LogDirectory, "update.log");

            Directory.CreateDirectory(AppPaths.LogDirectory);

            var scriptPath = Path.Combine(Path.GetTempPath(), $"amm-apply-{Guid.NewGuid():N}.cmd");

            // 注意：脚本只能写 ASCII。
            // .NET Core 默认不带 GBK(936) 等旧代码页，而且 cmd 对 UTF-8 中文也不友好，
            // 所以这里的提示/日志一律用英文，避免"编码不可用"这类意外。
            var script = $"""
                @echo off
                set "LOG={log}"
                echo [%date% %time%] waiting for AMM to exit... >> "%LOG%"

                :wait
                tasklist /fi "imagename eq AdofaiModManager.exe" 2>nul | find /i "AdofaiModManager.exe" >nul
                if not errorlevel 1 (
                    ping -n 2 127.0.0.1 >nul
                    goto wait
                )

                echo [%date% %time%] copying new version into "{target}" >> "%LOG%"
                robocopy "{payloadDir}" "{target}" /E /IS /IT /NFL /NDL /NJH /NJS /NP >> "%LOG%" 2>&1
                echo [%date% %time%] robocopy exit code %errorlevel% >> "%LOG%"

                echo [%date% %time%] starting AMM... >> "%LOG%"
                start "" "{exe}"

                rmdir /s /q "{payloadDir}" 2>nul
                rmdir /s /q "{workRoot}" 2>nul
                del "%~f0" 2>nul
                """;

            // 关键：cmd.exe 是按"系统 OEM 代码页"读取批处理文件的。
            // 如果脚本里有中文路径（例如 D:\umm优化\...），就必须用同一个代码页写入，
            // 否则 cmd 读到的路径是乱码，robocopy 会复制到错误的位置。
            // 而 .NET Core 默认不带 936 等旧代码页，需要先注册提供程序。
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            var encoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);

            // 另外：cmd 对换行很挑，必须是 CRLF（C# 源文件里是 LF）。
            var normalized = script.Replace("\r\n", "\n").Replace("\n", "\r\n");
            File.WriteAllText(scriptPath, normalized, encoding);

            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{scriptPath}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });

            return new SelfUpdateResult(true, "正在替换文件，AMM 即将退出并自动重启完成更新。");
        }
        catch (Exception ex)
        {
            return new SelfUpdateResult(false, $"准备替换失败：{ex.Message}");
        }
    }

    private static void ResetDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // 删不掉就在原目录上重试
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AdofaiModManager/0.1");
        return client;
    }
}
