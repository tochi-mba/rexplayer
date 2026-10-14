using System.Text.Json;
using System.Text.Json.Serialization;
using Rex.Media.AppCore.Captions;
using Rex.Media.Subtitles;

namespace Rex.Media.AppCore.Transcription;

/// <summary>
/// The cues to put in place of every cue that starts in <see cref="Replaced"/>: what a piece heard
/// changes in the subtitle track, and nothing more.
/// </summary>
public sealed record CueChange(TimeRange Replaced, IReadOnlyList<SubtitleCue> Cues);

/// <summary>
/// A transcript heard a piece at a time, in any order (SUB-21). Each piece's words go in at their
/// place by binary search, replacing what was there, and only the cues around the piece are made
/// again: a cue begun after a sentence's end or a breath is the same whatever came before it, so
/// the change runs from such a word before the piece to such a word after it. Safe to read from
/// one thread while another adds.
/// </summary>
public sealed class ProgressiveTranscript
{
    private readonly List<TranscriptWord> _words = [];
    private readonly object _gate = new();
    private TimeRanges _done = TimeRanges.Empty;
    private TimeSpan? _duration;
    private string _language;

    public ProgressiveTranscript(string model, string? language = null, TimeSpan? duration = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        Model = model;
        _language = language ?? "";
        _duration = duration;
    }

    public string Model { get; }

    public string Language
    {
        get
        {
            lock (_gate)
            {
                return _language;
            }
        }
    }

    /// <summary>The file's length, once known.</summary>
    public TimeSpan? Duration
    {
        get
        {
            lock (_gate)
            {
                return _duration;
            }
        }

        set
        {
            lock (_gate)
            {
                _duration = value;
            }
        }
    }

    /// <summary>The stretches heard so far.</summary>
    public TimeRanges Done
    {
        get
        {
            lock (_gate)
            {
                return _done;
            }
        }
    }

    public int WordCount
    {
        get
        {
            lock (_gate)
            {
                return _words.Count;
            }
        }
    }

    /// <summary>Whether every stretch of a file of known length has been heard.</summary>
    public bool IsComplete
    {
        get
        {
            lock (_gate)
            {
                return _duration is { } length && _done.Covers(new TimeRange(TimeSpan.Zero, length));
            }
        }
    }

    /// <summary>How much of the file has been heard, 0 to 1; 0 while its length is unknown.</summary>
    public double Share
    {
        get
        {
            lock (_gate)
            {
                return _duration is { Ticks: > 0 } length ? Math.Clamp(_done.Total / length, 0, 1) : 0;
            }
        }
    }

    /// <summary>Everything heard so far, as one transcript.</summary>
    public Transcript ToTranscript()
    {
        lock (_gate)
        {
            return new Transcript(_language, Model, [.. _words]);
        }
    }

    /// <summary>
    /// Takes in what a piece heard: <paramref name="heard"/> are timed from the start of what the
    /// piece heard, and only the words that start in what it keeps are kept, in place of any there
    /// before. Gives the change to the subtitle cues.
    /// </summary>
    public CueChange Add(SpeechChunk chunk, IReadOnlyList<TranscriptWord> heard, string? language)
    {
        ArgumentNullException.ThrowIfNull(heard);
        var offset = chunk.Heard.Start;
        List<TranscriptWord> kept = [.. heard
            .Where(word => word.Text.Trim().Length > 0)
            .Select(word => word with { Start = word.Start + offset, End = word.End + offset })
            .Where(word => chunk.Kept.Contains(word.Start))
            .OrderBy(word => word.Start)];
        lock (_gate)
        {
            _language = language is { Length: > 0 } spoken ? spoken : _language;
            var first = FirstStartingAtOrAfter(chunk.Kept.Start);
            _words.RemoveRange(first, FirstStartingAtOrAfter(chunk.Kept.End) - first);
            _words.InsertRange(first, kept);
            _done = _done.Add(chunk.Kept);

            // From a word that begins a cue, with an unchanged word before it, to the same after.
            var from = Math.Max(0, first - 1);
            while (from > 0 && !Transcript.StartsCue(_words, from))
            {
                from--;
            }

            var to = Math.Min(_words.Count, first + kept.Count + 1);
            while (to < _words.Count && !Transcript.StartsCue(_words, to))
            {
                to++;
            }

            var replaced = new TimeRange(from == 0 ? TimeSpan.Zero : _words[from].Start, to == _words.Count ? TimeSpan.MaxValue : _words[to].Start);
            return new CueChange(replaced, Transcript.CuesOf(_words, from, to));
        }
    }

    /// <summary>The transcript so far as JSON, to go on with later.</summary>
    public string ToJson()
    {
        lock (_gate)
        {
            return JsonSerializer.Serialize(
                new PartialFile(_language, Model, _duration?.TotalSeconds, [.. _done.Ranges.Select(range => new[] { range.Start.TotalSeconds, range.End.TotalSeconds })], [.. _words.Select(word => new TranscriptFileWord(word.Text, word.Start.TotalSeconds, word.End.TotalSeconds, word.Confidence))]),
                PartialJson.Default.PartialFile);
        }
    }

    /// <summary>A transcript so far from its JSON, or null when the text is not one.</summary>
    public static ProgressiveTranscript? FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            if (JsonSerializer.Deserialize(json, PartialJson.Default.PartialFile) is not { Model: { } model, Done: { } done, Words: { } words } file)
            {
                return null;
            }

            var transcript = new ProgressiveTranscript(model, file.Language, file.Duration is { } seconds ? TimeSpan.FromSeconds(seconds) : null);
            transcript._done = TimeRanges.Of(done.Where(pair => pair is { Length: 2 }).Select(pair => new TimeRange(TimeSpan.FromSeconds(pair[0]), TimeSpan.FromSeconds(pair[1]))));
            transcript._words.AddRange(words
                .Where(word => word.Text is { } text && text.Trim().Length > 0)
                .Select(word => new TranscriptWord(word.Text!, TimeSpan.FromSeconds(word.Start), TimeSpan.FromSeconds(Math.Max(word.Start, word.End)), word.Confidence))
                .OrderBy(word => word.Start));
            return transcript;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The index of the first word starting at or after <paramref name="time"/>: a binary search. Called holding the gate.</summary>
    private int FirstStartingAtOrAfter(TimeSpan time)
    {
        int low = 0, high = _words.Count;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (_words[middle].Start < time)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }
}

internal sealed record PartialFile(string? Language, string? Model, double? Duration, double[][]? Done, TranscriptFileWord[]? Words);

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(PartialFile))]
internal sealed partial class PartialJson : JsonSerializerContext;
