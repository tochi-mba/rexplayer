using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rex.Media.AppCore.Metadata;

/// <summary>
/// What a music lookup settled for one album, as the cache keeps it: the catalogue's release-group
/// identifier, what kind of release it is, when it first came out, its labels and genres, and
/// whether a cover was fetched. An album whose files already carry art is recorded with
/// <see cref="LocalArt"/> true and gets its facts but never a cover; one the catalogue does not
/// know is recorded with no identifier, so neither is asked again and again.
/// </summary>
public sealed record EnrichedAlbum
{
    /// <summary>The shape this build writes; an older record is fetched again for the facts it lacks.</summary>
    public const int CurrentSchema = 1;

    public int Schema { get; init; }

    public required string Album { get; init; }

    public string? Artist { get; init; }

    /// <summary>The release group in the catalogue, or null when the lookup found no match.</summary>
    public string? ReleaseGroupId { get; init; }

    /// <summary>Whether a cover was fetched and sits in the cache under the same key.</summary>
    public bool HasCover { get; init; }

    /// <summary>The files carry their own artwork, so no cover was fetched; local art always wins.</summary>
    public bool LocalArt { get; init; }

    /// <summary>The release group's primary type: "Album", "Single", "EP", "Broadcast" or "Other".</summary>
    public string? Type { get; init; }

    /// <summary>Its secondary types, such as "Compilation", "Live" or "Soundtrack".</summary>
    public IReadOnlyList<string> SecondaryTypes { get; init; } = [];

    /// <summary>When it first came out, as the catalogue writes it ("2024-08-08", "2018").</summary>
    public string? FirstReleased { get; init; }

    /// <summary>The labels it came out on.</summary>
    public IReadOnlyList<string> Labels { get; init; } = [];

    /// <summary>The catalogue's genres for it, the most voted first.</summary>
    public IReadOnlyList<string> Genres { get; init; } = [];

    /// <summary>Whether the record is in this build's shape; an album the catalogue did not know never needs another try.</summary>
    [JsonIgnore]
    public bool Current => Schema >= CurrentSchema || (ReleaseGroupId is null && !LocalArt);

    /// <summary>The record read back from the cache, or null for text that is not one.</summary>
    public static EnrichedAlbum? FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            return JsonSerializer.Deserialize(json, EnrichedAlbumJson.Default.EnrichedAlbum);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, EnrichedAlbumJson.Default.EnrichedAlbum);
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(EnrichedAlbum))]
internal sealed partial class EnrichedAlbumJson : JsonSerializerContext;
