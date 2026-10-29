// Spec: ISO/IEC 14496-3 section 1.6.2.1 (AudioSpecificConfig: core rate, SBR output rate and frame length); RFC 7845 section 5.1 (the Opus identification header, 48 kHz output, channel mapping families 0 and 1) and RFC 6716 section 3.1 (frames per packet from the TOC byte).
using Rex.Media.Codecs.Aac;
using Rex.Media.Codecs.Opus;
using Rex.Media.Primitives;

namespace Rex.Media.Codecs;

/// <summary>The compressed formats the Mac's AudioToolbox decodes for rexplayer.</summary>
public enum AudioToolboxFormat
{
    Aac,
    Opus,
}

/// <summary>
/// How a track is handed to an AudioToolbox decoder (ADR-0008: the decision lives here, the Mac
/// adapter only follows it): the format, the rate it is coded at and the rate it plays at, the
/// channels, the samples in a packet (zero where packets vary), and the configuration bytes the
/// decoder takes, AAC's ES_Descriptor or Opus's identification header with its pre-skip given as none.
/// </summary>
public sealed record AudioToolboxSetup(AudioToolboxFormat Format, int CodedRate, int OutputRate, int Channels, int FramesPerPacket, byte[] Cookie)
{
    /// <summary>Opus always decodes at 48 kHz, whatever rate the encoder was given (RFC 7845 section 5.1).</summary>
    public const int OpusRate = 48_000;

    /// <summary>The longest Opus packet: 120 ms at 48 kHz (RFC 6716 section 3.1).</summary>
    public const int LongestOpusPacket = 5_760;

    /// <summary>The samples in the longest packet this setup can produce, for sizing the output buffer.</summary>
    public int LongestPacket => FramesPerPacket > 0 ? FramesPerPacket : LongestOpusPacket;

    /// <summary>The setup for <paramref name="track"/>; throws when AudioToolbox is not given such a track.</summary>
    public static AudioToolboxSetup For(TrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return track.Codec switch
        {
            CodecId.Aac => Aac(track),
            CodecId.Opus => Opus(track),
            _ => throw new MediaFormatException($"AudioToolbox is not asked to decode {track.Codec}."),
        };
    }

    /// <summary>The samples in one packet as the decoder must be told, or zero where the format fixes it.</summary>
    public int SamplesIn(ReadOnlySpan<byte> packet) => Format == AudioToolboxFormat.Opus ? OpusPacket.Samples(packet) : 0;

    /// <summary>
    /// When the <paramref name="frames"/> samples AudioToolbox gave for a packet of
    /// <paramref name="packetSamples"/> starting at <paramref name="packetStart"/> begin. Its Opus
    /// decoder keeps back the first samples of the first packet after it opens or is reset (its own
    /// 2.5 ms delay) and gives the rest, so what comes out ends where the packet ends: timed from
    /// the packet's start, every sample after would play that much early. AAC packets come out whole.
    /// </summary>
    public MediaTime OutputStart(MediaTime packetStart, int packetSamples, int frames) =>
        Format == AudioToolboxFormat.Opus && packetStart.IsKnown && packetSamples > 0
            ? packetStart + MediaTime.FromSamples(packetSamples - frames, OutputRate)
            : packetStart;

    private static AudioToolboxSetup Aac(TrackInfo track)
    {
        if (!AacConfig.TryParse(track.CodecPrivate, out var config))
        {
            throw new MediaFormatException("The track is not AAC with its configuration.");
        }

        var channels = config.Ps ? 2 : Math.Max(config.Channels, track.Audio?.Channels ?? 0);
        if (channels <= 0)
        {
            throw new MediaFormatException("The AAC configuration names no channel count.");
        }

        var output = config.OutputSampleRate > 0 ? config.OutputSampleRate : config.SampleRate;
        var frames = config.FrameLength * (output > config.SampleRate ? 2 : 1);
        return new AudioToolboxSetup(AudioToolboxFormat.Aac, config.SampleRate, output, channels, frames, EsDescriptor.ForAac(track.CodecPrivate));
    }

    private static AudioToolboxSetup Opus(TrackInfo track)
    {
        if (track.CodecPrivate.Length == 0)
        {
            throw new MediaFormatException("The Opus track carries no identification header.");
        }

        var head = OpusHead.Parse(track.CodecPrivate);
        if (head.MappingFamily > 1 || head.Channels is < 1 or > 2)
        {
            throw new MediaFormatException($"AudioToolbox is given mono and stereo Opus; this stream has {head.Channels} channels in mapping family {head.MappingFamily}.");
        }

        // AudioToolbox leaves the pre-skip out itself, but the packets' times already put those
        // samples before zero, where playback leaves them out; told of it as well, the decoder would
        // drop them a second time and every sample after would play early. Windows is given none too.
        var cookie = track.CodecPrivate.ToArray();
        cookie[10] = cookie[11] = 0;
        return new AudioToolboxSetup(AudioToolboxFormat.Opus, OpusRate, OpusRate, head.Channels, 0, cookie);
    }
}
