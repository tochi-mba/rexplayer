using System.Text.Json;
using Rex.Media.AppCore.Cli;
using Rex.Media.TestKit;
using Rex.Media.Tests.Engine;
using Rex.Media.Video;

namespace Rex.Media.Tests.AppCore;

/// <summary>rexplay play --window, with a recording presenter standing in for the window.</summary>
public sealed class CliWindowTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("rexplayer-window-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private (int Exit, string Out, string Error) Play(Func<string, IVideoPresenter?>? window, params string[] extra)
    {
        var video = Path.Combine(_directory, "clip.mkv");
        System.IO.File.WriteAllBytes(video, VideoPlaybackTests.Clip(5));
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = new CliHost { Out = output, Error = error, Version = "9.9.9", ExtraDecoders = [new FakeVideoDecoderFactory()] };
        host = window is null ? host : new CliHost { Out = output, Error = error, Version = "9.9.9", ExtraDecoders = [new FakeVideoDecoderFactory()], VideoWindow = window };
        var exit = CliApplication.Run(["play", video, "--aout", "wav:" + Path.Combine(_directory, "out.wav"), .. extra], host);
        return (exit, output.ToString(), error.ToString());
    }

    [Fact]
    public void PicturesAreShownInTheWindowAndCounted()
    {
        var presenter = new RecordingVideoPresenter();
        string? title = null;

        var (exit, output, _) = Play(name => { title = name; return presenter; }, "--window", "--json");

        Assert.Equal(0, exit);
        Assert.Equal("clip.mkv - rexplayer", title);
        Assert.Equal(5, presenter.Shown.Count);
        Assert.True(presenter.Disposed);
        using var document = JsonDocument.Parse(output);
        var stats = document.RootElement.GetProperty("data").GetProperty("stats");
        Assert.Equal((5, 5, 0, "grey"), (stats.GetProperty("videoFramesDecoded").GetInt32(), stats.GetProperty("videoFramesPresented").GetInt32(), stats.GetProperty("videoFramesDropped").GetInt32(), stats.GetProperty("videoDecoder").GetString()));
    }

    [Fact]
    public void WithoutTheOptionNoWindowOpens()
    {
        var opened = false;

        var (exit, _, _) = Play(_ => { opened = true; return new RecordingVideoPresenter(); });

        Assert.Equal(0, exit);
        Assert.False(opened);
    }

    [Fact]
    public void AHostWithoutWindowsSaysSo()
    {
        // A host that says nothing about windows has none.
        var (exit, _, error) = Play(null, "--window");

        Assert.Equal(1, exit);
        Assert.Contains("cannot open a window", error, StringComparison.Ordinal);
    }
}
