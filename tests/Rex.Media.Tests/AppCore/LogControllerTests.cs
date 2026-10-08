using Rex.Media.AppCore.Commands;
using Rex.Media.Diagnostics;
using Rex.Media.Engine;
using Rex.Media.Settings;
using Rex.Media.TestKit;
using Rex.Media.Tests.Containers;
using static Rex.Media.Tests.AppCore.ControllerHarness;
using static Rex.Media.TestKit.EbmlWriter;
using Id = Rex.Media.Containers.Matroska.MatroskaId;

namespace Rex.Media.Tests.AppCore;

/// <summary>The player writes down what it does, in enough detail to diagnose a problem from the log alone.</summary>
public sealed class LogControllerTests
{
    private static string Logged(RexLog log) => string.Join("\n", log.Tail(500).Select(entry => entry.Format()));

    [Fact]
    [Capability("SYS-01")]
    public void EveryItemItsTracksAndEveryCommandAreLogged()
    {
        var log = RexLog.InMemory();
        using var harness = new ControllerHarness(settings: new PlayerSettings { TitleSeconds = 0 }, log: log);
        harness.Files["a.wav"] = Count(0, 400);

        harness.Controller.Open(["a.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Ended);
        harness.Controller.Execute(CommandCatalog.Mute);
        harness.Controller.Stop();

        var text = Logged(log);
        Assert.Contains("[INFO] player: Opening 1 item(s): a.wav", text, StringComparison.Ordinal);
        Assert.Contains("[INFO] player: Starting \"a\" (a.wav).", text, StringComparison.Ordinal);
        Assert.Contains("[INFO] player: Opened WAV", text, StringComparison.Ordinal);
        Assert.Contains("Tracks: #0 audio PCM 8000 Hz 1 ch", text, StringComparison.Ordinal);
        Assert.Contains("[DEBUG] player: Opening -> Playing", text, StringComparison.Ordinal);
        Assert.Contains("[INFO] player: Reached the end of a.wav.", text, StringComparison.Ordinal);
        Assert.Contains("[DEBUG] player: Command mute", text, StringComparison.Ordinal);
        Assert.Contains("[INFO] player: Stopped.", text, StringComparison.Ordinal);
    }

    [Fact]
    [Capability("SYS-01")]
    public void WhatWentWrongIsLoggedWithItsReason()
    {
        var log = RexLog.InMemory();
        using var harness = new ControllerHarness(settings: new PlayerSettings { TitleSeconds = 0 }, log: log);
        harness.Files["broken.wav"] = [1, 2, 3, 4];
        harness.Files["list.m3u"] = "#EXTM3U\n#EXT-X-TARGETDURATION:6\n"u8.ToArray();

        harness.Controller.Open(["list.m3u", "broken.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Faulted);

        var text = Logged(log);
        Assert.Contains("[WARN] player: The playlist list.m3u could not be read: ", text, StringComparison.Ordinal);
        Assert.Contains("[ERROR] player: broken.wav could not be played: ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ATrackThatCannotBeDecodedIsLoggedAndTheRestPlays()
    {
        var log = RexLog.InMemory();
        using var harness = new ControllerHarness(settings: new PlayerSettings { TitleSeconds = 0 }, log: log, pictures: true);
        var video = Element(
            Id.TrackEntry,
            UInt(Id.TrackNumber, 2),
            UInt(Id.TrackType, 1),
            Text(Id.CodecId, "V_MPEG4/ISO/AVC"),
            UInt(Id.DefaultDuration, 40_000_000),
            Element(Id.Video, UInt(Id.PixelWidth, 16), UInt(Id.PixelHeight, 8)));
        var sound = Element(
            Id.TrackEntry,
            UInt(Id.TrackNumber, 1),
            UInt(Id.TrackType, 2),
            Text(Id.CodecId, "A_TRUEHD"),
            Text(Id.Language, "eng"),
            Text(Id.Name, "Main"),
            Element(Id.Audio, Float(Id.SamplingFrequency, 48000), UInt(Id.Channels, 2)));
        var blocks = Enumerable.Range(0, 3).Select(i => MatroskaCraftedTests.Simple(2, (short)(i * 40), true, (byte)i)).ToArray();
        harness.Files["film.mkv"] = MatroskaCraftedTests.Mkv(MatroskaCraftedTests.Tracks(sound, video), MatroskaCraftedTests.Cluster(0, blocks));

        harness.Controller.Open(["film.mkv"]);
        harness.PumpUntil(c => c.Info is not null);

        Assert.StartsWith("Playing without sound: No decoder for Dolby TrueHD", harness.Messages[^1], StringComparison.Ordinal);
        var text = Logged(log);
        Assert.Contains("[WARN] player: Track 1 cannot be played: ", text, StringComparison.Ordinal);
        Assert.Contains("#1 audio Dolby TrueHD 48000 Hz 2 ch [eng] \"Main\" default", text, StringComparison.Ordinal);
        Assert.Contains("#2 video H.264 / AVC 16x8", text, StringComparison.Ordinal);
    }
}
