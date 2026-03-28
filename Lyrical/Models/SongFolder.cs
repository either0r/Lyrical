using System.Collections.ObjectModel;

namespace Lyrical.Models;

public sealed class SongFolder
{
    public string Name { get; init; } = string.Empty;

    public string RelativePath { get; init; } = string.Empty;

    public ObservableCollection<SongFolder> Children { get; } = [];

    public int DirectSongCount { get; set; }

    public int TotalSongCount { get; set; }

    public bool IsRoot => string.IsNullOrWhiteSpace(RelativePath);

    public string DisplayName => IsRoot
        ? $"All Songs ({TotalSongCount})"
        : $"{Name} ({TotalSongCount})";
}