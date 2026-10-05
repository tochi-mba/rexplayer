namespace Rex.Media.Primitives;

/// <summary>
/// One elementary stream inside a container: what it is, how to decode it and what to call it in the
/// track menus. Demuxers fill these in from headers; nothing here is guessed from the payload.
/// </summary>
public sealed record TrackInfo
{
    /// <summary>The container's identifier for the stream, unique within one file.</summary>
    public required int Id { get; init; }

    public required CodecId Codec { get; init; }

    public MediaKind Kind => Codec.Kind();

    /// <summary>Decoder configuration from the container (avcC, ASC, FLAC STREAMINFO, ...), or empty.</summary>
    public byte[] CodecPrivate { get; init; } = [];

    public MediaTime Duration { get; init; } = MediaTime.Unknown;

    /// <summary>An ISO 639 language code as the container gave it, or null.</summary>
    public string? Language { get; init; }

    public string? Title { get; init; }

    public bool IsDefault { get; init; }

    public bool IsForced { get; init; }

    /// <summary>Average bits per second, when the container records it.</summary>
    public long? BitRate { get; init; }

    public AudioTrackInfo? Audio { get; init; }

    public VideoTrackInfo? Video { get; init; }
}

/// <summary>Audio parameters a decoder needs before the first packet.</summary>
public sealed record AudioTrackInfo
{
    public required int SampleRate { get; init; }

    public required int Channels { get; init; }

    public ChannelLayout Layout { get; init; }

    /// <summary>Bits per stored sample for PCM; the source precision for lossless codecs; 0 if not meaningful.</summary>
    public int BitsPerSample { get; init; }

    /// <summary>For PCM: how samples are stored.</summary>
    public SampleFormat PcmFormat { get; init; }

    public bool BigEndian { get; init; }

    /// <summary>Bytes per block for PCM and ADPCM.</summary>
    public int BlockAlign { get; init; }

    /// <summary>Samples per channel per block for block-based codecs such as ADPCM; 0 if not fixed.</summary>
    public int SamplesPerBlock { get; init; }

    /// <summary>Samples the decoder must discard from the start (encoder delay), for gapless playback.</summary>
    public int LeadingPadding { get; init; }

    /// <summary>Samples to discard from the end (encoder padding), for gapless playback.</summary>
    public int TrailingPadding { get; init; }
}

/// <summary>Video parameters a decoder and the renderer need before the first packet.</summary>
public sealed record VideoTrackInfo
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    /// <summary>The shape of one pixel; 1:1 for square pixels.</summary>
    public Rational PixelAspect { get; init; } = new(1, 1);

    /// <summary>Nominal frames per second when the container states it.</summary>
    public Rational? FrameRate { get; init; }

    /// <summary>Clockwise rotation the picture should be displayed with: 0, 90, 180 or 270.</summary>
    public int Rotation { get; init; }

    public ColorInfo Color { get; init; } = ColorInfo.Unspecified;
}
