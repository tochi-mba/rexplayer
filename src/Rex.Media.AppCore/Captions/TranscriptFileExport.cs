using System.Text;
using Rex.Media.IO;

namespace Rex.Media.AppCore.Captions;

/// <summary>
/// Atomic transcript export. A model's generated and edited originals are
/// never modified by exporting, and failed writes cannot publish partial SRT,
/// VTT or JSON files.
/// </summary>
public static class TranscriptFileExport
{
    public static void Write(string path, Transcript transcript)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(transcript);

        var text = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".srt" => transcript.ToSrt(),
            ".vtt" => transcript.ToWebVtt(),
            ".json" => transcript.ToJson(),
            _ => throw new NotSupportedException("Choose an SRT, VTT or JSON transcript file."),
        };
        AtomicFile.Write(path, Encoding.UTF8.GetBytes(text));
    }
}
