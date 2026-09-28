using AdofaiModManager.Models;
using AdofaiModManager.Services;

var gamePath = args.Length > 0 ? args[0] : @"D:\SteamLibrary\steamapps\common\A Dance of Fire and Ice";

var modService = new ModService(gamePath);

if (!Directory.Exists(modService.ModsPath))
{
    Console.WriteLine($"找不到 Mods 目录：{modService.ModsPath}");
    return 1;
}

var mods = modService.Scan();
Console.WriteLine($"已安装 {mods.Count} 个 mod，开始逐个解析更新源并检查…");
Console.WriteLine(new string('-', 100));

var store = new UpdateSourceStore(Path.Combine(Path.GetTempPath(), "amm-test-update-sources.json"));
var service = new GitHubUpdateService(store);

var updatable = 0;
var failed = 0;
var noSource = 0;

foreach (var mod in mods)
{
    var source = service.ResolveSource(mod);

    Console.WriteLine($"{mod.DisplayName}  (Id={mod.Id}, v{mod.Version})");
    Console.WriteLine($"    更新源: {source.Description}");

    if (source.Kind == UpdateSourceKind.None)
    {
        noSource++;
        Console.WriteLine("    结果  : 无更新源（需手动绑定 GitHub）");
        continue;
    }

    var result = await service.CheckAsync(source, mod.Id, mod.Version);

    if (!result.Success)
    {
        failed++;
        Console.WriteLine($"    结果  : ❌ {result.Message}");
    }
    else if (result.UpdateAvailable)
    {
        updatable++;
        Console.WriteLine($"    结果  : ⬆ 可更新 {mod.Version} → {result.RemoteVersion}");
        Console.WriteLine($"    下载  : {result.FileName ?? result.DownloadUrl}");
    }
    else
    {
        Console.WriteLine($"    结果  : ✓ 已是最新（{result.RemoteVersion ?? mod.Version}）");
    }
}

Console.WriteLine(new string('-', 100));
Console.WriteLine($"汇总：{mods.Count} 个 mod，可更新 {updatable}，检查失败 {failed}，无更新源 {noSource}");
return 0;
