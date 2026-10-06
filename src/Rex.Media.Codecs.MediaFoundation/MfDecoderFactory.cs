using Rex.Media.Interop.MediaFoundation;
using Rex.Media.Primitives;
using Windows.Win32;

namespace Rex.Media.Codecs.MediaFoundation;

/// <summary>
/// The decode ladder's Windows rung: the codecs rexplayer leaves to Windows (AAC, AC-3, E-AC-3,
/// H.264, HEVC, VP9, AV1) through the Media Foundation decoder transforms. It ranks below
/// rexplayer's own decoders, so it is only reached for codecs they do not cover (ADR-009).
/// </summary>
public sealed class MfDecoderFactory : IDecoderFactory
{
    private readonly Dictionary<Guid, bool> _available = [];

    public string Name => "Windows Media Foundation";

    public DecoderSource Source => DecoderSource.OsSoftware;

    public int Rank => 50;

    /// <summary>The Media Foundation subtype for an audio codec, or null when Windows is not asked to decode it.</summary>
    internal static Guid? AudioSubtype(CodecId codec) => codec switch
    {
        CodecId.Aac => PInvoke.MFAudioFormat_AAC,
        CodecId.Ac3 => PInvoke.MFAudioFormat_Dolby_AC3,
        CodecId.Eac3 => PInvoke.MFAudioFormat_Dolby_DDPlus,
        _ => null,
    };

    /// <summary>The Media Foundation subtype for a video codec, or null when Windows is not asked to decode it.</summary>
    internal static Guid? VideoSubtype(CodecId codec) => codec switch
    {
        CodecId.H264 => PInvoke.MFVideoFormat_H264,
        CodecId.Hevc => PInvoke.MFVideoFormat_HEVC,
        _ => null,
    };

    public bool CanDecode(TrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(track);
        var (category, major, subtype) = track switch
        {
            { Audio: not null } when AudioSubtype(track.Codec) is { } audio => (PInvoke.MFT_CATEGORY_AUDIO_DECODER, PInvoke.MFMediaType_Audio, audio),
            { Video: not null } when VideoSubtype(track.Codec) is { } video => (PInvoke.MFT_CATEGORY_VIDEO_DECODER, PInvoke.MFMediaType_Video, video),
            _ => (Guid.Empty, Guid.Empty, Guid.Empty),
        };
        if (subtype == Guid.Empty)
        {
            return false;
        }

        lock (_available)
        {
            if (!_available.TryGetValue(subtype, out var found))
            {
                found = MfTransform.Names(category, major, subtype).Count > 0;
                _available[subtype] = found;
            }

            return found;
        }
    }

    public IAudioDecoder CreateAudio(TrackInfo track) => new MfAudioDecoder(track);

    public IVideoDecoder CreateVideo(TrackInfo track) => new MfVideoDecoder(track);
}
