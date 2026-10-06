using Rex.Media.Codecs;
using Rex.Media.Primitives;

namespace Rex.Media.AppCore;

/// <summary>Which decoders Windows offers for one codec: in software, and on the graphics card.</summary>
public sealed record WindowsDecoders(CodecId Codec, IReadOnlyList<string> Software, IReadOnlyList<string> Hardware);

/// <summary>
/// What this machine gives rexplayer to play with (TOOL-11): Windows' decoders per codec, the
/// graphics adapter and the default audio output. The host gathers it; the command line reports it
/// alongside the codecs rexplayer decodes itself, so a missing extension is plain to see.
/// </summary>
public sealed record SystemReport
{
    public required string Windows { get; init; }

    public IReadOnlyList<WindowsDecoders> Decoders { get; init; } = [];

    /// <summary>The graphics adapter pictures are drawn with, or null when none would start.</summary>
    public string? Graphics { get; init; }

    public bool GraphicsInSoftware { get; init; }

    /// <summary>The default audio output's mix format, or null when no output is plugged in.</summary>
    public AudioFormat? AudioOutput { get; init; }

    /// <summary>The codecs rexplayer decodes with its own code, from the shipped decoders.</summary>
    public static IReadOnlyList<CodecId> OwnCodecs()
    {
        var decoders = MediaRegistries.Decoders();
        return [.. Enum.GetValues<CodecId>().Where(codec => codec != CodecId.Unknown && decoders.Factories.Any(factory => factory.Source == DecoderSource.Own && factory.CanDecode(Probe(codec))))];
    }

    /// <summary>A track of a codec as a decoder factory is asked about it.</summary>
    private static TrackInfo Probe(CodecId codec) => new()
    {
        Id = 1,
        Codec = codec,
        Audio = new AudioTrackInfo { SampleRate = 48_000, Channels = 2, BitsPerSample = 16, PcmFormat = SampleFormat.S16, BlockAlign = 4, SamplesPerBlock = 1 },
    };
}
