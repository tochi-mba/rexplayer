// Spec: XSPF Version 1 section 4.1.1.2.14 (a track: location, title, creator, duration and extension data), the model every playlist format is read into.
namespace Rex.Media.Library.Playlists;

/// <summary>
/// One entry of a playlist file: where the media is (a path or a URL) and what the playlist says
/// about it. <see cref="Start"/> and <see cref="End"/> mark a part of a file, as a cue sheet's
/// tracks of a whole-album file are.
/// </summary>
public sealed record PlaylistEntry(string Location)
{
    public string? Title { get; init; }

    public string? Artist { get; init; }

    public TimeSpan? Duration { get; init; }

    /// <summary>Where in the file the entry starts.</summary>
    public TimeSpan Start { get; init; }

    /// <summary>Where in the file the entry ends, or null for the end of the file.</summary>
    public TimeSpan? End { get; init; }

    /// <summary>Data other programs keep with the entry, by the program's name, so it survives being saved again.</summary>
    public IReadOnlyDictionary<string, string> Extensions { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
}
