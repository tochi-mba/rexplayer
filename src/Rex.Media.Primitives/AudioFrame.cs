using System.Buffers;

namespace Rex.Media.Primitives;

/// <summary>
/// Decoded audio as 32-bit float, one plane per channel, nominally in [-1, 1]. Every decoder
/// produces this and every audio processor works on it, so there is exactly one conversion into
/// float (in the decoder) and one out of it (at the sink). Planes live in one pooled array, and the
/// frame object itself is pooled; disposing returns both.
/// </summary>
public sealed class AudioFrame : IDisposable
{
    private static readonly SlotPool<AudioFrame> Pool = new(256);

    private float[]? _samples;
    private int _capacity;
    private SlotPool<AudioFrame>? _home;

    private AudioFrame()
    {
    }

    public int SampleRate { get; private set; }

    public int Channels { get; private set; }

    public ChannelLayout Layout { get; private set; }

    /// <summary>Samples per channel.</summary>
    public int SampleCount { get; private set; }

    /// <summary>Samples per channel the frame can hold.</summary>
    public int Capacity => _capacity;

    /// <summary>When the first sample should be heard.</summary>
    public MediaTime Pts { get; set; }

    public long Generation { get; set; }

    public bool IsDisposed => _samples is null;

    public MediaTime Duration => MediaTime.FromSamples(SampleCount, SampleRate);

    public static AudioFrame Rent(int sampleRate, int channels, int sampleCount, ChannelLayout layout = ChannelLayout.None) =>
        Rent(Pool, sampleRate, channels, sampleCount, layout);

    /// <summary>
    /// Rents from <paramref name="pool"/>; with no pool the frame is dropped when disposed instead of
    /// being handed to someone else, which lets a test watch a disposed frame stay disposed.
    /// </summary>
    internal static AudioFrame Rent(SlotPool<AudioFrame>? pool, int sampleRate, int channels, int sampleCount, ChannelLayout layout = ChannelLayout.None)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(channels, AudioFormat.MaxChannels);
        ArgumentOutOfRangeException.ThrowIfNegative(sampleCount);
        var capacity = Math.Max(sampleCount, 1);
        var frame = pool?.Take() ?? new AudioFrame();
        frame._home = pool;
        frame._samples = ArrayPool<float>.Shared.Rent(capacity * channels);
        frame._capacity = capacity;
        frame.SampleRate = sampleRate;
        frame.Channels = channels;
        frame.Layout = layout != ChannelLayout.None && layout.ChannelCount() == channels ? layout : ChannelLayouts.Default(channels);
        frame.SampleCount = sampleCount;
        frame.Pts = MediaTime.Unknown;
        frame.Generation = 0;
        return frame;
    }

    /// <summary>The samples of one channel.</summary>
    public Span<float> Channel(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Channels);
        return Samples.AsSpan(index * _capacity, SampleCount);
    }

    /// <summary>Changes how many samples are valid, up to the capacity.</summary>
    public void SetSampleCount(int sampleCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sampleCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sampleCount, _capacity);
        _ = Samples;
        SampleCount = sampleCount;
    }

    /// <summary>Drops <paramref name="count"/> samples from the start, moving the timestamp with them.</summary>
    public void TrimStart(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        count = Math.Min(count, SampleCount);
        for (var channel = 0; channel < Channels; channel++)
        {
            var plane = Samples.AsSpan(channel * _capacity, SampleCount);
            plane[count..].CopyTo(plane);
        }

        SampleCount -= count;
        if (Pts.IsKnown)
        {
            Pts += MediaTime.FromSamples(count, SampleRate);
        }
    }

    public void Dispose()
    {
        var samples = Interlocked.Exchange(ref _samples, null);
        if (samples is null)
        {
            return;
        }

        ArrayPool<float>.Shared.Return(samples);
        SampleCount = 0;
        _home?.Return(this);
    }

    private float[] Samples => _samples ?? throw new ObjectDisposedException(nameof(AudioFrame));
}
