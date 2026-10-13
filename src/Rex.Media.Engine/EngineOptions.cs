using Rex.Media.Audio;
using Rex.Media.Codecs;
using Rex.Media.Containers;
using Rex.Media.Diagnostics;
using Rex.Media.Video;

namespace Rex.Media.Engine;

/// <summary>Everything a media session is built from. The host (CLI, app, tests) chooses the parts.</summary>
public sealed class EngineOptions
{
    public required DemuxerRegistry Demuxers { get; init; }

    public required DecoderRegistry Decoders { get; init; }

    /// <summary>Creates the audio output for a session: a WASAPI endpoint, a WAV capture, or the null sink.</summary>
    public required Func<IAudioSink> AudioSinkFactory { get; init; }

    public RexLog Log { get; init; } = RexLog.InMemory();

    /// <summary>
    /// Where the session marks how long opening, the first sound and the first picture took (the
    /// timing lines of the log); null marks nothing.
    /// </summary>
    public Milestones? Milestones { get; init; }

    public TimeProvider Time { get; init; } = TimeProvider.System;

    public ResamplerQuality ResamplerQuality { get; init; } = ResamplerQuality.Normal;

    /// <summary>Packets queued between the demuxer and the audio decoder; roughly three seconds of PCM.</summary>
    public int AudioQueueCapacity { get; init; } = 64;

    /// <summary>
    /// Makes the window or recorder pictures are shown in, or null to play sound only. Video packets
    /// are only decoded when this is set.
    /// </summary>
    public Func<IVideoPresenter>? VideoPresenterFactory { get; init; }

    /// <summary>
    /// Whether video decoders may decode on the graphics card behind the presenter. Off, every
    /// picture is decoded by the processor, as on a computer whose graphics card cannot decode.
    /// </summary>
    public bool GraphicsCardDecoding { get; init; } = true;

    /// <summary>Packets waiting for the video decoder. The queue may hold up to four times this while audio is starving.</summary>
    public int VideoQueueCapacity { get; init; } = 32;

    /// <summary>Start playing as soon as the media opens.</summary>
    public bool AutoPlay { get; init; } = true;

    /// <summary>Consecutive undecodable packets after which a track is given up on (ADR-007 error policy).</summary>
    public int MaxConsecutiveCorruptPackets { get; init; } = 30;

    /// <summary>How often position events are posted while playing.</summary>
    public TimeSpan PositionInterval { get; init; } = TimeSpan.FromMilliseconds(100);
}

/// <summary>How exactly a seek lands.</summary>
public enum SeekMode
{
    /// <summary>On the exact sample and frame asked for: decode from the keyframe and discard up to the target.</summary>
    Precise,

    /// <summary>On the nearest keyframe at or before the target: fast, for scrubbing.</summary>
    Keyframe,
}
