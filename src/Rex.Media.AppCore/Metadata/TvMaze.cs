using System.Text;
using System.Text.Json;

namespace Rex.Media.AppCore.Metadata;

/// <summary>One show a TV catalogue offered for a name, before any is chosen, with the facts its card wants.</summary>
public sealed record ShowCandidate(int Id, string Name, int? Year, string? Summary, IReadOnlyList<string> Genres, double? Rating, string? ImageUrl)
{
    /// <summary>"Running" or "Ended", as the catalogue says it.</summary>
    public string? Status { get; init; }

    /// <summary>The network or streaming channel that airs it.</summary>
    public string? Network { get; init; }

    public int? RuntimeMinutes { get; init; }

    public string? Language { get; init; }

    /// <summary>When it first aired (ISO); null when unknown.</summary>
    public string? Premiered { get; init; }

    /// <summary>When it ended; null while it runs or when unknown.</summary>
    public string? Ended { get; init; }
}

/// <summary>
/// The TVmaze catalogue (api.tvmaze.com, free, no key): where to ask and how to read its answers.
/// Only the show's name ever leaves the computer, and only once the user turned TV lookups on.
/// Summaries arrive as small HTML and are flattened to plain text here, so nothing downstream
/// ever renders fetched markup.
/// </summary>
public static class TvMaze
{
    /// <summary>Searching shows by name.</summary>
    public static Uri SearchUri(string name) => new("https://api.tvmaze.com/search/shows?q=" + Uri.EscapeDataString(name ?? throw new ArgumentNullException(nameof(name))));

    /// <summary>One show by the identifier the user's own choice named.</summary>
    public static Uri ShowUri(int id) => new($"https://api.tvmaze.com/shows/{id}");

    /// <summary>A show's episodes, every season in one answer.</summary>
    public static Uri EpisodesUri(int id) => new($"https://api.tvmaze.com/shows/{id}/episodes");

    /// <summary>A show's main cast.</summary>
    public static Uri CastUri(int id) => new($"https://api.tvmaze.com/shows/{id}/cast");

    /// <summary>A show's crew, where the creators are named.</summary>
    public static Uri CrewUri(int id) => new($"https://api.tvmaze.com/shows/{id}/crew");

    /// <summary>A show's seasons, each with its own overview and poster.</summary>
    public static Uri SeasonsUri(int id) => new($"https://api.tvmaze.com/shows/{id}/seasons");

    /// <summary>The shows a search answered with, best first, however the answer is shaped; malformed JSON throws <see cref="JsonException"/>.</summary>
    public static IReadOnlyList<ShowCandidate> ParseSearch(string json)
    {
        using var doc = JsonDocument.Parse(json ?? throw new ArgumentNullException(nameof(json)));
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var candidates = new List<ShowCandidate>();
        foreach (var result in doc.RootElement.EnumerateArray())
        {
            if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("show", out var show) && Candidate(show) is { } candidate)
            {
                candidates.Add(candidate);
            }
        }

        return candidates;
    }

    /// <summary>The one show an identifier names, or null when the answer holds none.</summary>
    public static ShowCandidate? ParseShow(string json)
    {
        using var doc = JsonDocument.Parse(json ?? throw new ArgumentNullException(nameof(json)));
        return Candidate(doc.RootElement);
    }

    /// <summary>A show's episodes in season-and-number order, summaries flattened to plain text.</summary>
    public static IReadOnlyList<EnrichedEpisode> ParseEpisodes(string json)
    {
        using var doc = JsonDocument.Parse(json ?? throw new ArgumentNullException(nameof(json)));
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var episodes = new List<EnrichedEpisode>();
        foreach (var episode in doc.RootElement.EnumerateArray())
        {
            if (episode.ValueKind == JsonValueKind.Object
                && Int(episode, "season") is { } season
                && Int(episode, "number") is { } number
                && Text(episode, "name") is { Length: > 0 } name)
            {
                episodes.Add(new EnrichedEpisode(season, number, name, NullIfEmpty(Plain(Text(episode, "summary"))))
                {
                    AirDate = NullIfEmpty(Text(episode, "airdate")),
                    RuntimeMinutes = Int(episode, "runtime") is > 0 and var minutes ? minutes : null,
                    StillUrl = Image(episode),
                });
            }
        }

        return episodes;
    }

    /// <summary>A catalogue's small HTML flattened to one line of plain text: tags dropped, core entities decoded, whitespace collapsed.</summary>
    public static string Plain(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return "";
        }

        var text = new StringBuilder(html.Length);
        var inTag = false;
        foreach (var letter in html)
        {
            if (letter == '<')
            {
                inTag = true;
            }
            else if (letter == '>')
            {
                inTag = false;
                text.Append(' ');
            }
            else if (!inTag)
            {
                text.Append(letter);
            }
        }

        var decoded = text
            .Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
            .Replace("&quot;", "\"").Replace("&#39;", "'").Replace("&apos;", "'").Replace("&nbsp;", " ")
            .ToString();
        return string.Join(' ', decoded.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static ShowCandidate? Candidate(JsonElement show)
    {
        if (show.ValueKind != JsonValueKind.Object || Int(show, "id") is not { } id || Text(show, "name") is not { Length: > 0 } name)
        {
            return null;
        }

        int? year = Text(show, "premiered") is { Length: >= 4 } premiered && int.TryParse(premiered[..4], out var parsed) ? parsed : null;
        var genres = new List<string>();
        if (show.TryGetProperty("genres", out var listed) && listed.ValueKind == JsonValueKind.Array)
        {
            genres.AddRange(listed.EnumerateArray().Where(genre => genre.ValueKind == JsonValueKind.String).Select(genre => genre.GetString()!));
        }

        double? rating = show.TryGetProperty("rating", out var rated) && rated.ValueKind == JsonValueKind.Object
            && rated.TryGetProperty("average", out var average) && average.ValueKind == JsonValueKind.Number ? average.GetDouble() : null;
        string? network = null;
        foreach (var airer in new[] { "network", "webChannel" })
        {
            network ??= show.TryGetProperty(airer, out var channel) && channel.ValueKind == JsonValueKind.Object
                ? NullIfEmpty(Text(channel, "name")) : null;
        }

        return new ShowCandidate(id, name, year, NullIfEmpty(Plain(Text(show, "summary"))), genres, rating, NullIfEmpty(Image(show)))
        {
            Status = NullIfEmpty(Text(show, "status")),
            Network = network,
            RuntimeMinutes = Int(show, "averageRuntime") is > 0 and var averageMinutes
                ? averageMinutes
                : Int(show, "runtime") is > 0 and var minutes ? minutes : null,
            Language = NullIfEmpty(Text(show, "language")),
            Premiered = NullIfEmpty(Text(show, "premiered")),
            Ended = NullIfEmpty(Text(show, "ended")),
        };
    }

    /// <summary>A show's main cast: each person, who they play, and a photo to fetch lazily.</summary>
    public static IReadOnlyList<CastMember> ParseCast(string json)
    {
        using var doc = JsonDocument.Parse(json ?? throw new ArgumentNullException(nameof(json)));
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var cast = new List<CastMember>();
        foreach (var credit in doc.RootElement.EnumerateArray().Take(12))
        {
            if (credit.ValueKind == JsonValueKind.Object
                && credit.TryGetProperty("person", out var person) && person.ValueKind == JsonValueKind.Object
                && Text(person, "name") is { Length: > 0 } name)
            {
                var character = credit.TryGetProperty("character", out var role) && role.ValueKind == JsonValueKind.Object
                    ? NullIfEmpty(Text(role, "name")) : null;
                cast.Add(new CastMember(name, character, Image(person)));
            }
        }

        return cast;
    }

    /// <summary>The creators a show's crew answer names.</summary>
    public static IReadOnlyList<string> ParseCreators(string json)
    {
        using var doc = JsonDocument.Parse(json ?? throw new ArgumentNullException(nameof(json)));
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var creators = new List<string>();
        foreach (var credit in doc.RootElement.EnumerateArray())
        {
            if (credit.ValueKind == JsonValueKind.Object && Text(credit, "type") == "Creator"
                && credit.TryGetProperty("person", out var person) && person.ValueKind == JsonValueKind.Object
                && Text(person, "name") is { Length: > 0 } name && !creators.Contains(name, StringComparer.Ordinal))
            {
                creators.Add(name);
            }
        }

        return creators;
    }

    /// <summary>A show's seasons, each with its own overview and poster to fetch lazily.</summary>
    public static IReadOnlyList<SeasonDetails> ParseSeasons(string json)
    {
        using var doc = JsonDocument.Parse(json ?? throw new ArgumentNullException(nameof(json)));
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var seasons = new List<SeasonDetails>();
        foreach (var season in doc.RootElement.EnumerateArray())
        {
            if (season.ValueKind == JsonValueKind.Object && Int(season, "number") is { } number)
            {
                seasons.Add(new SeasonDetails(number, NullIfEmpty(Plain(Text(season, "summary"))), Image(season)));
            }
        }

        return seasons;
    }

    /// <summary>The best image an element carries, original over medium; null when it has none.</summary>
    private static string? Image(JsonElement element) =>
        element.TryGetProperty("image", out var art) && art.ValueKind == JsonValueKind.Object
            ? NullIfEmpty(Text(art, "original") ?? Text(art, "medium")) : null;

    private static int? Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? NullIfEmpty(string? text) => string.IsNullOrEmpty(text) ? null : text;
}
