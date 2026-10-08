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
        CodecId.Opus => PInvoke.MFAudioFormat_Opus,
        _ => null,
    };

    /// <summary>The Media Foundation subtype for a video codec, or null when Windows is not asked to decode it.</summary>
    internal static Guid? VideoSubtype(CodecId codec) => codec switch
    {
        CodecId.H264 => PInvoke.MFVideoFormat_H264,
        CodecId.Hevc => PInvoke.MFVideoFormat_HEVC,
        _ => null,
    };

    /// <summary>The codecs Windows is asked about for a system report, with the transform category and major type each needs.</summary>
    private static readonly (CodecId Codec, bool Video, Guid Subtype)[] Surveyed =
    [
        (CodecId.Aac, false, PInvoke.MFAudioFormat_AAC),
        (CodecId.Ac3, false, PInvoke.MFAudioFormat_Dolby_AC3),
        (CodecId.Eac3, false, PInvoke.MFAudioFormat_Dolby_DDPlus),
        (CodecId.Mp3, false, PInvoke.MFAudioFormat_MP3),
        (CodecId.Flac, false, PInvoke.MFAudioFormat_FLAC),
        (CodecId.Alac, false, PInvoke.MFAudioFormat_ALAC),
        (CodecId.Opus, false, PInvoke.MFAudioFormat_Opus),
        (CodecId.Wma, false, PInvoke.MFAudioFormat_WMAudioV8),
        (CodecId.H264, true, PInvoke.MFVideoFormat_H264),
        (CodecId.Hevc, true, PInvoke.MFVideoFormat_HEVC),
        (CodecId.Vp9, true, PInvoke.MFVideoFormat_VP90),
        (CodecId.Av1, true, PInvoke.MFVideoFormat_AV1),
        (CodecId.Mpeg2Video, true, PInvoke.MFVideoFormat_MPEG2),
        (CodecId.Vc1, true, PInvoke.MFVideoFormat_WVC1),
    ];

    /// <summary>For each codec a system report covers, the names of Windows' software and graphics-card decoders.</summary>
    public static IReadOnlyList<(CodecId Codec, IReadOnlyList<string> Software, IReadOnlyList<string> Hardware)> Survey() =>
    [
        .. Surveyed.Select(entry =>
        {
            var (category, major) = entry.Video
                ? (PInvoke.MFT_CATEGORY_VIDEO_DECODER, PInvoke.MFMediaType_Video)
                : (PInvoke.MFT_CATEGORY_AUDIO_DECODER, PInvoke.MFMediaType_Audio);
            return (entry.Codec, MfTransform.Names(category, major, entry.Subtype), MfTransform.Names(category, major, entry.Subtype, hardware: true));
        }),
    ];

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

    public IVideoDecoder CreateVideo(TrackInfo track, object? gpu) => new MfVideoDecoder(track, gpu);
}
