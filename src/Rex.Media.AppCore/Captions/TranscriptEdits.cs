using System.Text.Json;
using System.Text.Json.Serialization;
using Rex.Media.AppCore.Transcription;

namespace Rex.Media.AppCore.Captions;

/// <summary>An independent transcript snapshot with only the coverage actually heard for it.</summary>
public sealed record TranscriptRevision(Transcript Content, TimeRanges Done, TimeSpan? Duration, bool Complete);

public sealed partial class TranscriptCache
{
    /// <summary>User corrections live outside the disposable generated cache files.</summary>
    public string EditedPathFor(string media, string model) => Path.Combine(Root, "edited", Path.GetFileName(PathFor(media, model)));

    /// <summary>Loads this model's edited snapshot without borrowing another variant's coverage.</summary>
    public TranscriptRevision? LoadEdited(string media, string model)
    {
        var path = EditedPathFor(media, model);
        if (!File.Exists(path) || Text(path) is not { } text)
        {
            return null;
        }

        try
        {
            var file = JsonSerializer.Deserialize(text, EditedTranscriptJson.Default.EditedTranscriptFile);
            if (file?.Transcript is null || file.Done is null || Transcript.FromJson(file.Transcript) is not { } transcript || transcript.Model != model)
            {
                return null;
            }

            return new TranscriptRevision(transcript,
                TimeRanges.Of(file.Done.Select(range => new TimeRange(TimeSpan.FromTicks(range.Start), TimeSpan.FromTicks(range.End)))),
                file.Duration is { } duration ? TimeSpan.FromTicks(duration) : null, file.Complete);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Saves corrections atomically, independently of ongoing recognition and other models.</summary>
    public void SaveEdited(string media, TranscriptRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        var path = EditedPathFor(media, revision.Content.Model);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var file = new EditedTranscriptFile(revision.Content.ToJson(),
            [.. revision.Done.Ranges.Select(range => new EditedTranscriptRange(range.Start.Ticks, range.End.Ticks))],
            revision.Duration?.Ticks, revision.Complete);
        Write(path, JsonSerializer.Serialize(file, EditedTranscriptJson.Default.EditedTranscriptFile));
    }

    /// <summary>Explicitly resets this model's corrections; generated speech and other models remain.</summary>
    public void ForgetEdited(string media, string model) => Delete(EditedPathFor(media, model));
}

internal sealed record EditedTranscriptRange(long Start, long End);
internal sealed record EditedTranscriptFile(string? Transcript, EditedTranscriptRange[]? Done, long? Duration, bool Complete);

[JsonSerializable(typeof(EditedTranscriptFile))]
internal sealed partial class EditedTranscriptJson : JsonSerializerContext;
