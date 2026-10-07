using System.Text.Json;
using System.Text.Json.Serialization;
using Rex.Media.IO;

namespace Rex.Media.AppCore.Player;

/// <summary>What was playing, and where, the last time the marker was written.</summary>
public sealed record ResumePoint(string Location, string Title, double PositionSeconds);

/// <summary>
/// Crash recovery (WIN-14): while something plays, the window keeps a small marker of what and
/// where; a clean close deletes it. A marker found at start-up means the last run ended without
/// closing, so the window offers to carry on from there.
/// </summary>
public static class ResumeMarker
{
    public static void Write(string path, ResumePoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        AtomicFile.Write(path, JsonSerializer.SerializeToUtf8Bytes(point, ResumeJson.Default.ResumePoint));
    }

    /// <summary>The point the marker holds, or null when there is no marker or it cannot be read.</summary>
    public static ResumePoint? Read(string path)
    {
        try
        {
            return File.Exists(path) && JsonSerializer.Deserialize(File.ReadAllBytes(path), ResumeJson.Default.ResumePoint) is { Location.Length: > 0 } point
                ? point
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Removes the marker (and its backup): the run closed cleanly, or the offer was answered.</summary>
    public static void Clear(string path)
    {
        File.Delete(path);
        File.Delete(path + AtomicFile.BackupSuffix);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ResumePoint))]
internal sealed partial class ResumeJson : JsonSerializerContext;
