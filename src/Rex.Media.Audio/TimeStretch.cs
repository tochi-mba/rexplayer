using Rex.Media.Primitives;

namespace Rex.Media.Audio;

/// <summary>
/// Changes the speed of sound without changing its pitch (AU-15), by waveform-similarity
/// overlap-add (WSOLA, from Verhelst and Roelands' published description). Output is built from
/// Hann-windowed frames of 40 ms laid down every 20 ms; the frame for each step is read from where
/// the speed says it should start, moved by up to 10 ms to whichever position best matches how the
/// previous frame would have carried on, so the waveform joins without a click or a warble. One
/// offset serves every channel, found on their sum, so the stereo image holds. At speed 1 the sound
/// passes through untouched.
/// </summary>
public sealed class TimeStretch
{
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly int _frame;
    private readonly int _hop;
    private readonly int _search;
    private readonly float[] _window;
    private readonly List<float>[] _input;
    private readonly float[][] _overlap;
    private double _readPosition;
    private int _previousStart;
    private bool _started;
    private MediaTime _pts = MediaTime.Unknown;
    private long _emitted;

    public TimeStretch(int sampleRate, int channels, double rate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        if (!double.IsFinite(rate) || rate < MinimumRate || rate > MaximumRate)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), rate, $"The speed must be from {MinimumRate} to {MaximumRate}.");
        }

        (_sampleRate, _channels, Rate) = (sampleRate, channels, rate);
        _frame = Math.Max(16, sampleRate / 25) & ~1;
        _hop = _frame / 2;
        _search = Math.Max(4, sampleRate / 100);
        _window = [.. Enumerable.Range(0, _frame).Select(i => (float)(0.5 - (0.5 * Math.Cos(2 * Math.PI * i / _frame))))];
        _input = [.. Enumerable.Range(0, channels).Select(_ => new List<float>())];
        _overlap = [.. Enumerable.Range(0, channels).Select(_ => new float[_frame])];
    }

    public const double MinimumRate = 0.25;

    public const double MaximumRate = 4.0;

    public double Rate { get; }

    /// <summary>Whether this stretch serves frames of <paramref name="sampleRate"/> and <paramref name="channels"/>.</summary>
    public bool Fits(int sampleRate, int channels) => sampleRate == _sampleRate && channels == _channels;

    /// <summary>
    /// Feeds a frame; returns the stretched sound ready so far (the caller's to dispose), or null
    /// when more input is needed. At speed 1 the input itself is returned.
    /// </summary>
    public AudioFrame? Process(AudioFrame input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (Rate == 1)
        {
            return input;
        }

        if (!_pts.IsKnown && input.Pts.IsKnown)
        {
            _pts = input.Pts;
        }

        for (var channel = 0; channel < _channels; channel++)
        {
            var samples = channel < input.Channels ? input.Channel(channel)[..input.SampleCount] : default;
            for (var i = 0; i < input.SampleCount; i++)
            {
                _input[channel].Add(samples.IsEmpty ? 0 : samples[i]);
            }
        }

        return Produce(input.Layout);
    }

    /// <summary>Forgets everything buffered, as after a seek; output resumes at <paramref name="pts"/>.</summary>
    public void Reset(MediaTime pts)
    {
        foreach (var channel in _input)
        {
            channel.Clear();
        }

        foreach (var overlap in _overlap)
        {
            Array.Clear(overlap);
        }

        (_readPosition, _previousStart, _started, _pts, _emitted) = (0, 0, false, pts, 0);
    }

    private AudioFrame? Produce(ChannelLayout layout)
    {
        var steps = new List<int>();
        var available = _input[0].Count;
        var position = _readPosition;
        while ((int)position + _search + _frame <= available)
        {
            steps.Add(BestStart((int)position));
            (_previousStart, _started) = (steps[^1], true);
            position += _hop * Rate;
        }

        if (steps.Count == 0)
        {
            return null;
        }

        var output = AudioFrame.Rent(_sampleRate, _channels, steps.Count * _hop, layout);
        output.Pts = _pts.IsKnown ? _pts + MediaTime.FromSamples((long)Math.Round(_emitted * Rate), _sampleRate) : MediaTime.Unknown;
        for (var channel = 0; channel < _channels; channel++)
        {
            var source = _input[channel];
            var overlap = _overlap[channel];
            var target = output.Channel(channel);
            for (var step = 0; step < steps.Count; step++)
            {
                // Add the windowed frame to what is left of the last one, emit the first half (now
                // complete), and keep the second half for the next frame to add to.
                for (var i = 0; i < _frame; i++)
                {
                    overlap[i] += source[steps[step] + i] * _window[i];
                }

                overlap.AsSpan(0, _hop).CopyTo(target[(step * _hop)..]);
                overlap.AsSpan(_hop, _hop).CopyTo(overlap);
                overlap.AsSpan(_hop, _hop).Clear();
            }
        }

        _emitted += output.SampleCount;

        // Input no frame can reach any more is let go. (The last frame's start may then lie before
        // what is kept: only its continuation, a hop on, is ever read again.)
        var keepFrom = Math.Max(0, Math.Min((int)position - _search, _previousStart + _hop));
        foreach (var channel in _input)
        {
            channel.RemoveRange(0, Math.Min(keepFrom, channel.Count));
        }

        _readPosition = position - keepFrom;
        _previousStart -= keepFrom;
        return output;
    }

    /// <summary>
    /// Where to read the next frame near <paramref name="ideal"/>: the offset whose start best
    /// matches how the previous frame carries on, by correlation over the channels' sum.
    /// </summary>
    private int BestStart(int ideal)
    {
        if (!_started)
        {
            return ideal;
        }

        var natural = _previousStart + _hop;
        var best = ideal;
        var bestScore = double.NegativeInfinity;
        var from = Math.Max(0, ideal - _search);
        for (var candidate = from; candidate <= ideal + _search; candidate++)
        {
            double score = 0;
            for (var i = 0; i < _hop; i += 2)
            {
                double a = 0, b = 0;
                for (var channel = 0; channel < _channels; channel++)
                {
                    a += _input[channel][natural + i];
                    b += _input[channel][candidate + i];
                }

                score += a * b;
            }

            if (score > bestScore)
            {
                (best, bestScore) = (candidate, score);
            }
        }

        return best;
    }
}
