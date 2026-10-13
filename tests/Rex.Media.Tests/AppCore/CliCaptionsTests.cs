using System.Globalization;
using System.Text.Json;
using Rex.Media.AppCore.Cli;
using Rex.Media.TestKit;
using Rex.Media.Tests.Containers;
using static Rex.Media.TestKit.EbmlWriter;
using Id = Rex.Media.Containers.Matroska.MatroskaId;

namespace Rex.Media.Tests.AppCore;

/// <summary>The captions command: the words of a subtitle file placed over a crafted film with speech-like sound and grey pictures.</summary>
public sealed class CliCaptionsTests : IDisposable
{
    private const int Rate = 8000;
    private readonly string _directory = Directory.CreateTempSubdirectory("rexplayer-captions-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>
    /// A Matroska film <paramref name="seconds"/> long: 16x8 H.264 pictures ten a second (decoded
    /// grey by the fake decoder) and 8 kHz mono PCM "speech": a 120 Hz buzz in syllables, four a
    /// second, the last second shouted.
    /// </summary>
    private string Film(string name, double seconds, bool withVideo = true, string? folder = null)
    {
        var tracks = new List<byte[]> { MatroskaCraftedTests.PcmTrack(2) };
        if (withVideo)
        {
            tracks.Insert(0, Element(Id.TrackEntry, UInt(Id.TrackNumber, 1), UInt(Id.TrackType, 1), Text(Id.CodecId, "V_MPEG4/ISO/AVC"), Element(Id.Video, UInt(Id.PixelWidth, 16), UInt(Id.PixelHeight, 8))));
        }

        var clusters = new List<byte[]>();
        for (var cluster = 0; cluster < (int)Math.Ceiling(seconds); cluster++)
        {
            var blocks = new List<byte[]>();
            for (var tenth = 0; tenth < 10 && cluster + (tenth / 10.0) < seconds; tenth++)
            {
                var at = cluster + (tenth / 10.0);
                if (withVideo)
                {
                    blocks.Add(MatroskaCraftedTests.Simple(1, (short)(tenth * 100), tenth == 0, (byte)((cluster * 10) + tenth)));
                }

                // 100 ms of sound: a syllable of 60 ms then quiet, loud in the last second.
                var pcm = new byte[Rate / 10 * 2];
                var amplitude = at >= seconds - 1 ? 0.8 : 0.2;
                for (var i = 0; i < Rate / 10; i++)
                {
                    var voiced = tenth % 2 == 0 && i < Rate * 0.06 || tenth % 2 == 1 && i < Rate * 0.02;
                    var t = at + (i / (double)Rate);
                    var sample = voiced ? amplitude * ((0.6 * Math.Sin(2 * Math.PI * 120 * t)) + (0.3 * Math.Sin(2 * Math.PI * 240 * t))) : 0;
                    var value = (short)(sample * short.MaxValue);
                    (pcm[2 * i], pcm[(2 * i) + 1]) = ((byte)value, (byte)(value >> 8));
                }

                blocks.Add(MatroskaCraftedTests.Simple(2, (short)(tenth * 100), true, pcm));
            }

            clusters.Add(MatroskaCraftedTests.Cluster((ulong)(cluster * 1000), [.. blocks]));
        }

        var path = Path.Combine(folder ?? _directory, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllBytes(path, MatroskaCraftedTests.Mkv([MatroskaCraftedTests.Tracks([.. tracks]), .. clusters]));
        return path;
    }

    private string Subtitles(string name, string text)
    {
        var path = Path.Combine(_directory, name);
        System.IO.File.WriteAllText(path, text);
        return path;
    }

    private static (int Exit, string Out, string Error) Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = new CliHost { Out = output, Error = error, Version = "9.9.9", ExtraDecoders = [new FakeVideoDecoderFactory()] };
        return (CliApplication.Run(args, host), output.ToString(), error.ToString());
    }

    private static JsonElement Data(string output)
    {
        var line = Assert.Single(output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        var root = JsonDocument.Parse(line).RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean(), line);
        return root.GetProperty("data");
    }

    [Fact]
    [Capability("OSD-11")]
    public void TheWordsOfTheSubtitlesArePlacedFrameByFrameAsTheyAreSaid()
    {
        var film = Film("talk.mkv", 4);
        var srt = Subtitles("talk.srt", """
            1
            00:00:00,200 --> 00:00:02,000
            a lot of people watch videos on mute.

            2
            00:00:02,900 --> 00:00:03,900
            but here is the BIG one
            """);

        var (exit, output, error) = Run("captions", film, "--subtitles", srt, "--json");

        Assert.Equal(0, exit);
        Assert.Equal("", error);
        var data = Data(output);
        Assert.Equal((16, 8, 30, 2), (data.GetProperty("width").GetInt32(), data.GetProperty("height").GetInt32(), data.GetProperty("fps").GetInt32(), data.GetProperty("cues").GetInt32()));
        Assert.InRange(data.GetProperty("picturesRead").GetInt32(), 10, 13);
        Assert.Equal(121, data.GetProperty("frames").GetArrayLength());
        var words = data.GetProperty("words").EnumerateArray().ToList();
        // Case follows the voice (the shouted second is in capitals), so the words are compared without it.
        Assert.Equal(["a", "lot", "of", "people", "watch", "videos", "on", "mute.", "but", "here", "is", "the", "big", "one"], words.Select(word => word.GetProperty("text").GetString()!.ToLowerInvariant()));
        // Each word comes in after the one before, within its cue.
        var times = words.Select(word => word.GetProperty("at").GetDouble()).ToList();
        Assert.Equal(times, times.Order());
        Assert.InRange(times[0], 0.2, 0.8);
        Assert.InRange(times[7], 1.2, 2.0);
        Assert.InRange(times[8], 2.9, 3.3);
        // The shouted second: its words are stressed.
        Assert.Contains(words.Skip(8), word => word.GetProperty("stress").GetString() is "shout" or "peak");
        var frame = data.GetProperty("frames")[58];
        Assert.Equal(1.933, frame.GetProperty("at").GetDouble());
        var drawn = frame.GetProperty("words").EnumerateArray().ToList();
        Assert.NotEmpty(drawn);
        // The crafted picture is 16 by 8 pixels, far smaller than a readable word, so the words
        // start at its edge and run off it; on a real picture they stay inside.
        Assert.All(drawn, word =>
        {
            Assert.InRange(word.GetProperty("x").GetDouble(), 0, 200);
            Assert.InRange(word.GetProperty("y").GetDouble(), 0, 100);
            Assert.InRange(word.GetProperty("opacity").GetDouble(), 0, 1);
            Assert.StartsWith("#", word.GetProperty("color").GetString(), StringComparison.Ordinal);
        });
        Assert.Equal("🔇", drawn.Single(word => word.GetProperty("text").GetString() == "mute.").GetProperty("glyph").GetString());
    }

    [Fact]
    [Capability("OSD-11")]
    public void TheOptionsChooseTheStretchLayoutEnergyAndWhetherThePictureIsRead()
    {
        var film = Film("talk.mkv", 3);
        var srt = Subtitles("talk.srt", "1\n00:00:00,000 --> 00:00:03,000\none two three four five six\n");

        var (exit, output, _) = Run("captions", film, "--subtitles", srt, "--start", "1", "--stop", "2", "--fps", "10", "--energy", "200", "--layout", "centre", "--no-avoid", "--json");

        Assert.Equal(0, exit);
        var data = Data(output);
        Assert.Equal((10, 1.0, 1.0, 0), (data.GetProperty("fps").GetInt32(), data.GetProperty("from").GetDouble(), data.GetProperty("seconds").GetDouble(), data.GetProperty("picturesRead").GetInt32()));
        Assert.Equal(11, data.GetProperty("frames").GetArrayLength());
        // Centred: every word's centre near the middle of the 16-pixel picture.
        var last = data.GetProperty("frames")[10].GetProperty("words").EnumerateArray().ToList();
        Assert.NotEmpty(last);
        Assert.All(last, word => Assert.InRange(word.GetProperty("x").GetDouble(), 2, 14));

        var (humanExit, human, _) = Run("captions", film, "--subtitles", srt, "--stop", "1.5");
        Assert.Equal(0, humanExit);
        Assert.Contains("words from 1 cues over 1.5 s.", human, StringComparison.Ordinal);
        Assert.Contains(" s  one ", human, StringComparison.Ordinal);
    }

    [Fact]
    public void SubtitlesBesideTheFilmAreFoundAndBadInputIsRefused()
    {
        var film = Film("beside.mkv", 2);
        Subtitles("beside.srt", "1\n00:00:00,000 --> 00:00:02,000\nfound beside\n");
        var (exit, output, _) = Run("captions", film, "--json");
        Assert.Equal(0, exit);
        Assert.Equal(2, Data(output).GetProperty("words").GetArrayLength());

        // In a folder of its own, since any subtitle file beside a film is offered for it.
        var alone = Film("alone.mkv", 2, folder: Path.Combine(_directory, "alone"));
        var (none, _, error) = Run("captions", alone);
        Assert.Equal(1, none);
        Assert.Contains("no subtitles beside", error, StringComparison.Ordinal);

        var (missing, _, missingError) = Run("captions", alone, "--subtitles", Path.Combine(_directory, "nowhere.srt"));
        Assert.Equal(1, missing);
        Assert.Contains("no subtitle file at", missingError, StringComparison.Ordinal);

        var empty = Subtitles("empty.srt", "not subtitles at all");
        var (nothing, _, emptyError) = Run("captions", alone, "--subtitles", empty);
        Assert.Equal(1, nothing);
        Assert.Contains("holds no cues", emptyError, StringComparison.Ordinal);

        var srt = Subtitles("fine.srt", "1\n00:00:00,000 --> 00:00:02,000\nfine\n");
        var (energy, _, energyError) = Run("captions", alone, "--subtitles", srt, "--energy", "5");
        Assert.Equal(1, energy);
        Assert.Contains("--energy takes a percentage", energyError, StringComparison.Ordinal);
        var (layout, _, layoutError) = Run("captions", alone, "--subtitles", srt, "--layout", "sideways");
        Assert.Equal(1, layout);
        Assert.Contains("--layout takes scatter, stack, centre, left or confetti", layoutError, StringComparison.Ordinal);
        var (late, _, lateError) = Run("captions", alone, "--subtitles", srt, "--start", "5");
        Assert.Equal(1, late);
        Assert.Contains("nothing to caption", lateError, StringComparison.Ordinal);
    }

    [Fact]
    public void AFilmWithoutPicturesIsCaptionedOnAStandardFrame()
    {
        var film = Film("sound.mkv", 2, withVideo: false);
        var srt = Subtitles("sound.srt", "1\n00:00:00,000 --> 00:00:02,000\nonly sound here\n");

        var (exit, output, _) = Run("captions", film, "--subtitles", srt, "--json");

        Assert.Equal(0, exit);
        var data = Data(output);
        Assert.Equal((1920, 1080, 0), (data.GetProperty("width").GetInt32(), data.GetProperty("height").GetInt32(), data.GetProperty("picturesRead").GetInt32()));
        Assert.Equal(3, data.GetProperty("words").GetArrayLength());
    }

    [Fact]
    public void APictureNoDecoderCanReadIsSaidSoAndCaptionsGoOnWithoutIt()
    {
        var film = Film("unread.mkv", 2);
        var srt = Subtitles("unread.srt", "1\n00:00:00,000 --> 00:00:02,000\nstill shown\n");
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = new CliHost { Out = output, Error = error, Version = "9.9.9" };

        Assert.Equal(0, CliApplication.Run(["captions", film, "--subtitles", srt, "--json"], host));
        Assert.Contains("The picture is not read", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, Data(output.ToString()).GetProperty("picturesRead").GetInt32());
        Assert.Equal(string.Empty, CultureInfo.InvariantCulture.Name);
    }
}
