using Rex.Media.Audio;
using Rex.Media.Settings;

namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// The visualisations drawn in software (AU-18): takes the sound as it is heard, works out its beat
/// (<see cref="MusicPulse"/>), has the chosen <see cref="VisualScene"/> draw on a canvas the shape
/// of the stage, and gives BGRA pixels for the window to show, scaled up smoothly.
/// </summary>
public sealed class VisualStage
{
    /// <summary>The widest the canvas is drawn: wider stages show it scaled up, which suits soft light.</summary>
    public const int MaxWidth = 560;

    private readonly MusicPulse _pulse = new();
    private readonly float[] _mono = new float[SpectrumAnalyzer.Size];
    private VisualScene? _scene;
    private VisualizerChoice _choice;

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

    public Raster Canvas { get; }

    public VisualContext Context { get; }

    public MusicPulse Pulse => _pulse;

    /// <summary>The last frame drawn, BGRA, row by row.</summary>
    public byte[] Pixels { get; }

    /// <summary>Whether the visualisation chosen last needs the camera.</summary>
    public bool UsesCamera => _scene?.UsesCamera ?? false;

    /// <summary>The canvas for a stage of <paramref name="width"/> by <paramref name="height"/>: its shape, at most <see cref="MaxWidth"/> across.</summary>
    public static (int Width, int Height) SizeFor(double width, double height)
    {
        if (!(width >= 2 && height >= 2))
        {
            return (16, 9);
        }

        var scale = Math.Min(1, MaxWidth / width);
        return (Math.Max(2, (int)Math.Round(width * scale)), Math.Max(2, (int)Math.Round(height * scale)));
    }

    /// <summary>Whether <paramref name="choice"/> is drawn here rather than with the window's shapes.</summary>
    public static bool Draws(VisualizerChoice choice) => VisualScene.For(choice) is not null;

    /// <summary>
    /// Draws a frame of <paramref name="choice"/> from the newest sound (each channel
    /// <see cref="SpectrumAnalyzer.Size"/> samples), <paramref name="elapsed"/> after the last frame,
    /// with its <paramref name="options"/>; false when <paramref name="choice"/> is not drawn here.
    /// </summary>
    public bool Draw(VisualizerChoice choice, IReadOnlyDictionary<string, string> options, ReadOnlySpan<float> left, ReadOnlySpan<float> right, int sampleRate, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (left.Length != SpectrumAnalyzer.Size || right.Length != SpectrumAnalyzer.Size)
        {
            throw new ArgumentException($"Each channel takes {SpectrumAnalyzer.Size} samples.", nameof(left));
        }

        if (_scene is null || choice != _choice)
        {
            _choice = choice;
            _scene = VisualScene.For(choice);
            _pulse.Reset();
            Canvas.Clear(Rgb.Black);
        }

        if (_scene is null)
        {
            return false;
        }

        Context.Choice = choice;
        Context.Options = options;
        Context.Dt = Math.Clamp(elapsed.TotalSeconds, 0, 0.25);
        var gain = (float)Context.Number(Player.VisualizerOptions.Sensitivity);
        for (var i = 0; i < _mono.Length; i++)
        {
            Context.Left[i] = Math.Clamp(left[i] * gain, -1, 1);
            Context.Right[i] = Math.Clamp(right[i] * gain, -1, 1);
            _mono[i] = (Context.Left[i] + Context.Right[i]) / 2;
        }

        _pulse.Update(_mono, Math.Max(1, sampleRate), elapsed);
        _scene.Draw(Context);
        Canvas.ToBgra(Pixels, 1, 0.35f);
        return true;
    }

    /// <summary>Starts again from silence, as when the song changes or the music stops.</summary>
    public void Reset()
    {
        _pulse.Reset();
        _scene = null;
        Canvas.Clear(Rgb.Black);
    }
}

