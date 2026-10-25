using Rex.Media.AppCore.Player;
using Rex.Media.Settings;

namespace Rex.Media.AppCore.Visuals;

/// <summary>A picture handed to a visualisation: the song's cover, or the camera's latest frame, as BGRA rows.</summary>
public sealed record VisualPicture(byte[] Bgra, int Width, int Height)
{
    /// <summary>One channel (0 blue, 1 green, 2 red) at a point between pixels, 0 to 1, the edge pixel beyond the edges.</summary>
    public float Sample(double x, double y, int channel)
    {
        if (Width <= 0 || Height <= 0 || channel is < 0 or > 2
            || Bgra is null || Width > Bgra.Length / 4 / Height)
        {
            // Unplugged camera streams and failed decodes can deliver empty or
            // partial frames. Keep a silent black fallback rather than throwing
            // from the render loop or reading beyond the pixel buffer.
            return 0;
        }

        x = double.IsFinite(x) ? Math.Clamp(x, 0, Width - 1) : 0;
        y = double.IsFinite(y) ? Math.Clamp(y, 0, Height - 1) : 0;
        y = Math.Clamp(y, 0, Height - 1);
        var (x0, y0) = ((int)x, (int)y);
        var (x1, y1) = (Math.Min(x0 + 1, Width - 1), Math.Min(y0 + 1, Height - 1));
        var (fx, fy) = ((float)(x - x0), (float)(y - y0));
        var top = Lerp(Bgra[(((y0 * Width) + x0) * 4) + channel], Bgra[(((y0 * Width) + x1) * 4) + channel], fx);
        var bottom = Lerp(Bgra[(((y1 * Width) + x0) * 4) + channel], Bgra[(((y1 * Width) + x1) * 4) + channel], fx);
        return Lerp(top, bottom, fy) / 255f;
    }

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);
}

/// <summary>Everything a visualisation draws a frame from (AU-18).</summary>
public sealed class VisualContext
{
    private static readonly IReadOnlyDictionary<string, string> NoOptions = new Dictionary<string, string>();

    public VisualContext(Raster canvas, MusicPulse pulse)
    {
        Canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        Pulse = pulse ?? throw new ArgumentNullException(nameof(pulse));
    }

    public Raster Canvas { get; internal set; }

    public MusicPulse Pulse { get; }

    /// <summary>The visualisation being drawn, whose settings <see cref="Options"/> holds.</summary>
    public VisualizerChoice Choice { get; set; }

    public IReadOnlyDictionary<string, string> Options { get; set; } = NoOptions;

    /// <summary>The newest sound of each channel, oldest sample first, sensitivity applied.</summary>
    public float[] Left { get; set; } = [];

    public float[] Right { get; set; } = [];

    /// <summary>Where the song is, in seconds, and how far through (0 to 1).</summary>
    public double Seconds { get; set; }

    public double Progress { get; set; }

    /// <summary>Seconds since the last frame.</summary>
    public double Dt { get; set; }

    /// <summary>Windows' accent colour, for the accent palette.</summary>
    public Argb Accent { get; set; } = new(255, 96, 205, 255);

    public VisualPicture? Cover { get; set; }

    /// <summary>The camera's latest picture, when a visualisation that uses it is on and it works.</summary>
    public VisualPicture? Camera { get; set; }

    /// <summary>The camera's outline of the listener: 255 where they are, <see cref="MaskWidth"/> by <see cref="MaskHeight"/>.</summary>
    public byte[]? Mask { get; set; }

    public int MaskWidth { get; set; }

    public int MaskHeight { get; set; }

    /// <summary>Where chance comes from: seeded in tests, so a frame can be drawn again exactly.</summary>
    public Random Random { get; set; } = new();

    /// <summary>A setting's number, read once a frame.</summary>
    public double Number(string key)
    {
        if (!_numbers.TryGetValue(key, out var value))
        {
            value = VisualizerOptions.Number(Options, Choice, key);
            _numbers[key] = value;
        }

        return value;
    }

    public bool Toggle(string key) => VisualizerOptions.Toggle(Options, Choice, key);

    public int Pick(string key) => VisualizerOptions.Choice(Options, Choice, key);

    private readonly Rgb[] _paints = new Rgb[256];
    private readonly Dictionary<string, double> _numbers = new(StringComparer.Ordinal);

    /// <summary>
    /// Readies a frame: the palette is worked out once, into a table every <see cref="Paint"/> reads,
    /// and the settings are read afresh (they may have changed since the last frame).
    /// </summary>
    public void Prepare()
    {
        _numbers.Clear();
        var palette = (VisualPalette)Pick(VisualizerOptions.Colors);
        var own = VisualizerOptions.Rgb(Options, Choice, VisualizerOptions.Color);
        for (var i = 0; i < _paints.Length; i++)
        {
            _paints[i] = Rgb.From(Visualizers.PaletteColor(palette, i / 255.0, Seconds, Accent, own));
        }
    }

    /// <summary>
    /// The frame rate the scenes' trails were made at: <see cref="Trail"/> takes its fade, zoom, turn
    /// and shift as one frame at this rate does them.
    /// </summary>
    public const double TrailRate = 30;

    /// <summary>
    /// Starts a frame with trails (<see cref="Raster.Feedback"/>): <paramref name="keep"/>,
    /// <paramref name="zoom"/>, <paramref name="turn"/> and the shift are what a thirtieth of a second
    /// does, applied for <see cref="Dt"/>, so trails last as long and drift as fast at sixty frames a
    /// second, or 144, as at thirty.
    /// </summary>
    public void Trail(double keep, double zoom, double turn, double shiftX, double shiftY)
    {
        var frames = (double.IsFinite(Dt) ? Math.Clamp(Dt, 0, 0.25) : 0) * TrailRate;
        Canvas.Feedback((float)Math.Pow(Math.Clamp(keep, 0, 1), frames), Math.Pow(zoom > 0.01 ? zoom : 1, frames), turn * frames, shiftX * frames, shiftY * frames);
    }

    /// <summary>The visualisation's colour <paramref name="share"/> (0 to 1) along its palette, from the table <see cref="Prepare"/> made.</summary>
    public Rgb Paint(double share) => _paints[double.IsFinite(share) ? (int)Math.Round(Math.Clamp(share, 0, 1) * 255) : 0];
}

/// <summary>A visualisation drawn on a <see cref="Raster"/>: it keeps what it needs from frame to frame.</summary>
public abstract class VisualScene
{
    /// <summary>The visualisations drawn here, and how each scene is made; the rest the window draws with shapes.</summary>
    private static readonly Dictionary<VisualizerChoice, Func<VisualScene>> Scenes = new()
    {
        [VisualizerChoice.Vinyl] = () => new VinylScene(),
        [VisualizerChoice.Halo] = () => new HaloScene(),
        [VisualizerChoice.Mirror] = () => new MirrorScene(),
        [VisualizerChoice.Aurora] = () => new AuroraScene(),
        [VisualizerChoice.Embers] = () => new EmbersScene(),
        [VisualizerChoice.Resonance] = () => new ResonanceScene(),
        [VisualizerChoice.Groove] = () => new GrooveScene(),
        [VisualizerChoice.Flight] = () => new FlightScene(),
        [VisualizerChoice.Cymatics] = () => new CymaticsScene(),
        [VisualizerChoice.Ridgelines] = () => new RidgelinesScene(),
        [VisualizerChoice.Synthwave] = () => new SynthwaveScene(),
        [VisualizerChoice.Fireworks] = () => new FireworksScene(),
        [VisualizerChoice.Lightning] = () => new LightningScene(),
        [VisualizerChoice.Lava] = () => new LavaScene(),
        [VisualizerChoice.Constellation] = () => new ConstellationScene(),
        [VisualizerChoice.Galaxy] = () => new GalaxyScene(),
        [VisualizerChoice.Kaleidoscope] = () => new KaleidoscopeScene(),
        [VisualizerChoice.Murmuration] = () => new MurmurationScene(),
        [VisualizerChoice.Ink] = () => new InkScene(),
        [VisualizerChoice.Skyline] = () => new SkylineScene(),
        [VisualizerChoice.Mandala] = () => new MandalaScene(),
        [VisualizerChoice.Clones] = () => new ClonesScene(),
        [VisualizerChoice.TimeWarp] = () => new TimeWarpScene(),
        [VisualizerChoice.Aura] = () => new AuraScene(),
        [VisualizerChoice.LedWall] = () => new LedWallScene(),
        [VisualizerChoice.Stardust] = () => new StardustScene(),
        [VisualizerChoice.HallOfMirrors] = () => new HallOfMirrorsScene(),
        [VisualizerChoice.LightPainting] = () => new LightPaintingScene(),
        [VisualizerChoice.Scanner] = () => new ScannerScene(),
        [VisualizerChoice.Shatter] = () => new ShatterScene(),
        [VisualizerChoice.Spotlight] = () => new SpotlightScene(),
        [VisualizerChoice.InfinityMirror] = () => new InfinityMirrorScene(),
        [VisualizerChoice.Water] = () => new WaterScene(),
        [VisualizerChoice.Ripples] = () => new RipplesScene(),
        [VisualizerChoice.Strobe] = () => new StrobeScene(),
        [VisualizerChoice.Silhouette] = () => new SilhouetteScene(),
        [VisualizerChoice.BeatEdit] = () => new BeatEditScene(),
    };

    /// <summary>The scene for <paramref name="choice"/>, or null for those the window draws with shapes.</summary>
    public static VisualScene? For(VisualizerChoice choice) => Scenes.TryGetValue(choice, out var make) ? make() : null;

    /// <summary>Whether <paramref name="choice"/> is drawn by a scene rather than with the window's shapes.</summary>
    public static bool Draws(VisualizerChoice choice) => Scenes.ContainsKey(choice);

    /// <summary>Draws the next frame on <see cref="VisualContext.Canvas"/>.</summary>
    public abstract void Draw(VisualContext context);
}

