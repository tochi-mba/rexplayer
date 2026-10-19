using System.Text;
using System.Text.Json;

namespace Rex.Media.AppCore.Metadata;

/// <summary>One show a TV catalogue offered for a name, before any is chosen.</summary>
public sealed record ShowCandidate(int Id, string Name, int? Year, string? Summary, IReadOnlyList<string> Genres, double? Rating, string? ImageUrl);

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
                episodes.Add(new EnrichedEpisode(season, number, name, NullIfEmpty(Plain(Text(episode, "summary")))));
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
        string? image = show.TryGetProperty("image", out var art) && art.ValueKind == JsonValueKind.Object
            ? Text(art, "original") ?? Text(art, "medium") : null;
        return new ShowCandidate(id, name, year, NullIfEmpty(Plain(Text(show, "summary"))), genres, rating, NullIfEmpty(image));
    }

    private static int? Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? NullIfEmpty(string? text) => string.IsNullOrEmpty(text) ? null : text;
}
