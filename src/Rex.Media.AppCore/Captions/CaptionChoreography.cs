using Rex.Media.AppCore.Player;
using Rex.Media.Subtitles;

namespace Rex.Media.AppCore.Captions;

/// <summary>Where clusters of words go on the picture.</summary>
public enum CaptionLayout
{
    /// <summary>Each cluster somewhere new, staggered, the way creators cut them.</summary>
    Scatter,

    /// <summary>Centred rows near the bottom, where subtitles sit.</summary>
    Stack,

    /// <summary>Centred rows in the middle of the picture.</summary>
    Centre,
}

/// <summary>How kinetic captions look: the user's choices, as the window reads them from the settings.</summary>
public sealed record CaptionStyle(Argb Text, Argb Accent, VisualPalette Palette, double Energy, CaptionLayout Layout, bool Glyphs, bool UpperCase)
{
    /// <summary>White text, a gold accent, warm stress colours, full energy, scattered, with glyphs and shouts in capitals.</summary>
    public static CaptionStyle Default { get; } = new(new Argb(255, 255, 255, 255), new Argb(255, 255, 180, 0), VisualPalette.Warm, 1, CaptionLayout.Scatter, true, true);
}

/// <summary>
/// One word on the picture this frame: its centre (from the caption area's corner), its size as a
/// multiple of the base font size, how it is drawn, and how far through its arrival or departure
/// it is. <see cref="Flash"/> is the white burst behind a peak, 1 at the peak fading to 0;
/// <see cref="Shake"/> is how far off its place it is shaken, in base font sizes.
/// </summary>
public sealed record CaptionWord(
    int Id,
    string Text,
    double X,
    double Y,
    double Size,
    double Rotation,
    Argb Color,
    bool Bold,
    bool Italic,
    bool Serif,
    double Opacity,
    double Glow,
    double Shake,
    double Flash,
    string? Glyph,
    Stress Stress);

/// <summary>
/// Turns the words as they come in, with how each was said, into where and how they show
/// (OSD-11), frame by frame.
/// <para>
/// Every word pops in with an overshoot. A word said louder than most of the speaker's last words
/// is a shout: bigger by how loud, bold, in capitals, coloured along the palette; louder than
/// nearly all is a peak: the palette's far end, a white flash behind it and a shake that dies in a
/// quarter second. A word said with a lift in the voice, not shouted, is a lilt: an italic serif in
/// the accent colour. A word said quieter than most is a hush: smaller, lower case, a little
/// faded. A word that took much longer than the ones before it is stretched: it comes in slowly.
/// A fast talker's words pack tighter, and the oldest leave as the cluster fills.
/// </para>
/// <para>
/// Words gather in clusters: a cluster starts where the layout says the next one goes, each word
/// after the previous on its row, a shout on a row of its own; the cluster ends when the voice
/// pauses or the cue ends, and its words fade together.
/// </para>
/// </summary>
public sealed class CaptionChoreography
{
    /// <summary>How long a word takes to pop in, in seconds.</summary>
    public const double PopSeconds = 0.14;

    /// <summary>How long a cluster takes to fade out.</summary>
    public const double FadeSeconds = 0.18;

    /// <summary>Words living at once before the oldest row leaves.</summary>
    public const int MostWords = 14;

    private const double FlashSeconds = 0.35;
    private const double ShakeSeconds = 0.25;

    /// <summary>Where each cluster in turn starts, as shares of the area, when scattered.</summary>
    private static readonly (double X, double Y)[] Scatter =
    [
        (0.08, 0.14), (0.5, 0.48), (0.1, 0.72), (0.56, 0.2), (0.5, 0.78), (0.12, 0.42),
    ];

    private readonly List<Placed> _words = [];
    private readonly CaptionStyle _style;
    private int _nextId;
    private int _clusters;
    private int _row;
    private double _rowEm;
    private double _cursorEm;
    private double _lastReveal = double.NegativeInfinity;
    private double _meanGap = 0.3;
    private int _rowsRetired;

    public CaptionChoreography(CaptionStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        _style = style with { Energy = Math.Clamp(double.IsFinite(style.Energy) ? style.Energy : 1, 0.25, 3) };
    }

    /// <summary>Whether no word is on the picture, not even fading.</summary>
    public bool IsEmpty => _words.Count == 0;

    /// <summary>Whether a cluster is open: a word is living, not fading.</summary>
    public bool IsOpen => _words.Any(word => word.Died is null);

    /// <summary>
    /// A word just spoken at <paramref name="seconds"/>, with the voice's <paramref name="frame"/> and
    /// its <paramref name="stress"/> among the speaker's words.
    /// </summary>
    public void Reveal(SubtitleWord word, SpeechFrame frame, Stress stress, double seconds)
    {
        ArgumentNullException.ThrowIfNull(word);
        if (!IsOpen)
        {
            if (!IsEmpty)
            {
                // A fading cluster leaves at once when the next begins, so words never pile up.
                _words.Clear();
            }

            _clusters++;
            (_row, _rowEm, _cursorEm, _rowsRetired) = (0, 0, 0, 0);
        }

        var gap = seconds - _lastReveal;
        var stretched = double.IsFinite(gap) && gap > 1.8 * _meanGap && gap < 3;
        if (double.IsFinite(gap) && gap < 3)
        {
            _meanGap += (gap - _meanGap) * 0.3;
        }

        _lastReveal = seconds;

        var lilt = stress is Stress.Plain && frame.Pitch >= 0.72;
        var size = stress switch
        {
            Stress.Hush => 0.8,
            Stress.Shout => 1.8 + (0.8 * Math.Clamp((frame.Loudness - 0.75) / 0.25, 0, 1)),
            Stress.Peak => 2.6,
            _ => lilt ? 1.4 : 1,
        };
        size = 1 + ((size - 1) * _style.Energy);
        var text = word.Text;
        if (_style.UpperCase && stress is Stress.Shout or Stress.Peak)
        {
            text = text.ToUpperInvariant();
        }
        else if (stress is Stress.Hush)
        {
            text = text.ToLowerInvariant();
        }

        var glyph = _style.Glyphs ? CaptionGlyphs.For(word.Text) : null;
        var widthEm = (0.58 * text.Length * size) + (glyph is null ? 0 : 1.3 * size);
        var loud = stress is Stress.Peak ? 1 : Math.Clamp((frame.Loudness - 0.6) / 0.4, 0, 1);
        var color = word.Style.Color is { } own ? Argb.From(own, 100)
            : stress is Stress.Shout or Stress.Peak ? Visualizers.PaletteColor(_style.Palette, loud, seconds, _style.Accent, (_style.Accent.R << 16) | (_style.Accent.G << 8) | _style.Accent.B)
            : lilt ? _style.Accent
            : _style.Text;

        // A shout takes a row of its own; otherwise the row goes on until it is full.
        var own = stress is Stress.Shout or Stress.Peak;
        if (_cursorEm > 0 && (own || _words.Any(placed => placed.Died is null && placed.Row == _row && placed.Own)))
        {
            NewRow();
        }

        var placed = new Placed(_nextId++, text, stress, lilt, stretched, seconds, _row, _cursorEm, widthEm, size, color, glyph, word.Style.Italic || lilt, lilt, word.Style.Bold || own, own);
        _words.Add(placed);
        _cursorEm += widthEm + (0.35 * size);
        _rowEm = Math.Max(_rowEm, size);
        if (own)
        {
            NewRow();
        }

        // The cluster is full: its oldest row leaves.
        while (_words.Count(w => w.Died is null) > MostWords)
        {
            var oldest = _words.Where(w => w.Died is null).Min(w => w.Row);
            foreach (var w in _words.Where(w => w.Died is null && w.Row == oldest))
            {
                w.Died = seconds;
            }

            _rowsRetired++;
        }
    }

    /// <summary>The voice paused or the cue ended: every word fades.</summary>
    public void Clear(double seconds)
    {
        foreach (var word in _words)
        {
            word.Died ??= seconds;
        }
    }

    /// <summary>Everything leaves at once, as on a seek.</summary>
    public void Reset()
    {
        _words.Clear();
        (_row, _rowEm, _cursorEm, _rowsRetired, _lastReveal, _meanGap) = (0, 0, 0, 0, double.NegativeInfinity, 0.3);
    }

    /// <summary>
    /// The words on the picture at <paramref name="seconds"/>, laid out in an <paramref name="area"/>
    /// whose base font is <paramref name="fontSize"/> tall, in the frame's own pace.
    /// </summary>
    public IReadOnlyList<CaptionWord> Scene(double seconds, Area area, double fontSize, float pace = 0)
    {
        _words.RemoveAll(word => word.Died is { } died && seconds - died >= FadeSeconds);
        if (_words.Count == 0)
        {
            return [];
        }

        var em = Math.Max(1, fontSize);
        var tight = Math.Clamp((pace - 3) / 4, 0, 1);
        var rowGap = 1.15 - (0.25 * tight);
        var anchor = Anchor();
        var rows = _words.GroupBy(word => word.Row).OrderBy(group => group.Key).ToList();
        var rowHeights = rows.ToDictionary(group => group.Key, group => group.Max(word => word.Size) * rowGap * em);
        var top = anchor.Y * area.Height;
        var totalHeight = rowHeights.Values.Sum();
        top = Math.Min(top, Math.Max(0, area.Height - totalHeight));

        var scene = new List<CaptionWord>(_words.Count);
        var y = top;
        foreach (var group in rows)
        {
            var height = rowHeights[group.Key];
            var rowWidth = group.Max(word => (word.ColumnEm + word.WidthEm) * em);
            var stagger = _style.Layout == CaptionLayout.Scatter && group.Key % 2 == 1 ? 1.5 * em : 0;
            var left = _style.Layout == CaptionLayout.Scatter ? (anchor.X * area.Width) + stagger : (area.Width - rowWidth) / 2;
            left = Math.Clamp(left, 0, Math.Max(0, area.Width - rowWidth));
            foreach (var word in group)
            {
                var age = seconds - word.Born;
                var pop = Pop(age / (word.Stretched ? PopSeconds * 2.5 : PopSeconds));
                var opacity = word.Died is { } died ? Math.Clamp(1 - ((seconds - died) / FadeSeconds), 0, 1) : 1;
                opacity *= word.Stress is Stress.Hush ? 0.75 : 1;
                var shake = word.Stress is Stress.Peak ? 0.12 * _style.Energy * Math.Max(0, 1 - (age / ShakeSeconds)) : 0;
                var flash = word.Stress is Stress.Peak ? Math.Clamp(1 - (age / FlashSeconds), 0, 1) : 0;
                var rotation = word.Own ? ((word.Id % 5) - 2) * 1.5 : word.Lilt ? -2 : 0;
                scene.Add(new CaptionWord(
                    word.Id,
                    word.Text,
                    left + ((word.ColumnEm + (word.WidthEm / 2)) * em),
                    y + (height / 2),
                    word.Size * pop,
                    rotation,
                    word.Color,
                    word.Bold,
                    word.Italic,
                    word.Serif,
                    opacity,
                    word.Own || word.Lilt ? 0.6 : 0,
                    shake,
                    flash,
                    word.Glyph,
                    word.Stress));
            }

            y += height;
        }

        return scene;
    }

    /// <summary>The pop: from small through an overshoot to full size over <paramref name="t"/> (0 to 1), 1 after.</summary>
    public static double Pop(double t)
    {
        if (!double.IsFinite(t) || t >= 1)
        {
            return 1;
        }

        t = Math.Max(0, t);
        return t < 0.5 ? 0.6 + ((1.35 - 0.6) * (t / 0.5)) : 1.35 - ((1.35 - 1) * ((t - 0.5) / 0.5));
    }

    private void NewRow()
    {
        _row++;
        (_rowEm, _cursorEm) = (0, 0);
    }

    private (double X, double Y) Anchor() => _style.Layout switch
    {
        CaptionLayout.Scatter => Scatter[(_clusters + Scatter.Length - 1) % Scatter.Length],
        CaptionLayout.Centre => (0.5, 0.4),
        _ => (0.5, 0.72),
    };

    private sealed class Placed(int id, string text, Stress stress, bool lilt, bool stretched, double born, int row, double columnEm, double widthEm, double size, Argb color, string? glyph, bool italic, bool serif, bool bold, bool own)
    {
        public int Id { get; } = id;

        public string Text { get; } = text;

        public Stress Stress { get; } = stress;

        public bool Lilt { get; } = lilt;

        public bool Stretched { get; } = stretched;

        public double Born { get; } = born;

        public double? Died { get; set; }

        public int Row { get; } = row;

        public double ColumnEm { get; } = columnEm;

        public double WidthEm { get; } = widthEm;

        public double Size { get; } = size;

        public Argb Color { get; } = color;

        public string? Glyph { get; } = glyph;

        public bool Italic { get; } = italic;

        public bool Serif { get; } = serif;

        public bool Bold { get; } = bold;

        public bool Own { get; } = own;
    }
}
