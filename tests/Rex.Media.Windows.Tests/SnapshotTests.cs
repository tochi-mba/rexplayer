using System.Text.Json;
using Rex.Media.AppCore.Cli;
using Rex.Media.Codecs.MediaFoundation;
using Rex.Media.TestKit;

namespace Rex.Media.Windows.Tests;

/// <summary>The snapshot command end to end, with Windows decoding the video, against FFmpeg's picture.</summary>
public sealed class SnapshotTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("rexplayer-snapshot-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    [Capability("PB-16")]
    public void ASnapshotShowsThePictureAnIndependentPlayerShowsAtThatMoment()
    {
        var output = Path.Combine(_folder, "at-0.25.png");
        var printed = new StringWriter();
        var host = new CliHost { Out = printed, Error = TextWriter.Null, Version = "0.0.0", ExtraDecoders = [new MfDecoderFactory()] };

        var exit = CliApplication.Run(["snapshot", RepoPaths.Combine("tests/fixtures/mp4/h264-aac.mp4"), "--at", "0.25", "--out", output, "--json"], host);

        Assert.Equal(0, exit);
        using var result = JsonDocument.Parse(printed.ToString());
        var data = result.RootElement.GetProperty("data");
        Assert.Equal(0.24, data.GetProperty("at").GetDouble());
        Assert.Equal((128, 72), (data.GetProperty("width").GetInt32(), data.GetProperty("height").GetInt32()));
        var ours = PngReader.Read(File.ReadAllBytes(output));
        var theirs = PngReader.Read(File.ReadAllBytes(RepoPaths.Combine("tests/fixtures/mp4/h264-aac.snapshot-0.24.png")));
        Assert.Equal((theirs.Width, theirs.Height), (ours.Width, ours.Height));
        Assert.True(ours.Psnr(theirs) > 35, $"PSNR {ours.Psnr(theirs):F1} dB");
    }
}
