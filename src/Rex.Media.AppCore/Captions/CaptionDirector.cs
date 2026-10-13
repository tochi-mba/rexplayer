using Rex.Media.AppCore.Player;
using Rex.Media.Settings;
using Rex.Media.Subtitles;

namespace Rex.Media.AppCore.Captions;

/// <summary>
/// Runs kinetic captions a frame at a time (OSD-11), so the window only draws what this says: it
/// hears the voice (<see cref="SpeechPulse"/>), follows the cue being spoken and brings its words in
/// as they are said (<see cref="WordAligner"/>), and lays them out clear of what matters in the
/// picture (<see cref="CaptionChoreography"/>, <see cref="PictureTimeline"/>).
/// <para>
/// A cluster of words ends when the voice pauses, when a cue ends a sentence, when the next cue is
/// a while coming, or when nothing is being said; a seek starts afresh.
/// </para>
/// </summary>
public sealed class CaptionDirector
{
    /// <summary>A jump in position larger than this, or any jump back, is a seek.</summary>
    public const double SeekJump = 1.5;

    /// <summary>A gap between cues longer than this ends the cluster.</summary>
    public const double CueGap = 0.3;

    private readonly SpeechPulse _pulse = new();
    private readonly WordAligner _aligner = new();
    private CaptionChoreography _choreography;
    private SubtitleCue? _cue;
    private SubtitleCue? _lastCue;
    private double _last = double.NaN;

    public CaptionDirector(CaptionStyle style)
    {
        Style = style ?? throw new ArgumentNullException(nameof(style));
        _choreography = new CaptionChoreography(style);
    }

    public CaptionStyle Style { get; private set; }

    /// <summary>The voice as last heard.</summary>
    public SpeechPulse Voice => _pulse;

    /// <summary>The words revealed so far of the cue being spoken, and how many it has.</summary>
    public (int Revealed, int Of) Progress => (_aligner.Revealed, _aligner.Words.Count);

    /// <summary>Takes a new look; what is showing starts afresh with it.</summary>
    public void Restyle(CaptionStyle style)
    {
        Style = style ?? throw new ArgumentNullException(nameof(style));
        _choreography = new CaptionChoreography(style);
        (_cue, _lastCue) = (null, null);
    }

    /// <summary>
    /// The words to draw at <paramref name="position"/>, <paramref name="elapsed"/> after the last
    /// frame: <paramref name="mono"/> is the newest <see cref="SpeechPulse.Size"/> samples heard (or
    /// empty when there is no sound to read), <paramref name="cues"/> the subtitle cues showing now,
    /// <paramref name="area"/> where captions may go and <paramref name="fontSize"/> their base size;
    /// <paramref name="pictures"/>, when the picture is being read, keeps the words off what matters.
    /// </summary>
    public IReadOnlyList<CaptionWord> Frame(TimeSpan position, ReadOnlySpan<float> mono, int sampleRate, TimeSpan elapsed, IReadOnlyList<SubtitleCue> cues, Area area, double fontSize, PictureTimeline? pictures = null)
    {
        ArgumentNullException.ThrowIfNull(cues);
        var now = position.TotalSeconds;
        if (!double.IsNaN(_last) && (now < _last - 0.05 || now > _last + SeekJump))
        {
            Seeked();
        }

        _last = now;
        var heard = mono.Length == SpeechPulse.Size && sampleRate > 0
            ? _pulse.Update(mono, sampleRate, elapsed)
            : new SpeechFrame(0.5f, 0.5f, false, 0, false, 0);

        var cue = cues.Count > 0 ? cues[0] : null;
        if (cue != _cue)
        {
            Follow(cue, now);
        }

        // Between cues the words stay, unless the next is a while coming.
        if (_cue is null && _lastCue is { } last && now - last.End.TotalSeconds > CueGap)
        {
            _choreography.Clear(now);
        }

        if (_cue is { } spoken)
        {
            var before = _aligner.Revealed;
            var after = _aligner.Advance(now - spoken.Start.TotalSeconds, heard);
            for (var i = before; i < after; i++)
            {
                var stress = _aligner.Timed && mono.Length == 0 ? Stress.Plain : _pulse.StressOf(heard);
                _choreography.Reveal(_aligner.Words[i], heard, stress, now);
            }

            if (heard.SilentFor >= WordAligner.Pause && _aligner.Revealed == _aligner.Words.Count)
            {
                _choreography.Clear(now);
            }
        }

        var picture = pictures?.Over(now, _cue is { } showing ? Math.Max(now, Math.Min(showing.End.TotalSeconds, now + 3)) : now);
        return _choreography.Scene(now, area, fontSize, heard.Pace, picture);
    }

    /// <summary>The look for <paramref name="settings"/>: their kinetic caption choices, in the subtitle text colour.</summary>
    public static CaptionStyle StyleFor(PlayerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings = settings.Normalize();
        var kinetic = settings.KineticCaptions;
        return new CaptionStyle(
            Argb.From(settings.SubtitleColor, settings.SubtitleOpacity),
            Argb.From(kinetic.Accent, 100),
            kinetic.Colors switch
            {
                CaptionColors.Fire => VisualPalette.Fire,
                CaptionColors.Neon => VisualPalette.Neon,
                CaptionColors.Sunset => VisualPalette.Sunset,
                CaptionColors.Ocean => VisualPalette.Ocean,
                CaptionColors.Rainbow => VisualPalette.Rainbow,
                CaptionColors.Accent => VisualPalette.Accent,
                _ => VisualPalette.Warm,
            },
            kinetic.Energy / 100.0,
            kinetic.Layout switch
            {
                CaptionLayoutChoice.Stack => CaptionLayout.Stack,
                CaptionLayoutChoice.Centre => CaptionLayout.Centre,
                _ => CaptionLayout.Scatter,
            },
            kinetic.Glyphs,
            kinetic.ShoutsInCapitals,
            kinetic.AvoidPicture);
    }

    private void Follow(SubtitleCue? cue, double now)
    {
        // The cluster ends with the sentence, or when the next words are a while coming.
        var previous = _cue ?? _lastCue;
        var ends = previous is null
            || EndsSentence(previous)
            || (cue is not null && cue.Start.TotalSeconds - previous.End.TotalSeconds > CueGap);
        if (ends)
        {
            _choreography.Clear(now);
        }

        _lastCue = _cue ?? _lastCue;
        _cue = cue;
        if (cue is not null)
        {
            _aligner.Begin(cue);
        }
    }

    private void Seeked()
    {
        _choreography.Reset();
        _pulse.Reset();
        (_cue, _lastCue) = (null, null);
    }

    private static bool EndsSentence(SubtitleCue cue) => cue.Text.TrimEnd().TrimEnd('"', '\'', ')', '”', '’') is { Length: > 0 } text && text[^1] is '.' or '!' or '?' or '…';
}
