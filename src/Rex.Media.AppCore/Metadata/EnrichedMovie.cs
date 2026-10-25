using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rex.Media.AppCore.Metadata;

/// <summary>
/// Everything fetched for one movie, as the cache keeps it: facts from Wikidata, the synopsis and
/// the poster from English Wikipedia. A record with <see cref="CatalogueId"/> 0 means the lookup
/// ran and found nothing, so the movie is not searched again and again.
/// </summary>
public sealed record EnrichedMovie
{
    /// <summary>The movie's Wikidata item number (83495 for Q83495), or 0 when the lookup found no match.</summary>
    public required int CatalogueId { get; init; }

    /// <summary>The movie's title as Wikidata spells it.</summary>
    public required string Title { get; init; }

    public int? Year { get; init; }

    /// <summary>The opening of the movie's Wikipedia article, CC BY-SA: shown only with <see cref="Wikipedia.DescriptionCredit"/> and a link to <see cref="ArticleUrl"/>.</summary>
    public string? Overview { get; init; }

    /// <summary>The Wikipedia article <see cref="Overview"/> comes from, for its credit.</summary>
    public string? ArticleUrl { get; init; }

    public IReadOnlyList<string> Genres { get; init; } = [];

    /// <summary>Whether a poster was fetched and sits in the cache under the same key.</summary>
    public bool HasPoster { get; init; }

    public int? RuntimeMinutes { get; init; }

    /// <summary>The studio (the first production company Wikidata names).</summary>
    public string? Studio { get; init; }

    /// <summary>The original language's English name ("English").</summary>
    public string? OriginalLanguage { get; init; }

    /// <summary>The first country of origin Wikidata names.</summary>
    public string? Country { get; init; }

    /// <summary>The MPA rating ("R", say), when Wikidata records one.</summary>
    public string? ContentRating { get; init; }

    /// <summary>A wide backdrop to fetch lazily at display time, when one is known.</summary>
    public string? BackdropUrl { get; init; }

    /// <summary>The main cast, in billing order, photos (from Wikimedia Commons) to be fetched lazily.</summary>
    public IReadOnlyList<CastMember> Cast { get; init; } = [];

    public IReadOnlyList<string> Directors { get; init; } = [];

    public IReadOnlyList<string> Writers { get; init; } = [];

    /// <summary>YouTube keys for trailers, opened in the browser, never embedded.</summary>
    public IReadOnlyList<string> TrailerKeys { get; init; } = [];

    /// <summary>The series this movie is part of ("Part of The Matrix"), when it is.</summary>
    public string? Collection { get; init; }

    /// <summary>The movie's IMDb identifier ("tt0133093"), when Wikidata records one.</summary>
    public string? ImdbId { get; init; }

    /// <summary>Whether the lookup found this movie, rather than recording that nothing matched.</summary>
    [JsonIgnore]
    public bool Found => CatalogueId > 0;

    /// <summary>
    /// The shape of record this build writes. Every record of an older shape (those from the
    /// catalogue earlier versions used, matches and no-matches alike) is fetched again in the
    /// background and replaced; a found one still shows until then.
    /// </summary>
    public const int CurrentSchema = 3;

    /// <summary>Which shape of record this is; records written before the field existed read as 0.</summary>
    public int Schema { get; init; }

    /// <summary>Whether the record needs no new fetch: written in this build's shape.</summary>
    [JsonIgnore]
    public bool Current => Schema >= CurrentSchema;

    /// <summary>A short line for a movie's card: genres, content rating and runtime, whichever it has.</summary>
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
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, EnrichedMovieJson.Default.EnrichedMovie);
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(EnrichedMovie))]
internal sealed partial class EnrichedMovieJson : JsonSerializerContext;
