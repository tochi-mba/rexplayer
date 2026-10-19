using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rex.Media.AppCore.Metadata;

/// <summary>
/// Everything fetched for one movie, as the cache keeps it. A record with <see cref="CatalogueId"/>
/// 0 means the lookup ran and found nothing, so the movie is not searched again and again.
/// </summary>
public sealed record EnrichedMovie
{
    /// <summary>The movie in the catalogue, or 0 when the lookup found no match.</summary>
    public required int CatalogueId { get; init; }

    /// <summary>The movie's title as the catalogue spells it.</summary>
    public required string Title { get; init; }

    public int? Year { get; init; }

    public string? Overview { get; init; }

    public IReadOnlyList<string> Genres { get; init; } = [];

    /// <summary>The catalogue's average rating out of 10, when it has one.</summary>
    public double? Rating { get; init; }

    /// <summary>Whether a poster was fetched and sits in the cache under the same key.</summary>
    public bool HasPoster { get; init; }

    /// <summary>Whether the lookup found this movie, rather than recording that nothing matched.</summary>
    [JsonIgnore]
    public bool Found => CatalogueId > 0;

    /// <summary>A short line for a movie's card: its genres and its rating, whichever it has.</summary>
    public string Blurb()
    {
        var parts = Genres.Take(3).ToList();
        if (Rating is { } rating)
        {
            parts.Add(rating.ToString("0.0", CultureInfo.InvariantCulture) + " / 10");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>The record read back from the cache, or null for text that is not one.</summary>
    public static EnrichedMovie? FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            return JsonSerializer.Deserialize(json, EnrichedMovieJson.Default.EnrichedMovie);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, EnrichedMovieJson.Default.EnrichedMovie);
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(EnrichedMovie))]
internal sealed partial class EnrichedMovieJson : JsonSerializerContext;
