using Rex.Media.Primitives;

namespace Rex.Media.Codecs.Apple;

/// <summary>
/// The Mac's own decoders, the counterpart of Windows Media Foundation (ADR-0009): H.264 and HEVC
/// through VideoToolbox, AAC through AudioToolbox. Ranked as Windows' decoders are, below
/// rexplayer's own for the codecs both cover. Tracks need their configuration in the container
/// (MP4's avcC, hvcC and esds, Matroska's CodecPrivate); raw Annex B and ADTS streams follow.
/// </summary>
public sealed class AppleDecoderFactory : IDecoderFactory
{
    private readonly bool _allowHardware;
    private readonly bool _allowH264Hardware;
    private readonly bool _allowHevcHardware;

    public AppleDecoderFactory(bool allowHardware = true,
        bool allowH264Hardware = true, bool allowHevcHardware = true)
    {
        _allowHardware = allowHardware;
        _allowH264Hardware = allowH264Hardware;
        _allowHevcHardware = allowHevcHardware;
    }

    public string Name => "macOS VideoToolbox and AudioToolbox";

    public DecoderSource Source => DecoderSource.OsSoftware;

    public int Rank => 50;

    public bool CanDecode(TrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return OperatingSystem.IsMacOS() && track.CodecPrivate.Length > 0 && track.Codec switch
        {
            CodecId.H264 or CodecId.Hevc => track.Video is not null,
            CodecId.Aac => track.Audio is not null,
            _ => false,
        };
    }

    public IAudioDecoder CreateAudio(TrackInfo track) => OperatingSystem.IsMacOS()
        ? new AtAacDecoder(track)
        : throw new PlatformNotSupportedException("AudioToolbox exists only on a Mac.");

    public IVideoDecoder CreateVideo(TrackInfo track) => OperatingSystem.IsMacOS()
        ? new VtVideoDecoder(track, _allowHardware && (track.Codec switch
        {
            CodecId.H264 => _allowH264Hardware,
            CodecId.Hevc => _allowHevcHardware,
            _ => false,
        }))
        : throw new PlatformNotSupportedException("VideoToolbox exists only on a Mac.");
}
