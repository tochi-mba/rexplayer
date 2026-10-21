namespace Rex.Media.AppCore.Metadata;

/// <summary>
/// "More like this" for a movie, from the library alone: with no recommendation service to ask,
/// the movies already in the library are ranked by what their cached facts share with this one
/// (the series, the directors, the cast, the genres), and only those sharing something are offered.
/// </summary>
public static class MoreLikeThis
{
    /// <summary>How many titles a list offers at most.</summary>
    public const int Limit = 8;

    /// <summary>What sharing each kind of fact is worth: one series outweighs a director, who outweighs an actor, who outweighs a genre.</summary>
    public const int SeriesWeight = 6;

    public const int DirectorWeight = 4;

    public const int CastWeight = 2;

    public const int GenreWeight = 1;

    /// <summary>
    /// The library's movies most like <paramref name="movie"/>, best first, ties kept in the
    /// library's order; the movie itself, movies with no facts and movies sharing nothing are left
    /// out, and a movie held in several files is offered once, as its first.
    /// </summary>
    public static IReadOnlyList<T> Rank<T>(EnrichedMovie movie, IReadOnlyList<(T Item, EnrichedMovie Facts)> library, int limit = Limit)
    {
        ArgumentNullException.ThrowIfNull(movie);
        ArgumentNullException.ThrowIfNull(library);
        return [.. library
            .Where(other => other.Facts.Found && other.Facts.CatalogueId != movie.CatalogueId)
            .DistinctBy(other => other.Facts.CatalogueId)
            .Select(other => (other.Item, Score: Score(movie, other.Facts)))
            .Where(scored => scored.Score > 0)
            .OrderByDescending(scored => scored.Score)
            .Take(Math.Max(0, limit))
            .Select(scored => scored.Item)];
    }

    /// <summary>How much two movies' facts share; 0 when nothing.</summary>
    public static int Score(EnrichedMovie one, EnrichedMovie other)
    {
        ArgumentNullException.ThrowIfNull(one);
        ArgumentNullException.ThrowIfNull(other);
        var series = one.Collection is { Length: > 0 } named && string.Equals(named, other.Collection, StringComparison.OrdinalIgnoreCase) ? SeriesWeight : 0;
        return series
            + (Shared(one.Directors, other.Directors) * DirectorWeight)
            + (Shared(one.Cast.Select(member => member.Name), other.Cast.Select(member => member.Name)) * CastWeight)
            + (Shared(one.Genres, other.Genres) * GenreWeight);
    }

    private static int Shared(IEnumerable<string> one, IEnumerable<string> other) =>
        one.Intersect(other, StringComparer.OrdinalIgnoreCase).Count();
}
