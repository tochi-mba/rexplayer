// Spec: Microsoft WAVE PCM sample layout (Multimedia Programming Interface and Data Specifications 1.0); ITU-T G.711 (A-law and µ-law companding).
using System.Buffers.Binary;
using Rex.Media.Primitives;

namespace Rex.Media.Codecs.Software.Pcm;

/// <summary>Decodes uncompressed PCM in every storage format, and G.711 A-law and µ-law.</summary>
public sealed class PcmDecoderFactory : IDecoderFactory
{
    public string Name => "rexplayer PCM";

    public DecoderSource Source => DecoderSource.Own;

    public int Rank => 100;

    public bool CanDecode(TrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return track.Audio is not null && track.Codec switch
        {
            CodecId.Pcm => track.Audio.PcmFormat != SampleFormat.Unknown,
            CodecId.Alaw or CodecId.Mulaw => true,
            _ => false,
        };
    }

    public IAudioDecoder CreateAudio(TrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (!CanDecode(track))
        {
            throw new NotSupportedException("The track is not PCM in a known sample format.");
        }

        return new PcmDecoder(track);
    }
}

/// <summary>
/// Converts interleaved integer, float or companded samples to float planes. Integers are scaled by
/// their full-scale value, so -32768 becomes exactly -1.0 and 32767 just under 1.0.
/// </summary>
public sealed class PcmDecoder : IAudioDecoder
{
    private static readonly float[] AlawTable = BuildTable(DecodeAlaw);
    private static readonly float[] MulawTable = BuildTable(DecodeMulaw);

    private readonly CodecId _codec;
    private readonly SampleFormat _format;
    private readonly bool _bigEndian;
    private readonly int _channels;
    private readonly int _sampleRate;
    private readonly ChannelLayout _layout;
    private readonly int _bytesPerSample;

    public PcmDecoder(TrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(track);
        var audio = track.Audio ?? throw new NotSupportedException("The track has no audio parameters.");
        _codec = track.Codec;
        _format = track.Codec == CodecId.Pcm ? audio.PcmFormat : SampleFormat.U8;
        _bigEndian = audio.BigEndian;
        _channels = audio.Channels;
        _sampleRate = audio.SampleRate;
        _layout = audio.Layout;
        _bytesPerSample = new AudioFormat(audio.SampleRate, audio.Channels, _format).BytesPerSample;
    }

    public string Name => "rexplayer PCM";

    public DecoderSource Source => DecoderSource.Own;

    public void Decode(Packet packet, ICollection<AudioFrame> output)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(output);
        var data = packet.Data.Span;
        var blockAlign = _bytesPerSample * _channels;
        var samples = data.Length / blockAlign;
        if (samples == 0)
        {
            return;
        }

        var frame = AudioFrame.Rent(_sampleRate, _channels, samples, _layout);
        frame.Pts = packet.Timestamp;
        frame.Generation = packet.Generation;
        for (var channel = 0; channel < _channels; channel++)
        {
            var plane = frame.Channel(channel);
            var offset = channel * _bytesPerSample;
            for (var i = 0; i < samples; i++)
            {
                plane[i] = ReadSample(data.Slice(offset + (i * blockAlign), _bytesPerSample));
            }
        }

        output.Add(frame);
    }

    public void Drain(ICollection<AudioFrame> output)
    {
    }

    public void Flush()
    {
    }

    public void Dispose()
    {
    }

    internal static float DecodeAlaw(byte value)
    {
        var a = value ^ 0x55;
        var exponent = (a >> 4) & 0x07;
        var mantissa = a & 0x0F;
        var magnitude = exponent == 0 ? (mantissa << 4) + 8 : ((mantissa << 4) + 0x108) << (exponent - 1);
        return ((a & 0x80) != 0 ? magnitude : -magnitude) / 32768f;
    }

    internal static float DecodeMulaw(byte value)
    {
        var u = ~value & 0xFF;
        var exponent = (u >> 4) & 0x07;
        var mantissa = u & 0x0F;
        var magnitude = (((mantissa << 3) + 0x84) << exponent) - 0x84;
        return ((u & 0x80) != 0 ? -magnitude : magnitude) / 32768f;
    }

    private float ReadSample(ReadOnlySpan<byte> bytes)
    {
        if (_codec == CodecId.Alaw)
        {
            return AlawTable[bytes[0]];
        }

        if (_codec == CodecId.Mulaw)
        {
            return MulawTable[bytes[0]];
        }

        return _format switch
        {
            SampleFormat.U8 => (bytes[0] - 128) / 128f,
            SampleFormat.S8 => (sbyte)bytes[0] / 128f,
            SampleFormat.S16 => (_bigEndian ? BinaryPrimitives.ReadInt16BigEndian(bytes) : BinaryPrimitives.ReadInt16LittleEndian(bytes)) / 32768f,
            SampleFormat.S24 => ReadInt24(bytes) / 8388608f,
            SampleFormat.S32 => (_bigEndian ? BinaryPrimitives.ReadInt32BigEndian(bytes) : BinaryPrimitives.ReadInt32LittleEndian(bytes)) / 2147483648f,
            SampleFormat.F32 => _bigEndian ? BinaryPrimitives.ReadSingleBigEndian(bytes) : BinaryPrimitives.ReadSingleLittleEndian(bytes),
            _ => (float)(_bigEndian ? BinaryPrimitives.ReadDoubleBigEndian(bytes) : BinaryPrimitives.ReadDoubleLittleEndian(bytes)),
        };
    }

    private int ReadInt24(ReadOnlySpan<byte> bytes)
    {
        var value = _bigEndian
            ? (bytes[0] << 16) | (bytes[1] << 8) | bytes[2]
            : (bytes[2] << 16) | (bytes[1] << 8) | bytes[0];
        return (value << 8) >> 8;
    }

    private static float[] BuildTable(Func<byte, float> decode)
    {
        var table = new float[256];
        for (var i = 0; i < 256; i++)
        {
            table[i] = decode((byte)i);
        }

        return table;
    }
}
