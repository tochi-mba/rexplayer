// Spec: the Xing/Info VBR header and the LAME tag (LAME project's published tag format, revision 1); the Fraunhofer VBRI header as documented by its authors.
using System.Buffers.Binary;
using System.Text;

namespace Rex.Media.Codecs.Mpeg;

/// <summary>
/// The tag an encoder leaves in an otherwise silent first frame: how many frames and bytes the
/// stream has (so a variable-bitrate file has a duration without reading it all) and, from LAME
/// and compatible encoders, how many samples of encoder delay and padding to cut for gapless
/// playback. The frame carrying the tag is not audio.
/// </summary>
public sealed record MpegInfoTag(string Kind, long? Frames, long? Bytes, int EncoderDelay, int EncoderPadding, string? Encoder)
{
    /// <summary>
    /// The delay every standard Layer III decoder adds (528 samples of filterbank delay plus one),
    /// which the gapless convention removes together with the encoder's own delay.
    /// </summary>
    public const int DecoderDelay = 529;

    /// <summary>Samples to drop from the start of the decoded stream.</summary>
    public int LeadingSamples => Encoder is null ? 0 : EncoderDelay + DecoderDelay;

    /// <summary>Samples to drop from the end of the decoded stream.</summary>
    public int TrailingSamples => Encoder is null ? 0 : Math.Max(0, EncoderPadding - DecoderDelay);

    /// <summary>Reads the tag from a whole frame, or returns null when the frame carries none.</summary>
    public static MpegInfoTag? Parse(ReadOnlySpan<byte> frame, MpegAudioHeader header)
    {
        var xingAt = header.PayloadOffset + header.SideInfoLength;
        if (frame.Length >= xingAt + 8 && (frame.Slice(xingAt, 4).SequenceEqual("Xing"u8) || frame.Slice(xingAt, 4).SequenceEqual("Info"u8)))
        {
            return ParseXing(frame, xingAt);
        }

        const int VbriAt = 4 + 32;
        if (frame.Length >= VbriAt + 18 && frame.Slice(VbriAt, 4).SequenceEqual("VBRI"u8))
        {
            var bytes = BinaryPrimitives.ReadUInt32BigEndian(frame[(VbriAt + 10)..]);
            var frames = BinaryPrimitives.ReadUInt32BigEndian(frame[(VbriAt + 14)..]);
            return new MpegInfoTag("VBRI", frames, bytes, 0, 0, null);
        }

        return null;
    }

    private static MpegInfoTag ParseXing(ReadOnlySpan<byte> frame, int at)
    {
        var kind = Encoding.ASCII.GetString(frame.Slice(at, 4));
        var flags = BinaryPrimitives.ReadUInt32BigEndian(frame[(at + 4)..]);
        var offset = at + 8;
        long? frames = null;
        long? bytes = null;
        if ((flags & 1) != 0 && offset + 4 <= frame.Length)
        {
            frames = BinaryPrimitives.ReadUInt32BigEndian(frame[offset..]);
            offset += 4;
        }

        if ((flags & 2) != 0 && offset + 4 <= frame.Length)
        {
            bytes = BinaryPrimitives.ReadUInt32BigEndian(frame[offset..]);
            offset += 4;
        }

        offset += ((flags & 4) != 0 ? 100 : 0) + ((flags & 8) != 0 ? 4 : 0);

        // The LAME extension: a 9-character encoder name, then fixed fields with the delay and
        // padding packed as two 12-bit values 21 bytes in.
        const int LameLength = 24;
        if (offset + LameLength > frame.Length || !IsEncoderName(frame.Slice(offset, 4)))
        {
            return new MpegInfoTag(kind, frames, bytes, 0, 0, null);
        }

        var encoder = Encoding.ASCII.GetString(frame.Slice(offset, 9)).TrimEnd('\0', ' ');
        var packed = frame.Slice(offset + 21, 3);
        var delay = (packed[0] << 4) | (packed[1] >> 4);
        var padding = ((packed[1] & 0x0F) << 8) | packed[2];
        return new MpegInfoTag(kind, frames, bytes, delay, padding, encoder);
    }

    /// <summary>LAME and the encoders that write its tag start it with four letters.</summary>
    private static bool IsEncoderName(ReadOnlySpan<byte> name)
    {
        foreach (var b in name)
        {
            if (b is not ((>= (byte)'A' and <= (byte)'Z') or (>= (byte)'a' and <= (byte)'z')))
            {
                return false;
            }
        }

        return true;
    }
}
