namespace Rex.Media.Primitives;

/// <summary>Every elementary-stream format the engine can name, decoded or not.</summary>
public enum CodecId
{
    Unknown = 0,

    // Audio
    Pcm,
    AdpcmIma,
    AdpcmMs,
    Alaw,
    Mulaw,
    Mp1,
    Mp2,
    Mp3,
    Aac,
    Ac3,
    Eac3,
    Dts,
    TrueHd,
    Flac,
    Alac,
    Vorbis,
    Opus,
    Wma,
    WmaPro,
    WmaLossless,
    AmrNb,
    AmrWb,
    Speex,

    // Video
    H264,
    Hevc,
    Vvc,
    Av1,
    Vp8,
    Vp9,
    Mpeg1Video,
    Mpeg2Video,
    Mpeg4Part2,
    H263,
    Vc1,
    Wmv1,
    Wmv2,
    Wmv3,
    Mjpeg,
    RawVideo,
    Theora,
    Dv,

    /// <summary>A still picture in a format Windows' imaging reads (JPEG, PNG, BMP, TIFF, WebP, HEIF), told apart by its bytes.</summary>
    Picture,

    /// <summary>GIF pictures, still or animated.</summary>
    Gif,

    // Subtitles
    SubRip,
    WebVtt,
    Ass,
    Ssa,
    MovText,
    VobSub,
    Pgs,
    DvbSubtitle,
    Cea608,
    PlainText,
}

/// <summary>What a stream carries.</summary>
public enum MediaKind
{
    Unknown = 0,
    Audio,
    Video,
    Subtitle,
    Data,
}

public static class CodecIdExtensions
{
    public static MediaKind Kind(this CodecId codec) => codec switch
    {
        >= CodecId.Pcm and <= CodecId.Speex => MediaKind.Audio,
        >= CodecId.H264 and <= CodecId.Gif => MediaKind.Video,
        >= CodecId.SubRip and <= CodecId.PlainText => MediaKind.Subtitle,
        _ => MediaKind.Unknown,
    };

    /// <summary>The name shown in Media Information and the stats overlay.</summary>
    public static string DisplayName(this CodecId codec) => codec switch
    {
        CodecId.Pcm => "PCM",
        CodecId.AdpcmIma => "IMA ADPCM",
        CodecId.AdpcmMs => "Microsoft ADPCM",
        CodecId.Alaw => "G.711 A-law",
        CodecId.Mulaw => "G.711 µ-law",
        CodecId.Mp1 => "MPEG-1 Layer I",
        CodecId.Mp2 => "MPEG-1 Layer II",
        CodecId.Mp3 => "MP3",
        CodecId.Aac => "AAC",
        CodecId.Ac3 => "Dolby Digital (AC-3)",
        CodecId.Eac3 => "Dolby Digital Plus (E-AC-3)",
        CodecId.Dts => "DTS",
        CodecId.TrueHd => "Dolby TrueHD",
        CodecId.Flac => "FLAC",
        CodecId.Alac => "Apple Lossless",
        CodecId.Vorbis => "Vorbis",
        CodecId.Opus => "Opus",
        CodecId.Wma => "Windows Media Audio",
        CodecId.WmaPro => "Windows Media Audio Professional",
        CodecId.WmaLossless => "Windows Media Audio Lossless",
        CodecId.AmrNb => "AMR narrowband",
        CodecId.AmrWb => "AMR wideband",
        CodecId.Speex => "Speex",
        CodecId.H264 => "H.264 / AVC",
        CodecId.Hevc => "H.265 / HEVC",
        CodecId.Vvc => "H.266 / VVC",
        CodecId.Av1 => "AV1",
        CodecId.Vp8 => "VP8",
        CodecId.Vp9 => "VP9",
        CodecId.Mpeg1Video => "MPEG-1 video",
        CodecId.Mpeg2Video => "MPEG-2 video",
        CodecId.Mpeg4Part2 => "MPEG-4 Part 2",
        CodecId.H263 => "H.263",
        CodecId.Vc1 => "VC-1",
        CodecId.Wmv1 => "Windows Media Video 7",
        CodecId.Wmv2 => "Windows Media Video 8",
        CodecId.Wmv3 => "Windows Media Video 9",
        CodecId.Mjpeg => "Motion JPEG",
        CodecId.RawVideo => "Uncompressed video",
        CodecId.Theora => "Theora",
        CodecId.Dv => "DV",
        CodecId.Picture => "Picture",
        CodecId.Gif => "GIF",
        CodecId.SubRip => "SubRip",
        CodecId.WebVtt => "WebVTT",
        CodecId.Ass => "Advanced SubStation Alpha",
        CodecId.Ssa => "SubStation Alpha",
        CodecId.MovText => "MP4 timed text",
        CodecId.VobSub => "DVD subtitles",
        CodecId.Pgs => "Blu-ray subtitles (PGS)",
        CodecId.DvbSubtitle => "DVB subtitles",
        CodecId.Cea608 => "CEA-608 captions",
        CodecId.PlainText => "Text",
        _ => "Unknown",
    };
}
