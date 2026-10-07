// Spec: W3C WebVTT section 6.4 (the active cues at a moment: every cue whose start is at or before it and whose end is after it, in start order).
namespace Rex.Media.Subtitles;

/// <summary>
/// One track's cues as they become known: all at once from a file, or a packet at a time from the
/// media as it is read (and read again after a seek, so a cue seen twice is kept once). Finds the
/// cues to show at a moment, with the track's delay applied.
/// </summary>
public sealed class SubtitleTrack(string name)
{
    private readonly List<SubtitleCue> _cues = [];
    private readonly HashSet<(TimeSpan, string)> _seen = [];
    private readonly object _gate = new();

    /// <summary>How the track is offered in menus: its language and title, or the file's name.</summary>
    public string Name { get; } = name;

    /// <summary>The track's language as its file name or the media says, when either does.</summary>
    public string? Language { get; init; }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _cues.Count;
            }
        }
    }

    /// <summary>Adds a cue, unless the same text at the same start is already there.</summary>
    public void Add(SubtitleCue cue)
    {
        ArgumentNullException.ThrowIfNull(cue);
        lock (_gate)
        {
            if (!_seen.Add((cue.Start, cue.Text)))
            {
                return;
            }

            var at = _cues.FindLastIndex(existing => existing.Start <= cue.Start) + 1;
            _cues.Insert(at, cue);
        }
    }

    public void AddRange(IEnumerable<SubtitleCue> cues)
    {
        ArgumentNullException.ThrowIfNull(cues);
        foreach (var cue in cues)
        {
            Add(cue);
        }
    }

    /// <summary>
    /// The cues on screen at <paramref name="position"/> in the media, when the subtitles are shown
    /// <paramref name="delay"/> later than they were made for (negative: sooner).
    /// </summary>
    public IReadOnlyList<SubtitleCue> At(TimeSpan position, TimeSpan delay = default)
    {
        var time = position - delay;
        lock (_gate)
        {
            return [.. _cues.TakeWhile(cue => cue.Start <= time).Where(cue => cue.IsShownAt(time))];
        }
    }
}
