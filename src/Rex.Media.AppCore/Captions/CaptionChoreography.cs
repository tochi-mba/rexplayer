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
public sealed record CaptionStyle(Argb Text, Argb Accent, VisualPalette Palette, double Energy, CaptionLayout Layout, bool Glyphs, bool UpperCase, bool AvoidPicture = true)
{
    /// <summary>White text, a gold accent, warm stress colours, full energy, scattered, with glyphs, shouts in capitals, clear of what matters.</summary>
    public static CaptionStyle Default { get; } = new(new Argb(255, 255, 255, 255), new Argb(255, 255, 180, 0), VisualPalette.Warm, 1, CaptionLayout.Scatter, true, true);
}

/// <summary>
/// One word on the picture this frame: its centre (from the caption area's corner), its size as a
/// multiple of the base font size, how it is drawn, how far through its arrival or departure it
/// is, and what keeps it readable. <see cref="Flash"/> is the white burst behind a peak, 1 at the
/// peak fading to 0; <see cref="Shake"/> is how far off its place it is shaken, in base font sizes.
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
    Stress Stress,
    Backing Backing);

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
/// A fast talker's rows pack tighter, and the oldest words leave as the cluster fills.
/// </para>
/// <para>
/// Words gather in clusters, flowing into rows as wide as reads comfortably, a shout on a row of
/// its own. When a cluster starts, the picture showing for it (<see cref="PictureSpace"/>) decides
/// where it goes: near where the layout puts it, but wherever covers least of the faces, text,
/// subject, movement and detail that matter. Every word is kept readable: never smaller than a
/// readable size, never wider than the picture, outlined, and on a dark plate where the picture
/// behind it is too bright or too busy for white text. The cluster ends when the voice pauses or
/// the cue ends, and its words fade together.
/// </para>
/// </summary>
public sealed class CaptionChoreography
{
    /// <summary>How long a word takes to pop in, in seconds.</summary>
    public const double PopSeconds = 0.14;

    /// <summary>How long a cluster takes to fade out.</summary>
    public const double FadeSeconds = 0.18;

    /// <summary>Words living at once before the oldest leave.</summary>
    public const int MostWords = 14;

    /// <summary>The smallest a word is drawn, as a share of the caption area's height, so it always reads.</summary>
    public const double SmallestShare = 0.028;

    private const double FlashSeconds = 0.35;
    private const double ShakeSeconds = 0.25;
    private const double RowShare = 0.8;

    /// <summary>Where each cluster in turn would start, as shares of the area, when scattered.</summary>
    private static readonly (double X, double Y)[] Scatter =
    [
        (0.08, 0.14), (0.45, 0.5), (0.1, 0.7), (0.5, 0.18), (0.4, 0.74), (0.12, 0.42),
    ];

    private readonly List<Placed> _words = [];
    private readonly CaptionStyle _style;
    private int _nextId;
    private int _clusters;
    private (double X, double Y)? _anchor;
    private double _lastReveal = double.NegativeInfinity;
    private double _meanGap = 0.3;

    public CaptionChoreography(CaptionStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        _style = style with { Energy = Math.Clamp(double.IsFinite(style.Energy) ? style.Energy : 1, 0.25, 3) };
    }

    /// <summary>Whether no word is on the picture, not even fading.</summary>
    public bool IsEmpty => _words.Count == 0;

    /// <summary>Whether a cluster is open: a word is living, not fading.</summary>
    public bool IsOpen => _words.Any(word => word.Died is null);

    /// <summary>Clusters started so far.</summary>
    public int Clusters => _clusters;

    /// <summary>
    /// A word just spoken at <paramref name="seconds"/>, with the voice's <paramref name="frame"/> and
    /// its <paramref name="stress"/> among the speaker's words.
    /// </summary>
    public void Reveal(SubtitleWord word, SpeechFrame frame, Stress stress, double seconds)
    {
        ArgumentNullException.ThrowIfNull(word);
        if (!IsOpen)
        {
            // A fading cluster leaves at once when the next begins, so words never pile up.
            _words.Clear();
            _clusters++;
            _anchor = null;
        }

        var gap = seconds - _lastReveal;
        var stretched = gap > 1.8 * _meanGap && gap < 3;
        if (gap < 3)
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
        var widthEm = (0.58 * text.Length) + (glyph is null ? 0 : 1.3);
        var own = stress is Stress.Shout or Stress.Peak;
        var loud = stress is Stress.Peak ? 1 : Math.Clamp((frame.Loudness - 0.6) / 0.4, 0, 1);
        var rgb = (_style.Accent.R << 16) | (_style.Accent.G << 8) | _style.Accent.B;
        var color = word.Style.Color is { } fromFile ? Argb.From(fromFile, 100)
            : own ? Visualizers.PaletteColor(_style.Palette, loud, seconds, _style.Accent, rgb)
            : lilt ? _style.Accent
            : _style.Text;

        _words.Add(new Placed(_nextId++, text, stress, lilt, stretched, seconds, widthEm, size, color, glyph, word.Style.Italic || lilt, lilt, word.Style.Bold || own, own));

        // The cluster is full: its oldest words leave together.
        var living = _words.Where(w => w.Died is null).ToList();
        if (living.Count > MostWords)
        {
            foreach (var old in living.Take(living.Count - MostWords + 4))
            {
                old.Died = seconds;
            }
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
        (_anchor, _lastReveal, _meanGap) = (null, double.NegativeInfinity, 0.3);
    }

    /// <summary>
    /// The words on the picture at <paramref name="seconds"/>, laid out in an <paramref name="area"/>
    /// whose base font is <paramref name="fontSize"/> tall, at the voice's <paramref name="pace"/>,
    /// placed clear of what matters in <paramref name="picture"/> (the picture showing, or null when
    /// it has not been read).
    /// </summary>
    public IReadOnlyList<CaptionWord> Scene(double seconds, Area area, double fontSize, float pace = 0, PictureSpace? picture = null)
    {
        _words.RemoveAll(word => word.Died is { } died && seconds - died >= FadeSeconds);
        if (_words.Count == 0 || area.Width <= 0 || area.Height <= 0)
        {
            return [];
        }

        var em = Math.Max(Math.Max(1, fontSize), area.Height * SmallestShare / 0.8);
        var tight = Math.Clamp((pace - 3) / 4, 0, 1);
        var rowGap = 1.2 - (0.25 * tight);
        var rows = Flow(em, area.Width * RowShare);
        var widths = rows.Select(row => row.Sum(word => word.Width + word.Space) - row[^1].Space).ToList();
        var heights = rows.Select(row => row.Max(word => word.Size) * rowGap * em).ToList();
        var (boxWidth, boxHeight) = (Math.Min(area.Width, widths.Max() + (1.5 * em)), Math.Min(area.Height, heights.Sum()));

        // The cluster's place is chosen once, when it is first drawn, so it never jumps; it is chosen
        // for a little more room than it starts with, as more words will come.
        if (_anchor is null)
        {
            var preferred = Preferred();
            var reserve = (Width: Math.Max(boxWidth, area.Width * 0.5) / area.Width, Height: Math.Max(boxHeight, 2.6 * rowGap * em) / area.Height);
            _anchor = _style.AvoidPicture && picture is not null
                ? picture.Place(reserve.Width, reserve.Height, (preferred.X - (Centred ? reserve.Width / 2 : 0), preferred.Y))
                : (preferred.X - (Centred ? reserve.Width / 2 : 0), preferred.Y);
        }

        var (anchorX, anchorY) = _anchor.Value;
        var top = Math.Clamp(anchorY * area.Height, 0, Math.Max(0, area.Height - boxHeight));
        var reservedWidth = Math.Max(boxWidth, area.Width * 0.5);
        var scene = new List<CaptionWord>(_words.Count);
        var y = top;
        for (var r = 0; r < rows.Count; r++)
        {
            var stagger = _style.Layout == CaptionLayout.Scatter && r % 2 == 1 ? 1.5 * em : 0;
            var left = (anchorX * area.Width) + (Centred ? (reservedWidth - widths[r]) / 2 : stagger);
            left = Math.Clamp(left, 0, Math.Max(0, area.Width - widths[r]));
            var x = left;
            foreach (var slot in rows[r])
            {
                scene.Add(Draw(slot, x, y + (heights[r] / 2), seconds, em, area, picture));
                x += slot.Width + slot.Space;
            }

            y += heights[r];
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

    private bool Centred => _style.Layout != CaptionLayout.Scatter;

    private CaptionWord Draw(Slot slot, double left, double centreY, double seconds, double em, Area area, PictureSpace? picture)
    {
        var word = slot.Word;
        var age = seconds - word.Born;
        var pop = Pop(age / (word.Stretched ? PopSeconds * 2.5 : PopSeconds));
        var opacity = word.Died is { } died ? Math.Clamp(1 - ((seconds - died) / FadeSeconds), 0, 1) : 1;
        opacity *= word.Stress is Stress.Hush ? 0.8 : 1;
        var shake = word.Stress is Stress.Peak ? 0.12 * _style.Energy * Math.Max(0, 1 - (age / ShakeSeconds)) : 0;
        var flash = word.Stress is Stress.Peak ? Math.Clamp(1 - (age / FlashSeconds), 0, 1) : 0;
        var rotation = word.Own ? ((word.Id % 5) - 2) * 1.5 : word.Lilt ? -2 : 0;
        var height = slot.Size * em;
        var backing = picture?.Behind(left / area.Width, (centreY - (height / 2)) / area.Height, slot.Width / area.Width, height / area.Height) ?? new Backing(0.6, 0);
        return new CaptionWord(
            word.Id,
            word.Text,
            left + (slot.Width / 2),
            centreY,
            slot.Size * pop,
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
            word.Stress,
            backing);
    }

    /// <summary>The words in rows no wider than <paramref name="rowWidth"/>, a shout alone on its row, every word shrunk to fit the row if it must.</summary>
    private List<List<Slot>> Flow(double em, double rowWidth)
    {
        var rows = new List<List<Slot>>();
        List<Slot>? row = null;
        var used = 0.0;
        var previousOwn = false;
        foreach (var word in _words)
        {
            var size = Math.Min(word.Size, rowWidth / Math.Max(1e-9, word.WidthEm * em));
            var width = word.WidthEm * size * em;
            var space = 0.35 * size * em;
            if (row is null || word.Own || previousOwn || used + width > rowWidth)
            {
                row = [];
                rows.Add(row);
                used = 0;
            }

            row.Add(new Slot(word, size, width, space));
            used += width + space;
            previousOwn = word.Own;
        }

        return rows;
    }

    private (double X, double Y) Preferred() => _style.Layout switch
    {
        CaptionLayout.Scatter => Scatter[(_clusters + Scatter.Length - 1) % Scatter.Length],
        CaptionLayout.Centre => (0.5, 0.4),
        _ => (0.5, 0.72),
    };

    private sealed record Slot(Placed Word, double Size, double Width, double Space);

    private sealed class Placed(int id, string text, Stress stress, bool lilt, bool stretched, double born, double widthEm, double size, Argb color, string? glyph, bool italic, bool serif, bool bold, bool own)
    {
        public int Id { get; } = id;

        public string Text { get; } = text;

        public Stress Stress { get; } = stress;

        public bool Lilt { get; } = lilt;

        public bool Stretched { get; } = stretched;

        public double Born { get; } = born;

        public double? Died { get; set; }

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
