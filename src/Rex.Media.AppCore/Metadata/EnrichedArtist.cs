using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rex.Media.AppCore.Metadata;

/// <summary>
/// What a music lookup settled for one artist, as the cache keeps it: MusicBrainz's facts (what
/// they are, where from, their years, genres and members), the photo Wikidata names on Wikimedia
/// Commons and the Wikipedia summary its sitelink leads to. An artist the catalogue does not know
/// is recorded with no identifier, so the name is not searched again and again.
/// </summary>
public sealed record EnrichedArtist
{
    /// <summary>The shape this build writes; an older record is fetched again.</summary>
    public const int CurrentSchema = 1;

    public int Schema { get; init; }

    /// <summary>The artist's name as the library spells it, which the record is kept under.</summary>
    public required string Name { get; init; }

    /// <summary>The artist in MusicBrainz, or null when the lookup found no match.</summary>
    public string? MusicBrainzId { get; init; }

    /// <summary>"Person", "Group", "Orchestra", "Choir", "Character" or "Other", as MusicBrainz says.</summary>
    public string? Type { get; init; }

    /// <summary>Where they come from: a city and its country ("Lagos, Nigeria"), or one of them.</summary>
    public string? Origin { get; init; }

    /// <summary>The first year they were active: a group's founding, or a person's work period start on Wikidata.</summary>
    public int? ActiveFrom { get; init; }

    /// <summary>The last year they were active, or null while they still are.</summary>
    public int? ActiveTo { get; init; }

    public IReadOnlyList<string> Genres { get; init; } = [];

    /// <summary>A group's members, current first; empty for a person.</summary>
    public IReadOnlyList<string> Members { get; init; } = [];

    /// <summary>The Wikidata item the photo and the summary came from.</summary>
    public string? WikidataId { get; init; }

    /// <summary>The Wikipedia summary (CC BY-SA), shown under "About" with its credit.</summary>
    public string? Bio { get; init; }

    /// <summary>The article <see cref="Bio"/> comes from, for the credit's link.</summary>
    public string? ArticleUrl { get; init; }

    /// <summary>The Commons file the photo is (freely licensed), for its credit.</summary>
    public string? PhotoFile { get; init; }

    /// <summary>Whether a photo was fetched and sits in the cache under the same key.</summary>
    public bool HasPhoto { get; init; }

    /// <summary>Whether the record is in this build's shape; one that found nothing never needs another try.</summary>
    [JsonIgnore]
    public bool Current => Schema >= CurrentSchema || MusicBrainzId is null;

    /// <summary>The record read back from the cache, or null for text that is not one.</summary>
    public static EnrichedArtist? FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            return JsonSerializer.Deserialize(json, EnrichedArtistJson.Default.EnrichedArtist);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, EnrichedArtistJson.Default.EnrichedArtist);
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(EnrichedArtist))]
internal sealed partial class EnrichedArtistJson : JsonSerializerContext;
