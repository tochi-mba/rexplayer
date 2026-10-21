// Spec: ISO/IEC 14496-1 (MPEG-4 Systems) sections 7.2.6.5 "ES_Descriptor", 7.2.6.6 "DecoderConfigDescriptor", 7.2.6.7 "DecoderSpecificInfo", 7.2.6.8 "SLConfigDescriptor" and 8.3.3 (expandable size field); objectTypeIndication 0x40 (Audio ISO/IEC 14496-3) and streamType 0x05 (AudioStream) from table 5 and table 6.
namespace Rex.Media.Codecs.Aac;

/// <summary>
/// An MPEG-4 ES_Descriptor wrapped round an AAC stream's AudioSpecificConfig: the form a decoder
/// that reads MP4's esds box wants its configuration in (macOS's AudioToolbox calls it the
/// magic cookie). Only the fields a decoder reads are set: the stream type, the object type and the
/// decoder-specific information; buffer sizes, bit rates and the stream id are zero, and the sync
/// layer is the predefined MP4 one.
/// </summary>
public static class EsDescriptor
{
    private const byte EsDescriptorTag = 0x03;
    private const byte DecoderConfigTag = 0x04;
    private const byte DecoderSpecificInfoTag = 0x05;
    private const byte SyncLayerConfigTag = 0x06;
    private const byte AudioObjectType = 0x40;

    /// <summary>streamType 5 (audio) in the top six bits, upStream 0, then the reserved bit set.</summary>
    private const byte AudioStream = (0x05 << 2) | 1;

    /// <summary>predefined = 2: the sync layer MP4 files use, needing no further fields.</summary>
    private const byte Mp4SyncLayer = 0x02;

    /// <summary>The ES_Descriptor for an AAC track whose AudioSpecificConfig is <paramref name="audioSpecificConfig"/>.</summary>
    public static byte[] ForAac(ReadOnlySpan<byte> audioSpecificConfig)
    {
        byte[] decoderSpecific = [DecoderSpecificInfoTag, .. Size(audioSpecificConfig.Length), .. audioSpecificConfig];
        byte[] decoderConfig =
        [
            AudioObjectType, AudioStream,
            0, 0, 0, // bufferSizeDB
            0, 0, 0, 0, // maxBitrate
            0, 0, 0, 0, // avgBitrate
            .. decoderSpecific,
        ];
        byte[] syncLayer = [SyncLayerConfigTag, 1, Mp4SyncLayer];
        byte[] body =
        [
            0, 0, // ES_ID
            0, // no dependency, URL or OCR stream; priority 0
            DecoderConfigTag, .. Size(decoderConfig.Length), .. decoderConfig,
            .. syncLayer,
        ];
        return [EsDescriptorTag, .. Size(body.Length), .. body];
    }

    /// <summary>A descriptor's size in the expandable form: seven bits a byte, the top bit set on every byte but the last.</summary>
    internal static byte[] Size(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        var bytes = new List<byte> { (byte)(length & 0x7F) };
        for (length >>= 7; length > 0; length >>= 7)
        {
            bytes.Insert(0, (byte)(0x80 | (length & 0x7F)));
        }

        return [.. bytes];
    }
}
