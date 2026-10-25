using System.Globalization;
using Rex.Media.AppCore.Player;
using Rex.Media.Primitives;

namespace Rex.Media.AppCore.Shell;

/// <summary>What kind of media Windows' media controls describe (its order is theirs).</summary>
public enum MediaCardKind
{
    Unknown,
    Music,
    Video,
    Image,
}

/// <summary>
/// What Windows' media controls show of the playing item: in the volume flyout, on the lock screen
/// and wherever else Windows lists what is playing. Never a file's location: the card is seen by
/// anyone who looks at the lock screen.
/// </summary>
/// <param name="Kind">Music, video or a picture.</param>
/// <param name="Title">The title.</param>
/// <param name="Subtitle">For music, the artist; for video, the show and episode, or the artist; for a picture, nothing.</param>
/// <param name="AlbumArtist">Music only: the album's artist.</param>
/// <param name="AlbumTitle">Music only: the album.</param>
/// <param name="TrackNumber">Music only: the track's number on its album, or 0.</param>
public sealed record MediaCard(MediaCardKind Kind, string Title, string Subtitle, string AlbumArtist, string AlbumTitle, int TrackNumber)
{
    /// <summary>The card for what <paramref name="player"/> is playing, or null while nothing is (or it is still opening).</summary>
    public static MediaCard? For(PlayerController player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return Describe(TransportAvailability.MediaOf(player), player.Title, player.Artist, player.Info?.Metadata, player.Item?.Location,
            player.Info?.FirstTrack(MediaKind.Video) is not null);
    }

    /// <summary>
    /// The card for <paramref name="media"/> titled <paramref name="title"/>; null for nothing, or for
    /// an item still opening, whose kind and title are not known yet.
    /// </summary>
    public static MediaCard? Describe(TransportMedia media, string title, string? artist, IReadOnlyDictionary<string, string>? metadata, string? location, bool hasVideo)
    {
        ArgumentNullException.ThrowIfNull(title);
        if (media is TransportMedia.None or TransportMedia.Unknown)
        {
            return null;
        }

        var shown = Shown(title);
        var by = Shown(artist ?? "");
        var kind = media switch
        {
            TransportMedia.Picture => MediaCardKind.Image,
            TransportMedia.Video => MediaCardKind.Video,
            TransportMedia.Stream when hasVideo => MediaCardKind.Video,
            _ => MediaCardKind.Music,
        };

        return kind switch
        {
            MediaCardKind.Image => new MediaCard(kind, shown, "", "", "", 0),
            MediaCardKind.Video => new MediaCard(kind, shown, location is not null && EpisodeGrouping.Identify(location) is { } episode ? Episode(episode) : by, "", "", 0),
            _ => new MediaCard(kind, shown, by, Shown(Tag(metadata, MetadataKeys.AlbumArtist)), Shown(Tag(metadata, MetadataKeys.Album)), TrackNumberOf(Tag(metadata, MetadataKeys.Track))),
        };
    }

    /// <summary>"The Show, season 1, episode 2", or "episodes 2 to 3" for a file that holds two.</summary>
    public static string Episode(EpisodeIdentity episode)
    {
        ArgumentNullException.ThrowIfNull(episode);
        var numbers = episode.Through is { } last && last != episode.Episode
            ? string.Create(CultureInfo.InvariantCulture, $"episodes {episode.Episode} to {last}")
            : string.Create(CultureInfo.InvariantCulture, $"episode {episode.Episode}");
        return string.Create(CultureInfo.InvariantCulture, $"{episode.Show}, season {episode.Season}, {numbers}");
    }

    /// <summary>The number before any "/total" in a track tag ("3/12" is 3); 0 when there is none.</summary>
    public static int TrackNumberOf(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        var slash = tag.IndexOf('/', StringComparison.Ordinal);
        return int.TryParse(slash >= 0 ? tag.AsSpan(0, slash) : tag.AsSpan(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0 ? number : 0;
    }

    /// <summary>Text safe to show on the lock screen: a location (a whole path, or a web address) is never shown.</summary>
    public static string Shown(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var trimmed = text.Trim();
        return Path.IsPathFullyQualified(trimmed) || trimmed.StartsWith(@"\\", StringComparison.Ordinal) || (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.Scheme is "file" or "http" or "https")
            ? ""
            : trimmed;
    }

    private static string Tag(IReadOnlyDictionary<string, string>? metadata, string key) => metadata?.GetValueOrDefault(key) ?? "";
}
