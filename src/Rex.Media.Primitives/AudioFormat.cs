namespace Rex.Media.Primitives;

/// <summary>How samples are stored in a PCM stream or a sink buffer.</summary>
public enum SampleFormat
{
    Unknown = 0,
    U8,
    S8,
    S16,
    S24,
    S32,
    F32,
    F64,
}

/// <summary>The shape of an audio stream: rate, channels and how each sample is stored.</summary>
public sealed record AudioFormat
{
    public AudioFormat(int sampleRate, int channels, SampleFormat sampleFormat, ChannelLayout layout = ChannelLayout.None, bool bigEndian = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(channels, MaxChannels);
        SampleRate = sampleRate;
        Channels = channels;
        SampleFormat = sampleFormat;
        Layout = layout != ChannelLayout.None && layout.ChannelCount() == channels ? layout : ChannelLayouts.Default(channels);
        BigEndian = bigEndian;
    }

    /// <summary>The most channels a stream may declare before the engine treats it as corrupt.</summary>
    public const int MaxChannels = 32;

    public int SampleRate { get; }

    public int Channels { get; }

    public SampleFormat SampleFormat { get; }

    public ChannelLayout Layout { get; }

    public bool BigEndian { get; }

    public int BytesPerSample => SampleFormat switch
    {
        SampleFormat.U8 or SampleFormat.S8 => 1,
        SampleFormat.S16 => 2,
        SampleFormat.S24 => 3,
        SampleFormat.S32 or SampleFormat.F32 => 4,
        SampleFormat.F64 => 8,
        _ => 0,
    };

    /// <summary>Bytes in one interleaved frame (one sample for every channel).</summary>
    public int BlockAlign => BytesPerSample * Channels;

    public override string ToString() => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{SampleRate} Hz, {Layout.Describe(Channels)}, {SampleFormat}");
}
