using System.Globalization;
using System.Text.Json.Nodes;
using Rex.Media.AppCore.Machine;
using Rex.Media.Audio;
using Rex.Media.Engine;
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.AppCore.Cli;

/// <summary>
/// The rexplay command line. In machine mode (<c>--json</c> or an <c>agent</c> command) it prints
/// exactly one JSON document on standard output, never prompts, and exits 0 on success or 1 on
/// failure; in human mode it prints plain sentences. Everything here is plain .NET, so the whole
/// command line runs in-process in tests.
/// </summary>
public static class CliApplication
{
    public static int Run(IReadOnlyList<string> args, CliHost host)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(host);
        var arguments = CliArguments.Parse(args);
        var machine = arguments.Json || arguments.Command.StartsWith("agent ", StringComparison.Ordinal);
        var command = arguments.Command.Length == 0 ? "help" : arguments.Command;
        try
        {
            return command switch
            {
                "help" => Help(arguments, host),
                "version" => Version(machine, host),
                "agent capabilities" => Capabilities(host),
                "probe" => Probe(arguments, machine, host),
                "play" => Play(arguments, machine, host),
                _ => throw new CliException($"'{command}' is not a rexplay command. Run 'rexplay help' to see them."),
            };
        }
        catch (Exception ex) when (ex is CliException or MediaFormatException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            if (machine)
            {
                host.Out.WriteLine(MachineEnvelope.Failure(command, ex.GetType().Name, ex.Message));
            }
            else
            {
                host.Error.WriteLine(ex.Message);
            }

            return 1;
        }
    }

    private static int Help(CliArguments arguments, CliHost host)
    {
        if (arguments.Positional.Count > 0)
        {
            var topic = string.Join(' ', arguments.Positional);
            var command = CliReference.Find(topic) ?? throw new CliException($"There is no command called '{topic}'.");
            host.Out.WriteLine(command.Usage);
            host.Out.WriteLine("  " + command.Summary);
            return 0;
        }

        host.Out.WriteLine("rexplay — the rexplayer command line, by REX Technologies.");
        host.Out.WriteLine();
        host.Out.WriteLine("Commands:");
        foreach (var command in CliReference.Commands)
        {
            host.Out.WriteLine($"  {command.Usage,-40} {command.Summary}");
        }

        host.Out.WriteLine();
        host.Out.WriteLine("Options:");
        foreach (var option in CliReference.Options)
        {
            host.Out.WriteLine($"  {(option.Name + " " + option.Argument).Trim(),-40} {option.Summary}");
        }

        return 0;
    }

    private static int Version(bool machine, CliHost host)
    {
        if (machine)
        {
            host.Out.WriteLine(MachineEnvelope.Success("version", new JsonObject { ["product"] = "rexplayer", ["version"] = host.Version }));
        }
        else
        {
            host.Out.WriteLine("rexplayer " + host.Version);
        }

        return 0;
    }

    private static int Capabilities(CliHost host)
    {
        var commands = new JsonArray();
        foreach (var command in CliReference.Commands)
        {
            commands.Add(new JsonObject
            {
                ["name"] = command.Name,
                ["usage"] = command.Usage,
                ["summary"] = command.Summary,
                ["machineReadable"] = command.MachineReadable,
            });
        }

        var options = new JsonArray();
        foreach (var option in CliReference.Options)
        {
            options.Add(new JsonObject { ["name"] = option.Name, ["argument"] = option.Argument, ["summary"] = option.Summary });
        }

        var containers = new JsonArray();
        foreach (var factory in MediaRegistries.Demuxers().Factories)
        {
            containers.Add(factory.Name);
        }

        var decoders = new JsonArray();
        foreach (var factory in MediaRegistries.Decoders([.. host.ExtraDecoders]).Factories)
        {
            decoders.Add(new JsonObject { ["name"] = factory.Name, ["source"] = factory.Source.ToString(), ["rank"] = factory.Rank });
        }

        host.Out.WriteLine(MachineEnvelope.Success("capabilities", new JsonObject
        {
            ["product"] = "rexplayer",
            ["version"] = host.Version,
            ["commands"] = commands,
            ["options"] = options,
            ["containers"] = containers,
            ["decoders"] = decoders,
        }));
        return 0;
    }

    private static int Probe(CliArguments arguments, bool machine, CliHost host)
    {
        var path = RequireFile(arguments);
        using var source = new FileByteSource(path);
        using var demuxer = MediaRegistries.Demuxers().Open(source, host.Cancellation);
        var info = demuxer.Info;
        if (machine)
        {
            host.Out.WriteLine(MachineEnvelope.Success("probe", Describe(path, info)));
            return 0;
        }

        host.Out.WriteLine($"{Path.GetFileName(path)}: {info.FormatName}, {info.Duration.ToClock()}");
        foreach (var track in info.Tracks)
        {
            host.Out.WriteLine("  " + DescribeTrack(track));
        }

        foreach (var (key, value) in info.Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            host.Out.WriteLine($"  {key}: {value}");
        }

        return 0;
    }

    private static int Play(CliArguments arguments, bool machine, CliHost host)
    {
        var paths = RequireFiles(arguments);
        var path = paths[0];
        var start = ParseTime(arguments.Option("--start"), "--start");
        var stop = ParseTime(arguments.Option("--stop"), "--stop");
        var volume = ParseVolume(arguments.Option("--volume"));
        var sinkFactory = ResolveSink(arguments.Option("--aout") ?? "default", host);
        var options = new EngineOptions
        {
            Demuxers = MediaRegistries.Demuxers(),
            Decoders = MediaRegistries.Decoders([.. host.ExtraDecoders]),
            AudioSinkFactory = sinkFactory,
            Log = host.Log,
            Time = host.Time,
        };

        string? failure = null;
        using var stopRequested = new ManualResetEventSlim(false);
        using (var session = new MediaSession(options, sessionEvent =>
        {
            switch (sessionEvent)
            {
                case ErrorEvent error:
                    failure = error.Message;
                    break;
                case PositionEvent position when stop is { } limit && position.Position >= limit:
                    stopRequested.Set();
                    break;
            }
        }))
        {
            session.Volume = volume;
            try
            {
                // Every file is queued at once, so each follows the one before without a gap.
                IByteSource[] following = [.. paths.Skip(1).Select(next => new FileByteSource(next))];
                session.OpenAsync(new FileByteSource(path), following, start).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is MediaFormatException or NotSupportedException or IOException or InvalidOperationException)
            {
                throw new CliException(failure ?? ex.Message);
            }

            var finished = session.WaitForFinishAsync();
            WaitHandle.WaitAny([((IAsyncResult)finished).AsyncWaitHandle, stopRequested.WaitHandle, host.Cancellation.WaitHandle]);
            var current = session.Position;
            var stats = session.GetStatsAsync().GetAwaiter().GetResult();
            var position = stats.EarlierItemsDuration + current;
            var state = session.State;
            var info = session.Info!;
            session.StopAsync().GetAwaiter().GetResult();
            if (state == SessionState.Faulted)
            {
                throw new CliException(failure ?? "Playback failed.");
            }

            if (machine)
            {
                var data = Describe(path, info);
                data["played"] = position.TotalSeconds;
                data["items"] = stats.ItemsStarted;
                data["finished"] = state == SessionState.Ended;
                data["stats"] = new JsonObject
                {
                    ["packetsRead"] = stats.PacketsRead,
                    ["bytesRead"] = stats.BytesRead,
                    ["audioFramesDecoded"] = stats.AudioFramesDecoded,
                    ["audioSamplesPlayed"] = stats.AudioSamplesPlayed,
                    ["corruptPackets"] = stats.CorruptPackets,
                    ["audioDecoder"] = stats.AudioDecoder,
                };
                host.Out.WriteLine(MachineEnvelope.Success("play", data));
            }
            else
            {
                host.Out.WriteLine(paths.Count == 1
                    ? $"Played {Path.GetFileName(path)}: {position.ToClock()} of {info.Duration.ToClock()}."
                    : $"Played {stats.ItemsStarted} of {paths.Count} files: {position.ToClock()}.");
            }
        }

        return 0;
    }

    private static Func<IAudioSink> ResolveSink(string value, CliHost host)
    {
        if (value == "default")
        {
            return () => host.DefaultAudioSink() ?? new NullAudioSink(host.Time);
        }

        if (value == "null")
        {
            return () => new NullAudioSink(host.Time);
        }

        if (value.StartsWith("wav:", StringComparison.Ordinal) && value.Length > 4)
        {
            var path = value[4..];
            return () => new WavFileSink(path);
        }

        throw new CliException($"'{value}' is not an audio output. Use default, null or wav:<path>.");
    }

    /// <summary>Every named file, each of which must exist.</summary>
    private static IReadOnlyList<string> RequireFiles(CliArguments arguments)
    {
        RequireFile(arguments);
        foreach (var path in arguments.Positional)
        {
            if (!File.Exists(path))
            {
                throw new CliException($"There is no file at {path}.");
            }
        }

        return arguments.Positional;
    }

    private static string RequireFile(CliArguments arguments)
    {
        if (arguments.Positional.Count == 0)
        {
            throw new CliException("Name a file to open.");
        }

        var path = arguments.Positional[0];
        if (!File.Exists(path))
        {
            throw new CliException($"There is no file at {path}.");
        }

        return path;
    }

    private static MediaTime? ParseTime(string? text, string option)
    {
        if (text is null)
        {
            return null;
        }

        return MediaTime.TryParseClock(text, out var time)
            ? time
            : throw new CliException($"{option} takes a time such as 90, 1:30 or 1:02:03.5, not '{text}'.");
    }

    private static double ParseVolume(string? text)
    {
        if (text is null)
        {
            return 1.0;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) && percent is >= 0 and <= 200
            ? percent / 100
            : throw new CliException($"--volume takes a percentage from 0 to 200, not '{text}'.");
    }

    private static JsonObject Describe(string path, MediaInfo info)
    {
        var tracks = new JsonArray();
        foreach (var track in info.Tracks)
        {
            var node = new JsonObject
            {
                ["id"] = track.Id,
                ["kind"] = track.Kind.ToString().ToLowerInvariant(),
                ["codec"] = track.Codec.ToString(),
                ["codecName"] = track.Codec.DisplayName(),
            };
            if (track.Audio is { } audio)
            {
                node["sampleRate"] = audio.SampleRate;
                node["channels"] = audio.Channels;
                node["bitsPerSample"] = audio.BitsPerSample;
            }

            tracks.Add(node);
        }

        var metadata = new JsonObject();
        foreach (var (key, value) in info.Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            metadata[key] = value;
        }

        return new JsonObject
        {
            ["file"] = Path.GetFileName(path),
            ["format"] = info.FormatName,
            ["duration"] = info.Duration.IsKnown ? info.Duration.TotalSeconds : null,
            ["tracks"] = tracks,
            ["metadata"] = metadata,
        };
    }

    private static string DescribeTrack(TrackInfo track)
    {
        var text = $"#{track.Id} {track.Kind.ToString().ToLowerInvariant()}: {track.Codec.DisplayName()}";
        if (track.Audio is { } audio)
        {
            text += string.Create(CultureInfo.InvariantCulture, $", {audio.SampleRate} Hz, {audio.Layout.Describe(audio.Channels)}");
            if (audio.BitsPerSample > 0)
            {
                text += string.Create(CultureInfo.InvariantCulture, $", {audio.BitsPerSample}-bit");
            }
        }

        return text;
    }
}

/// <summary>A command-line mistake, explained in a sentence that says what to do instead.</summary>
public sealed class CliException : Exception
{
    public CliException()
    {
    }

    public CliException(string message)
        : base(message)
    {
    }

    public CliException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
