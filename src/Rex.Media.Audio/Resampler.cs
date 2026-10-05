using Rex.Media.Primitives;

namespace Rex.Media.Audio;

public enum ResamplerQuality
{
    Fast,
    Normal,
    High,
}

/// <summary>
/// Changes sample rate with a Kaiser-windowed sinc filter evaluated in polyphase form. When the
/// ratio reduces to at most 2048 phases (44.1 kHz to 48 kHz is 160) every output sample uses an exact
/// phase; otherwise neighbouring phases are interpolated. Each phase is normalised to unity gain at
/// DC, and when the rate goes down the cut-off follows the new Nyquist frequency so nothing aliases.
/// Positions are kept as exact integers, so a stream resampled for hours never drifts.
/// </summary>
public sealed class Resampler
{
    private const int MaxExactPhases = 2048;

    private readonly int _channels;
    private readonly long _up;
    private readonly long _down;
    private readonly int _halfWidth;
    private readonly int _phases;
    private readonly bool _exact;
    private readonly float[] _table;
    private readonly int _taps;
    private float[][] _history;
    private long _historyStart;
    private int _historyCount;
    private long _nextOutput;
    private long _inputEnd;
    private MediaTime _basePts = MediaTime.Unknown;
    private long _generation;

    public Resampler(int inputRate, int outputRate, int channels, ResamplerQuality quality = ResamplerQuality.Normal)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inputRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        InputRate = inputRate;
        OutputRate = outputRate;
        _channels = channels;
        var divisor = Gcd(inputRate, outputRate);
        _up = outputRate / divisor;
        _down = inputRate / divisor;

        var (baseHalfWidth, beta, rolloff) = quality switch
        {
            ResamplerQuality.Fast => (8, 6.0, 0.88),
            ResamplerQuality.High => (48, 10.0, 0.96),
            _ => (24, 8.6, 0.93),
        };
        var ratio = Math.Min(1.0, outputRate / (double)inputRate);
        _halfWidth = (int)Math.Ceiling(baseHalfWidth / ratio);
        _taps = 2 * _halfWidth;
        _exact = _up <= MaxExactPhases;
        _phases = _exact ? (int)_up : MaxExactPhases;
        var cutoff = 0.5 * ratio * rolloff;
        _table = BuildTable(_phases, _halfWidth, cutoff, beta);
        _history = NewHistory(channels, 4096);
        Reset(MediaTime.Unknown);
    }

    public int InputRate { get; }

    public int OutputRate { get; }

    /// <summary>Input samples the filter needs ahead of an output sample: the resampler's delay.</summary>
    public int Latency => _halfWidth;

    /// <summary>Forgets buffered audio; the next output starts at <paramref name="pts"/>.</summary>
    public void Reset(MediaTime pts)
    {
        _historyStart = -_halfWidth;
        _historyCount = _halfWidth;
        foreach (var plane in _history)
        {
            Array.Clear(plane, 0, _halfWidth);
        }

        _nextOutput = 0;
        _inputEnd = 0;
        _basePts = pts;
    }

    /// <summary>Feeds a frame; returns the samples that can be produced so far, or null if none can yet.</summary>
    public AudioFrame? Process(AudioFrame input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Channels != _channels || input.SampleRate != InputRate)
        {
            throw new InvalidOperationException("The frame does not match the resampler's input format.");
        }

        if (!_basePts.IsKnown && input.Pts.IsKnown)
        {
            _basePts = input.Pts;
        }

        _generation = input.Generation;
        Append(input, input.SampleCount);
        _inputEnd = _historyStart + _historyCount;
        return Produce(input.Layout, drain: false);
    }

    /// <summary>Pads with silence to release every sample still inside the filter.</summary>
    public AudioFrame? Drain(ChannelLayout layout)
    {
        var silence = AudioFrame.Rent(InputRate, _channels, _halfWidth, layout);
        for (var channel = 0; channel < _channels; channel++)
        {
            silence.Channel(channel).Clear();
        }

        Append(silence, silence.SampleCount);
        silence.Dispose();
        return Produce(layout, drain: true);
    }

    private void Append(AudioFrame input, int count)
    {
        EnsureCapacity(_historyCount + count);
        for (var channel = 0; channel < _channels; channel++)
        {
            input.Channel(channel)[..count].CopyTo(_history[channel].AsSpan(_historyCount));
        }

        _historyCount += count;
    }

    private AudioFrame? Produce(ChannelLayout layout, bool drain)
    {
        var available = _historyStart + _historyCount;
        var realInputEnd = drain ? _inputEnd : long.MaxValue;
        var count = 0;
        while (true)
        {
            var position = (_nextOutput + count) * _down;
            var index = position / _up;
            if (index + _halfWidth >= available || (drain && index >= realInputEnd))
            {
                break;
            }

            count++;
        }

        if (count == 0)
        {
            Trim();
            return null;
        }

        var output = AudioFrame.Rent(OutputRate, _channels, count, layout);
        output.Generation = _generation;
        output.Pts = _basePts.IsKnown ? _basePts + MediaTime.FromSamples(_nextOutput, OutputRate) : MediaTime.Unknown;
        for (var n = 0; n < count; n++)
        {
            var position = (_nextOutput + n) * _down;
            var index = position / _up;
            var phase = position % _up;
            var start = (int)(index - _halfWidth + 1 - _historyStart);
            for (var channel = 0; channel < _channels; channel++)
            {
                output.Channel(channel)[n] = Convolve(_history[channel].AsSpan(start, _taps), phase);
            }
        }

        _nextOutput += count;
        Trim();
        return output;
    }

    private float Convolve(ReadOnlySpan<float> window, long phase)
    {
        if (_exact)
        {
            return Dot(window, _table.AsSpan((int)phase * _taps, _taps));
        }

        var scaled = phase * (double)_phases / _up;
        var lower = (int)scaled;
        var fraction = (float)(scaled - lower);
        var a = Dot(window, _table.AsSpan(lower * _taps, _taps));
        var b = Dot(window, _table.AsSpan((lower + 1) * _taps, _taps));
        return a + ((b - a) * fraction);
    }

    private static float Dot(ReadOnlySpan<float> samples, ReadOnlySpan<float> coefficients)
    {
        var sum = 0f;
        for (var i = 0; i < coefficients.Length; i++)
        {
            sum += samples[i] * coefficients[i];
        }

        return sum;
    }

    /// <summary>Drops history the next output sample no longer needs.</summary>
    private void Trim()
    {
        var nextIndex = _nextOutput * _down / _up;
        var keepFrom = nextIndex - _halfWidth + 1;
        var drop = (int)Math.Clamp(keepFrom - _historyStart, 0, _historyCount);
        if (drop == 0)
        {
            return;
        }

        foreach (var plane in _history)
        {
            plane.AsSpan(drop, _historyCount - drop).CopyTo(plane);
        }

        _historyStart += drop;
        _historyCount -= drop;
    }

    private void EnsureCapacity(int needed)
    {
        if (_history[0].Length >= needed)
        {
            return;
        }

        var grown = NewHistory(_channels, Math.Max(needed, _history[0].Length * 2));
        for (var channel = 0; channel < _channels; channel++)
        {
            _history[channel].AsSpan(0, _historyCount).CopyTo(grown[channel]);
        }

        _history = grown;
    }

    private static float[][] NewHistory(int channels, int length)
    {
        var history = new float[channels][];
        for (var channel = 0; channel < channels; channel++)
        {
            history[channel] = new float[length];
        }

        return history;
    }

    /// <summary>
    /// Row p holds the taps for an output that falls p/phases of the way between two input samples:
    /// tap m (for input offset m - halfWidth + 1) is h(p/phases - (m - halfWidth + 1)). One extra row
    /// lets interpolation between the last phase and the next whole sample stay in bounds.
    /// </summary>
    internal static float[] BuildTable(int phases, int halfWidth, double cutoff, double beta)
    {
        var taps = 2 * halfWidth;
        var table = new float[(phases + 1) * taps];
        var row = new double[taps];
        for (var p = 0; p <= phases; p++)
        {
            var fraction = p / (double)phases;
            var sum = 0.0;
            for (var m = 0; m < taps; m++)
            {
                var t = fraction - (m - halfWidth + 1);
                row[m] = 2 * cutoff * Sinc(2 * cutoff * t) * Kaiser(t / halfWidth, beta);
                sum += row[m];
            }

            for (var m = 0; m < taps; m++)
            {
                table[(p * taps) + m] = (float)(row[m] / sum);
            }
        }

        return table;
    }

    private static double Sinc(double x) => Math.Abs(x) < 1e-12 ? 1.0 : Math.Sin(Math.PI * x) / (Math.PI * x);

    private static double Kaiser(double x, double beta) =>
        Math.Abs(x) > 1 ? 0 : BesselI0(beta * Math.Sqrt(1 - (x * x))) / BesselI0(beta);

    private static double BesselI0(double x)
    {
        var sum = 1.0;
        var term = 1.0;
        var half = x / 2;
        for (var k = 1; k < 64; k++)
        {
            term *= half / k;
            var square = term * term;
            sum += square;
            if (square < sum * 1e-17)
            {
                break;
            }
        }

        return sum;
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a;
    }
}
