using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rex.Media.AppCore.Metadata;

/// <summary>A book's catalogue record, optionally carrying an Open Library description.</summary>
public sealed record EnrichedBook
{
    public const int CurrentSchema = 1;

    public int Schema { get; init; }

    public required string Title { get; init; }

    public string? Author { get; init; }

    /// <summary>True when a writer was learned from a title-only lookup rather than local tags.</summary>
    public bool InferredAuthor { get; init; }

    public string? WorkId { get; init; }

    public string? Synopsis { get; init; }

    [JsonIgnore]
    public bool Current => Schema >= CurrentSchema;

    public string ToJson() => JsonSerializer.Serialize(this, BookJson.Default.EnrichedBook);

    public static EnrichedBook? FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            return JsonSerializer.Deserialize(json, BookJson.Default.EnrichedBook);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(EnrichedBook))]
internal sealed partial class BookJson : JsonSerializerContext;

/// <summary>
/// Open Library's small work search and synopsis: match the complete title AND an author before
/// taking a description. Nothing is inferred from a similarly named work or its cover.
/// </summary>
public static class OpenLibraryBooks
{
    public static string BookKey(string title, string? author) =>
        MetadataMatch.Normalize(title) + "\n" + MetadataMatch.Normalize(author ?? "");

    public static Uri SearchUri(string title, string author) =>
        new("https://openlibrary.org/search.json?title=" + Uri.EscapeDataString(title)
            + "&author=" + Uri.EscapeDataString(author) + "&limit=5&fields=key,title,author_name");

    /// <summary>Search for a book whose local files have no writing credit.</summary>
    public static Uri UnknownAuthorUri(string title) =>
        new("https://openlibrary.org/search.json?title=" + Uri.EscapeDataString(title)
            + "&limit=50&fields=key,title,author_name");

    /// <summary>
    /// Accept a title-only writer only when every result was inspected and every exact-title
    /// work credits the same sole author. Missing, malformed or competing credits stay unknown.
    /// </summary>
    public static (string Author, string WorkId)? MatchUnknownAuthor(string title, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("docs", out var docs) || docs.ValueKind != JsonValueKind.Array
            || !(root.TryGetProperty("numFound", out var total) || root.TryGetProperty("num_found", out total))
            || total.ValueKind != JsonValueKind.Number || !total.TryGetInt32(out var count)
            || count < 0 || count > docs.GetArrayLength())
        {
            return null;
        }

        (string Author, string WorkId)? found = null;
        foreach (var candidate in docs.EnumerateArray())
        {
            if (candidate.ValueKind != JsonValueKind.Object
                || !candidate.TryGetProperty("title", out var named) || named.ValueKind != JsonValueKind.String
                || !string.Equals(MetadataMatch.Normalize(named.GetString()!), MetadataMatch.Normalize(title), StringComparison.Ordinal))
            {
                continue;
            }

            if (!candidate.TryGetProperty("author_name", out var authors) || authors.ValueKind != JsonValueKind.Array
                || authors.GetArrayLength() != 1 || authors[0].ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(authors[0].GetString())
                || !candidate.TryGetProperty("key", out var key) || key.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var id = key.GetString()!.Replace("/works/", "", StringComparison.Ordinal);
            if (!id.StartsWith("OL", StringComparison.Ordinal) || !id.EndsWith('W')
                || id.Length <= 3 || !id[2..^1].All(char.IsAsciiDigit))
            {
                return null;
            }

            var author = authors[0].GetString()!.Trim();
            if (found is { } previous &&
                (!string.Equals(MetadataMatch.Normalize(previous.Author), MetadataMatch.Normalize(author), StringComparison.Ordinal)
                    || previous.WorkId != id))
            {
                return null;
            }

            found = (author, id);
        }

        return found;
    }

    public static Uri WorkUri(string id) => new("https://openlibrary.org/works/" + id + ".json");

    public static Uri PageUri(string id) => new("https://openlibrary.org/works/" + id);

    /// <summary>The work identifier only when a returned candidate agrees on both title and author.</summary>
    public static string? Match(string title, string author, string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("docs", out var docs) || docs.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var candidate in docs.EnumerateArray())
        {
            if (candidate.ValueKind != JsonValueKind.Object ||
                !candidate.TryGetProperty("title", out var named) ||
                named.ValueKind != JsonValueKind.String ||
                !string.Equals(MetadataMatch.Normalize(named.GetString()!), MetadataMatch.Normalize(title), StringComparison.Ordinal) ||
                !candidate.TryGetProperty("author_name", out var authors) || authors.ValueKind != JsonValueKind.Array ||
                !authors.EnumerateArray().Any(found => found.ValueKind == JsonValueKind.String &&
                    string.Equals(MetadataMatch.Normalize(found.GetString()!), MetadataMatch.Normalize(author), StringComparison.Ordinal)) ||
                !candidate.TryGetProperty("key", out var key) || key.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var id = key.GetString()!.Replace("/works/", "", StringComparison.Ordinal);
            if (id.StartsWith("OL", StringComparison.Ordinal) && id.EndsWith('W')
                && id.Length > 3 && id[2..^1].All(char.IsAsciiDigit))
            {
                return id;
            }
        }

        return null;
    }

    /// <summary>A work's description is either a string or an object with a value; absent means no synopsis.</summary>
    public static string? Description(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("description", out var part))
        {
            return null;
        }

        if (part.ValueKind == JsonValueKind.Object && part.TryGetProperty("value", out var value))
        {
            part = value;
        }

        return part.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(part.GetString())
            ? part.GetString()!.Trim()
            : null;
    }
}
