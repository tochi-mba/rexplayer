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

    /// <summary>A block at the lower left, lines starting at the same edge, as a terminal prints.</summary>
    Left,

    /// <summary>Every word somewhere of its own on the picture, like confetti.</summary>
    Confetti,

    /// <summary>One line sliding steadily leftward along the bottom, as a news ticker runs.</summary>
    Ticker,

    /// <summary>Each word a step up and to the right of the one before, climbing from the lower left.</summary>
    Stairs,
}

/// <summary>The kind of typeface words are set in.</summary>
public enum CaptionFont
{
    Sans,
    Serif,
    Mono,

    /// <summary>A heavy, condensed display face, as posters use.</summary>
    Display,

    /// <summary>A rounded, friendly face.</summary>
    Rounded,

    /// <summary>A handwritten face, as a marker pen writes.</summary>
    Hand,
}

/// <summary>How words are cased: as the voice says (shouts in capitals, hushes in lower case), all lower, or all capitals.</summary>
public enum CaptionCase
{
    AsSaid,
    Lower,
    Upper,
}

/// <summary>How a word arrives.</summary>
public enum CaptionMotion
{
    /// <summary>Grows in with an overshoot.</summary>
    Pop,

    /// <summary>Slides in from the left as it fades up.</summary>
    Slide,

    /// <summary>Its letters appear one after another.</summary>
    Typewriter,

    /// <summary>Lands with two bounces.</summary>
    Bounce,

    /// <summary>Falls in from above.</summary>
    Drop,

    /// <summary>Fades up from nothing, settling from a little large.</summary>
    Fade,

    /// <summary>Comes down hard from far too big, as a rubber stamp lands.</summary>
    Stamp,
}

/// <summary>
/// How kinetic captions look: the user's choices, as the window reads them from the settings, or
/// one of <see cref="CaptionPresets"/>. <see cref="Highlight"/> keeps only the newest word bright,
/// as a sing-along does; <see cref="Flicker"/> makes a peak flicker as it lands;
/// <see cref="Tilt"/> scales how far words are turned; <see cref="Outline"/> scales the outline;
/// <see cref="PlateAlways"/> puts a dark plate behind every word, not only where the picture needs it,
/// and <see cref="PlateLight"/> makes the plates white (the text is then dark). <see cref="Prefill"/>
/// shows a cue's whole line at once, dim, and lights each word as it is said; <see cref="OneAtATime"/>
/// shows only the word being said; <see cref="Flat"/> sizes and colours every word alike, whatever the
/// voice did; <see cref="Cursor"/> blinks a cursor after the last word typed; <see cref="Split"/> draws
/// the word's red and cyan apart, as a broken feed; <see cref="Jitter"/> keeps every word trembling, in
/// base font sizes. <see cref="PlateTint"/> colours each word's plate in the word's own colour and
/// sets the word in ink; <see cref="Hollow"/> draws plain words as outlines only, filling in the
/// stressed ones; <see cref="Dissolve"/> is the seconds a word stays before it drifts up and fades
/// of its own accord (0: it stays with its cluster).
/// </summary>
public sealed record CaptionStyle(
    Argb Text,
    Argb Accent,
    VisualPalette Palette,
    double Energy,
    CaptionLayout Layout,
    bool Glyphs,
    bool UpperCase,
    bool AvoidPicture = true,
    CaptionFont Font = CaptionFont.Sans,
    CaptionCase Case = CaptionCase.AsSaid,
    CaptionMotion Motion = CaptionMotion.Pop,
    bool Highlight = false,
    bool Flicker = false,
    double Tilt = 1,
    double Outline = 1,
    bool PlateAlways = false,
    bool PlateLight = false,
    bool Prefill = false,
    bool OneAtATime = false,
    bool Flat = false,
    bool Cursor = false,
    bool Split = false,
    double Jitter = 0,
    bool PlateTint = false,
    bool Hollow = false,
    double Dissolve = 0)
{
    /// <summary>White text, a gold accent, warm stress colours, full energy, scattered, with glyphs, shouts in capitals, clear of what matters.</summary>
    public static CaptionStyle Default { get; } = new(new Argb(255, 255, 255, 255), new Argb(255, 255, 180, 0), VisualPalette.Warm, 1, CaptionLayout.Scatter, true, true);
}

/// <summary>
/// One word on the picture this frame: its centre (from the caption area's corner), its size as a
/// multiple of the base font size, how it is drawn, how far through its arrival or departure it
/// is, and what keeps it readable. <see cref="Settled"/> is its size once it has popped in, so
/// the window lays the text out once and scales it for the pop; <see cref="OffsetX"/> and
/// <see cref="OffsetY"/> move it from its place, in base font sizes, as it arrives; a typewriter's
/// <see cref="Text"/> grows letter by letter. <see cref="Lit"/> is false while a word waits, dim, to be
/// said; <see cref="PlateLight"/> asks for a white plate and <see cref="Split"/> for the red and cyan
/// drawn apart. <see cref="Flash"/> is the white burst behind a peak, 1 at the
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
    Backing Backing,
    double Settled,
    CaptionFont Font = CaptionFont.Sans,
    double OffsetX = 0,
    double OffsetY = 0,
    bool Lit = true,
    bool PlateLight = false,
    bool Split = false,
    bool PlateTint = false,
    bool Hollow = false);

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

    /// <summary>How long a dissolving word takes to fade once its time is up.</summary>
    public const double DissolveSeconds = 0.6;

    /// <summary>How fast a ticker runs, in widths of the area a second.</summary>
    public const double TickerSpeed = 0.18;

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

    // Working lists a scene reuses from frame to frame, so drawing allocates only the words it returns.
    private readonly List<List<Slot>> _rows = [];
    private readonly List<double> _widths = [];
    private readonly List<double> _heights = [];
    private readonly List<Placed> _living = [];
    private readonly List<Placed> _ordered = [];
    private int _rowCount;
    private Placed? _lastLiving;
    private readonly CaptionStyle _style;
    private int _nextId;
    private int _clusters;
    private (double X, double Y)? _anchor;
    private (double X, double Y) _shown;
    private (double Width, double Height) _reserve;
    private double _lastScene = double.NaN;
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
    public int Reveal(SubtitleWord word, SpeechFrame frame, Stress stress, double seconds) => Reveal(word, frame, stress, seconds, lit: true);

    /// <summary>
    /// A word of the line, shown dim and unsaid (<paramref name="lit"/> false) for a look that shows
    /// the whole line first, or said; the word's id, for <see cref="Light"/>.
    /// </summary>
    public int Reveal(SubtitleWord word, SpeechFrame frame, Stress stress, double seconds, bool lit)
    {
        ArgumentNullException.ThrowIfNull(word);
        if (_style.Flat)
        {
            stress = Stress.Plain;
        }

        if (!IsOpen)
        {
            // A fading cluster leaves at once when the next begins, so words never pile up.
            _words.Clear();
            _clusters++;
            _anchor = null;
            _reserve = (0, 0);
        }

        if (_style.OneAtATime)
        {
            // The word before fades as this one lands; the cluster, and its place, go on.
            foreach (var other in _words.Where(w => w.Died is null))
            {
                other.Died = seconds;
            }
        }

        var gap = seconds - _lastReveal;
        var stretched = gap > 1.8 * _meanGap && gap < 3;
        if (gap < 3)
        {
            _meanGap += (gap - _meanGap) * 0.3;
        }

        _lastReveal = seconds;

        var lilt = !_style.Flat && stress is Stress.Plain && frame.Pitch >= 0.72;
        var size = stress switch
        {
            Stress.Hush => 0.8,
            Stress.Shout => 1.8 + (0.8 * Math.Clamp((frame.Loudness - 0.75) / 0.25, 0, 1)),
            Stress.Peak => 2.6,
            _ => lilt ? 1.4 : 1,
        };
        size = 1 + ((size - 1) * _style.Energy);
        var text = _style.Case switch
        {
            CaptionCase.Lower => word.Text.ToLowerInvariant(),
            CaptionCase.Upper => word.Text.ToUpperInvariant(),
            _ when _style.UpperCase && stress is Stress.Shout or Stress.Peak => word.Text.ToUpperInvariant(),
            _ when stress is Stress.Hush => word.Text.ToLowerInvariant(),
            _ => word.Text,
        };

        var glyph = _style.Glyphs ? CaptionGlyphs.For(word.Text) : null;
        var widthEm = WidthEm(text, _style.Font, word.Style.Bold || stress is Stress.Shout or Stress.Peak) + (glyph is null ? 0 : 1.3) + (_style.PlateAlways ? 0.4 : 0);
        var own = stress is Stress.Shout or Stress.Peak;
        var loud = stress is Stress.Peak ? 1 : Math.Clamp((frame.Loudness - 0.6) / 0.4, 0, 1);
        var rgb = (_style.Accent.R << 16) | (_style.Accent.G << 8) | _style.Accent.B;
        var color = word.Style.Color is { } fromFile ? Argb.From(fromFile, 100)
            : own ? Visualizers.PaletteColor(_style.Palette, loud, seconds, _style.Accent, rgb)
            : lilt ? _style.Accent
            : _style.PlateTint ? (_style.Palette == VisualPalette.Rainbow ? Pastel(Visualizers.PaletteColor(VisualPalette.Rainbow, (_nextId % 7) / 7.0, 0, _style.Accent, rgb)) : _style.Accent)
            : _style.Text;
        if (_style.PlateTint && own)
        {
            color = Pastel(color);
        }

        var placed = new Placed(_nextId++, text, stress, lilt, stretched, seconds, widthEm, size, color, glyph, word.Style.Italic || lilt, lilt, word.Style.Bold || own, own) { Lit = lit };
        _words.Add(placed);

        // The cluster is full: its oldest words leave together (a line shown whole stays whole).
        var living = _words.Where(w => w.Died is null).ToList();
        if (living.Count > MostWords && !_style.Prefill)
        {
            foreach (var old in living.Take(living.Count - MostWords + 4))
            {
                old.Died = seconds;
            }
        }

        return placed.Id;
    }

    /// <summary>The word <paramref name="id"/>, shown dim before, is said now: it lights, in the accent colour, and pops again.</summary>
    public void Light(int id, double seconds)
    {
        if (_words.FirstOrDefault(word => word.Id == id) is { Lit: false } word)
        {
            word.Lit = true;
            word.Born = seconds;
            word.Color = _style.Accent;
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
        (_anchor, _lastReveal, _meanGap, _lastScene) = (null, double.NegativeInfinity, 0.3, double.NaN);
        _reserve = (0, 0);
    }

    /// <summary>
    /// The words on the picture at <paramref name="seconds"/>, laid out in an <paramref name="area"/>
    /// whose base font is <paramref name="fontSize"/> tall, at the voice's <paramref name="pace"/>,
    /// placed clear of what matters in <paramref name="picture"/> (the picture showing, or null when
    /// it has not been read).
    /// </summary>
    public IReadOnlyList<CaptionWord> Scene(double seconds, Area area, double fontSize, float pace = 0, PictureSpace? picture = null)
    {
        RemoveGone(seconds);
        if (_words.Count == 0 || area.Width <= 0 || area.Height <= 0)
        {
            return [];
        }

        var em = Math.Max(Math.Max(1, fontSize), area.Height * SmallestShare / 0.8);
        var tight = Math.Clamp((pace - 3) / 4, 0, 1);
        var rowGap = (_style.PlateAlways ? 1.5 : 1.2) - (0.25 * tight);
        if (_style.Layout == CaptionLayout.Confetti)
        {
            return Confetti(seconds, area, em, picture);
        }

        if (_style.Layout == CaptionLayout.Ticker)
        {
            return Ticker(seconds, area, em, picture);
        }

        if (_style.Layout == CaptionLayout.Stairs)
        {
            return Stairs(seconds, area, em, picture);
        }

        var rows = Flow(em, area.Width * RowShare);
        var (widths, heights) = (_widths, _heights);
        widths.Clear();
        heights.Clear();
        var (widest, tallest) = (double.MinValue, 0.0);
        for (var r = 0; r < _rowCount; r++)
        {
            var (width, size) = (0.0, double.MinValue);
            foreach (var word in rows[r])
            {
                width += word.Width + word.Space;
                size = Math.Max(size, word.Size);
            }

            widths.Add(width - rows[r][^1].Space);
            heights.Add(size * rowGap * em);
            widest = Math.Max(widest, widths[r]);
            tallest += heights[r];
        }

        var (boxWidth, boxHeight) = (Math.Min(area.Width, widest + (1.5 * em)), Math.Min(area.Height, tallest));
        var needed = (Width: Math.Max(boxWidth, area.Width * 0.5) / area.Width, Height: Math.Max(boxHeight, 2.6 * rowGap * em) / area.Height);
        _reserve = (Math.Max(_reserve.Width, needed.Width), Math.Max(_reserve.Height, needed.Height));

        // The cluster's place is chosen once, when it is first drawn, so it never jumps; it is chosen
        // for a little more room than it starts with, as more words will come.
        var dt = double.IsNaN(_lastScene) ? 1 : Math.Clamp(seconds - _lastScene, 0, 0.5);
        _lastScene = seconds;
        if (_anchor is null)
        {
            var preferred = Preferred();
            _anchor = _style.AvoidPicture && picture is not null
                ? picture.Place(_reserve.Width, _reserve.Height, (preferred.X - (Centred ? _reserve.Width / 2 : 0), preferred.Y))
                : (preferred.X - (Centred ? _reserve.Width / 2 : 0), preferred.Y);
            _shown = _anchor.Value;
        }
        else if (_style.AvoidPicture && picture is not null && picture.CoversProtected(_anchor.Value.X, _anchor.Value.Y, _reserve.Width, _reserve.Height))
        {
            // Protected content has come under the cluster: it moves off, to the nearest clear place.
            var moved = picture.Place(_reserve.Width, _reserve.Height, _anchor.Value);
            if (!picture.CoversProtected(moved.X, moved.Y, _reserve.Width, _reserve.Height))
            {
                _anchor = moved;
            }
        }

        // The cluster eases to a new place over about a sixth of a second, never jumping.
        var ease = Math.Min(1, dt * 6);
        _shown = (_shown.X + ((_anchor.Value.X - _shown.X) * ease), _shown.Y + ((_anchor.Value.Y - _shown.Y) * ease));
        var (anchorX, anchorY) = _shown;
        var top = Math.Clamp(anchorY * area.Height, 0, Math.Max(0, area.Height - boxHeight));
        var reservedWidth = Math.Max(boxWidth, area.Width * 0.5);
        var scene = new List<CaptionWord>(_words.Count);
        var y = top;
        for (var r = 0; r < _rowCount; r++)
        {
            var stagger = _style.Layout == CaptionLayout.Scatter && r % 2 == 1 ? 1.5 * em : 0;
            var left = (anchorX * area.Width) + (Centred ? (reservedWidth - widths[r]) / 2 : stagger);
            if (_style.Layout == CaptionLayout.Left)
            {
                left = anchorX * area.Width;
            }

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

    private bool Centred => _style.Layout is CaptionLayout.Stack or CaptionLayout.Centre;

    /// <summary>
    /// One line along the bottom, every word in the order said, the whole line sliding left at a
    /// steady pace from the right edge; a word that has slid off the left is gone.
    /// </summary>
    private IReadOnlyList<CaptionWord> Ticker(double seconds, Area area, double em, PictureSpace? picture)
    {
        var scene = new List<CaptionWord>(_words.Count);
        var height = 1.4 * em;
        var centreY = area.Height - (height * 0.9);
        var x = 0.0;
        var first = _words.Count > 0 ? _words[0].Born : seconds;
        var origin = area.Width - ((seconds - first) * TickerSpeed * area.Width);
        foreach (var word in _words)
        {
            var width = word.WidthEm * em;
            var left = origin + x;
            if (left + width < 0)
            {
                word.Died ??= seconds;
            }

            scene.Add(Draw(new Slot(word, 1, width, 0.6 * em), left, centreY, seconds, em, area, picture));
            x += width + (0.6 * em);
        }

        return scene;
    }

    /// <summary>
    /// Each word a step above and to the right of the one before, climbing from the lower left;
    /// when the stairs reach the top the oldest step leaves and the rest shift down.
    /// </summary>
    private IReadOnlyList<CaptionWord> Stairs(double seconds, Area area, double em, PictureSpace? picture)
    {
        var scene = new List<CaptionWord>(_words.Count);
        var living = _living;
        living.Clear();
        foreach (var word in _words)
        {
            if (word.Died is null)
            {
                living.Add(word);
            }
        }

        var step = 1.3 * em;
        var most = Math.Max(2, (int)((area.Height * 0.7) / step));
        var overflow = Math.Max(0, living.Count - most);
        for (var i = 0; i < overflow; i++)
        {
            living[i].Died ??= living[i + most].Born;
        }

        var bottom = area.Height * 0.86;
        var ordered = _ordered;
        ordered.Clear();
        ordered.AddRange(_words);
        var index = 0;
        foreach (var word in ordered)
        {
            var size = Math.Min(word.Size, (area.Width * 0.5) / Math.Max(1e-9, word.WidthEm * em));
            var width = word.WidthEm * size * em;
            var left = Math.Clamp((area.Width * 0.06) + (index * 1.4 * em), 0, Math.Max(0, area.Width - width));
            var centreY = bottom - (index * step);
            scene.Add(Draw(new Slot(word, size, width, 0), left, centreY, seconds, em, area, picture));
            if (word.Died is null)
            {
                index++;
            }
        }

        return scene;
    }

    /// <summary>A colour softened toward white, for a plate dark text reads on.</summary>
    public static Argb Pastel(Argb color) => new(255, (byte)(color.R + ((255 - color.R) * 0.55)), (byte)(color.G + ((255 - color.G) * 0.55)), (byte)(color.B + ((255 - color.B) * 0.55)));

    /// <summary>Every word at a spot of its own, chosen when it arrives and kept, clear of faces where the picture is read; each in its own hue.</summary>
    private IReadOnlyList<CaptionWord> Confetti(double seconds, Area area, double em, PictureSpace? picture)
    {
        var scene = new List<CaptionWord>(_words.Count);
        foreach (var word in _words)
        {
            var size = Math.Min(word.Size, area.Width * 0.8 / Math.Max(1e-9, word.WidthEm * em));
            var (width, height) = (word.WidthEm * size * em, size * em * 1.2);
            var widthShare = width / area.Width;
            var heightShare = height / area.Height;
            if (word.Spot is null || (_style.AvoidPicture && picture is not null && picture.CoversProtected(word.Spot.Value.X, word.Spot.Value.Y, widthShare, heightShare)))
            {
                word.Spot = ConfettiSpot(word, widthShare, heightShare, picture);
                word.SpotSize = (widthShare, heightShare);
            }

            var slot = new Slot(word, size, width, 0);
            var left = word.Spot.Value.X * area.Width;
            var centreY = (word.Spot.Value.Y * area.Height) + (height / 2);
            scene.Add(Draw(slot, left, centreY, seconds, em, area, picture, Visualizers.PaletteColor(_style.Palette, (word.Id % 7) / 7.0, seconds, _style.Accent, 0)));
        }

        return scene;
    }

    /// <summary>
    /// Where a confetti word lands: of a dozen spots chosen by its number, the one that overlaps the
    /// words already placed least (none, where there is room), and then, where the picture is read,
    /// the clearest place near it.
    /// </summary>
    private (double X, double Y) ConfettiSpot(Placed word, double width, double height, PictureSpace? picture)
    {
        var others = _words.Where(other => other != word && other.Spot is not null && other.Died is null).ToList();
        // A spot of its own by its number, then the nearest place on a fine grid that overlaps no other word and no face.
        var seed = ((uint)word.Id * 2654435761u) % 1000 / 1000.0;
        var seed2 = ((uint)word.Id * 40503u + 7919u) % 1000 / 1000.0;
        var wish = (X: 0.06 + (seed * Math.Max(0, 0.88 - width)), Y: 0.06 + (seed2 * Math.Max(0, 0.88 - height)));
        var best = wish;
        var least = double.MaxValue;
        const int Columns = 24;
        const int Rows = 32;
        for (var row = 0; row <= Rows; row++)
        {
            for (var column = 0; column <= Columns; column++)
            {
                var spot = (X: 0.04 + ((0.92 - width) * column / Columns), Y: 0.04 + ((0.92 - height) * row / Rows));
                if (spot.X < 0 || spot.Y < 0)
                {
                    continue;
                }

                var overlap = others.Sum(other => Overlap(spot, (width, height), other.Spot!.Value, other.SpotSize));
                var covered = _style.AvoidPicture && picture is not null && picture.CoversProtected(spot.X, spot.Y, width, height) ? 1 : 0;
                var distance = Math.Abs(spot.X - wish.X) + Math.Abs(spot.Y - wish.Y);
                var cost = (overlap * 40) + covered + (distance * 0.5);
                if (cost < least - 1e-9)
                {
                    (least, best) = (cost, spot);
                }
            }
        }

        return best;
    }

    /// <summary>How much of two boxes (shares of the area) lies over each other, as a share of the area.</summary>
    private static double Overlap((double X, double Y) a, (double Width, double Height) sizeA, (double X, double Y) b, (double Width, double Height) sizeB)
    {
        var across = Math.Min(a.X + sizeA.Width, b.X + sizeB.Width) - Math.Max(a.X, b.X);
        var down = Math.Min(a.Y + sizeA.Height, b.Y + sizeB.Height) - Math.Max(a.Y, b.Y);
        return across > 0 && down > 0 ? across * down : 0;
    }

    /// <summary>
    /// How wide <paramref name="text"/> is, in base font sizes, in a typeface of this kind: capitals
    /// and bold run wider than lower case, a serif's capitals widest, a monospace face the same for
    /// every letter, a condensed display face narrowest.
    /// </summary>
    public static double WidthEm(string text, CaptionFont font, bool bold)
    {
        ArgumentNullException.ThrowIfNull(text);
        double total = 0;
        foreach (var c in text)
        {
            var upper = char.IsUpper(c);
            total += font switch
            {
                CaptionFont.Mono => 0.6,
                CaptionFont.Display => upper ? 0.56 : 0.48,
                CaptionFont.Serif => upper ? 0.76 : 0.56,
                CaptionFont.Rounded => upper ? 0.66 : 0.55,
                CaptionFont.Hand => upper ? 0.72 : 0.6,
                _ => char.IsWhiteSpace(c) ? 0.3 : upper ? 0.7 : 0.56,
            } * (bold && font != CaptionFont.Mono ? 1.08 : 1);
        }

        return total;
    }

    private CaptionWord Draw(Slot slot, double left, double centreY, double seconds, double em, Area area, PictureSpace? picture, Argb? color = null)
    {
        var word = slot.Word;
        var age = seconds - word.Born;
        var arriving = Math.Clamp(age / (word.Stretched ? PopSeconds * 2.5 : PopSeconds), 0, 1);
        var (scale, offsetX, offsetY, text) = Arrive(word.Lit ? _style.Motion : CaptionMotion.Pop, word.Lit ? arriving : 1, word.Text, age);
        if (_style.Cursor && word.Died is null && word == _lastLiving)
        {
            text += (seconds * 2) % 1 < 0.5 ? "\u258C" : "\u2007";
        }

        var opacity = word.Died is { } died ? Math.Clamp(1 - ((seconds - died) / FadeSeconds), 0, 1) : 1;
        opacity *= word.Stress is Stress.Hush ? 0.8 : 1;
        if (!word.Lit)
        {
            opacity *= 0.4;
        }

        if (_style.Jitter > 0)
        {
            offsetX += _style.Jitter * Math.Sin((seconds * 53) + word.Id);
            offsetY += _style.Jitter * Math.Cos((seconds * 41) + (word.Id * 3));
        }

        if (_style.Motion is CaptionMotion.Slide or CaptionMotion.Drop or CaptionMotion.Fade)
        {
            opacity *= arriving;
        }
        else if (_style.Motion is CaptionMotion.Stamp)
        {
            opacity *= Math.Min(1, arriving * 3);
        }

        if (_style.Dissolve > 0 && word.Died is null)
        {
            // Of its own accord: the word drifts up as it goes.
            opacity *= Math.Clamp(1 - ((age - _style.Dissolve) / DissolveSeconds), 0, 1);
            offsetY -= 0.12 * Math.Max(0, age);
        }

        if (_style.Highlight && word.Died is null && word != _lastLiving)
        {
            opacity *= 0.55;
        }

        if (_style.Flicker && word.Stress is Stress.Peak && age < 0.4 && Math.Sin(age * 90) > 0.6)
        {
            opacity *= 0.35;
        }

        var shake = word.Stress is Stress.Peak ? 0.12 * _style.Energy * Math.Max(0, 1 - (age / ShakeSeconds)) : 0;
        var flash = word.Stress is Stress.Peak ? Math.Clamp(1 - (age / FlashSeconds), 0, 1) : 0;
        var rotation = _style.Tilt * (word.Own ? ((word.Id % 5) - 2) * 1.5 : word.Lilt ? -2 : 0);
        var height = slot.Size * em;
        var behind = picture?.Behind(left / area.Width, (centreY - (height / 2)) / area.Height, slot.Width / area.Width, height / area.Height) ?? new Backing(0.6, 0);
        var backing = new Backing(Math.Clamp(behind.Outline * _style.Outline, 0, 2), _style.PlateAlways ? Math.Max(behind.Plate, _style.PlateLight || _style.PlateTint ? 0.92 : 0.55) : behind.Plate);
        // Whatever the look, a word always has something keeping it readable: an outline where there is no plate, and a plate behind hollow letters over a busy picture.
        if (!_style.PlateLight && !_style.PlateTint && backing.Plate < 0.35 && backing.Outline < 0.45)
        {
            backing = backing with { Outline = 0.6 };
        }

        if (_style.Hollow && !word.Own && behind.Plate > 0 && backing.Plate < 0.4)
        {
            backing = backing with { Plate = 0.4 };
        }

        return new CaptionWord(
            word.Id,
            text,
            left + (slot.Width / 2),
            centreY,
            slot.Size * scale,
            rotation,
            color ?? word.Color,
            word.Bold,
            word.Italic,
            word.Serif,
            opacity,
            word.Own || word.Lilt ? 0.6 : 0,
            shake,
            flash,
            word.Glyph,
            word.Stress,
            backing,
            slot.Size,
            _style.Font,
            offsetX,
            offsetY,
            word.Lit,
            _style.PlateLight,
            _style.Split,
            _style.PlateTint,
            _style.Hollow && !word.Own);
    }

    /// <summary>
    /// How a word looks <paramref name="t"/> (0 to 1) of the way through arriving by
    /// <paramref name="motion"/>, <paramref name="age"/> seconds after it began: its scale, how far it
    /// is from its place (in base font sizes), and how much of its text shows.
    /// </summary>
    public static (double Scale, double OffsetX, double OffsetY, string Text) Arrive(CaptionMotion motion, double t, string text, double age)
    {
        ArgumentNullException.ThrowIfNull(text);
        t = double.IsFinite(t) ? Math.Clamp(t, 0, 1) : 1;
        switch (motion)
        {
            case CaptionMotion.Slide:
                return (1, -0.8 * (1 - t) * (1 - t), 0, text);
            case CaptionMotion.Drop:
                return (1, 0, -1.2 * (1 - t) * (1 - t), text);
            case CaptionMotion.Bounce:
                // Down past its place, up, and down again, settling.
                var bounce = t >= 1 ? 0 : Math.Abs(Math.Sin(t * Math.PI * 2.5)) * (1 - t) * 0.5;
                return (1 + (0.25 * (1 - t)), 0, -bounce, text);
            case CaptionMotion.Typewriter:
                var shown = age < 0 ? 0 : Math.Min(text.Length, 1 + (int)(age / 0.035));
                return (1, 0, 0, shown >= text.Length ? text : text[..shown]);
            case CaptionMotion.Fade:
                return (1 + (0.15 * (1 - t)), 0, 0, text);
            case CaptionMotion.Stamp:
                return (1 + (1.4 * (1 - t) * (1 - t)), 0, 0, text);
            default:
                return (Pop(t), 0, 0, text);
        }
    }

    /// <summary>The words in rows no wider than <paramref name="rowWidth"/>, a shout alone on its row, every word shrunk to fit the row if it must.</summary>
    private List<List<Slot>> Flow(double em, double rowWidth)
    {
        var rows = _rows;
        _rowCount = 0;
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
                if (_rowCount == rows.Count)
                {
                    rows.Add([]);
                }

                row = rows[_rowCount++];
                row.Clear();
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
        CaptionLayout.Left => (0.06, 0.62),
        _ => (0.5, 0.72),
    };

    /// <summary>
    /// Drops the words whose fade is over (and, dissolving, whose time is up), and notes the newest
    /// living word, without the closures a predicate would allocate every frame.
    /// </summary>
    private void RemoveGone(double seconds)
    {
        var kept = 0;
        _lastLiving = null;
        for (var i = 0; i < _words.Count; i++)
        {
            var word = _words[i];
            var gone = (word.Died is { } died && seconds - died >= FadeSeconds)
                || (_style.Dissolve > 0 && seconds - word.Born >= _style.Dissolve + DissolveSeconds);
            if (!gone)
            {
                _words[kept++] = word;
                _lastLiving = word.Died is null ? word : _lastLiving;
            }
        }

        _words.RemoveRange(kept, _words.Count - kept);
    }

    private readonly record struct Slot(Placed Word, double Size, double Width, double Space);

    private sealed class Placed(int id, string text, Stress stress, bool lilt, bool stretched, double born, double widthEm, double size, Argb color, string? glyph, bool italic, bool serif, bool bold, bool own)
    {
        public int Id { get; } = id;

        public string Text { get; } = text;

        public Stress Stress { get; } = stress;

        public bool Lilt { get; } = lilt;

        public bool Stretched { get; } = stretched;

        public double Born { get; set; } = born;

        public double? Died { get; set; }

        public bool Lit { get; set; } = true;

        public (double X, double Y)? Spot { get; set; }

        public (double Width, double Height) SpotSize { get; set; }

        public double WidthEm { get; } = widthEm;

        public double Size { get; } = size;

        public Argb Color { get; set; } = color;

        public string? Glyph { get; } = glyph;

        public bool Italic { get; } = italic;

        public bool Serif { get; } = serif;

        public bool Bold { get; } = bold;

        public bool Own { get; } = own;
    }
}
