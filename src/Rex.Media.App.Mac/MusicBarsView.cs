using Rex.Media.Audio;

namespace Rex.Media.App.Mac;

/// <summary>Which of the classic visualisations <see cref="MusicBarsView"/> draws.</summary>
internal enum MusicBarsMode
{
    None,
    Spectrum,
    Oscilloscope,
    Meters,
}

/// <summary>
/// The classic visualisations drawn as shapes, as the Windows stage draws them: spectrum bars with
/// rounded tops over a quiet baseline and their peak marks, the oscilloscope's line, and the level
/// meters' peak, average and hold marks for each channel. The window works out every height from
/// the sound (with the same analysers the Windows stage uses) and sets them here; this view only
/// draws them, top-left first, as Windows lays them out.
/// </summary>
internal sealed class MusicBarsView : NSView
{
    private MusicBarsMode _mode;
    private float[] _levels = [];
    private float[] _peaks = [];
    private CGColor[] _colors = [];
    private float[] _wave = [];
    private CGColor? _waveColor;
    private nfloat _thickness = 2;
    private readonly double[] _meterPeak = new double[2];
    private readonly double[] _meterAverage = new double[2];
    private readonly double[] _meterHold = new double[2];
    private readonly CGColor?[] _meterColors = new CGColor?[2];
    private static readonly CGColor White = new(1, 1, 1, 1);

    public MusicBarsView()
    {
        WantsLayer = true;
        LayerContentsRedrawPolicy = NSViewLayerContentsRedrawPolicy.OnSetNeedsDisplay;
    }

    public override bool IsFlipped => true;

    /// <summary>What is drawn now.</summary>
    public MusicBarsMode Mode => _mode;

    /// <summary>How many bars are drawn now: the spectrum's bars, or the two meters; none for the line.</summary>
    public int BarsShown => Hidden ? 0 : _mode switch
    {
        MusicBarsMode.Spectrum => _levels.Length,
        MusicBarsMode.Meters => 2,
        _ => 0,
    };

    /// <summary>Whether the spectrum is mirrored about the middle rather than standing on the foot.</summary>
    public bool Mirrored { get; set; }

    /// <summary>Whether the spectrum's peak marks show.</summary>
    public bool ShowsPeaks { get; set; }

    /// <summary>The quiet line under the bars.</summary>
    public CGColor Baseline { get; set; } = new(1, 1, 1, (nfloat)0.12);

    /// <summary>Draws nothing until told what to draw.</summary>
    public void Clear()
    {
        _mode = MusicBarsMode.None;
        NeedsDisplay = true;
    }

    /// <summary>The spectrum: each band's height and peak (0 to 1) and its colour, lowest band first.</summary>
    public void ShowSpectrum(IReadOnlyList<float> levels, IReadOnlyList<float> peaks, Func<int, CGColor> color)
    {
        ArgumentNullException.ThrowIfNull(levels);
        ArgumentNullException.ThrowIfNull(peaks);
        ArgumentNullException.ThrowIfNull(color);
        if (_levels.Length != levels.Count)
        {
            (_levels, _peaks, _colors) = (new float[levels.Count], new float[levels.Count], new CGColor[levels.Count]);
        }

        for (var i = 0; i < levels.Count; i++)
        {
            _levels[i] = levels[i];
            _peaks[i] = i < peaks.Count ? peaks[i] : 0;
            _colors[i]?.Dispose();
            _colors[i] = color(i);
        }

        _mode = MusicBarsMode.Spectrum;
        NeedsDisplay = true;
    }

    /// <summary>The oscilloscope: the samples drawn across the width, each from -1 to 1.</summary>
    public void ShowWave(ReadOnlySpan<float> samples, CGColor color, double thickness)
    {
        ArgumentNullException.ThrowIfNull(color);
        if (_wave.Length != samples.Length)
        {
            _wave = new float[samples.Length];
        }

        samples.CopyTo(_wave);
        _waveColor?.Dispose();
        (_waveColor, _thickness, _mode) = (color, (nfloat)thickness, MusicBarsMode.Oscilloscope);
        NeedsDisplay = true;
    }

    /// <summary>A channel's meter (0 left, 1 right): its peak, average and hold in decibels, and its colour.</summary>
    public void ShowMeter(int channel, LevelMeter meter, CGColor color)
    {
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(color);
        _meterPeak[channel] = LevelMeter.Position(meter.PeakDb);
        _meterAverage[channel] = LevelMeter.Position(meter.RmsDb);
        _meterHold[channel] = LevelMeter.Position(meter.HoldDb);
        _meterColors[channel]?.Dispose();
        _meterColors[channel] = color;
        _mode = MusicBarsMode.Meters;
        NeedsDisplay = true;
    }

    public override void DrawRect(CGRect dirtyRect)
    {
        if (NSGraphicsContext.CurrentContext?.CGContext is not { } context)
        {
            return;
        }

        var (width, height) = ((double)Bounds.Width, (double)Bounds.Height);
        switch (_mode)
        {
            case MusicBarsMode.Spectrum:
                DrawSpectrum(context, width, height);
                break;
            case MusicBarsMode.Oscilloscope:
                DrawWave(context, width, height);
                break;
            case MusicBarsMode.Meters:
                DrawMeters(context, width, height);
                break;
        }
    }

    /// <summary>
    /// Each bar takes seven tenths of its slot, centred in it; a silent band draws nothing, the
    /// baseline standing for it; a peak mark is a three-point line just above where the bar last reached.
    /// </summary>
    private void DrawSpectrum(CGContext context, double width, double height)
    {
        var count = _levels.Length;
        if (count == 0)
        {
            return;
        }

        context.SetFillColor(Baseline);
        context.FillRect(new CGRect(0, Mirrored ? Math.Floor(height / 2) : height - 1, width, 1));
        var slot = width / count;
        var barWidth = Math.Max(1, slot * 0.7);
        for (var i = 0; i < count; i++)
        {
            var left = (i * slot) + (slot * 0.15);
            var bar = _levels[i] * height * (Mirrored ? 0.95 : 1);
            if (bar > 0)
            {
                var top = Mirrored ? (height - bar) / 2 : height - bar;
                var radius = Math.Min(2, Math.Min(barWidth, bar) / 2);
                using var shape = CGPath.FromRoundedRect(new CGRect(left, top, barWidth, bar), (nfloat)radius, (nfloat)radius);
                context.SetFillColor(_colors[i]);
                context.AddPath(shape);
                context.FillPath();
            }

            if (ShowsPeaks)
            {
                var peak = _peaks[i] * height;
                if (peak >= 1)
                {
                    var top = Mirrored ? Math.Max(0, ((height - peak) / 2) - 4) : Math.Max(0, height - peak - 4);
                    context.SetFillColor(White);
                    context.FillRect(new CGRect(left, top, barWidth, 3));
                }
            }
        }
    }

    /// <summary>The newest sound across the width, the middle being silence, reaching nine tenths of the height.</summary>
    private void DrawWave(CGContext context, double width, double height)
    {
        if (_wave.Length < 2 || _waveColor is null)
        {
            return;
        }

        context.SetStrokeColor(_waveColor);
        context.SetLineWidth(_thickness);
        context.SetLineJoin(CGLineJoin.Round);
        context.SetLineCap(CGLineCap.Round);
        for (var step = 0; step < _wave.Length; step++)
        {
            var x = step * width / (_wave.Length - 1);
            var y = (height / 2) - (_wave[step] * height * 0.45);
            if (step == 0)
            {
                context.MoveTo((nfloat)x, (nfloat)y);
            }
            else
            {
                context.AddLineToPoint((nfloat)x, (nfloat)y);
            }
        }

        context.StrokePath();
    }

    /// <summary>
    /// For each channel, a bar 36 points high about the middle: the peak at under half strength, the
    /// average inside it at full, and a three-point white hold mark at the recent highest peak.
    /// </summary>
    private void DrawMeters(CGContext context, double width, double height)
    {
        for (var channel = 0; channel < 2; channel++)
        {
            if (_meterColors[channel] is not { } color)
            {
                continue;
            }

            var top = (height / 2) - 44 + (channel * 52);
            context.SetFillColor(color);
            context.SetAlpha(0.45f);
            context.FillRect(new CGRect(0, top, _meterPeak[channel] * width, 36));
            context.SetAlpha(1);
            context.FillRect(new CGRect(0, top, _meterAverage[channel] * width, 36));
            context.SetFillColor(White);
            context.FillRect(new CGRect(Math.Max(0, (_meterHold[channel] * width) - 3), top, 3, 36));
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var color in _colors)
            {
                color?.Dispose();
            }

            _waveColor?.Dispose();
            foreach (var color in _meterColors)
            {
                color?.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
