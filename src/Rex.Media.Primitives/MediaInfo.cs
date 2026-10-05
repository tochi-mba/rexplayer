namespace Rex.Media.Primitives;

/// <summary>What a demuxer learned about a file when it opened it.</summary>
public sealed record MediaInfo
{
    /// <summary>The container's name as shown in Media Information: "WAVE", "MP4", "Matroska".</summary>
    public required string FormatName { get; init; }

    public required IReadOnlyList<TrackInfo> Tracks { get; init; }

    public MediaTime Duration { get; init; } = MediaTime.Unknown;

    /// <summary>Whether seeking is possible at all (false for live streams and some broken files).</summary>
    public bool IsSeekable { get; init; } = true;

    /// <summary>Title, artist, album and the rest, keyed by <see cref="MetadataKeys"/> names.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<Chapter> Chapters { get; init; } = [];

    /// <summary>Embedded cover art (encoded image bytes), if the file carries any.</summary>
    public byte[]? CoverArt { get; init; }

    public TrackInfo? FirstTrack(MediaKind kind) => Tracks.FirstOrDefault(track => track.Kind == kind);
}

/// <summary>A named point in the timeline.</summary>
public sealed record Chapter(MediaTime Start, string Title);

/// <summary>Canonical metadata names, so every tag format maps into one vocabulary.</summary>
public static class MetadataKeys
{
    public const string Title = "title";
    public const string Artist = "artist";
    public const string Album = "album";
    public const string AlbumArtist = "album_artist";
    public const string Genre = "genre";
    public const string Date = "date";
    public const string Track = "track";
    public const string Disc = "disc";
    public const string Comment = "comment";
    public const string Composer = "composer";
    public const string Copyright = "copyright";
    public const string Encoder = "encoder";
    public const string Lyrics = "lyrics";
    public const string ReplayGainTrackGain = "replaygain_track_gain";
    public const string ReplayGainTrackPeak = "replaygain_track_peak";
    public const string ReplayGainAlbumGain = "replaygain_album_gain";
    public const string ReplayGainAlbumPeak = "replaygain_album_peak";
}
