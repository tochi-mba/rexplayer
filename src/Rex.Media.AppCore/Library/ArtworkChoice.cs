using System.Text.RegularExpressions;

namespace Rex.Media.AppCore.Library;

/// <summary>Where a card's picture comes from, best first.</summary>
public enum ArtworkSourceKind
{
    /// <summary>A picture the user keeps in the show's, season's or movie's folder.</summary>
    FolderArt,

    /// <summary>The online catalogue's poster, already in the metadata cache.</summary>
    OnlinePoster,

    /// <summary>A season's own picture ("season01-poster.jpg"), when the show has no poster of its own.</summary>
    SeasonArt,

    /// <summary>A picture beside one episode, named after it ("Pilot.jpg", "Pilot-thumb.jpg").</summary>
    EpisodeArt,

    /// <summary>The catalogue's still of one episode, in the metadata cache.</summary>
    EpisodeStill,

    /// <summary>A strong frame of the video itself, made and cached by rexplayer.</summary>
    GeneratedFrame,
}

/// <summary>A card's picture: where it comes from, and the image file for every kind but a generated frame.</summary>
public sealed record ArtworkSource(ArtworkSourceKind Kind, string? Path);

/// <summary>
/// Which picture a show, an episode or a movie shows (LIB-05). A show: art in its folder or its
/// season's folder, then the catalogue's poster, then a season's own art, then a strong frame of its
/// first episode cut to a poster's shape. An episode: art beside it, then the catalogue's still,
/// then a frame from inside the episode. A movie: art beside it or in its folder, then the
/// catalogue's poster, then a frame cut to a poster's shape. The disk is only asked through
/// <c>exists</c>, so every decision is tested without files.
/// </summary>
public static partial class ArtworkChoice
{
    /// <summary>The picture file types looked for, in order.</summary>
    public static IReadOnlyList<string> Extensions { get; } = [".jpg", ".jpeg", ".png", ".webp"];

    /// <summary>The names a folder's own art goes by, in order.</summary>
    public static IReadOnlyList<string> FolderNames { get; } = ["poster", "folder", "show", "cover", "default"];

    /// <summary>Folders that hold a library rather than one show: their art is never a show's.</summary>
    private static readonly HashSet<string> Libraries = new(StringComparer.OrdinalIgnoreCase)
    {
        "Movies", "Movie", "Films", "Film", "Videos", "Video", "My Videos", "TV", "TV Shows", "TV Series", "Shows",
        "Series", "Downloads", "Download", "Media", "Library", "Anime", "Cartoons", "Kids",
    };

    /// <summary>
    /// A show's poster, from the path of its first episode. <paramref name="onlinePoster"/> is the
    /// cached catalogue poster, or null when there is none (enrichment off, no match, not fetched yet).
    /// </summary>
    public static ArtworkSource ShowPoster(string firstEpisode, int season, string? onlinePoster, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(firstEpisode);
        ArgumentNullException.ThrowIfNull(exists);
        var (show, seasonFolder) = Folders(firstEpisode);
        var places = new[] { show, seasonFolder }.Where(folder => folder is not null).Cast<string>().ToList();
        if (Find(places, FolderNames, exists) is { } folderArt)
        {
            return new ArtworkSource(ArtworkSourceKind.FolderArt, folderArt);
        }

        if (onlinePoster is not null)
        {
            return new ArtworkSource(ArtworkSourceKind.OnlinePoster, onlinePoster);
        }

        return Find(places, SeasonNames(season), exists) is { } seasonArt
            ? new ArtworkSource(ArtworkSourceKind.SeasonArt, seasonArt)
            : new ArtworkSource(ArtworkSourceKind.GeneratedFrame, null);
    }

    /// <summary>
    /// One episode's picture: art beside it, then <paramref name="still"/> (the cached catalogue still,
    /// or null), then a frame from inside it. The show's poster is never an episode's picture, so a
    /// season of episodes is told apart at a glance.
    /// </summary>
    public static ArtworkSource EpisodePicture(string episode, string? still, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(episode);
        ArgumentNullException.ThrowIfNull(exists);
        if (Beside(episode, ["", "-thumb", ".thumb"], exists) is { } own)
        {
            return new ArtworkSource(ArtworkSourceKind.EpisodeArt, own);
        }

        return still is not null
            ? new ArtworkSource(ArtworkSourceKind.EpisodeStill, still)
            : new ArtworkSource(ArtworkSourceKind.GeneratedFrame, null);
    }

    /// <summary>A movie's poster: art beside it ("Film.poster.jpg", "Film.jpg") or in its own folder, then the cached catalogue poster, then a frame.</summary>
    public static ArtworkSource MoviePoster(string movie, string? onlinePoster, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(movie);
        ArgumentNullException.ThrowIfNull(exists);
        if (Beside(movie, [".poster", "-poster", ""], exists) is { } own)
        {
            return new ArtworkSource(ArtworkSourceKind.FolderArt, own);
        }

        var folder = Path.GetDirectoryName(movie) is { Length: > 0 } parent ? parent : null;
        if (folder is not null && !Libraries.Contains(Path.GetFileName(folder)) && Find([folder], FolderNames, exists) is { } folderArt)
        {
            return new ArtworkSource(ArtworkSourceKind.FolderArt, folderArt);
        }

        return onlinePoster is not null
            ? new ArtworkSource(ArtworkSourceKind.OnlinePoster, onlinePoster)
            : new ArtworkSource(ArtworkSourceKind.GeneratedFrame, null);
    }

    /// <summary>The metadata cache's key for one episode's still, from the show's normalised name.</summary>
    public static string StillKey(string show, int season, int episode)
    {
        ArgumentNullException.ThrowIfNull(show);
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{show}-s{season:00}e{episode:00}");
    }

    /// <summary>
    /// A dark tint of its own for each show, so its tiles are told apart before their pictures come:
    /// a hue from the name, at a fixed low brightness that white text reads well on.
    /// </summary>
    public static (byte Red, byte Green, byte Blue) Tint(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var hash = 17u;
        foreach (var character in name.ToUpperInvariant())
        {
            hash = unchecked((hash * 31) + character);
        }

        // HSV with saturation 0.45 and value 0.3: every hue stays dark.
        var hue = hash % 360 / 60.0;
        var (high, low) = (0.3 * 255, 0.3 * 0.55 * 255);
        var rising = low + ((high - low) * (hue - Math.Floor(hue)));
        var falling = high - ((high - low) * (hue - Math.Floor(hue)));
        var (red, green, blue) = (int)hue switch
        {
            0 => (high, rising, low),
            1 => (falling, high, low),
            2 => (low, high, rising),
            3 => (low, falling, high),
            4 => (rising, low, high),
            _ => (high, low, falling),
        };
        return ((byte)Math.Round(red), (byte)Math.Round(green), (byte)Math.Round(blue));
    }

    /// <summary>
    /// The show's folder and the season's folder an episode sits in. A folder that names a season
    /// ("Season 2", "S02", "Show S02 (720p)", "Specials") is the season's, and its parent the show's;
    /// otherwise the episode's own folder is the show's. A folder that holds a library is neither.
    /// </summary>
    public static (string? Show, string? Season) Folders(string episode)
    {
        ArgumentNullException.ThrowIfNull(episode);
        var folder = Path.GetDirectoryName(episode) is { Length: > 0 } parent ? parent : null;
        if (folder is null)
        {
            return (null, null);
        }

        if (!SeasonFolder().IsMatch(Path.GetFileName(folder)))
        {
            return (Owned(folder), null);
        }

        return (Path.GetDirectoryName(folder) is { Length: > 0 } outer ? Owned(outer) : null, folder);
    }

    /// <summary>A season's own art names: "season02-poster", "season02", "season 2", "season-specials-poster".</summary>
    private static IReadOnlyList<string> SeasonNames(int season) => season == 0
        ? ["season-specials-poster", "season00-poster", "season-specials", "specials"]
        : [$"season{season:00}-poster", $"season{season:00}", $"season {season}", $"season{season}"];

    private static string? Owned(string folder) => Libraries.Contains(Path.GetFileName(folder)) ? null : folder;

    /// <summary>The first picture named one of <paramref name="names"/> in the first of <paramref name="folders"/> that has one.</summary>
    private static string? Find(IReadOnlyList<string> folders, IReadOnlyList<string> names, Func<string, bool> exists)
    {
        foreach (var folder in folders)
        {
            foreach (var name in names)
            {
                foreach (var extension in Extensions)
                {
                    var candidate = Path.Combine(folder, name + extension);
                    if (exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>A picture named after <paramref name="file"/> with one of <paramref name="suffixes"/>, beside it.</summary>
    private static string? Beside(string file, IReadOnlyList<string> suffixes, Func<string, bool> exists)
    {
        var folder = Path.GetDirectoryName(file) ?? "";
        var stem = Path.GetFileNameWithoutExtension(file);
        return Find([folder], [.. suffixes.Select(suffix => stem + suffix)], exists);
    }

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:(?:season|series|staffel|saison|temporada)[ ._-]*\d{1,3}|s\d{1,3}|specials?)(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeasonFolder();
}
