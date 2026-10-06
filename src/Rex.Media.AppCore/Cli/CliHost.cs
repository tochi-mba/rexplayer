using Rex.Media.Audio;
using Rex.Media.Codecs;
using Rex.Media.Diagnostics;
using Rex.Media.Video;

namespace Rex.Media.AppCore.Cli;

/// <summary>
/// What the command line needs from the machine it runs on. The rexplay executable fills this with
/// the console, the Windows audio device, the Media Foundation decoders and a Direct3D window; tests fill it with
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

    /// <summary>Describes the machine for probe --system, or gives null when this host cannot.</summary>
    public Func<SystemReport?> SystemReport { get; init; } = () => null;

    /// <summary>Opens a window with this title for pictures, or gives null when this host has no windows.</summary>
    public Func<string, IVideoPresenter?> VideoWindow { get; init; } = _ => null;

    public RexLog Log { get; init; } = RexLog.InMemory(LogLevel.Warning);

    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>Cancelled when the user presses Ctrl+C.</summary>
    public CancellationToken Cancellation { get; init; }
}
