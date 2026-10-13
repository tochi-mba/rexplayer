using System.Globalization;
using System.Text.Json.Nodes;
using Rex.Media.AppCore.Machine;
using Rex.Media.AppCore.Visuals;
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.AppCore.Cli;

/// <summary>
/// What the visualisations hear in a file (AU-18): its sound decoded, as fast as it decodes, and
/// heard by <see cref="MusicPulse"/> thirty frames a second (or as many as <c>--fps</c> asks), as the
/// window hears it while playing.
/// </summary>
public static partial class CliApplication
{
    /// <summary>Frames a second the music is heard at, as the window draws.</summary>
    private const int PulseFps = 30;

    private static int Pulse(CliArguments arguments, bool machine, CliHost host)
    {
        var path = Path.GetFullPath(RequireFile(arguments));
        var start = ParseTime(arguments.Option("--start"), "--start")?.ToTimeSpan() ?? TimeSpan.Zero;
        var stop = ParseTime(arguments.Option("--stop"), "--stop")?.ToTimeSpan();
        var fps = ParseFps(arguments.Option("--fps"));
        var (mono, rate) = DecodeMono(path, host);
        var first = (int)Math.Min(mono.Length, start.TotalSeconds * rate);
        var last = (int)Math.Min(mono.Length, stop is { } end ? end.TotalSeconds * rate : mono.Length);
        if (last - first < rate / fps || rate == 0)
        {
            throw new CliException($"There is no sound to hear in {Path.GetFileName(path)} at those times.");
        }

        var pulse = new MusicPulse();
        var window = new float[MusicPulse.AnalysisSize];
        var beats = new JsonArray();
        var kicks = new JsonArray();
        var tempos = new JsonArray();
        var (bass, punch, loudness, times) = (new List<double>(), new List<double>(), new List<double>(), new List<double>());
        double? lockedAt = null;
        var (heard, filled) = (0, 0);
        var frames = (int)((long)(last - first) * fps / rate);
        for (var frame = 1; frame <= frames; frame++)
        {
            host.Cancellation.ThrowIfCancellationRequested();
            var endSample = first + (int)((long)frame * rate / fps);
            for (var i = 0; i < window.Length; i++)
            {
                var at = endSample - window.Length + i;
                window[i] = at >= first ? mono[at] : 0;
            }

            pulse.Update(window, rate, TimeSpan.FromSeconds(1.0 / fps));
            var t = start.TotalSeconds + ((double)frame / fps);
            bass.Add(pulse.Bass);
            punch.Add(pulse.Punch);
            loudness.Add(pulse.Loudness);
            lockedAt ??= pulse.Locked ? t : null;
            if (pulse.Kick.Hit)
            {
                kicks.Add(new JsonObject { ["at"] = Round(t), ["strength"] = Round(pulse.Kick.Strength) });
            }

            if (pulse.Beat)
            {
                (heard, filled) = pulse.Filled ? (heard, filled + 1) : (heard + 1, filled);
                times.Add(t);
                beats.Add(new JsonObject { ["at"] = Round(t), ["strength"] = Round(pulse.BeatStrength), ["filled"] = pulse.Filled });
            }

            if (frame % (fps * 5) == 0)
            {
                tempos.Add(new JsonObject { ["at"] = Round(t), ["bpm"] = Round(pulse.Tempo), ["locked"] = pulse.Locked });
            }
        }

        // How even the beats are while the clock is locked: the spread of the gaps between them against their mean.
        var gaps = times.Zip(times.Skip(1), (a, b) => b - a).Where((_, i) => lockedAt is { } locked && times[i] >= locked).ToList();
        var mean = gaps.Count > 0 ? gaps.Average() : 0;
        var evenness = gaps.Count > 1 && mean > 0 ? Math.Sqrt(gaps.Sum(gap => (gap - mean) * (gap - mean)) / gaps.Count) / mean : 0;
        var data = new JsonObject
        {
            ["file"] = path,
            ["sampleRate"] = rate,
            ["fps"] = fps,
            ["from"] = Round(start.TotalSeconds),
            ["seconds"] = Round((double)(last - first) / rate),
            ["tempo"] = Round(pulse.Tempo),
            ["lockedAt"] = lockedAt is { } when ? Round(when) : null,
            ["beatsHeard"] = heard,
            ["beatsFilled"] = filled,
            ["beatGapSpread"] = Round(evenness),
            ["levels"] = new JsonObject { ["bass"] = Percentiles(bass), ["punch"] = Percentiles(punch), ["loudness"] = Percentiles(loudness) },
            ["tempoOverTime"] = tempos,
            ["beats"] = beats,
            ["kicks"] = kicks,
        };
        if (machine)
        {
            host.Out.WriteLine(MachineEnvelope.Success("pulse", data));
        }
        else
        {
            host.Out.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{Path.GetFileName(path)}: {pulse.Tempo:0.0} beats a minute{(lockedAt is { } locked ? $", locked at {locked:0.0} s" : ", never locked")}; {heard} beats heard, {filled} stood in for; beat gaps vary by {evenness:P1}."));
        }

        return 0;
    }

    /// <summary>The first audio track of <paramref name="path"/>, every channel averaged, at its own rate.</summary>
    private static (float[] Mono, int Rate) DecodeMono(string path, CliHost host)
    {
        using var source = new FileByteSource(path);
        using var demuxer = MediaRegistries.Demuxers().Open(source, host.Cancellation);
        var track = demuxer.Info.FirstTrack(MediaKind.Audio) ?? throw new CliException(Path.GetFileName(path) + " has no sound.");
        using var decoder = MediaRegistries.Decoders([.. host.ExtraDecoders]).CreateAudio(track).Decoder ?? throw new CliException($"rexplay cannot decode the sound of {Path.GetFileName(path)}.");
        var mono = new List<float>();
        var rate = 0;
        var frames = new List<AudioFrame>();
        void Take()
        {
            foreach (var frame in frames)
            {
                using (frame)
                {
                    rate = rate == 0 ? frame.SampleRate : rate;
                    for (var sample = 0; sample < frame.SampleCount; sample++)
                    {
                        var total = 0f;
                        for (var channel = 0; channel < frame.Channels; channel++)
                        {
                            total += frame.Channel(channel)[sample];
                        }

                        mono.Add(total / Math.Max(1, frame.Channels));
                    }
                }
            }

            frames.Clear();
        }

        while (demuxer.ReadPacket(host.Cancellation) is { } packet)
        {
            using (packet)
            {
                if (packet.TrackId == track.Id)
                {
                    decoder.Decode(packet, frames);
                }
            }

            Take();
        }

        decoder.Drain(frames);
        Take();
        return ([.. mono], rate);
    }

    /// <summary>The frames a second <c>--fps</c> asks for: a whole number from 1 to 1000, or thirty when it is not given.</summary>
    private static int ParseFps(string? text) => text is null ? PulseFps
        : int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var fps) && fps is >= 1 and <= 1000 ? fps
        : throw new CliException($"--fps takes a whole number of frames a second from 1 to 1000, not '{text}'.");

    private static JsonObject Percentiles(List<double> values)
    {
        var sorted = values.Order().ToList();
        double At(double share) => sorted.Count == 0 ? 0 : Round(sorted[(int)Math.Round(share * (sorted.Count - 1))]);
        return new JsonObject { ["p5"] = At(0.05), ["p50"] = At(0.5), ["p95"] = At(0.95) };
    }

    private static double Round(double value) => Math.Round(value, 3);
}
