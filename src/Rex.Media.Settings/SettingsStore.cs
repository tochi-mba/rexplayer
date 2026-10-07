using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Rex.Media.IO;

namespace Rex.Media.Settings;

/// <summary>
/// Reads and writes <see cref="PlayerSettings"/> as JSON. Writes are atomic with a backup of the
/// previous file (<see cref="AtomicFile"/>), so a crash mid-save never loses the settings; a file
/// that cannot be read falls back to the backup, then to the defaults, and every load is normalized.
/// </summary>
public static class SettingsStore
{
    /// <summary>The settings in <paramref name="path"/>, or the defaults when there are none to read.</summary>
    public static PlayerSettings Load(string path)
    {
        PlayerSettings? settings = null;
        AtomicFile.Read(path, bytes => (settings = TryParse(bytes)) is not null);
        return (settings ?? new PlayerSettings()).Normalize();
    }

    public static void Save(string path, PlayerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        AtomicFile.Write(path, JsonSerializer.SerializeToUtf8Bytes(settings.Normalize(), SettingsJson.Default.PlayerSettings));
    }

    private static readonly JsonDocumentOptions Lenient = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>
    /// The settings in <paramref name="bytes"/>, or null when they are not settings this version can
    /// read. A file that leaves settings out (one from an older version, or written by hand) keeps
    /// the defaults for those: the file's values are laid over a complete set of defaults before
    /// reading, because the serializer would otherwise set every missing setting to zero.
    /// </summary>
    internal static PlayerSettings? TryParse(byte[] bytes)
    {
        try
        {
            // Editors such as Notepad may save with a byte-order mark, which the JSON reader refuses.
            ReadOnlySpan<byte> json = bytes;
            json = json.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? json[3..] : json;
            if (JsonNode.Parse(json, documentOptions: Lenient) is not JsonObject given)
            {
                return null;
            }

            var merged = JsonSerializer.SerializeToNode(new PlayerSettings(), SettingsJson.Default.PlayerSettings)!.AsObject();
            foreach (var (name, value) in given)
            {
                merged[name] = value?.DeepClone();
            }

            return merged.Deserialize(SettingsJson.Default.PlayerSettings);
        }
        catch (JsonException)
        {
            // A damaged or hand-edited file: the caller falls back to the backup or the defaults.
            return null;
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(PlayerSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
