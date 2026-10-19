using System.Globalization;
using System.Text.Json;

namespace Rex.Media.AppCore.Metadata;

/// <summary>One movie the TMDB catalogue offered for a name, before any is chosen.</summary>
public sealed record MovieCandidate(int Id, string Title, int? Year, string? Overview, double? Rating, string? PosterPath, IReadOnlyList<int> GenreIds);

/// <summary>The fuller facts one movie's details answer carries, beyond what the search already said.</summary>
public sealed record MovieDetails(
    string? Tagline,
    int? RuntimeMinutes,
    string? Status,
    string? Studio,
    string? OriginalLanguage,
    string? ContentRating,
    IReadOnlyList<string> Genres,
    string? BackdropUrl,
    IReadOnlyList<CastMember> Cast,
    IReadOnlyList<string> Directors,
    IReadOnlyList<string> Writers,
    IReadOnlyList<string> TrailerKeys,
    IReadOnlyList<TitleRef> Recommendations,
    string? Collection);

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

    /// <summary>Everything about one movie in a single answer: facts, credits, trailers, suggestions and certifications.</summary>
    public static Uri DetailsUri(string apiKey, int id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        return new Uri($"https://api.themoviedb.org/3/movie/{id}?api_key=" + Uri.EscapeDataString(apiKey)
            + "&append_to_response=credits%2Cvideos%2Crecommendations%2Crelease_dates");
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

    /// <summary>The fuller facts one movie's details answer carries; null when the answer holds no movie.</summary>
    public static MovieDetails? ParseDetails(string json)
    {
        using var doc = JsonDocument.Parse(json ?? throw new ArgumentNullException(nameof(json)));
        var movie = doc.RootElement;
        if (movie.ValueKind != JsonValueKind.Object || !movie.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        var genres = new List<string>();
        if (movie.TryGetProperty("genres", out var listed) && listed.ValueKind == JsonValueKind.Array)
        {
            genres.AddRange(listed.EnumerateArray()
                .Where(genre => genre.ValueKind == JsonValueKind.Object)
                .Select(genre => Text(genre, "name"))
                .Where(name => !string.IsNullOrEmpty(name))!);
        }

        string? studio = null;
        if (movie.TryGetProperty("production_companies", out var companies) && companies.ValueKind == JsonValueKind.Array)
        {
            studio = companies.EnumerateArray().Where(company => company.ValueKind == JsonValueKind.Object)
                .Select(company => Text(company, "name")).FirstOrDefault(name => !string.IsNullOrEmpty(name));
        }

        string? collection = movie.TryGetProperty("belongs_to_collection", out var belongs) && belongs.ValueKind == JsonValueKind.Object
            ? Text(belongs, "name") : null;
        var cast = new List<CastMember>();
        var directors = new List<string>();
        var writers = new List<string>();
        if (movie.TryGetProperty("credits", out var credits) && credits.ValueKind == JsonValueKind.Object)
        {
            if (credits.TryGetProperty("cast", out var billed) && billed.ValueKind == JsonValueKind.Array)
            {
                foreach (var person in billed.EnumerateArray().Take(12))
                {
                    if (person.ValueKind == JsonValueKind.Object && Text(person, "name") is { Length: > 0 } name)
                    {
                        cast.Add(new CastMember(name, Text(person, "character"),
                            Text(person, "profile_path") is { Length: > 0 } photo ? "https://image.tmdb.org/t/p/w185" + photo : null));
                    }
                }
            }

            if (credits.TryGetProperty("crew", out var crew) && crew.ValueKind == JsonValueKind.Array)
            {
                foreach (var person in crew.EnumerateArray())
                {
                    if (person.ValueKind != JsonValueKind.Object || Text(person, "name") is not { Length: > 0 } name)
                    {
                        continue;
                    }

                    if (Text(person, "job") == "Director" && !directors.Contains(name, StringComparer.Ordinal))
                    {
                        directors.Add(name);
                    }
                    else if (Text(person, "department") == "Writing" && writers.Count < 5 && !writers.Contains(name, StringComparer.Ordinal))
                    {
                        writers.Add(name);
                    }
                }
            }
        }

        var trailers = new List<string>();
        if (movie.TryGetProperty("videos", out var videos) && videos.ValueKind == JsonValueKind.Object
            && videos.TryGetProperty("results", out var clips) && clips.ValueKind == JsonValueKind.Array)
        {
            trailers.AddRange(clips.EnumerateArray()
                .Where(clip => clip.ValueKind == JsonValueKind.Object && Text(clip, "site") == "YouTube" && Text(clip, "type") == "Trailer")
                .Select(clip => Text(clip, "key"))
                .Where(key => !string.IsNullOrEmpty(key))
                .Take(3)!);
        }

        var suggested = new List<TitleRef>();
        if (movie.TryGetProperty("recommendations", out var recommended) && recommended.ValueKind == JsonValueKind.Object
            && recommended.TryGetProperty("results", out var titles) && titles.ValueKind == JsonValueKind.Array)
        {
            foreach (var suggestion in titles.EnumerateArray().Take(20))
            {
                if (suggestion.ValueKind == JsonValueKind.Object && Text(suggestion, "title") is { Length: > 0 } title)
                {
                    int? when = Text(suggestion, "release_date") is { Length: >= 4 } date
                        && int.TryParse(date[..4], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
                    suggested.Add(new TitleRef(title, when));
                }
            }
        }

        string? certification = null;
        if (movie.TryGetProperty("release_dates", out var certified) && certified.ValueKind == JsonValueKind.Object
            && certified.TryGetProperty("results", out var countries) && countries.ValueKind == JsonValueKind.Array)
        {
            certification = countries.EnumerateArray()
                .Where(country => country.ValueKind == JsonValueKind.Object && Text(country, "iso_3166_1") == "US"
                    && country.TryGetProperty("release_dates", out var dates) && dates.ValueKind == JsonValueKind.Array)
                .SelectMany(country => country.GetProperty("release_dates").EnumerateArray())
                .Where(release => release.ValueKind == JsonValueKind.Object)
                .Select(release => Text(release, "certification"))
                .FirstOrDefault(rating => !string.IsNullOrEmpty(rating));
        }

        return new MovieDetails(
            Tagline: string.IsNullOrEmpty(Text(movie, "tagline")) ? null : Text(movie, "tagline"),
            RuntimeMinutes: movie.TryGetProperty("runtime", out var runtime) && runtime.ValueKind == JsonValueKind.Number
                && runtime.GetInt32() is > 0 and var minutes ? minutes : null,
            Status: Text(movie, "status"),
            Studio: studio,
            OriginalLanguage: Text(movie, "original_language"),
            ContentRating: certification,
            Genres: genres,
            BackdropUrl: Text(movie, "backdrop_path") is { Length: > 0 } backdrop ? "https://image.tmdb.org/t/p/w1280" + backdrop : null,
            Cast: cast,
            Directors: directors,
            Writers: writers,
            TrailerKeys: trailers,
            Recommendations: suggested,
            Collection: collection);
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
