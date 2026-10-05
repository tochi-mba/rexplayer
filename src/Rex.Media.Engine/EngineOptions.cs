using Rex.Media.Audio;
using Rex.Media.Codecs;
using Rex.Media.Containers;
using Rex.Media.Diagnostics;

namespace Rex.Media.Engine;

/// <summary>Everything a media session is built from. The host (CLI, app, tests) chooses the parts.</summary>
public sealed class EngineOptions
{
    public required DemuxerRegistry Demuxers { get; init; }

    public required DecoderRegistry Decoders { get; init; }

    /// <summary>Creates the audio output for a session: a WASAPI endpoint, a WAV capture, or the null sink.</summary>
    public required Func<IAudioSink> AudioSinkFactory { get; init; }

    public RexLog Log { get; init; } = RexLog.InMemory();

    public TimeProvider Time { get; init; } = TimeProvider.System;

    public ResamplerQuality ResamplerQuality { get; init; } = ResamplerQuality.Normal;

    /// <summary>Packets queued between the demuxer and the audio decoder; roughly three seconds of PCM.</summary>
    public int AudioQueueCapacity { get; init; } = 64;

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
