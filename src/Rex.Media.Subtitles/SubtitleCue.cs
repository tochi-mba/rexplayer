// Spec: W3C WebVTT section 3 (cues and cue text), used as the shared model every subtitle format is read into.
namespace Rex.Media.Subtitles;

/// <summary>Where on the picture a cue sits.</summary>
public enum SubtitlePlacement
{
    Bottom,
    Middle,
    Top,
}

/// <summary>A stretch of text with one look: bold, italic, underlined, and a colour (0xRRGGBB) or the default.</summary>
public sealed record SubtitleRun(string Text, bool Bold = false, bool Italic = false, bool Underline = false, int? Color = null);

/// <summary>One line of a cue, made of runs.</summary>
public sealed record SubtitleLine(IReadOnlyList<SubtitleRun> Runs)
{
    /// <summary>The line's text without its styling.</summary>
    public string Text => string.Concat(Runs.Select(run => run.Text));
}

/// <summary>
/// What every subtitle format is read into: text shown from <see cref="Start"/> until
/// <see cref="End"/>, as styled lines, at a place on the picture.
/// </summary>
public sealed record SubtitleCue(TimeSpan Start, TimeSpan End, IReadOnlyList<SubtitleLine> Lines, SubtitlePlacement Placement = SubtitlePlacement.Bottom)
{
    /// <summary>The cue's text without styling, one line per line.</summary>
    public string Text => string.Join("\n", Lines.Select(line => line.Text));

    /// <summary>Whether the cue is on screen at <paramref name="time"/> (from its start, up to its end).</summary>
    public bool IsShownAt(TimeSpan time) => time >= Start && time < End;
}
