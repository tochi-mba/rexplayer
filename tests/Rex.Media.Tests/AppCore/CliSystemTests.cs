using System.Text.Json;
using Rex.Media.AppCore;
using Rex.Media.AppCore.Cli;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>rexplay probe --system, with the host's report made up here.</summary>
public sealed class CliSystemTests
{
    private static readonly SystemReport Report = new()
    {
        Windows = "10.0.26200.0",
        Graphics = "A graphics card",
        AudioOutput = new AudioFormat(48_000, 2, SampleFormat.F32),
        Decoders =
        [
            new WindowsDecoders(CodecId.Aac, ["Windows AAC"], []),
            new WindowsDecoders(CodecId.H264, ["Windows H.264"], ["Card H.264"]),
            new WindowsDecoders(CodecId.Hevc, [], []),
            new WindowsDecoders(CodecId.Mp3, ["Windows MP3"], []),
        ],
    };

    private static (int Exit, string Out, string Error) Run(Func<SystemReport?>? report, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = report is null
            ? new CliHost { Out = output, Error = error, Version = "9.9.9" }
            : new CliHost { Out = output, Error = error, Version = "9.9.9", SystemReport = report };
        return (CliApplication.Run(args, host), output.ToString(), error.ToString());
    }

    [Fact]
    [Capability("TOOL-11")]
    public void TheReportSaysWhoDecodesEachCodec()
    {
        var (exit, output, _) = Run(() => Report, "probe", "--system");

        Assert.Equal(0, exit);
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Windows 10.0.26200.0", lines[0]);
        Assert.Equal("Graphics: A graphics card", lines[1]);
        Assert.Equal("Audio output: 48000 Hz, 2 channels", lines[2]);
        Assert.Contains("  MP3: rexplayer, Windows", lines);
        Assert.Contains("  FLAC: rexplayer", lines);
        Assert.Contains("  AAC: Windows", lines);
        Assert.Contains("  H.264 / AVC: Windows, graphics card", lines);
        Assert.Contains("  H.265 / HEVC: not available", lines);
    }

    [Fact]
    public void TheReportSaysWhatIsMissing()
    {
        var (_, output, _) = Run(() => Report with { Graphics = null, AudioOutput = null }, "probe", "--system");
        var (_, software, _) = Run(() => Report with { GraphicsInSoftware = true }, "probe", "--system");

        Assert.Contains("Graphics: none would start", output, StringComparison.Ordinal);
        Assert.Contains("Audio output: none plugged in", output, StringComparison.Ordinal);
        Assert.Contains("Graphics: A graphics card (software)", software, StringComparison.Ordinal);
    }

    [Fact]
    public void InMachineModeTheReportIsOneJsonDocument()
    {
        var (exit, output, _) = Run(() => Report, "probe", "--system", "--json");
        var (_, empty, _) = Run(() => Report with { AudioOutput = null }, "probe", "--system", "--json");

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(output);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(("10.0.26200.0", "A graphics card", false), (data.GetProperty("windows").GetString(), data.GetProperty("graphics").GetString(), data.GetProperty("graphicsInSoftware").GetBoolean()));
        Assert.Equal(48_000, data.GetProperty("audioOutput").GetProperty("sampleRate").GetInt32());
        var h264 = data.GetProperty("codecs").EnumerateArray().Single(c => c.GetProperty("codec").GetString() == "H264");
        Assert.False(h264.GetProperty("own").GetBoolean());
        Assert.Equal(["Card H.264"], h264.GetProperty("windowsHardware").EnumerateArray().Select(e => e.GetString()));
        var flac = data.GetProperty("codecs").EnumerateArray().Single(c => c.GetProperty("codec").GetString() == "Flac");
        Assert.True(flac.GetProperty("own").GetBoolean());
        Assert.Equal(0, flac.GetProperty("windows").GetArrayLength());
        using var withoutOutput = JsonDocument.Parse(empty);
        Assert.Equal(JsonValueKind.Null, withoutOutput.RootElement.GetProperty("data").GetProperty("audioOutput").ValueKind);
    }

    [Fact]
    public void AHostThatCannotDescribeTheSystemSaysSo()
    {
        var (exit, _, error) = Run(null, "probe", "--system");

        Assert.Equal(1, exit);
        Assert.Contains("cannot describe the system", error, StringComparison.Ordinal);
    }

    [Fact]
    public void RexplayersOwnCodecsAreTheShippedDecoders()
    {
        Assert.Equal([CodecId.Pcm, CodecId.Alaw, CodecId.Mulaw, CodecId.Mp3, CodecId.Flac, CodecId.Vorbis], SystemReport.OwnCodecs());
    }
}
