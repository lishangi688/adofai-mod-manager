namespace AdofaiModManager.Models;

public sealed class ToolListPage
{
    public List<ToolListItem> Items { get; set; } = [];

    public int Total { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }
}

public sealed class ToolListItem
{
    public string Id { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Summary { get; set; }

    public string? IconUrl { get; set; }

    public List<ModAuthor> Authors { get; set; } = [];

    public ToolLatestVersion? LatestVersion { get; set; }

    public string VersionLabel => string.IsNullOrWhiteSpace(LatestVersion?.VersionId)
        ? string.Empty
        : "v" + LatestVersion!.VersionId;
}

public sealed class ToolLatestVersion
{
    public string Id { get; set; } = string.Empty;

    public string? VersionId { get; set; }

    public string? DisplayName { get; set; }

    public string? Tag { get; set; }
}

public sealed class ToolDetail
{
    public string Id { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Summary { get; set; }

    public List<ToolVersion> Versions { get; set; } = [];
}

public sealed class ToolVersion
{
    public string Id { get; set; } = string.Empty;

    public string? VersionId { get; set; }

    public string? Changelog { get; set; }

    public ToolVersionFile? File { get; set; }
}

public sealed class ToolVersionFile
{
    public string Id { get; set; } = string.Empty;

    public string? Name { get; set; }

    public long Size { get; set; }

    public string? DownloadUrl { get; set; }
}
