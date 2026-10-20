using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rex.Media.AppCore.Metadata;

/// <summary>
/// What a music lookup settled for one album, as the cache keeps it: the catalogue's release-group
/// identifier and whether a cover was fetched. An album whose files already carry art is recorded
/// with <see cref="LocalArt"/> true and never looked up; one the catalogue does not know is
/// recorded with no identifier, so neither is asked again and again.
/// </summary>
public sealed record EnrichedAlbum
{
    public required string Album { get; init; }

    public string? Artist { get; init; }

    /// <summary>The release group in the catalogue, or null when the lookup found no match.</summary>
    public string? ReleaseGroupId { get; init; }

    /// <summary>Whether a cover was fetched and sits in the cache under the same key.</summary>
    public bool HasCover { get; init; }

    /// <summary>The files carry their own artwork, so nothing was looked up; local art always wins.</summary>
    public bool LocalArt { get; init; }

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
