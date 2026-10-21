using Microsoft.UI.Xaml;

namespace Rex.Media.App;

/// <summary>
/// A library line or card in the music pages: a song in the Songs table or an album's track list,
/// or an album, artist, mix or playlist card on the music Home. It carries the columns those
/// layouts show apart (artist, album, plays) and, for a card, what opening it and its play button do.
/// Everything else (artwork, the now-playing mark, downloads) is the library line's own.
/// </summary>
public sealed partial class MusicRow(
    string name,
    string? detail,
    string extra,
    object item,
    string placeholder = "♪",
    string badge = "",
    bool isPlaying = false) : LibraryRow(name, detail, extra, item, hasPicture: true, picturePlaceholder: placeholder, badge: badge, isPlaying: isPlaying)
{
    /// <summary>The song's artists, as its line names them.</summary>
    public string Artist { get; init; } = "";

    /// <summary>The song's album.</summary>
    public string Album { get; init; } = "";

    /// <summary>The artists a track has beyond the album's own: "feat. Travis Scott"; empty for none.</summary>
    public string Featured { get; init; } = "";

    public Visibility FeaturedVisibility => Featured.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>How many times it was played, as the table's column says it; empty for never.</summary>
    public string PlaysText { get; init; } = "";

    /// <summary>The song's plays against its album's most played, 0 to 1, drawn as a subtle bar.</summary>
    public double PlayShare { get; init; }

    /// <summary>The width of the play-count bar, out of <see cref="PlayBarWidth"/>.</summary>
    public double PlayBar => Math.Round(PlayShare * PlayBarWidth);

    public const double PlayBarWidth = 48;

    /// <summary>A card's round frame, for an artist's portrait; square for the rest.</summary>
    public bool Round { get; init; }

    /// <summary>The corner radius of the card's picture: a circle for an artist, gently rounded otherwise.</summary>
    public CornerRadius PictureCorners => Round ? new CornerRadius(999) : new CornerRadius(8);

    /// <summary>A picture to show in place of the media's own, such as an artist's fetched photo.</summary>
    public string? PicturePath { get; init; }

    /// <summary>What choosing the card does: open its page.</summary>
    public Action? Open { get; init; }

    /// <summary>What the card's play button does, without opening it.</summary>
    public Action? PlayNow { get; init; }
}
