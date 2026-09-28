namespace AdofaiModManager.Models;

/// <summary>
/// UMM 的 Info.json 结构（mod 元数据）。
/// </summary>
public sealed class ModInfoJson
{
    public string? Id { get; set; }
    public string? DisplayName { get; set; }
    public string? Author { get; set; }
    public string? Version { get; set; }
    public string? ManagerVersion { get; set; }
    public string? GameVersion { get; set; }
    public string[]? Requirements { get; set; }
    public string[]? LoadAfter { get; set; }
    public string? AssemblyName { get; set; }
    public string? EntryMethod { get; set; }
    public string? HomePage { get; set; }
    public string? Repository { get; set; }
    public string? ContentType { get; set; }
}
