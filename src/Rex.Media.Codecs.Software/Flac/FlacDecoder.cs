// Spec: IETF RFC 9639 (FLAC), sections 9.1 "Frame header", 9.2 "Subframes" (constant, verbatim, fixed and linear predictors, coded residual), 9.3 "Frame footer" and 9.1.3 channel order.
using Rex.Media.Codecs.Flac;
using Rex.Media.Primitives;

namespace Rex.Media.Codecs.Software.Flac;

/// <summary>Decodes FLAC, rexplayer's own implementation of RFC 9639.</summary>
public sealed class FlacDecoderFactory : IDecoderFactory
{
    public string Name => "rexplayer FLAC";

    public DecoderSource Source => DecoderSource.Own;

    public int Rank => 100;

    public bool CanDecode(TrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return track.Codec == CodecId.Flac && track.Audio is not null;
    }

    public IAudioDecoder CreateAudio(TrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (!CanDecode(track))
        {
            throw new NotSupportedException("The track is not FLAC.");
        }

        return new FlacDecoder(track);
    }
}

/// <summary>
/// Turns one FLAC frame per packet into float samples. Frames are independent, so there is nothing
/// to drain or flush. A frame whose checksum fails, or whose bits do not parse, becomes silence of
/// the frame's length, so the timeline stays intact and a damaged file never plays as noise.
/// </summary>
public sealed class FlacDecoder : IAudioDecoder
{
    private const int MaxLpcOrder = 32;

    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly int _streamBits;
    private readonly ChannelLayout _layout;
    private readonly long[] _coefficients = new long[MaxLpcOrder];
    private long[][] _samples;

    public FlacDecoder(TrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(track);
        var audio = track.Audio ?? throw new NotSupportedException("The track has no audio parameters.");
        if (audio.Channels is < 1 or > 8)
        {
            throw new NotSupportedException("FLAC carries one to eight channels.");
        }

        if (audio.BitsPerSample is not (0 or (>= 4 and <= 32)))
        {
            throw new NotSupportedException("FLAC carries 4 to 32 bits per sample.");
        }

        _sampleRate = audio.SampleRate;
        _channels = audio.Channels;
        _streamBits = audio.BitsPerSample;
        _layout = FlacStreamInfo.Layout(_channels);
        var blockSize = track.CodecPrivate.Length >= FlacStreamInfo.Size ? FlacStreamInfo.Parse(track.CodecPrivate).MaxBlockSize : 4608;
        _samples = new long[_channels][];
        for (var channel = 0; channel < _channels; channel++)
        {
            _samples[channel] = new long[blockSize];
        }
    }

    public string Name => "rexplayer FLAC";

    public DecoderSource Source => DecoderSource.Own;

    /// <summary>Frames replaced by silence because they were damaged.</summary>
    public int CorruptFrames { get; private set; }

    public void Decode(Packet packet, ICollection<AudioFrame> output)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(output);
        var data = packet.Data.Span;
        if (!FlacFrameHeader.TryParse(data, out var header))
        {
            // Without a header there is no length to fill with silence; the frame is dropped.
            CorruptFrames++;
            return;
        }

        var frame = AudioFrame.Rent(_sampleRate, _channels, header.BlockSize, _layout);
        frame.Pts = packet.Timestamp;
        frame.Generation = packet.Generation;
        try
        {
            DecodeFrame(data, header);
            Convert(frame, header.BitsPerSample == 0 ? _streamBits : header.BitsPerSample);
        }
        catch (MediaFormatException)
        {
            CorruptFrames++;
            for (var channel = 0; channel < _channels; channel++)
            {
                frame.Channel(channel).Clear();
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

    private void DecodeFrame(ReadOnlySpan<byte> data, FlacFrameHeader header)
    {
        var bits = header.BitsPerSample == 0 ? _streamBits : header.BitsPerSample;
        if (bits == 0)
        {
            throw new MediaFormatException("The FLAC frame does not say its sample size and the stream does not either.");
        }

        if (header.Channels != _channels)
        {
            throw new MediaFormatException("The FLAC frame has a different channel count from the stream.");
        }

        if (header.BlockSize > _samples[0].Length)
        {
            for (var channel = 0; channel < _channels; channel++)
            {
                _samples[channel] = new long[header.BlockSize];
            }
        }

        var reader = new BitReader(data);
        reader.SkipBits(header.Length * 8L);
        for (var channel = 0; channel < _channels; channel++)
        {
            // The side channel of a decorrelated pair holds a difference, which needs one more bit.
            var side = header.ChannelMode switch
            {
                FlacChannelMode.SideRight => channel == 0,
                FlacChannelMode.LeftSide or FlacChannelMode.MidSide => channel == 1,
                _ => false,
            };
            DecodeSubframe(ref reader, _samples[channel].AsSpan(0, header.BlockSize), side ? bits + 1 : bits);
        }

        reader.AlignToByte();
        var crcEnd = reader.BytePosition;
        if (crcEnd + 2 > data.Length || ((data[crcEnd] << 8) | data[crcEnd + 1]) != Crc.Crc16Flac.Compute(data[..crcEnd]))
        {
            throw new MediaFormatException("The FLAC frame's checksum does not match.");
        }

        Restore(header);
    }

    private void DecodeSubframe(ref BitReader reader, Span<long> samples, int bits)
    {
        if (reader.ReadBit())
        {
            throw new MediaFormatException("A FLAC subframe header's padding bit is set.");
        }

        var type = (int)reader.ReadBits(6);
        var wasted = 0;
        if (reader.ReadBit())
        {
            wasted = (int)reader.ReadUnary() + 1;
            bits -= wasted;
            if (bits <= 0)
            {
                throw new MediaFormatException("A FLAC subframe wastes more bits than the sample has.");
            }
        }

        switch (type)
        {
            case 0:
                samples.Fill(ReadSigned(ref reader, bits));
                break;
            case 1:
                for (var i = 0; i < samples.Length; i++)
                {
                    samples[i] = ReadSigned(ref reader, bits);
                }

                break;
            case >= 8 and <= 12:
                DecodeFixed(ref reader, samples, bits, type - 8);
                break;
            case >= 32:
                DecodeLpc(ref reader, samples, bits, type - 31);
                break;
            default:
                throw new MediaFormatException("A FLAC subframe uses a reserved type.");
        }

        if (wasted > 0)
        {
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] <<= wasted;
            }
        }
    }

    private static void DecodeFixed(ref BitReader reader, Span<long> samples, int bits, int order)
    {
        ReadWarmUp(ref reader, samples, bits, order);
        DecodeResidual(ref reader, samples, order);
        switch (order)
        {
            case 1:
                for (var i = 1; i < samples.Length; i++)
                {
                    samples[i] += samples[i - 1];
                }

                break;
            case 2:
                for (var i = 2; i < samples.Length; i++)
                {
                    samples[i] += (2 * samples[i - 1]) - samples[i - 2];
                }

                break;
            case 3:
                for (var i = 3; i < samples.Length; i++)
                {
                    samples[i] += (3 * samples[i - 1]) - (3 * samples[i - 2]) + samples[i - 3];
                }

                break;
            case 4:
                for (var i = 4; i < samples.Length; i++)
                {
                    samples[i] += (4 * samples[i - 1]) - (6 * samples[i - 2]) + (4 * samples[i - 3]) - samples[i - 4];
                }

                break;
        }
    }

    private void DecodeLpc(ref BitReader reader, Span<long> samples, int bits, int order)
    {
        ReadWarmUp(ref reader, samples, bits, order);
        var precision = (int)reader.ReadBits(4) + 1;
        if (precision == 16)
        {
            throw new MediaFormatException("A FLAC subframe uses the forbidden coefficient precision.");
        }

        var shift = reader.ReadSignedBits(5);
        if (shift < 0)
        {
            throw new MediaFormatException("A FLAC subframe has a negative prediction shift.");
        }

        // The first coefficient weighs the newest sample.
        var coefficients = _coefficients.AsSpan(0, order);
        for (var j = 0; j < order; j++)
        {
            coefficients[j] = reader.ReadSignedBits(precision);
        }

        DecodeResidual(ref reader, samples, order);
        for (var i = order; i < samples.Length; i++)
        {
            long sum = 0;
            for (var j = 0; j < order; j++)
            {
                sum += coefficients[j] * samples[i - 1 - j];
            }

            samples[i] += sum >> shift;
        }
    }

    private static void ReadWarmUp(ref BitReader reader, Span<long> samples, int bits, int order)
    {
        if (order > samples.Length)
        {
            throw new MediaFormatException("A FLAC predictor's order is larger than its block.");
        }

        for (var i = 0; i < order; i++)
        {
            samples[i] = ReadSigned(ref reader, bits);
        }
    }

    /// <summary>
    /// Reads the residual into <paramref name="samples"/> after the warm-up samples; prediction then
    /// adds to it in place. Partitions are Rice-coded with a per-partition parameter, or escaped to
    /// fixed-width raw values when the encoder found that smaller.
    /// </summary>
    private static void DecodeResidual(ref BitReader reader, Span<long> samples, int order)
    {
        var method = reader.ReadBits(2);
        if (method > 1)
        {
            throw new MediaFormatException("A FLAC residual uses a reserved coding method.");
        }

        var parameterBits = method == 0 ? 4 : 5;
        var escape = method == 0 ? 15u : 31u;
        var partitionOrder = (int)reader.ReadBits(4);
        var perPartition = samples.Length >> partitionOrder;
        if (perPartition << partitionOrder != samples.Length || perPartition < order)
        {
            throw new MediaFormatException("A FLAC residual's partitions do not divide its block.");
        }

        var index = order;
        for (var partition = 0; partition < 1 << partitionOrder; partition++)
        {
            var end = (partition + 1) * perPartition;
            var parameter = reader.ReadBits(parameterBits);
            if (parameter == escape)
            {
                var width = (int)reader.ReadBits(5);
                for (; index < end; index++)
                {
                    samples[index] = width == 0 ? 0 : reader.ReadSignedBits(width);
                }

                continue;
            }

            var k = (int)parameter;
            for (; index < end; index++)
            {
                var folded = ((ulong)reader.ReadUnary() << k) | reader.ReadBits(k);
                samples[index] = (long)(folded >> 1) ^ -(long)(folded & 1);
            }
        }
    }

    private static long ReadSigned(ref BitReader reader, int bits)
    {
        if (bits <= 32)
        {
            return reader.ReadSignedBits(bits);
        }

        // Only the side channel of 32-bit audio is wider: 33 bits.
        var shift = 64 - bits;
        return (long)(reader.ReadBits64(bits) << shift) >> shift;
    }

    private void Restore(FlacFrameHeader header)
    {
        var count = header.BlockSize;
        var first = _samples[0];
        var second = _channels > 1 ? _samples[1] : first;
        switch (header.ChannelMode)
        {
            case FlacChannelMode.LeftSide:
                for (var i = 0; i < count; i++)
                {
                    second[i] = first[i] - second[i];
                }

                break;
            case FlacChannelMode.SideRight:
                for (var i = 0; i < count; i++)
                {
                    first[i] += second[i];
                }

                break;
            case FlacChannelMode.MidSide:
                for (var i = 0; i < count; i++)
                {
                    var side = second[i];
                    var mid = (first[i] << 1) | (side & 1);
                    first[i] = (mid + side) >> 1;
                    second[i] = (mid - side) >> 1;
                }

                break;
        }
    }

    private void Convert(AudioFrame frame, int bits)
    {
        var scale = 1.0 / (1L << (bits - 1));
        for (var channel = 0; channel < _channels; channel++)
        {
            var source = _samples[channel];
            var plane = frame.Channel(channel);
            for (var i = 0; i < plane.Length; i++)
            {
                plane[i] = (float)(source[i] * scale);
            }
        }
    }
}
