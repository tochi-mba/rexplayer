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
/// Whether an online lookup is allowed at all: each kind only once the user turned it on, movies
/// only once a TMDB key is pasted, and nothing for a file under an excluded folder. The decision
/// is made before any name leaves the computer, so an excluded file is never even searched for.
/// </summary>
public static class MetadataPolicy
{
    /// <summary>Whether <paramref name="path"/> lies in or under one of <paramref name="excluded"/>.</summary>
    public static bool Excludes(IReadOnlyList<string> excluded, string path)
    {
        ArgumentNullException.ThrowIfNull(excluded);
        ArgumentNullException.ThrowIfNull(path);
        return excluded.Any(folder => MediaLibrary.Holds(folder, path));
    }

    /// <summary>Whether movie lookups can run: the switch is on and a TMDB key is pasted.</summary>
    public static bool MoviesReady(MetadataSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Movies && settings.TmdbApiKey.Length > 0;
    }

    /// <summary>Whether a lookup of <paramref name="kind"/> may run for the file at <paramref name="path"/>.</summary>
    public static bool Allows(MetadataSettings settings, MetadataWork kind, string path)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var on = kind switch
        {
            MetadataWork.TvShow => settings.TvShows,
            MetadataWork.Movie => MoviesReady(settings),
            MetadataWork.Music => settings.Music,
            _ => false,
        };
        return on && !Excludes(settings.Excluded, path);
    }
}
