using Rex.Media.AppCore.Cli;
using Rex.Media.Audio;
using Rex.Media.Engine;
using Rex.Media.Primitives;
using Rex.Media.Tests.Engine;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

public sealed class CliPlaybackFailureTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("rexplayer-cli-fail-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void APlaybackThatFaultsPartWayExitsWithTheReason()
    {
        var input = Path.Combine(_directory, "long.wav");
        File.WriteAllBytes(input, Pcm.SineWav(440, 8000, 1, 2.0));
        using var error = new StringWriter();
        var host = new CliHost
        {
            Out = TextWriter.Null,
            Error = error,
            Version = "1",
            ExtraDecoders = [new BrokenDecoderFactory()],
        };

        var exit = CliApplication.Run(["play", input, "--aout", "wav:" + Path.Combine(_directory, "x.wav")], host);

        Assert.Equal(1, exit);
        Assert.Equal("No playable streams remain.", error.ToString().Trim());
    }

    [Fact]
    public void AHostWithoutADevicePlaysToTheNullSink()
    {
        var input = Path.Combine(_directory, "short.wav");
        File.WriteAllBytes(input, Pcm.SineWav(440, 8000, 1, 0.02));
        var host = new CliHost { Out = TextWriter.Null, Error = TextWriter.Null, Version = "1" };

        Assert.Null(host.DefaultAudioSink());
        Assert.Equal(0, CliApplication.Run(["play", input], host));
    }

    [Fact]
    public void TheNullSinkTakesAnExplicitBuffer()
    {
        using var sink = new NullAudioSink(buffer: TimeSpan.FromMilliseconds(50));

        Assert.Equal(TimeSpan.FromMilliseconds(50), sink.Buffer);
    }

    [Fact]
    public void TheShippedRegistriesBuildAnIdleSessionAtZero()
    {
        using var session = new MediaSession(new EngineOptions
        {
            Demuxers = Rex.Media.AppCore.MediaRegistries.Demuxers(),
            Decoders = Rex.Media.AppCore.MediaRegistries.Decoders(),
            AudioSinkFactory = () => new RecordingAudioSink(),
        });

        Assert.Equal(SessionState.Idle, session.State);
        Assert.Equal(MediaTime.Zero, session.Position);
    }
}
