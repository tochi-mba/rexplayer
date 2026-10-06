// Spec: ISO/IEC 14496-3 (MPEG-4 Audio) subpart 1 annex 1.A sections 1.A.2.2.1 "adts_fixed_header" and "adts_variable_header", 1.A.3.2 (their semantics: profile as object type minus one, frame length including the header).
namespace Rex.Media.Codecs.Aac;

/// <summary>
/// The 7-byte (9 with a CRC) header in front of every frame of an ADTS stream, the form AAC takes in
/// .aac files, MPEG transport streams and internet radio.
/// </summary>
public readonly record struct AdtsHeader
{
    /// <summary>The audio object type: the header's profile plus one (2 is LC).</summary>
    public int ObjectType { get; init; }

    public int SampleRate { get; init; }

    /// <summary>The channel configuration; 0 means a program_config_element in the frame says.</summary>
    public int ChannelConfiguration { get; init; }

    /// <summary>The whole frame's length, header included.</summary>
    public int FrameLength { get; init; }

    /// <summary>The header's own length: 9 when a CRC follows the fixed fields, else 7.</summary>
    public int HeaderLength { get; init; }

    /// <summary>Raw data blocks in the frame, each 1024 samples per channel.</summary>
    public int Blocks { get; init; }

    /// <summary>Reads a header at the start of <paramref name="data"/>; false when there is none.</summary>
    public static bool TryParse(ReadOnlySpan<byte> data, out AdtsHeader header)
    {
        header = default;
        if (data.Length < 7 || data[0] != 0xFF || (data[1] & 0xF6) != 0xF0)
        {
            return false;
        }

        var rateIndex = (data[2] >> 2) & 0xF;
        var headerLength = (data[1] & 1) == 0 ? 9 : 7;
        var frameLength = ((data[3] & 3) << 11) | (data[4] << 3) | (data[5] >> 5);
        if (rateIndex >= AacConfig.SampleRates.Length || frameLength < headerLength)
        {
            return false;
        }

        header = new AdtsHeader
        {
            ObjectType = (data[2] >> 6) + 1,
            SampleRate = AacConfig.SampleRates[rateIndex],
            ChannelConfiguration = ((data[2] & 1) << 2) | (data[3] >> 6),
            FrameLength = frameLength,
            HeaderLength = headerLength,
            Blocks = (data[6] & 3) + 1,
        };
        return true;
    }

    /// <summary>The AudioSpecificConfig a decoder needs for frames with this header.</summary>
    public byte[] ToAudioSpecificConfig() => AacConfig.Build(ObjectType, SampleRate, ChannelConfiguration);
}
