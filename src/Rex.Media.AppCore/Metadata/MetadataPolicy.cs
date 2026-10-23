using Rex.Media.Library;
using Rex.Media.Settings;

namespace Rex.Media.AppCore.Metadata;

/// <summary>The kinds of online lookup the library can ask for.</summary>
public enum MetadataWork
{
    TvShow,
    Movie,
    Music,
}

/// <summary>
/// Whether an online lookup is allowed at all: each kind only once the user turned its switch on
/// (no catalogue needs a key or a sign-up), and nothing for a file under an excluded folder. The
/// decision is made before any name leaves the computer, so an excluded file is never even searched for.
/// </summary>
public static class MetadataPolicy
{
    /// <summary>The credit Preferences and Help, About give the free services everything fetched comes from.</summary>
    public const string DataCredit = "Data from Wikidata, TVmaze, MusicBrainz and the Cover Art Archive. Descriptions from Wikipedia, under CC BY-SA. Artist photos from Wikimedia Commons, under their own free licences.";

    /// <summary>Whether <paramref name="path"/> lies in or under one of <paramref name="excluded"/>.</summary>
    public static bool Excludes(IReadOnlyList<string> excluded, string path)
    {
        ArgumentNullException.ThrowIfNull(excluded);
        ArgumentNullException.ThrowIfNull(path);
        return excluded.Any(folder => MediaLibrary.Holds(folder, path));
    }

    /// <summary>
    /// Shared albums, shows and artists may span private and public folders.
    /// A grouped lookup is allowed only when at least one actual source file
    /// permits the requested kind of enrichment.
    /// </summary>
    public static bool AllowsAny(MetadataSettings settings, MetadataWork kind, IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(paths);
        return paths.Any(path => !string.IsNullOrWhiteSpace(path) && Allows(settings, kind, path));
    }

    /// <summary>Whether a lookup of <paramref name="kind"/> may run for the file at <paramref name="path"/>.</summary>
    public static bool Allows(MetadataSettings settings, MetadataWork kind, string path)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var on = kind switch
        {
            MetadataWork.TvShow => settings.TvShows,
            MetadataWork.Movie => settings.Movies,
            MetadataWork.Music => settings.Music,
            _ => false,
        };
        return on && !Excludes(settings.Excluded, path);
    }
}
