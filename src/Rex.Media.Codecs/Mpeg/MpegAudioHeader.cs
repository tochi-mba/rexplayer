// Spec: ISO/IEC 11172-3 clause 2.4.1.3 and 2.4.2.3 (audio frame header); ISO/IEC 13818-3 clause 2.4.2.3 (lower sampling frequencies); the MPEG 2.5 extension of the version field for 8 to 12 kHz.
using Rex.Media.Primitives;

namespace Rex.Media.Codecs.Mpeg;

/// <summary>Which MPEG audio standard a frame follows; 2.5 is the de facto extension to 8, 11.025 and 12 kHz.</summary>
public enum MpegVersion
{
    Mpeg25,
    Mpeg2,
    Mpeg1,
}

/// <summary>How the channels of a frame are coded.</summary>
public enum MpegChannelMode
{
    Stereo,
    JointStereo,
    DualChannel,
    Mono,
}

/// <summary>
/// The four-byte header in front of every MPEG audio frame. It says how long the frame is, so a
/// demuxer can walk from frame to frame, and how its audio is coded. A bitrate of zero is "free
/// format": the length is constant but must be found from the distance to the next header.
/// </summary>
public readonly record struct MpegAudioHeader(
    MpegVersion Version,
    int Layer,
    bool HasCrc,
    int BitrateKbps,
    int SampleRate,
    bool Padding,
    MpegChannelMode Mode,
    int ModeExtension,
    int Emphasis)
{
    public const int Size = 4;

    private static readonly int[][] Bitrates =
    [
        [0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448],
        [0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384],
        [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320],
        [0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256],
        [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160],
    ];

    private static readonly int[] SampleRates = [44_100, 48_000, 32_000];

    public int Channels => Mode == MpegChannelMode.Mono ? 1 : 2;

    /// <summary>Whether the frame follows MPEG-2 or 2.5 ("lower sampling frequencies").</summary>
    public bool IsLowSampleRate => Version != MpegVersion.Mpeg1;

    public int SamplesPerFrame => Layer switch
    {
        1 => 384,
        2 => 1152,
        _ => IsLowSampleRate ? 576 : 1152,
    };

    /// <summary>The frame's length in bytes including this header, or 0 for free format.</summary>
    public int FrameLength => BitrateKbps == 0 ? 0 : FrameLengthAt(BitrateKbps * 1000);

    /// <summary>Bytes of Layer III side information after the header (and CRC).</summary>
    public int SideInfoLength => (IsLowSampleRate, Channels) switch
    {
        (false, 1) => 17,
        (false, _) => 32,
        (true, 1) => 9,
        _ => 17,
    };

    /// <summary>Where the frame's payload starts: after the header and the optional CRC.</summary>
    public int PayloadOffset => Size + (HasCrc ? 2 : 0);

    /// <summary>Position of <see cref="SampleRate"/> in the scalefactor band tables: 44.1, 48, 32, 22.05, 24, 16, 11.025, 12, 8 kHz.</summary>
    public int SampleRateIndex => SampleRate switch
    {
        44_100 => 0,
        48_000 => 1,
        32_000 => 2,
        22_050 => 3,
        24_000 => 4,
        16_000 => 5,
        11_025 => 6,
        12_000 => 7,
        _ => 8,
    };

    public CodecId Codec => Layer switch
    {
        1 => CodecId.Mp1,
        2 => CodecId.Mp2,
        _ => CodecId.Mp3,
    };

    /// <summary>Reads a header from the first four bytes; false for anything that is not one (including reserved values).</summary>
    public static bool TryParse(ReadOnlySpan<byte> data, out MpegAudioHeader header)
    {
        header = default;
        if (data.Length < Size || data[0] != 0xFF || (data[1] & 0xE0) != 0xE0)
        {
            return false;
        }

        var versionBits = (data[1] >> 3) & 3;
        var layerBits = (data[1] >> 1) & 3;
        var bitrateIndex = data[2] >> 4;
        var rateIndex = (data[2] >> 2) & 3;
        var emphasis = data[3] & 3;
        if (versionBits == 1 || layerBits == 0 || bitrateIndex == 15 || rateIndex == 3 || emphasis == 2)
        {
            return false;
        }

        var version = versionBits switch
        {
            0 => MpegVersion.Mpeg25,
            2 => MpegVersion.Mpeg2,
            _ => MpegVersion.Mpeg1,
        };
        var layer = 4 - layerBits;
        var table = version == MpegVersion.Mpeg1 ? layer - 1 : layer == 1 ? 3 : 4;
        var rate = SampleRates[rateIndex] >> (version switch
        {
            MpegVersion.Mpeg1 => 0,
            MpegVersion.Mpeg2 => 1,
            _ => 2,
        });
        header = new MpegAudioHeader(
            version,
            layer,
            (data[1] & 1) == 0,
            Bitrates[table][bitrateIndex],
            rate,
            (data[2] & 2) != 0,
            (MpegChannelMode)(data[3] >> 6),
            (data[3] >> 4) & 3,
            emphasis);
        return true;
    }

    /// <summary>Whether two headers can belong to one stream: same version, layer and sampling rate.</summary>
    public bool SameStreamAs(MpegAudioHeader other) => Version == other.Version && Layer == other.Layer && SampleRate == other.SampleRate;

    /// <summary>The frame length a given bitrate would give; free-format streams use it in reverse.</summary>
    public int FrameLengthAt(int bitsPerSecond)
    {
        var padding = Padding ? 1 : 0;
        return Layer == 1
            ? ((12 * bitsPerSecond / SampleRate) + padding) * 4
            : (SamplesPerFrame / 8 * bitsPerSecond / SampleRate) + padding;
    }
}
