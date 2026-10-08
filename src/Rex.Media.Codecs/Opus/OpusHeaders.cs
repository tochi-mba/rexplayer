// Spec: IETF RFC 6716 section 3.1 (the TOC byte: configuration, stereo flag and frame count code) and RFC 7845 section 5.1 (the identification header "OpusHead": version, channels, pre-skip, input rate, output gain, channel mapping).
using System.Buffers.Binary;
using Rex.Media.Primitives;

namespace Rex.Media.Codecs.Opus;

/// <summary>What an Opus packet's first byte says about its length.</summary>
public static class OpusPacket
{
    // Frame lengths at 48 kHz, by configuration: SILK (0-11), hybrid (12-15), CELT (16-31).
    private static readonly int[] SilkFrames = [480, 960, 1920, 2880];
    private static readonly int[] CeltFrames = [120, 240, 480, 960];

    /// <summary>The samples at 48 kHz the packet decodes to; 0 for an empty or malformed packet.</summary>
    public static int Samples(ReadOnlySpan<byte> packet)
    {
        if (packet.IsEmpty)
        {
            return 0;
        }

        var config = packet[0] >> 3;
        var frame = config < 12 ? SilkFrames[config & 3] : config < 16 ? ((config & 1) == 0 ? 480 : 960) : CeltFrames[config & 3];
        var count = (packet[0] & 3) switch
        {
            0 => 1,
            1 or 2 => 2,
            _ => packet.Length > 1 ? packet[1] & 0x3F : 0,
        };

        // A packet holds at most 120 ms.
        var total = frame * count;
        return total > 5760 ? 0 : total;
    }
}

/// <summary>The Opus identification header (RFC 7845 section 5.1).</summary>
public sealed record OpusHead(int Channels, int PreSkip, int InputSampleRate, double OutputGainDb, int MappingFamily, int Streams, int CoupledStreams, byte[] Mapping)
{
    public static OpusHead Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 19 || !data.StartsWith("OpusHead"u8))
        {
            throw new MediaFormatException("The Opus identification header is missing.");
        }

        if (data[8] >> 4 != 0)
        {
            throw new MediaFormatException($"Opus header version {data[8]} is not one rexplayer can read.");
        }

        var channels = data[9];
        var family = data[18];
        if (channels == 0)
        {
            throw new MediaFormatException("The Opus header names no channels.");
        }

        var (streams, coupled, mapping) = (1, channels == 2 ? 1 : 0, channels == 2 ? new byte[] { 0, 1 } : new byte[] { 0 });
        if (family != 0)
        {
            if (data.Length < 21 + channels)
            {
                throw new MediaFormatException("The Opus header's channel mapping is cut short.");
            }

            (streams, coupled, mapping) = (data[19], data[20], data.Slice(21, channels).ToArray());
        }
        else if (channels > 2)
        {
            throw new MediaFormatException("Opus mapping family 0 carries one or two channels.");
        }

        return new OpusHead(
            channels,
            BinaryPrimitives.ReadUInt16LittleEndian(data[10..]),
            (int)BinaryPrimitives.ReadUInt32LittleEndian(data[12..]),
            BinaryPrimitives.ReadInt16LittleEndian(data[16..]) / 256.0,
            family,
            streams,
            coupled,
            mapping);
    }
}
