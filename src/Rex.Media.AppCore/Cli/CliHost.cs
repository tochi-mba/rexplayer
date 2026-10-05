using Rex.Media.Audio;
using Rex.Media.Codecs;
using Rex.Media.Diagnostics;

namespace Rex.Media.AppCore.Cli;

/// <summary>
/// What the command line needs from the machine it runs on. The rexplay executable fills this with
/// the console, the Windows audio device and the Media Foundation decoders; tests fill it with
/// string writers and capture sinks.
/// </summary>
public sealed class CliHost
{
    public required TextWriter Out { get; init; }

    public required TextWriter Error { get; init; }

    public required string Version { get; init; }

    /// <summary>
    /// The sink for <c>--aout default</c>: the Windows default device in the real executable. Null
    /// means there is no device, and playback falls back to the null sink.
    /// </summary>
    public Func<IAudioSink?> DefaultAudioSink { get; init; } = () => null;

    /// <summary>Decoders only this machine has, added below rexplayer's own on the decode ladder.</summary>
    public IReadOnlyList<IDecoderFactory> ExtraDecoders { get; init; } = [];

    public RexLog Log { get; init; } = RexLog.InMemory(LogLevel.Warning);

    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>Cancelled when the user presses Ctrl+C.</summary>
    public CancellationToken Cancellation { get; init; }
}
