using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rex.Media.AppCore.Metadata;

/// <summary>One episode as a catalogue knows it: its place, its title and, when there is one, a plain-text synopsis.</summary>
public sealed record EnrichedEpisode(int Season, int Number, string Name, string? Summary)
{
    /// <summary>When it first aired, as the catalogue writes it (ISO, "2011-04-17"); null when unknown.</summary>
    public string? AirDate { get; init; }

    public int? RuntimeMinutes { get; init; }

    /// <summary>A still from the episode to fetch lazily at display time, when the catalogue has one.</summary>
    public string? StillUrl { get; init; }
}

/// <summary>
/// Everything fetched for one show, as the cache keeps it: the catalogue's identifier, the plain
/// facts a library card wants, and every episode. A record with <see cref="CatalogueId"/> 0 means
/// the lookup ran and found nothing, so the show is not searched again and again.
/// </summary>
public sealed record EnrichedShow
{
    /// <summary>The show in the catalogue, or 0 when the lookup found no match.</summary>
    public required int CatalogueId { get; init; }

    /// <summary>The show's name as the catalogue spells it.</summary>
    public required string Name { get; init; }

    public string? Summary { get; init; }

    public IReadOnlyList<string> Genres { get; init; } = [];

    /// <summary>The catalogue's average rating out of 10, when it has one.</summary>
    public double? Rating { get; init; }

    /// <summary>Whether a poster was fetched and sits in the cache under the same key.</summary>
    public bool HasPoster { get; init; }

    /// <summary>"Running" or "Ended", as the catalogue says it.</summary>
    public string? Status { get; init; }

    /// <summary>The network or streaming channel that airs it.</summary>
    public string? Network { get; init; }

    public int? RuntimeMinutes { get; init; }

    public string? Language { get; init; }

    /// <summary>When it first aired, as the catalogue writes it (ISO); null when unknown.</summary>
    public string? Premiered { get; init; }

    /// <summary>When it ended; null while it runs or when unknown.</summary>
    public string? Ended { get; init; }

    /// <summary>The main cast, photos to be fetched lazily.</summary>
    public IReadOnlyList<CastMember> Cast { get; init; } = [];

    public IReadOnlyList<string> Creators { get; init; } = [];

    /// <summary>Each season's overview and poster, for the show's page.</summary>
    public IReadOnlyList<SeasonDetails> Seasons { get; init; } = [];

    public IReadOnlyList<EnrichedEpisode> Episodes { get; init; } = [];

    /// <summary>Whether the lookup found this show, rather than recording that nothing matched.</summary>
    [JsonIgnore]
    public bool Found => CatalogueId > 0;

    /// <summary>The record read back from the cache, or null for text that is not one.</summary>
    public static EnrichedShow? FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            return JsonSerializer.Deserialize(json, MetadataRecordsJson.Default.EnrichedShow);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, MetadataRecordsJson.Default.EnrichedShow);

    /// <summary>The fetched title of one episode, or null when the catalogue does not know it.</summary>
    public string? EpisodeName(int season, int? number) => Episode(season, number)?.Name;

    /// <summary>The fetched synopsis of one episode, or null when the catalogue does not know it.</summary>
    public string? EpisodeSummary(int season, int? number) => Episode(season, number)?.Summary;

    /// <summary>A short line for a show's card: its genres and its rating, whichever it has.</summary>
    public string Blurb()
    {
        var parts = Genres.Take(3).ToList();
        if (Rating is { } rating)
        {
            parts.Add(rating.ToString("0.0", CultureInfo.InvariantCulture) + " / 10");
        }

        return string.Join(" · ", parts);
    }

    private EnrichedEpisode? Episode(int season, int? number) =>
        number is null ? null : Episodes.FirstOrDefault(episode => episode.Season == season && episode.Number == number);
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(EnrichedShow))]
internal sealed partial class MetadataRecordsJson : JsonSerializerContext;
