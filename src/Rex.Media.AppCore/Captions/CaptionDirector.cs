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

    /// <summary>The least time between two shouted words.</summary>
    public const double ShoutGap = 1.2;

    /// <summary>The longest a cluster stays before it gives way.</summary>
    public const double LongestCluster = 4.5;

    private readonly SpeechPulse _pulse = new();
    private readonly WordAligner _aligner = new();
    private CaptionChoreography _choreography;
    private SubtitleCue? _cue;
    private SubtitleCue? _lastCue;
    private double _last = double.NaN;
    private double _lastShout = double.NegativeInfinity;
    private double _clusterBegan = double.NaN;
    private readonly List<int> _lineIds = [];

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
                var word = _aligner.Words[i];
                var stress = _aligner.Timed && mono.Length == 0 ? Stress.Plain : _pulse.StressOf(heard);
                // A shout is one word in a breath, and never a tiny word: "a" and "in" are not what anyone stresses.
                if (stress is Stress.Shout or Stress.Peak && (word.Text.Count(char.IsLetterOrDigit) < 3 || now - _lastShout < ShoutGap))
                {
                    stress = Stress.Plain;
                }

                if (stress is Stress.Shout or Stress.Peak)
                {
                    _lastShout = now;
                }

                if (!_choreography.IsOpen)
                {
                    _clusterBegan = now;
                }

                if (Style.Prefill && i < _lineIds.Count)
                {
                    _choreography.Light(_lineIds[i], now);
                }
                else
                {
                    _choreography.Reveal(word, heard, stress, now);
                }
            }

            // A cluster that has run on for a while gives way, so the picture is never buried.
            if (_choreography.IsOpen && now - _clusterBegan > LongestCluster)
            {
                _choreography.Clear(now);
            }

            if (heard.SilentFor >= WordAligner.Pause && _aligner.Revealed == _aligner.Words.Count)
            {
                _choreography.Clear(now);
            }
        }

        var picture = pictures?.Over(now, _cue is { } showing ? Math.Max(now, Math.Min(showing.End.TotalSeconds, now + 3)) : now);
        return _choreography.Scene(now, area, fontSize, heard.Pace, picture);
    }

    /// <summary>The look for <paramref name="settings"/>: their preset with their choices over it, or their own look, in the subtitle text colour.</summary>
    public static CaptionStyle StyleFor(PlayerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings = settings.Normalize();
        var kinetic = settings.KineticCaptions;
        var own = new CaptionStyle(
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
                CaptionLayoutChoice.Left => CaptionLayout.Left,
                CaptionLayoutChoice.Confetti => CaptionLayout.Confetti,
                _ => CaptionLayout.Scatter,
            },
            kinetic.Glyphs,
            kinetic.ShoutsInCapitals,
            kinetic.AvoidPicture,
            (CaptionFont)kinetic.Font,
            (CaptionCase)kinetic.Case,
            (CaptionMotion)kinetic.Motion,
            kinetic.Highlight,
            kinetic.Flicker,
            kinetic.Tilt / 100.0,
            kinetic.Outline / 100.0,
            kinetic.PlateAlways,
            kinetic.PlateLight,
            kinetic.Prefill,
            kinetic.OneAtATime,
            kinetic.Flat,
            kinetic.Motion == CaptionMotionChoice.Typewriter,
            kinetic.Split,
            kinetic.Jitter / 100.0);
        return CaptionPresets.Style(kinetic.Preset, own);
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
        _lineIds.Clear();
        if (cue is not null)
        {
            _aligner.Begin(cue);
            if (Style.Prefill)
            {
                // The whole line first, dim; each word lights as it is said.
                if (_choreography.IsOpen)
                {
                    _choreography.Clear(now);
                }

                var quiet = new SpeechFrame(0.5f, 0.5f, false, 0, false, 0);
                foreach (var word in _aligner.Words)
                {
                    _lineIds.Add(_choreography.Reveal(word, quiet, Stress.Plain, now, lit: false));
                }
            }
        }
    }

    private void Seeked()
    {
        _choreography.Reset();
        _pulse.Reset();
        (_cue, _lastCue, _lastShout, _clusterBegan) = (null, null, double.NegativeInfinity, double.NaN);
    }

    /// <summary>A cue ending in punctuation ends its clause, and so its cluster.</summary>
    private static bool EndsSentence(SubtitleCue cue) => cue.Text.TrimEnd().TrimEnd('"', '\'', ')', '”', '’') is { Length: > 0 } text && text[^1] is ',' or ':' or ';' or '.' or '!' or '?' or '…';
}
