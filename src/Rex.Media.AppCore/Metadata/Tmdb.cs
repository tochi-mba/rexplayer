using System.Globalization;
using System.Text.Json;

namespace Rex.Media.AppCore.Metadata;

/// <summary>One movie the TMDB catalogue offered for a name, before any is chosen.</summary>
public sealed record MovieCandidate(int Id, string Title, int? Year, string? Overview, double? Rating, string? PosterPath, IReadOnlyList<int> GenreIds);

/// <summary>
/// The TMDB catalogue (api.themoviedb.org), reached only with the user's own free API key: where
/// to ask and how to read the answers. This product uses the TMDB API but is not endorsed or
/// certified by TMDB; the Preferences section says so beside the key. Only the movie's title and
/// year ever leave the computer, and only once the user turned movie lookups on and pasted a key.
/// </summary>
public static class Tmdb
{
    /// <summary>Searching movies by title, and by year when the file names one.</summary>
    public static Uri SearchUri(string apiKey, string title, int? year)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentNullException.ThrowIfNull(title);
        var when = year is { } dated ? "&year=" + dated.ToString(CultureInfo.InvariantCulture) : "";
        return new Uri("https://api.themoviedb.org/3/search/movie?api_key=" + Uri.EscapeDataString(apiKey) + when + "&query=" + Uri.EscapeDataString(title));
    }

    /// <summary>The catalogue's genre names, one small answer for every movie.</summary>
    public static Uri GenresUri(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        return new Uri("https://api.themoviedb.org/3/genre/movie/list?api_key=" + Uri.EscapeDataString(apiKey));
    }

    /// <summary>A poster already sized for a card.</summary>
    public static Uri PosterUri(string posterPath) =>
        new("https://image.tmdb.org/t/p/w500" + (posterPath ?? throw new ArgumentNullException(nameof(posterPath))));

    /// <summary>The movies a search answered with, in the catalogue's order; malformed JSON throws <see cref="JsonException"/>.</summary>
    public static IReadOnlyList<MovieCandidate> ParseSearch(string json)
    {
        using var doc = JsonDocument.Parse(json ?? throw new ArgumentNullException(nameof(json)));
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var candidates = new List<MovieCandidate>();
        foreach (var movie in results.EnumerateArray())
        {
            if (movie.ValueKind != JsonValueKind.Object
                || movie.TryGetProperty("id", out var id) is false || id.ValueKind != JsonValueKind.Number
                || Text(movie, "title") is not { Length: > 0 } title)
            {
                continue;
            }

            int? year = Text(movie, "release_date") is { Length: >= 4 } date
                && int.TryParse(date[..4], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
            double? rating = movie.TryGetProperty("vote_average", out var rated) && rated.ValueKind == JsonValueKind.Number
                && rated.GetDouble() is > 0 and var average ? average : null;
            var genres = new List<int>();
            if (movie.TryGetProperty("genre_ids", out var listed) && listed.ValueKind == JsonValueKind.Array)
            {
                genres.AddRange(listed.EnumerateArray().Where(genre => genre.ValueKind == JsonValueKind.Number).Select(genre => genre.GetInt32()));
            }

            var overview = Text(movie, "overview");
            candidates.Add(new MovieCandidate(id.GetInt32(), title, year,
                string.IsNullOrEmpty(overview) ? null : overview, rating, Text(movie, "poster_path"), genres));
        }

        return candidates;
    }

    /// <summary>The genre names by identifier; empty when the answer is not the usual shape.</summary>
    public static IReadOnlyDictionary<int, string> ParseGenres(string json)
    {
        using var doc = JsonDocument.Parse(json ?? throw new ArgumentNullException(nameof(json)));
        var names = new Dictionary<int, string>();
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("genres", out var genres) || genres.ValueKind != JsonValueKind.Array)
        {
            return names;
        }

        foreach (var genre in genres.EnumerateArray())
        {
            if (genre.ValueKind == JsonValueKind.Object
                && genre.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number
                && Text(genre, "name") is { Length: > 0 } name)
            {
                names[id.GetInt32()] = name;
            }
        }

        return names;
    }

    /// <summary>
    /// The candidate <paramref name="title"/> names, or null when none can be taken with
    /// confidence: the title must match exactly after normalizing, a year from the file must agree
    /// within a year (releases straddle premieres), and the catalogue's order breaks ties.
    /// </summary>
    public static MovieCandidate? Pick(string title, int? year, IReadOnlyList<MovieCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(candidates);
        var wanted = MetadataMatch.Normalize(title);
        if (wanted.Length == 0)
        {
            return null;
        }

        var exact = candidates.Where(candidate => MetadataMatch.Normalize(candidate.Title) == wanted)
            .Where(candidate => year is null || candidate.Year is null || Math.Abs(candidate.Year.Value - year.Value) <= 1)
            .ToList();
        if (exact.Count == 0)
        {
            return null;
        }

        // With a year, a dated candidate beats an undated one; otherwise the catalogue's order stands.
        return exact.OrderBy(candidate => year is not null && candidate.Year is null).First();
    }

    /// <summary>One key for a movie wherever it is named: the title and the year, normalized.</summary>
    public static string MovieKey(string title, int? year) =>
        MetadataMatch.Normalize(title ?? throw new ArgumentNullException(nameof(title)))
            + (year is { } dated ? " " + dated.ToString(CultureInfo.InvariantCulture) : "");

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
