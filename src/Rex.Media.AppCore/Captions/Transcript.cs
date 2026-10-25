using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rex.Media.IO;
using Rex.Media.Subtitles;

namespace Rex.Media.AppCore.Captions;

/// <summary>One word heard: its text, when it was said, and how sure the model was (0 to 1).</summary>
public sealed record TranscriptWord(string Text, TimeSpan Start, TimeSpan End, float Confidence = 1);

/// <summary>A generated transcript or the user's independent edited version for one speech model.</summary>
public readonly record struct TranscriptVariant(string Model, bool Edited)
{
    public string Quality => Model switch
    {
        "tiny.en" => "Fast",
        "base.en" => "Default",
        "small.en" => "Best",
        _ => Model,
    };

    public string DisplayName => Edited ? $"{Quality} · Edited" : $"{Quality} · Original";

    public string TrackName => $"Transcript ({Model}, {(Edited ? "edited" : "original")})";

    /// <summary>
    /// Whether <paramref name="name"/> is the saved original of <paramref name="activeModel"/> that was
    /// loaded while another quality was active. Once that model is active its live track (named
    /// without the edition) carries the same words and keeps growing, so the saved copy would show
    /// as a second, frozen "Original" of the same quality.
    /// </summary>
    public static bool ReplacedByLiveTrack(string name, string activeModel) =>
        name == new TranscriptVariant(activeModel, false).TrackName;

    /// <summary>Recognises named variants, including generated tracks from older versions.</summary>
    public static TranscriptVariant? FromTrackName(string name)
    {
        if (!name.StartsWith("Transcript (", StringComparison.Ordinal) || !name.EndsWith(')'))
        {
            return null;
        }

        var identity = name[12..^1];
        var edited = identity.EndsWith(", edited", StringComparison.Ordinal);
        var model = edited ? identity[..^8] : identity.EndsWith(", original", StringComparison.Ordinal) ? identity[..^10] : identity;
        return model.Length > 0 ? new TranscriptVariant(model, edited) : null;
    }
}

/// <summary>
/// What a speech model heard in a file (SUB-19): every word with the moment it was said, so
/// captions land on the syllables. Becomes an ordinary subtitle track (one cue a sentence or
/// breath, its runs timed), an SRT or WebVTT file, and JSON for the cache.
/// </summary>
public sealed record Transcript(string Language, string Model, IReadOnlyList<TranscriptWord> Words)
{
    /// <summary>A silence longer than this between words ends a cue.</summary>
    public static readonly TimeSpan Breath = TimeSpan.FromSeconds(0.7);

    /// <summary>A cue holds at most this many words.</summary>
    public const int MostWordsInACue = 10;

    /// <summary>A cue lasts at most this long.</summary>
    public static readonly TimeSpan LongestCue = TimeSpan.FromSeconds(4);

    /// <summary>How long the last cue of a stretch stays after its last word.</summary>
    private static readonly TimeSpan Linger = TimeSpan.FromMilliseconds(400);

    /// <summary>Nothing heard.</summary>
    public static Transcript Empty { get; } = new("", "", []);

    /// <summary>Whether anything was heard.</summary>
    public bool IsEmpty => Words.Count == 0;

    /// <summary>The model-labelled SubRip path beside a media file, kept distinct from authored subtitles.</summary>
    public static string SidecarSrtPath(string mediaPath, string model, bool edited = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        var quality = model switch
        {
            "tiny.en" => "fast",
            "base.en" => "default",
            "small.en" => "best",
            _ => SanitizeModel(model),
        };
        var directory = Path.GetDirectoryName(mediaPath);
        var version = edited ? "edited" : "original";
        var name = $"{Path.GetFileNameWithoutExtension(mediaPath)}.transcript.{quality}.{version}.srt";
        return string.IsNullOrEmpty(directory) ? name : Path.Combine(directory, name);
    }

    /// <summary>Atomically replaces the model-labelled SubRip copy beside the media.</summary>
    public void WriteSidecarSrt(string mediaPath) =>
        AtomicFile.Write(SidecarSrtPath(mediaPath, Model), Encoding.UTF8.GetBytes(ToSrt()));

    private static string SanitizeModel(string model)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(model.Trim().ToLowerInvariant().Select(c => c is '/' or '\\' || invalid.Contains(c) ? '-' : c).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "unknown" : safe;
    }

    /// <summary>
    /// The words as cues: a cue ends at a sentence's end, a breath, the most words, or the longest
    /// a cue may last; each run carries when its word is said from the cue's start, so a cue is
    /// timed word by word.
    /// </summary>
    public IReadOnlyList<SubtitleCue> ToCues()
    {
        List<TranscriptWord> spoken = [.. Words.Where(word => word.Text.Trim().Length > 0)];
        return CuesOf(spoken, 0, spoken.Count);
    }

    /// <summary>
    /// The cues of <paramref name="words"/> from <paramref name="from"/> up to <paramref name="to"/>
    /// (words with text, in time order), the last one ending by the start of the word after them.
    /// Cues begun at a <see cref="StartsCue"/> word are the same whatever came before it, which is
    /// what lets a transcript heard piece by piece rebuild only the cues around each piece.
    /// </summary>
    public static List<SubtitleCue> CuesOf(IReadOnlyList<TranscriptWord> words, int from, int to)
    {
        ArgumentNullException.ThrowIfNull(words);
        var cues = new List<SubtitleCue>();
        var cue = new List<TranscriptWord>();
        for (var i = from; i < to; i++)
        {
            var word = words[i];
            if (cue.Count > 0 && (StartsCue(words, i) || cue.Count >= MostWordsInACue || word.Start - cue[0].Start > LongestCue))
            {
                cues.Add(Cue(cue, word.Start));
                cue.Clear();
            }

            cue.Add(word);
        }

        if (cue.Count > 0)
        {
            cues.Add(Cue(cue, to < words.Count ? words[to].Start : null));
        }

        return cues;
    }

    /// <summary>Whether word <paramref name="index"/> begins a cue whatever came before it: the first word, or one after a sentence's end or a breath.</summary>
    public static bool StartsCue(IReadOnlyList<TranscriptWord> words, int index)
    {
        ArgumentNullException.ThrowIfNull(words);
        return index <= 0 || index >= words.Count || EndsSentence(words[index - 1].Text) || words[index].Start - words[index - 1].End > Breath;
    }

    /// <summary>The transcript as a subtitle track named <paramref name="name"/>.</summary>
    public SubtitleTrack ToTrack(string name)
    {
        var track = new SubtitleTrack(name) { Language = Language.Length > 0 ? Language : null };
        track.AddRange(ToCues());
        return track;
    }

    /// <summary>The transcript as a SubRip file.</summary>
    public string ToSrt()
    {
        var text = new StringBuilder();
        var number = 1;
        foreach (var cue in ToCues())
        {
            text.Append(CultureInfo.InvariantCulture, $"{number++}\n{Stamp(cue.Start, ',')} --> {Stamp(cue.End, ',')}\n{cue.Text}\n\n");
        }

        return text.ToString();
    }

    /// <summary>The transcript as a WebVTT file, each word carrying its time.</summary>
    public string ToWebVtt()
    {
        var text = new StringBuilder("WEBVTT\n\n");
        foreach (var cue in ToCues())
        {
            text.Append(CultureInfo.InvariantCulture, $"{Stamp(cue.Start, '.')} --> {Stamp(cue.End, '.')}\n");
            foreach (var run in cue.Lines[0].Runs)
            {
                if (run.At is { } at)
                {
                    text.Append(CultureInfo.InvariantCulture, $"<{Stamp(cue.Start + at, '.')}>");
                }

                text.Append(run.Text);
            }

            text.Append("\n\n");
        }

        return text.ToString();
    }

    /// <summary>The transcript as enhanced LRC, retaining reliable word times.</summary>
    public string ToLrc()
    {
        var text = new StringBuilder();
        foreach (var cue in ToCues())
        {
            text.Append('[').Append(LrcStamp(cue.Start)).Append(']');
            var runs = cue.Lines[0].Runs;
            var times = runs.Select(run => run.At is { } at ? LrcCentiseconds(cue.Start + at) : -1L).ToArray();
            var wordTimed = times.Length > 0 && times[0] >= LrcCentiseconds(cue.Start)
                && times.Zip(times.Skip(1)).All(pair => pair.First < pair.Second);
            foreach (var run in runs.Select((value, index) => (value, index)))
            {
                if (wordTimed)
                {
                    text.Append('<').Append(LrcStamp(cue.Start + run.value.At!.Value)).Append('>');
                }

                text.Append(run.value.Text);
            }

            text.Append('\n');
        }

        return text.ToString();
    }

    private static long LrcCentiseconds(TimeSpan time) => Math.Max(0, time.Ticks / (TimeSpan.TicksPerMillisecond * 10));

    private static string LrcStamp(TimeSpan time)
    {
        var centiseconds = LrcCentiseconds(time);
        return string.Create(CultureInfo.InvariantCulture, $"{centiseconds / 6000:00}:{centiseconds / 100 % 60:00}.{centiseconds % 100:00}");
    }

    // Export is a UTF-8 file, not an HTML script: keep speech in its original
    // writing system instead of JSON-escaping every non-ASCII character.
    private static readonly TranscriptJson ExportJson = new(new JsonSerializerOptions(TranscriptJson.Default.Options)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    /// <summary>The transcript as JSON, for the cache.</summary>
    public string ToJson() => JsonSerializer.Serialize(new TranscriptFile(Language, Model, [.. Words.Select(word => new TranscriptFileWord(word.Text, word.Start.TotalSeconds, word.End.TotalSeconds, word.Confidence))]), ExportJson.TranscriptFile);

    /// <summary>A transcript from its JSON, or null when the text is not one.</summary>
    public static Transcript? FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            var file = JsonSerializer.Deserialize(json, TranscriptJson.Default.TranscriptFile);
            return file?.Words is null ? null : new Transcript(file.Language ?? "", file.Model ?? "", [.. file.Words.Select(word => new TranscriptWord(word.Text ?? "", TimeSpan.FromSeconds(word.Start), TimeSpan.FromSeconds(Math.Max(word.Start, word.End)), word.Confidence))]);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static SubtitleCue Cue(List<TranscriptWord> words, TimeSpan? nextStart)
    {
        var start = words[0].Start;
        var end = words[^1].End + Linger;
        if (nextStart is { } next && end > next)
        {
            end = next;
        }

        if (end <= start)
        {
            end = start + TimeSpan.FromMilliseconds(100);
        }

        var runs = new List<SubtitleRun>();
        for (var i = 0; i < words.Count; i++)
        {
            runs.Add(new SubtitleRun(words[i].Text.Trim() + (i < words.Count - 1 ? " " : ""), At: words[i].Start - start));
        }

        return new SubtitleCue(start, end, [new SubtitleLine(runs)]);
    }

    private static bool EndsSentence(string word) => word.TrimEnd().TrimEnd('"', '\'', ')', '”', '’') is { Length: > 0 } text && text[^1] is '.' or '!' or '?' or '…';

    private static string Stamp(TimeSpan time, char fraction)
    {
        time = time < TimeSpan.Zero ? TimeSpan.Zero : time;
        return string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}{fraction}{time.Milliseconds:000}");
    }
}

/// <summary>Turns speech in a media file into a <see cref="Transcript"/>; the window and the command line are given one by their host.</summary>
public interface ISpeechTranscriber
{
    /// <summary>The model's name, for the transcript and the track's name.</summary>
    string ModelName { get; }

    /// <summary>
    /// Hears <paramref name="path"/> in <paramref name="language"/> (a two-letter code, or null to
    /// tell), reporting progress from 0 to 1.
    /// </summary>
    Task<Transcript> TranscribeAsync(string path, string? language, IProgress<double>? progress, CancellationToken cancellationToken);
}

internal sealed record TranscriptFile(string? Language, string? Model, TranscriptFileWord[]? Words);

internal sealed record TranscriptFileWord(string? Text, double Start, double End, float Confidence);

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(TranscriptFile))]
internal sealed partial class TranscriptJson : JsonSerializerContext;


/// <summary>
/// Deletes only the exact model-labelled SRT copies rexplayer emits beside known media files.
/// Authored subtitles, files in subtitle folders and transcript caches are left untouched.
/// </summary>
public static class TranscriptSidecarCleanup
{
    /// <summary>How many exports were removed, and how many could not be removed.</summary>
    public readonly record struct Result(int Removed, int Failed);

    /// <summary>
    /// The caller supplies media paths and known transcription model ids, never arbitrary
    /// directories to search. Only the two names written by <see cref="Transcript.SidecarSrtPath"/>
    /// are candidates for each pair.
    /// </summary>
    public static Result Remove(IEnumerable<string> mediaPaths, IEnumerable<string> models,
        Func<string, bool>? exists = null, Action<string>? delete = null)
    {
        ArgumentNullException.ThrowIfNull(mediaPaths);
        ArgumentNullException.ThrowIfNull(models);
        exists ??= File.Exists;
        delete ??= File.Delete;
        var knownModels = models.Where(model => !string.IsNullOrWhiteSpace(model))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var paths = mediaPaths.Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase).SelectMany(media =>
                knownModels.SelectMany(model => new[]
                {
                    Transcript.SidecarSrtPath(media, model),
                    Transcript.SidecarSrtPath(media, model, edited: true),
                })).Distinct(StringComparer.OrdinalIgnoreCase);
        var removed = 0;
        var failed = 0;
        foreach (var path in paths)
        {
            try
            {
                if (exists(path))
                {
                    delete(path);
                    removed++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                failed++;
            }
        }

        return new Result(removed, failed);
    }
}
