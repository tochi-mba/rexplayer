using System.Globalization;
using System.Text.Json.Nodes;
using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Machine;
using Rex.Media.AppCore.Player;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.Settings;
using Rex.Media.Subtitles;
using Rex.Media.Video;

namespace Rex.Media.AppCore.Cli;

/// <summary>
/// Kinetic captions for a file without a window (OSD-11): the sound decoded and heard as the
/// window hears it, the picture read ahead as the window reads it, and the words of a subtitle
/// file placed and moved frame by frame, written out so a test, an agent or a renderer sees
/// exactly what the window would draw.
/// </summary>
public static partial class CliApplication
{
    /// <summary>Pictures read a second for placing captions, as the window reads them.</summary>
    private const double CaptionPicturesPerSecond = 3;

    private static int Captions(CliArguments arguments, bool machine, CliHost host)
    {
        var path = Path.GetFullPath(RequireFile(arguments));
        var start = ParseTime(arguments.Option("--start"), "--start")?.ToTimeSpan() ?? TimeSpan.Zero;
        var stop = ParseTime(arguments.Option("--stop"), "--stop")?.ToTimeSpan();
        var fps = ParseFps(arguments.Option("--fps"));
        var settings = CaptionSettings(arguments);
        var track = LoadCaptionWords(path, arguments.Option("--subtitles"), host);
        var (mono, rate) = DecodeMono(path, host);
        var (width, height, pictures) = ReadPictures(path, start, stop, settings.KineticCaptions.AvoidPicture, host);
        var length = rate > 0 ? mono.Length / (double)rate : 0;
        var last = Math.Min(length, stop?.TotalSeconds ?? length);
        if (last - start.TotalSeconds < 1.0 / fps)
        {
            throw new CliException($"There is nothing to caption in {Path.GetFileName(path)} at those times.");
        }

        var director = new CaptionDirector(CaptionDirector.StyleFor(settings));
        var area = new Area(0, 0, width, height);
        var fontSize = SubtitleLook.For(settings, height).FontSize;
        var window = new float[SpeechPulse.Size];
        var frames = new JsonArray();
        var revealed = new JsonArray();
        var seen = new HashSet<int>();
        var count = (int)((last - start.TotalSeconds) * fps);
        for (var frame = 0; frame <= count; frame++)
        {
            host.Cancellation.ThrowIfCancellationRequested();
            var at = start.TotalSeconds + ((double)frame / fps);
            var endSample = (int)Math.Min(mono.Length, at * rate);
            for (var i = 0; i < window.Length; i++)
            {
                var sample = endSample - window.Length + i;
                window[i] = sample >= 0 ? mono[sample] : 0;
            }

            var position = TimeSpan.FromSeconds(at);
            var words = director.Frame(position, rate > 0 ? window : [], rate, TimeSpan.FromSeconds(1.0 / fps), track.At(position, TimeSpan.Zero), area, fontSize, pictures);
            var drawn = new JsonArray();
            foreach (var word in words)
            {
                drawn.Add(WordJson(word));
                if (seen.Add(word.Id))
                {
                    revealed.Add(new JsonObject { ["at"] = Round(at), ["text"] = word.Text, ["stress"] = word.Stress.ToString().ToLowerInvariant(), ["size"] = Round(word.Settled) });
                }
            }

            frames.Add(new JsonObject { ["at"] = Round(at), ["words"] = drawn });
        }

        if (machine)
        {
            host.Out.WriteLine(MachineEnvelope.Success("captions", new JsonObject
            {
                ["file"] = path,
                ["width"] = width,
                ["height"] = height,
                ["fps"] = fps,
                ["fontSize"] = Round(fontSize),
                ["from"] = Round(start.TotalSeconds),
                ["seconds"] = Round(last - start.TotalSeconds),
                ["cues"] = track.Count,
                ["picturesRead"] = pictures?.Count ?? 0,
                ["words"] = revealed,
                ["frames"] = frames,
            }));
        }
        else
        {
            host.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileName(path)}: {revealed.Count} words from {track.Count} cues over {last - start.TotalSeconds:0.0} s."));
            foreach (var word in revealed.OfType<JsonObject>())
            {
                host.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{(double)word["at"]!,7:0.00} s  {(string)word["text"]!,-20} {(string)word["stress"]!}"));
            }
        }

        return 0;
    }

    /// <summary>The caption settings the options ask for, over the defaults.</summary>
    private static PlayerSettings CaptionSettings(CliArguments arguments)
    {
        var kinetic = new KineticCaptionSettings { On = true };
        if (arguments.Option("--energy") is { } energyText)
        {
            kinetic = kinetic with
            {
                Energy = int.TryParse(energyText, NumberStyles.None, CultureInfo.InvariantCulture, out var energy) && energy is >= KineticCaptionSettings.LeastEnergy and <= KineticCaptionSettings.MostEnergy
                    ? energy
                    : throw new CliException($"--energy takes a percentage from {KineticCaptionSettings.LeastEnergy} to {KineticCaptionSettings.MostEnergy}, not '{energyText}'."),
            };
        }

        if (arguments.Option("--layout") is { } layoutText)
        {
            kinetic = kinetic with
            {
                Layout = Enum.TryParse<CaptionLayoutChoice>(layoutText, ignoreCase: true, out var layout) ? layout
                    : throw new CliException($"--layout takes scatter, stack or centre, not '{layoutText}'."),
            };
        }

        if (arguments.Option("--style") is { } styleText)
        {
            kinetic = kinetic with
            {
                Preset = Enum.TryParse<CaptionPreset>(styleText, ignoreCase: true, out var preset) ? preset
                    : throw new CliException($"--style takes one of {string.Join(", ", Enum.GetNames<CaptionPreset>().Select(name => name.ToLowerInvariant()))}, not '{styleText}'."),
            };
        }

        if (arguments.Option("--no-avoid") is not null)
        {
            kinetic = kinetic with { AvoidPicture = false };
        }

        return new PlayerSettings { KineticCaptions = kinetic };
    }

    /// <summary>The subtitle file asked for, or the first beside the media.</summary>
    private static SubtitleTrack LoadCaptionWords(string path, string? subtitles, CliHost host)
    {
        var file = subtitles is not null ? Path.GetFullPath(subtitles) : SubtitleSidecars.Find(path).FirstOrDefault()?.Path
            ?? throw new CliException($"There are no subtitles beside {Path.GetFileName(path)}; give a subtitle file with --subtitles.");
        if (!File.Exists(file))
        {
            throw new CliException($"There is no subtitle file at {file}.");
        }

        host.Cancellation.ThrowIfCancellationRequested();
        var (text, _) = SubtitleText.Decode(File.ReadAllBytes(file), 1252);
        var track = new SubtitleTrack(Path.GetFileName(file));
        try
        {
            track.AddRange(SubtitleFile.Parse(text).Cues);
        }
        catch (FormatException)
        {
            // Said below, as a file with nothing in it is.
        }

        if (track.Count == 0)
        {
            throw new CliException($"{Path.GetFileName(file)} holds no cues rexplay can read.");
        }

        return track;
    }

    /// <summary>
    /// The video's size, and its pictures from <paramref name="start"/> to <paramref name="stop"/> read
    /// a few a second for placing captions (none when they are not to be avoided, or there is no video).
    /// </summary>
    private static (int Width, int Height, PictureTimeline? Pictures) ReadPictures(string path, TimeSpan start, TimeSpan? stop, bool avoid, CliHost host)
    {
        using var source = new FileByteSource(path);
        using var demuxer = MediaRegistries.Demuxers().Open(source, host.Cancellation);
        if (demuxer.Info.FirstTrack(MediaKind.Video) is not { Video: { } video } track)
        {
            return (1920, 1080, null);
        }

        var size = (video.Width, video.Height);
        if (!avoid)
        {
            return (size.Width, size.Height, null);
        }

        var opened = MediaRegistries.Decoders([.. host.ExtraDecoders]).CreateVideo(track);
        if (opened.Decoder is not { } decoder)
        {
            host.Error.WriteLine($"The picture is not read, so captions are placed without it: {opened.Reason}");
            return (size.Width, size.Height, null);
        }

        using (decoder)
        {
            var pictures = new PictureTimeline();
            if (start > TimeSpan.Zero && demuxer.Info.IsSeekable)
            {
                demuxer.Seek(new MediaTime(start.Ticks), host.Cancellation);
            }

            var frames = new List<VideoFrame>();
            PictureSpace? before = null;
            var next = start.TotalSeconds;
            var done = false;
            void Take()
            {
                foreach (var frame in frames)
                {
                    using (frame)
                    {
                        var at = frame.Pts.IsKnown ? frame.Pts.ToTimeSpan().TotalSeconds : next;
                        if (done || at < next)
                        {
                            continue;
                        }

                        if (stop is { } end && at > end.TotalSeconds + 1)
                        {
                            done = true;
                            continue;
                        }

                        using var bgra = ColorConverter.ToBgra(frame);
                        var space = PictureSpace.Read(bgra.Plane(0), bgra.Width, bgra.Height, bgra.Stride(0), before);
                        pictures.Add(at, space);
                        (before, next) = (space, at + (1 / CaptionPicturesPerSecond));
                    }
                }

                frames.Clear();
            }

            while (!done && demuxer.ReadPacket(host.Cancellation) is { } packet)
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

            if (!done)
            {
                decoder.Drain(frames);
                Take();
            }

            return (size.Width, size.Height, pictures);
        }
    }

    private static JsonObject WordJson(CaptionWord word) => new()
    {
        ["id"] = word.Id,
        ["text"] = word.Text,
        ["x"] = Round(word.X),
        ["y"] = Round(word.Y),
        ["size"] = Round(word.Size),
        ["settled"] = Round(word.Settled),
        ["rotation"] = Round(word.Rotation),
        ["color"] = string.Create(CultureInfo.InvariantCulture, $"#{word.Color.R:X2}{word.Color.G:X2}{word.Color.B:X2}"),
        ["opacity"] = Round(word.Opacity),
        ["bold"] = word.Bold,
        ["italic"] = word.Italic,
        ["serif"] = word.Serif,
        ["glow"] = Round(word.Glow),
        ["shake"] = Round(word.Shake),
        ["flash"] = Round(word.Flash),
        ["glyph"] = word.Glyph,
        ["stress"] = word.Stress.ToString().ToLowerInvariant(),
        ["font"] = word.Font.ToString().ToLowerInvariant(),
        ["offsetX"] = Round(word.OffsetX),
        ["offsetY"] = Round(word.OffsetY),
        ["outline"] = Round(word.Backing.Outline),
        ["plate"] = Round(word.Backing.Plate),
    };
}
