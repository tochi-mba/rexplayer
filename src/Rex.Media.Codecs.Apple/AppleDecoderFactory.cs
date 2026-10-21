using Rex.Media.Primitives;

namespace Rex.Media.Codecs.Apple;

/// <summary>
/// The Mac's own decoders, the counterpart of Windows Media Foundation (ADR-0009): H.264 and HEVC
/// through VideoToolbox. Ranked as Windows' decoders are, below rexplayer's own for the codecs
/// both cover. Tracks need their parameter sets in the container (MP4's avcC and hvcC, Matroska's
/// CodecPrivate); raw Annex B streams follow.
/// </summary>
public sealed class AppleDecoderFactory : IDecoderFactory
{
    public string Name => "macOS VideoToolbox";

    public DecoderSource Source => DecoderSource.OsSoftware;

    public int Rank => 50;

    public bool CanDecode(TrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return OperatingSystem.IsMacOS() && track.Video is not null && track.CodecPrivate.Length > 0 && track.Codec is CodecId.H264 or CodecId.Hevc;
    }

    public IAudioDecoder CreateAudio(TrackInfo track) => throw new NotSupportedException($"{Name} decodes video only.");

    public IVideoDecoder CreateVideo(TrackInfo track) => OperatingSystem.IsMacOS()
        ? new VtVideoDecoder(track)
        : throw new PlatformNotSupportedException("VideoToolbox exists only on a Mac.");
}
