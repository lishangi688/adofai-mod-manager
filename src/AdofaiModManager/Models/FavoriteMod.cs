using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace AdofaiModManager.Models;

/// <summary>收藏的（在线）mod。</summary>
public sealed class FavoriteMod : INotifyPropertyChanged
{
    private ImageSource? _iconSource;

    public string SiteId { get; set; } = string.Empty;

    public string? ResourceType { get; set; }

    public string Slug { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Summary { get; set; }

    public string? IconUrl { get; set; }

    public string? AuthorsLabel { get; set; }

    public string? VersionLabel { get; set; }

    public string? HomepageUrl { get; set; }

    public string? SourceUrl { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.Now;

    [JsonIgnore]
    public string Initial =>
        string.IsNullOrWhiteSpace(DisplayName) ? "?" : DisplayName.Trim()[..1].ToUpperInvariant();

    [JsonIgnore]
    public string Subtitle => string.IsNullOrWhiteSpace(VersionLabel)
        ? (AuthorsLabel ?? string.Empty)
        : $"{VersionLabel}  ·  {AuthorsLabel}";

    [JsonIgnore]
    public ImageSource? IconSource
    {
        get => _iconSource;
        set
        {
            _iconSource = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconSource)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
