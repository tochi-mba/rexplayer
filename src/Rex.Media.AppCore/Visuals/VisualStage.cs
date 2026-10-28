using Rex.Media.Audio;
using Rex.Media.Settings;

namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// The visualisations drawn in software (AU-18): takes the sound as it is heard, works out its beat
/// (<see cref="MusicPulse"/>), has the chosen <see cref="VisualScene"/> draw on a canvas the size of
/// the stage in the screen's own pixels, and gives BGRA pixels for the window to show. Only when the
/// stage is wider than <see cref="MaxSide"/>, or a slow computer cannot keep up (<see cref="Quality"/>),
/// is the canvas smaller than the stage and shown scaled up.
/// </summary>
public sealed class VisualStage
{
    /// <summary>The longest side the canvas is drawn at, in pixels: full HD, beyond which the picture is scaled up.</summary>
    public const int MaxSide = 1920;

    private readonly MusicPulse _pulse = new();
    private readonly float[] _mono = new float[SoundSize];
    private VisualScene? _scene;
    private VisualizerChoice _choice;

    // The next frame builds a scene's caches for a new size or a new scene, so its time says nothing
    // about how fast frames are; and after a change of quality the next is held off for a while, so
    // the canvas cannot bounce between two sizes (each change costs a frame of rebuilding).
    private bool _settling = true;
    private int _hold;

    /// <summary>Frames after a change of <see cref="Quality"/> before it may change again: about half a second.</summary>
    public const int HoldFrames = 30;

    /// <summary>Samples of each channel a frame is drawn from: what <see cref="MusicPulse"/> analyses.</summary>
    public const int SoundSize = MusicPulse.AnalysisSize;

    public VisualStage(int width, int height, Random? random = null)
    {
        Canvas = new Raster(width, height);
        Pixels = new byte[width * height * 4];
        Context = new VisualContext(Canvas, _pulse)
        {
            Left = new float[SpectrumAnalyzer.Size],
            Right = new float[SpectrumAnalyzer.Size],
            Random = random ?? new Random(),
        };
    }

    public Raster Canvas { get; private set; }

    public VisualContext Context { get; }

    public MusicPulse Pulse => _pulse;

    /// <summary>The last frame drawn, BGRA, row by row.</summary>
    public byte[] Pixels { get; private set; }

    /// <summary>
    /// How finely the canvas is drawn, from 1 (the stage's own pixels, up to <see cref="MaxSide"/>) down to
    /// <see cref="LowestQuality"/>: it falls when frames take too long to draw, so a slow computer
    /// keeps its music moving smoothly, and climbs back when there is time to spare.
    /// </summary>
    public double Quality { get; private set; } = 1;

    /// <summary>The least <see cref="Quality"/> falls to.</summary>
    public const double LowestQuality = 0.4;

    /// <summary>Whether <see cref="Quality"/> has changed since the canvas was last sized, so it should be sized again.</summary>
    public bool WantsResize { get; private set; }

    /// <summary>How long frames take to draw, smoothed, in milliseconds.</summary>
    public double DrawMilliseconds { get; private set; }

    /// <summary>
    /// Whether <see cref="Pixels"/> hold a frame drawn at the canvas's present size: false from a
    /// resize until the next frame is drawn, so the window keeps showing the last picture meanwhile
    /// rather than an empty one.
    /// </summary>
    public bool HasFrame { get; private set; }

    /// <summary>
    /// Takes how long a frame took to draw. Over about 25 ms a frame (fewer than forty a second
    /// with time for the rest) the canvas shrinks a step; under 10 ms it grows a step back. The
    /// first frame at a new size or of a new scene does not count, and a change waits
    /// <see cref="HoldFrames"/> frames after the last one.
    /// </summary>
    public void Took(double milliseconds)
    {
        if (_settling)
        {
            _settling = false;
            return;
        }

        DrawMilliseconds = DrawMilliseconds == 0 ? milliseconds : (DrawMilliseconds * 0.9) + (milliseconds * 0.1);
        if (_hold > 0)
        {
            _hold--;
            return;
        }

        var quality = DrawMilliseconds > 25 ? Math.Max(LowestQuality, Quality * 0.85)
            : DrawMilliseconds < 10 ? Math.Min(1, Quality * 1.1)
            : Quality;
        if (Math.Abs(quality - Quality) > 0.001)
        {
            (Quality, WantsResize, DrawMilliseconds, _hold) = (quality, true, 17, HoldFrames);
        }
    }

    /// <summary>
    /// The canvas for a stage <paramref name="width"/> by <paramref name="height"/> layout units
    /// across on a screen of <paramref name="scale"/> pixels a unit: the stage's own pixels, so
    /// nothing is scaled up at full quality until its longest side would pass
    /// <see cref="MaxSide"/>, or <paramref name="maxSide"/> on a computer that cannot spare the time.
    /// Quality scales that fitted size, including on smaller windows.
    /// </summary>
    public static (int Width, int Height) SizeFor(double width, double height, double quality = 1, double scale = 1, int maxSide = MaxSide)
    {
        if (!(width >= 2 && height >= 2) || !double.IsFinite(width) || !double.IsFinite(height))
        {
            return (16, 9);
        }

        scale = double.IsFinite(scale) && scale > 0 ? scale : 1;
        var longest = Math.Max(width, height);
        var fitted = Math.Min(Math.Clamp(maxSide, 16, MaxSide), longest * scale)
            * (double.IsFinite(quality) ? Math.Clamp(quality, LowestQuality, 1) : 1);
        // Divide before multiplying so even very large finite layout sizes or scales retain
        // their aspect ratio instead of producing infinity times zero.
        return (Math.Max(2, (int)Math.Round(width / longest * fitted)), Math.Max(2, (int)Math.Round(height / longest * fitted)));
    }

    /// <summary>
    /// Draws a frame of <paramref name="choice"/> from the newest sound (each channel
    /// <see cref="SoundSize"/> samples), <paramref name="elapsed"/> after the last frame, with its
    /// <paramref name="options"/>; false when <paramref name="choice"/> is not drawn here. The scenes'
    /// own copies of the channels (<see cref="VisualContext.Left"/>) are the newest
    /// <see cref="SpectrumAnalyzer.Size"/>, with the sensitivity applied; the beat is heard from the
    /// sound as it is, the sensitivity setting how strongly it moves things.
    /// </summary>
    public bool Draw(VisualizerChoice choice, IReadOnlyDictionary<string, string> options, ReadOnlySpan<float> left, ReadOnlySpan<float> right, int sampleRate, TimeSpan elapsed, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellation.ThrowIfCancellationRequested();
        // A short or unavailable source need not fabricate a full frame of
        // audio. Zero-pad at the older end so the newest actual samples still
        // line up with the latest sound; an absent right channel means mono.
        // Longer capture buffers use their newest samples without allocating.
        left = left.Length > SoundSize ? left[^SoundSize..] : left;
        right = right.Length > SoundSize ? right[^SoundSize..] : right;
        if (right.IsEmpty)
        {
            right = left;
        }

        if (_scene is null || choice != _choice)
        {
            _choice = choice;
            _scene = VisualScene.For(choice);
            _pulse.Reset();
            Canvas.Clear(Rgb.Black);
            _settling = true;
        }

        if (_scene is null)
        {
            return false;
        }

        Context.Choice = choice;
        Context.Options = options;
        Context.Dt = Math.Clamp(elapsed.TotalSeconds, 0, 0.25);
        Context.Prepare();
        var gain = (float)Context.Number(Player.VisualizerOptions.Sensitivity);
        var skip = SoundSize - Context.Left.Length;
        var leftPad = SoundSize - left.Length;
        var rightPad = SoundSize - right.Length;
        for (var i = 0; i < _mono.Length; i++)
        {
            var l = i >= leftPad ? SafeSample(left[i - leftPad]) : 0;
            var r = i >= rightPad ? SafeSample(right[i - rightPad]) : 0;
            _mono[i] = (l + r) * 0.5f;
            if (i >= skip)
            {
                Context.Left[i - skip] = SafeSample(l * gain);
                Context.Right[i - skip] = SafeSample(r * gain);
            }
        }

        _pulse.Sensitivity = gain;
        _pulse.Update(_mono, Math.Max(1, sampleRate), elapsed);
        cancellation.ThrowIfCancellationRequested();
        _scene.Draw(Context);
        cancellation.ThrowIfCancellationRequested();
        Canvas.Settle();
        Canvas.ToBgra(Pixels, 1, 0.35f);
        HasFrame = true;
        if (Context.Toggle(Player.VisualizerOptions.Meter))
        {
            // Over the finished picture, so it never feeds the scene's trails.
            Meter();
        }

        return true;
    }

    /// <summary>Clamp decoded samples, treating damaged or unavailable audio as silence.</summary>
    public static float SafeSample(float sample) => float.IsFinite(sample) ? Math.Clamp(sample, -1, 1) : 0;

    /// <summary>
    /// The beat meter, along the bottom: the bands as thin bars; the bass, middle and treble, the
    /// punch and the loudness as blocks; a square lit on each beat (half lit for one the clock stood
    /// in for), and a line across it that fills through the beat, white once the clock is locked.
    /// </summary>
    private void Meter()
    {
        var (w, h) = (Canvas.Width, Canvas.Height);
        var strip = Math.Max(8, h / 8);
        var top = h - strip;
        Shade(0, top, w, strip, Rgb.Black, 0.8f);
        var bands = _pulse.Bands;
        var across = w * 0.6;
        for (var band = 0; band < bands.Count; band++)
        {
            var tall = (int)(bands[band] * (strip - 2));
            Shade((int)(band * across / bands.Count), h - 1 - tall, Math.Max(1, (int)(across / bands.Count) - 1), tall, Context.Paint((double)band / bands.Count), 1);
        }

        var levels = _levels;
        (levels[0], levels[1], levels[2], levels[3], levels[4]) = (_pulse.Bass, _pulse.Mid, _pulse.Treble, _pulse.Punch, _pulse.Loudness);
        var block = w * 0.05;
        for (var i = 0; i < levels.Length; i++)
        {
            var tall = (int)(levels[i] * (strip - 2));
            Shade((int)(across + (i * block) + 2), h - 1 - tall, (int)(block - 3), tall, i == 3 ? Rgb.White : Context.Paint(i / 4.0), 1);
        }

        _beatLight = _pulse.Beat ? (_pulse.Filled ? 0.5f : 1) : Math.Max(0, _beatLight - (float)(Context.Dt * 6));
        // The square keeps a few pixels even on a stage too narrow for all of it.
        var side = Math.Max(3, Math.Min(strip - 4, (int)(w - (across + (levels.Length * block) + 6) - 4)));
        var left = Math.Min((int)(across + (levels.Length * block) + 6), w - side);
        Shade(left, top + 2, side, side, Rgb.White, Math.Min(1, 0.12f + _beatLight));
        Shade(left, h - 3, (int)(side * _pulse.Phase), 2, _pulse.Locked ? Rgb.White : new Rgb(0.5f, 0.5f, 0.5f), 1);
    }

    /// <summary>Lays <paramref name="color"/> over a rectangle of the finished pixels at <paramref name="opacity"/>, clipped to them.</summary>
    private void Shade(int x, int y, int width, int height, Rgb color, float opacity)
    {
        var (w, h) = (Canvas.Width, Canvas.Height);
        var (b, g, r) = (Math.Clamp(color.B, 0, 1) * 255, Math.Clamp(color.G, 0, 1) * 255, Math.Clamp(color.R, 0, 1) * 255);
        for (var py = Math.Max(0, y); py < Math.Min(h, y + height); py++)
        {
            for (var px = Math.Max(0, x); px < Math.Min(w, x + width); px++)
            {
                var at = ((py * w) + px) * 4;
                Pixels[at] = (byte)(Pixels[at] + ((b - Pixels[at]) * opacity));
                Pixels[at + 1] = (byte)(Pixels[at + 1] + ((g - Pixels[at + 1]) * opacity));
                Pixels[at + 2] = (byte)(Pixels[at + 2] + ((r - Pixels[at + 2]) * opacity));
            }
        }
    }

    private readonly float[] _levels = new float[5];
    private float _beatLight;

    /// <summary>
    /// Draws at a new size from the next frame, as when the window is resized; the scene keeps what
    /// it has drawn so far (a record keeps the song cut into it), only the canvas is new.
    /// </summary>
    public void Resize(int width, int height)
    {
        WantsResize = false;
        if (width == Canvas.Width && height == Canvas.Height)
        {
            return;
        }

        Canvas = new Raster(width, height);
        Pixels = new byte[width * height * 4];
        Context.Canvas = Canvas;
        (HasFrame, _settling) = (false, true);
    }

    /// <summary>Starts again from silence, as when the song changes or the music stops.</summary>
    public void Reset()
    {
        _pulse.Reset();
        _scene = null;
        _beatLight = 0;
        Canvas.Clear(Rgb.Black);
    }
}

