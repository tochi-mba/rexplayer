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

    public string? Tagline { get; init; }

    public int? RuntimeMinutes { get; init; }

    /// <summary>"Released", "In Production" and the like, as the catalogue says it.</summary>
    public string? Status { get; init; }

    /// <summary>The studio (the first production company the catalogue names).</summary>
    public string? Studio { get; init; }

    public string? OriginalLanguage { get; init; }

    /// <summary>The content rating ("PG-13", say), from the catalogue's US certifications.</summary>
    public string? ContentRating { get; init; }

    /// <summary>A wide backdrop to fetch lazily at display time, when the catalogue has one.</summary>
    public string? BackdropUrl { get; init; }

    /// <summary>The main cast, in billing order, photos to be fetched lazily.</summary>
    public IReadOnlyList<CastMember> Cast { get; init; } = [];

    public IReadOnlyList<string> Directors { get; init; } = [];

    public IReadOnlyList<string> Writers { get; init; } = [];

    /// <summary>YouTube keys for trailers, opened in the browser, never embedded.</summary>
    public IReadOnlyList<string> TrailerKeys { get; init; } = [];

    /// <summary>Titles the catalogue suggests beside this one; shown only when the library holds them.</summary>
    public IReadOnlyList<TitleRef> Recommendations { get; init; } = [];

    /// <summary>The collection this movie belongs to ("Part of the Rocky collection"), when it does.</summary>
    public string? Collection { get; init; }

    /// <summary>Whether the lookup found this movie, rather than recording that nothing matched.</summary>
    [JsonIgnore]
    public bool Found => CatalogueId > 0;

    /// <summary>A short line for a movie's card: genres, content rating, runtime and rating, whichever it has.</summary>
    public string Blurb()
    {
        var parts = Genres.Take(3).ToList();
        if (ContentRating is { Length: > 0 } certified)
        {
            parts.Add(certified);
        }

        if (RuntimeMinutes is { } minutes)
        {
            parts.Add(minutes.ToString(CultureInfo.InvariantCulture) + " min");
        }

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
            var movie = JsonSerializer.Deserialize(json, EnrichedMovieJson.Default.EnrichedMovie);
            return movie is null ? null : movie with
            {
                Genres = movie.Genres ?? [],
                Cast = movie.Cast ?? [],
                Directors = movie.Directors ?? [],
                Writers = movie.Writers ?? [],
                TrailerKeys = movie.TrailerKeys ?? [],
                Recommendations = movie.Recommendations ?? [],
            };
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
