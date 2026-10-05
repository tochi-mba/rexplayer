using System.Text.Json;
using Rex.Media.AppCore;
using Rex.Media.AppCore.Cli;
using Rex.Media.AppCore.Machine;
using Rex.Media.Audio;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

public sealed class CliApplicationTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("rexplayer-cli-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Fixture(string name, byte[] bytes)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static (int Exit, string Out, string Error) Run(Func<IAudioSink?>? device = null, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = new CliHost
        {
            Out = output,
            Error = error,
            Version = "9.9.9",
            DefaultAudioSink = device ?? (() => null),
        };
        var exit = CliApplication.Run(args, host);
        return (exit, output.ToString(), error.ToString());
    }

    private static JsonElement Single(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
        return JsonDocument.Parse(lines[0]).RootElement;
    }

    [Fact]
    [Capability("TOOL-02")]
    public void CapabilitiesIsOneJsonLineDescribingTheBuild()
    {
        var (exit, output, _) = Run(null, "agent", "capabilities");
        var document = Single(output);

        Assert.Equal(0, exit);
        Assert.True(document.GetProperty("ok").GetBoolean());
        Assert.Equal(MachineEnvelope.ProtocolVersion, document.GetProperty("protocolVersion").GetInt32());
        Assert.Equal("capabilities", document.GetProperty("command").GetString());
        var data = document.GetProperty("data");
        Assert.Equal("9.9.9", data.GetProperty("version").GetString());
        Assert.Equal(CliReference.Commands.Count, data.GetProperty("commands").GetArrayLength());
        Assert.Equal(CliReference.Options.Count, data.GetProperty("options").GetArrayLength());
        Assert.Contains("wav", data.GetProperty("containers").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("rexplayer PCM", data.GetProperty("decoders")[0].GetProperty("name").GetString());
        Assert.DoesNotContain("\\u003C", output, StringComparison.Ordinal);
    }

    [Fact]
    public void VersionPrintsTextOrJson()
    {
        Assert.Equal("rexplayer 9.9.9", Run(null, "version").Out.Trim());
        Assert.Equal("9.9.9", Single(Run(null, "version", "--json").Out).GetProperty("data").GetProperty("version").GetString());
    }

    [Fact]
    public void HelpListsEveryCommandAndOption()
    {
        var (exit, output, _) = Run();

        Assert.Equal(0, exit);
        foreach (var command in CliReference.Commands)
        {
            Assert.Contains(command.Usage, output, StringComparison.Ordinal);
        }

        foreach (var option in CliReference.Options)
        {
            Assert.Contains(option.Name, output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void HelpExplainsOneCommand()
    {
        var (exit, output, _) = Run(null, "help", "probe");

        Assert.Equal(0, exit);
        Assert.StartsWith("rexplay probe <file>", output, StringComparison.Ordinal);
        Assert.Equal(1, Run(null, "help", "nonsense").Exit);
    }

    [Fact]
    public void ProbeDescribesTheFile()
    {
        var path = Fixture("probe.wav", new WavBuilder().Info(("INAM", "Sungba")).Format(1, 2, 44_100, 4, 16).Data(new byte[4 * 44_100]).Build());

        var text = Run(null, "probe", path).Out;
        var json = Single(Run(null, "--json", "probe", path).Out).GetProperty("data");

        Assert.Contains("probe.wav: WAVE, 0:01", text, StringComparison.Ordinal);
        Assert.Contains("#0 audio: PCM, 44100 Hz, Stereo, 16-bit", text, StringComparison.Ordinal);
        Assert.Contains("title: Sungba", text, StringComparison.Ordinal);
        Assert.Equal("WAVE", json.GetProperty("format").GetString());
        Assert.Equal(1.0, json.GetProperty("duration").GetDouble());
        Assert.Equal("Sungba", json.GetProperty("metadata").GetProperty("title").GetString());
        Assert.Equal(2, json.GetProperty("tracks")[0].GetProperty("channels").GetInt32());
    }

    [Fact]
    public void ProbeLeavesOutZeroBitDepths()
    {
        var path = Fixture("ulaw.wav", new WavBuilder().Format(7, 1, 8000, 1, 0).Data(new byte[8]).Build());

        var text = Run(null, "probe", path).Out;

        Assert.Contains("G.711 µ-law, 8000 Hz, Mono", text, StringComparison.Ordinal);
        Assert.DoesNotContain("-bit", text, StringComparison.Ordinal);
    }

    [Fact]
    [Capability("PB-22")]
    public void PlayWritesACaptureAndReportsWhatHappened()
    {
        var input = Fixture("tone.wav", Pcm.SineWav(440, 8000, 1, 0.5));
        var capture = Path.Combine(_directory, "out", "capture.wav");

        var document = Single(Run(null, "play", input, "--aout", "wav:" + capture, "--json").Out);

        var data = document.GetProperty("data");
        Assert.True(document.GetProperty("ok").GetBoolean());
        Assert.True(data.GetProperty("finished").GetBoolean());
        Assert.Equal(0.5, data.GetProperty("played").GetDouble());
        Assert.Equal(4000, data.GetProperty("stats").GetProperty("audioSamplesPlayed").GetInt64());
        Assert.True(File.Exists(capture));
    }

    [Fact]
    public void PlayHonoursStartVolumeAndHumanOutput()
    {
        var input = Fixture("tone.wav", Pcm.SineWav(440, 8000, 1, 1.0));
        var capture = Path.Combine(_directory, "start.wav");

        var (exit, output, _) = Run(null, "play", input, "--aout=wav:" + capture, "--start", "0:00.5", "--volume", "50");

        Assert.Equal(0, exit);
        Assert.Equal("Played tone.wav: 0:01 of 0:01.", output.Trim());
        Assert.InRange(new FileInfo(capture).Length, 16_000, 16_100);
    }

    [Fact]
    [Capability("PB-23")]
    public void PlayStopsAtTheStopTime()
    {
        var input = Fixture("long.wav", Pcm.SineWav(440, 8000, 1, 2.0));

        var data = Single(Run(null, "play", input, "--aout", "null", "--stop", "0.2", "--json").Out).GetProperty("data");

        Assert.False(data.GetProperty("finished").GetBoolean());
        Assert.InRange(data.GetProperty("played").GetDouble(), 0.15, 1.0);
    }

    [Fact]
    public void TheDefaultOutputUsesTheDeviceOrFallsBackToSilence()
    {
        var input = Fixture("short.wav", Pcm.SineWav(440, 8000, 1, 0.05));
        var device = new RecordingAudioSink(channels: 1);

        Assert.Equal(0, Run(() => device, "play", input).Exit);
        Assert.Equal(400, device.SampleCount);
        Assert.Equal(0, Run(null, "play", input).Exit);
    }

    [Theory]
    [InlineData("play")]
    [InlineData("probe")]
    public void AMissingFileIsExplained(string command)
    {
        var (exit, _, error) = Run(null, command);
        Assert.Equal(1, exit);
        Assert.Equal("Name a file to open.", error.Trim());

        var missing = Run(null, command, Path.Combine(_directory, "missing.wav"));
        Assert.StartsWith("There is no file at ", missing.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--aout", "speaker", "'speaker' is not an audio output. Use default, null or wav:<path>.")]
    [InlineData("--aout", "wav:", "'wav:' is not an audio output. Use default, null or wav:<path>.")]
    [InlineData("--start", "soon", "--start takes a time such as 90, 1:30 or 1:02:03.5, not 'soon'.")]
    [InlineData("--stop", "x", "--stop takes a time such as 90, 1:30 or 1:02:03.5, not 'x'.")]
    [InlineData("--volume", "300", "--volume takes a percentage from 0 to 200, not '300'.")]
    [InlineData("--volume", "loud", "--volume takes a percentage from 0 to 200, not 'loud'.")]
    public void BadOptionsAreExplained(string option, string value, string message)
    {
        var input = Fixture("tone.wav", Pcm.SineWav(440, 8000, 1, 0.05));

        var (exit, _, error) = Run(null, "play", input, option, value);

        Assert.Equal(1, exit);
        Assert.Equal(message, error.Trim());
    }

    [Fact]
    public void FailuresInMachineModeAreJson()
    {
        var input = Fixture("noise.wav", [1, 2, 3, 4]);

        var document = Single(Run(null, "play", input, "--json").Out);

        Assert.False(document.GetProperty("ok").GetBoolean());
        Assert.Equal("play", document.GetProperty("command").GetString());
        Assert.Equal("CliException", document.GetProperty("error").GetProperty("type").GetString());
        Assert.StartsWith("rexplayer does not recognise", document.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnplayableTrackFailsCleanly()
    {
        var input = Fixture("mp3.wav", new WavBuilder().Format(0x55, 2, 44_100, 1, 0).Data(new byte[10]).Build());

        var (exit, _, error) = Run(null, "play", input);

        Assert.Equal(1, exit);
        Assert.Equal("No decoder for MP3 is available.", error.Trim());
    }

    [Fact]
    public void UnknownCommandsAreExplained()
    {
        var (exit, _, error) = Run(null, "dance");

        Assert.Equal(1, exit);
        Assert.Equal("'dance' is not a rexplay command. Run 'rexplay help' to see them.", error.Trim());
        Assert.False(Single(Run(null, "agent", "dance").Out).GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void ArgumentsAreChecked()
    {
        Assert.Throws<ArgumentNullException>(() => CliApplication.Run(null!, new CliHost { Out = TextWriter.Null, Error = TextWriter.Null, Version = "1" }));
        Assert.Throws<ArgumentNullException>(() => CliApplication.Run([], null!));
    }

    [Fact]
    public void TheRegistriesHoldTheShippedFormats()
    {
        Assert.Equal(["wav", "aiff"], MediaRegistries.Demuxers().Factories.Select(f => f.Name));
        Assert.Single(MediaRegistries.Decoders().Factories);
        Assert.Throws<ArgumentNullException>(() => MediaRegistries.Decoders(null!));
    }
}
