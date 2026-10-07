using Rex.Media.AppCore.Commands;
using Rex.Media.Audio;
using Rex.Media.AppCore.Player;
using Rex.Media.Engine;
using Rex.Media.IO;
using Rex.Media.Settings;
using Rex.Media.TestKit;
using static Rex.Media.Tests.AppCore.ControllerHarness;

namespace Rex.Media.Tests.AppCore;

public sealed class PlayerControllerTests
{
    /// <summary>
    /// The playlist has played to its end, on <paramref name="title"/>. The state changes to Ended a
    /// moment before the end itself is reported, so both are waited for.
    /// </summary>
    private static bool EndedOn(PlayerController controller, string title) =>
        controller.State == SessionState.Ended && controller.Item?.Title == title && controller.Playlist.Current is null;

    [Fact]
    [Capability("PB-14")]
    [Capability("VID-26")]
    public void APlaylistPlaysItsItemsOneAfterAnotherSampleForSample()
    {
        using var harness = new ControllerHarness();
        harness.Files["a.wav"] = Count(0, 800);
        harness.Files["b.wav"] = Count(1000, 800);
        harness.Files["c.wav"] = Count(2000, 400);

        harness.Controller.Open(["a.wav", "b.wav", "c.wav"]);
        harness.PumpUntil(controller => EndedOn(controller, "c"));

        Assert.Equal(Expected((0, 800), (1000, 800), (2000, 400)), harness.Played());
        Assert.Equal(["a", "b", "c"], harness.Messages);
        Assert.Equal(TimeSpan.FromSeconds(0.05), harness.Controller.Duration);
        Assert.Equal(harness.Controller.Duration, harness.Controller.Position);
        Assert.Null(harness.Controller.Playlist.Current);
        Assert.False(harness.Controller.IsPlaying);
        Assert.True(harness.PositionChanges > 3);
    }

    [Fact]
    public void AnItemQueuedOnlyAfterTheEndStillFollowsInTheSameSession()
    {
        using var harness = new ControllerHarness();
        harness.Files["a.wav"] = Count(0, 400);
        harness.Files["b.wav"] = Count(500, 400);

        harness.Controller.Open(["a.wav", "b.wav"]);
        harness.WaitForEngine<EndedEvent>();
        harness.PumpUntil(controller => EndedOn(controller, "b"));

        Assert.Single(harness.Sinks);
        Assert.Equal(Expected((0, 400), (500, 400)), harness.Played());
    }

    [Fact]
    public void AQueuedItemThatWillNotOpenIsSkippedAndTheOneAfterItPlays()
    {
        using var harness = new ControllerHarness();
        harness.Files["a.wav"] = Count(0, 400);
        harness.Files["broken.wav"] = [1, 2, 3, 4];
        harness.Files["c.wav"] = Count(500, 400);

        harness.Controller.Open(["a.wav", "broken.wav", "c.wav"]);
        harness.WaitForEngine<EndedEvent>();
        harness.PumpUntil(controller => EndedOn(controller, "c"));

        Assert.Contains(harness.Messages, m => m.StartsWith("broken.wav could not be played", StringComparison.Ordinal));
        Assert.Equal(Expected((0, 400), (500, 400)), harness.Played());
        Assert.Equal(2, harness.Sinks.Count);
    }

    [Fact]
    public void RepeatOnePlaysTheItemAgainUntilStopped()
    {
        using var harness = new ControllerHarness(settings: new PlayerSettings { Repeat = RepeatMode.One });
        harness.Files["a.wav"] = Count(0, 200);

        harness.Controller.Open(["a.wav"]);
        Assert.True(harness.Controller.IsPlaying);
        harness.PumpUntil(_ => harness.Played().Length >= 600);
        harness.Controller.Stop();

        Assert.Equal(Expected((0, 200), (0, 200), (0, 200)), harness.Played()[..600]);
        Assert.Equal(SessionState.Idle, harness.Controller.State);
        Assert.Equal(TimeSpan.Zero, harness.Controller.Position);
    }

    [Fact]
    public void FilesThatCannotBeOpenedOrReadAreReportedAndSkipped()
    {
        using var harness = new ControllerHarness();
        harness.Files["not-media.wav"] = [9, 9, 9, 9, 9, 9, 9, 9];
        harness.Files["a.wav"] = Count(0, 400);

        harness.Controller.Open(["missing.wav", "not-media.wav", "a.wav"]);
        harness.PumpUntil(controller => EndedOn(controller, "a"));

        Assert.Equal("missing could not be played: missing.wav is missing.", harness.Messages[0]);
        Assert.StartsWith("not-media could not be played: ", harness.Messages[1], StringComparison.Ordinal);
        Assert.Equal(Expected((0, 400)), harness.Played());
        Assert.Null(harness.Controller.Failure);
    }

    [Fact]
    public void WhenEveryEntryFailsPlaybackStopsInsteadOfGoingRoundForever()
    {
        using var harness = new ControllerHarness(settings: new PlayerSettings { Repeat = RepeatMode.All });

        harness.Controller.Open(["one.wav", "two.wav"]);
        harness.Pump();

        Assert.Equal(SessionState.Faulted, harness.Controller.State);
        Assert.Equal("two.wav is missing.", harness.Controller.Failure);
        Assert.Equal(2, harness.Messages.Count);
    }

    [Fact]
    public void ANextItemThatCannotBeReadIsReportedWhenItsTurnComes()
    {
        using var harness = new ControllerHarness();
        harness.Files["a.wav"] = Count(0, 400);

        harness.Controller.Open(["a.wav", "gone.wav"]);
        harness.PumpUntil(controller => controller.State == SessionState.Faulted);

        Assert.Equal("gone", harness.Controller.Item!.Title);
        Assert.Equal(["a", "gone could not be played: gone.wav is missing."], harness.Messages);
    }

    [Fact]
    public void ReportsFromMediaTheUserHasMovedAwayFromAreIgnored()
    {
        using var harness = new ControllerHarness();
        harness.Files["not-media.wav"] = [9, 9, 9, 9, 9, 9, 9, 9];
        harness.Files["a.wav"] = Count(0, 400);

        harness.Controller.Open(["not-media.wav", "a.wav"]);
        harness.WaitForEngine<StateChangedEvent>(2);
        harness.Controller.PlayAt(1);
        harness.PumpUntil(controller => EndedOn(controller, "a"));

        Assert.Equal(["a"], harness.Messages);
        Assert.Null(harness.Controller.Failure);
    }

    [Fact]
    public void TransportCommandsDriveThePlaylistAndTheSession()
    {
        using var harness = new ControllerHarness(autoPlay: false);
        harness.Files["a.wav"] = Count(0, 40_000);
        harness.Files["b.wav"] = Count(0, 400);
        var controller = harness.Controller;
        var changes = 0;
        controller.Changed += (_, _) => changes++;

        Assert.True(controller.Execute(CommandCatalog.PlayPause));
        Assert.Equal(SessionState.Idle, controller.State);

        controller.Open(["a.wav", "b.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Ready && c.Info is not null);
        Assert.True(controller.CanSeek);
        Assert.Equal("a", controller.Title);
        Assert.Equal(TimeSpan.FromSeconds(5), controller.Duration);

        controller.Seek(TimeSpan.FromSeconds(4));
        Assert.Equal(TimeSpan.FromSeconds(4), controller.Position);
        Assert.True(controller.Execute(CommandCatalog.Previous));
        Assert.Equal(TimeSpan.Zero, controller.Position);

        Assert.True(controller.Execute(CommandCatalog.Next));
        harness.PumpUntil(c => c.State == SessionState.Ready && c.Item?.Title == "b");
        Assert.True(controller.Execute(CommandCatalog.Previous));
        harness.PumpUntil(c => c.State == SessionState.Ready && c.Item?.Title == "a");

        Assert.True(controller.Execute(CommandCatalog.Stop));
        Assert.Equal(SessionState.Idle, controller.State);
        Assert.True(controller.Execute(CommandCatalog.PlayPause));
        harness.PumpUntil(c => c.State == SessionState.Ready && c.Item?.Title == "a");

        controller.PlayAt(1);
        harness.PumpUntil(c => c.State == SessionState.Ready && c.Item?.Title == "b");
        Assert.True(controller.Execute(CommandCatalog.Next));
        Assert.Equal(SessionState.Idle, controller.State);
        Assert.Null(controller.Playlist.Current);

        Assert.True(controller.Execute(CommandCatalog.PlayPause));
        harness.PumpUntil(c => c.State == SessionState.Ready && c.Item?.Title == "a");

        // Ready, so play: the first item plays through and the second follows it in the same session.
        Assert.True(controller.Execute(CommandCatalog.PlayPause));
        harness.PumpUntil(c => EndedOn(c, "b"));
        Assert.True(changes > 10);
        Assert.False(controller.Execute(CommandCatalog.ToggleFullScreen));
    }

    [Fact]
    [Capability("PB-04")]
    public void JumpsMoveWithinTheMediaAndSayWhereTheyLanded()
    {
        using var harness = new ControllerHarness(autoPlay: false, settings: new PlayerSettings { TitleSeconds = 0 });
        harness.Files["a.wav"] = Count(0, 8000 * 400);
        var controller = harness.Controller;

        Assert.True(controller.Execute(CommandCatalog.JumpForwardShort));
        controller.Seek(TimeSpan.FromSeconds(1));
        Assert.Empty(harness.Messages);

        controller.Open(["a.wav"]);
        harness.PumpUntil(c => c.Info is not null);
        foreach (var (command, expected) in new[]
        {
            (CommandCatalog.JumpForwardLong, 300), (CommandCatalog.JumpForwardMedium, 360), (CommandCatalog.JumpForwardShort, 370),
            (CommandCatalog.JumpForwardVeryShort, 373), (CommandCatalog.JumpBackVeryShort, 370), (CommandCatalog.JumpBackShort, 360),
            (CommandCatalog.JumpBackMedium, 300), (CommandCatalog.JumpBackLong, 0), (CommandCatalog.JumpBackLong, 0),
        })
        {
            Assert.True(controller.Execute(command));
            Assert.Equal(TimeSpan.FromSeconds(expected), controller.Position);
        }

        controller.Seek(TimeSpan.FromHours(1));
        Assert.Equal(TimeSpan.FromSeconds(400), controller.Position);
        Assert.Equal(["5:00", "6:00", "6:10", "6:13", "6:10", "6:00", "5:00", "0:00", "0:00"], harness.Messages);

        Assert.True(controller.Execute(CommandCatalog.ShowPosition));
        Assert.Equal("6:40 / 6:40", harness.Messages[^1]);
    }

    [Fact]
    [Capability("OSD-07")]
    public void VolumeMuteRepeatAndShuffleChangeAndSaySo()
    {
        using var harness = new ControllerHarness(autoPlay: false, settings: new PlayerSettings { Volume = 1.2, Muted = true, Shuffle = true });
        harness.Files["a.wav"] = Count(0, 400);
        var controller = harness.Controller;
        Assert.True(controller.Muted);
        Assert.True(controller.Playlist.Shuffle);

        Assert.True(controller.Execute(CommandCatalog.VolumeUp));
        Assert.True(controller.Execute(CommandCatalog.VolumeUp));
        Assert.Equal(1.25, controller.Volume);
        controller.Open(["a.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Ready);
        Assert.True(controller.Execute(CommandCatalog.VolumeDown));
        Assert.Equal(1.2, controller.Volume);
        Assert.True(controller.Execute(CommandCatalog.Mute));
        Assert.True(controller.Execute(CommandCatalog.Mute));
        controller.SetVolume(double.NaN);
        Assert.Equal(1, controller.Volume);

        foreach (var command in new[] { CommandCatalog.CycleRepeat, CommandCatalog.CycleRepeat, CommandCatalog.CycleRepeat, CommandCatalog.ToggleShuffle, CommandCatalog.ToggleShuffle })
        {
            Assert.True(controller.Execute(command));
        }

        Assert.Equal(
            ["Volume 125 %", "Volume 125 %", "a", "Volume 120 %", "Sound on", "Muted", "Repeat all", "Repeat one", "Repeat off", "Shuffle off", "Shuffle on"],
            harness.Messages);
        Assert.Equal(RepeatMode.Off, controller.Playlist.Repeat);
    }

    [Fact]
    public void ShowingThePositionOfUnknownLengthGivesThePositionAlone()
    {
        using var harness = new ControllerHarness();

        Assert.True(harness.Controller.Execute(CommandCatalog.ShowPosition));

        Assert.Equal(["0:00"], harness.Messages);
    }

    [Fact]
    [Capability("LIB-02")]
    public void EnqueuedMediaWaitsItsTurnOrStartsWhenNothingPlays()
    {
        using var harness = new ControllerHarness(autoPlay: false);
        harness.Files["a.wav"] = Count(0, 400);
        harness.Files["b.wav"] = Count(0, 400);
        var controller = harness.Controller;

        controller.Open(["a.wav"], enqueue: true);
        harness.PumpUntil(c => c.State == SessionState.Ready && c.Item?.Title == "a");
        controller.Open(["b.wav"], enqueue: true);
        harness.Pump();

        Assert.Equal("a", controller.Item!.Title);
        Assert.Equal(["a", "b"], controller.Playlist.Items.Select(item => item.Title));

        controller.Open([]);
        Assert.Equal("There is nothing there rexplayer can play.", harness.Messages[^1]);
    }

    [Fact]
    public void FoldersOpenAsTheMediaInsideThem()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rexplayer-controller-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "2.wav"), Count(500, 400));
            File.WriteAllBytes(Path.Combine(folder, "1.wav"), Count(0, 400));
            using var harness = new ControllerHarness(openSource: path => new FileByteSource(path), diskFolders: true);

            harness.Controller.Open([folder]);
            harness.PumpUntil(c => EndedOn(c, "2"));

            Assert.Equal(Expected((0, 400), (500, 400)), harness.Played());
            Assert.Equal(["1", "2"], harness.Messages);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ACommandTheSessionRefusesFailsQuietly()
    {
        var observed = PlayerController.Observe(Task.FromException(new ObjectDisposedException("session")));

        await observed;
        Assert.True(observed.IsCompletedSuccessfully);
    }

    [Fact]
    public void EverythingTheControllerNeedsIsRequired()
    {
        Assert.Throws<ArgumentNullException>(() => new PlayerController(null!, _ => null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => new PlayerController(_ => null!, null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => new PlayerController(_ => null!, _ => null!, null!));
        Assert.Throws<ArgumentNullException>(() => new PlayerController(_ => null!, _ => null!, _ => { }).Open(null!));
    }

    /// <summary>A Matroska file with two 8 kHz PCM tracks, the second named "Commentary".</summary>
    private static byte[] TwoAudioTracks()
    {
        var blocks = Enumerable.Range(0, 5).SelectMany(i => new[]
        {
            Rex.Media.Tests.Containers.MatroskaCraftedTests.Simple(1, (short)(i * 40), true, Pcm.Int16(new float[320])),
            Rex.Media.Tests.Containers.MatroskaCraftedTests.Simple(2, (short)(i * 40), true, Pcm.Int16(new float[320])),
        }).ToArray();
        return Rex.Media.Tests.Containers.MatroskaCraftedTests.Mkv(
            EbmlWriter.Element(Rex.Media.Containers.Matroska.MatroskaId.Info, EbmlWriter.UInt(Rex.Media.Containers.Matroska.MatroskaId.TimestampScale, 1_000_000), EbmlWriter.Float(Rex.Media.Containers.Matroska.MatroskaId.Duration, 200)),
            Rex.Media.Tests.Containers.MatroskaCraftedTests.Tracks(
                Rex.Media.Tests.Containers.MatroskaCraftedTests.PcmTrack(1),
                Rex.Media.Tests.Containers.MatroskaCraftedTests.PcmTrack(2, EbmlWriter.Text(Rex.Media.Containers.Matroska.MatroskaId.Name, "Commentary"))),
            Rex.Media.Tests.Containers.MatroskaCraftedTests.Cluster(0, blocks));
    }

    [Fact]
    [Capability("PB-17")]
    public async Task TheNextAudioTrackCarriesOnFromTheSameMoment()
    {
        using var harness = new ControllerHarness(autoPlay: false, settings: new PlayerSettings { TitleSeconds = 0 });
        harness.Files["two.mkv"] = TwoAudioTracks();
        harness.Files["one.wav"] = Count(0, 400);
        var controller = harness.Controller;

        Assert.True(controller.Execute(CommandCatalog.CycleAudioTrack));
        Assert.Equal("There are no audio tracks to choose from.", harness.Messages[^1]);
        Assert.Null(await controller.StatsAsync());

        controller.Open(["two.mkv"]);
        harness.PumpUntil(c => c.State == SessionState.Ready && c.AudioTrack == 1);
        Assert.Equal(2, controller.AudioTracks.Count);
        Assert.NotNull(await controller.StatsAsync());

        controller.Seek(TimeSpan.FromMilliseconds(100));
        Assert.True(controller.Execute(CommandCatalog.CycleAudioTrack));
        Assert.Equal("Audio track 2 of 2: Commentary", harness.Messages[^1]);
        Assert.Equal(TimeSpan.FromMilliseconds(100), controller.Position);
        harness.PumpUntil(c => c.State == SessionState.Ready && c.AudioTrack == 2);

        Assert.True(controller.Execute(CommandCatalog.CycleAudioTrack));
        Assert.StartsWith("Audio track 1 of 2: ", harness.Messages[^1], StringComparison.Ordinal);
        harness.PumpUntil(c => c.State == SessionState.Ready && c.AudioTrack == 1);

        controller.Open(["one.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Ready && c.Item?.Title == "one");
        Assert.True(controller.Execute(CommandCatalog.CycleAudioTrack));
        Assert.Equal("There is only one audio track.", harness.Messages[^1]);
    }

    [Fact]
    [Capability("UI-07")]
    public void SoundSettingsReachWhatIsPlayingAtOnce()
    {
        using var harness = new ControllerHarness(autoPlay: false, settings: new PlayerSettings { TitleSeconds = 0 });
        harness.Files["a.wav"] = Count(0, 400);
        var controller = harness.Controller;
        Assert.Same(SoundSettings.Plain.ReplayGain, ReplayGainSettings.Off);
        Assert.False(controller.Sound.Equalizer.IsAudible);

        controller.Open(["a.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Ready);
        controller.Settings = controller.Settings with { EqualizerEnabled = true, EqualizerGains = [6, 0, 0, 0, 0, 0, 0, 0, 0, 0], Stereo = StereoChoice.Mono, Loudness = LoudnessChoice.Album, LoudnessPreamp = 3 };

        Assert.True(controller.Sound.Equalizer.IsAudible);
        Assert.Equal((StereoMode.Mono, ReplayGainMode.Album, 3.0), (controller.Sound.StereoMode, controller.Sound.ReplayGain.Mode, controller.Sound.ReplayGain.Preamp));
        Assert.Throws<ArgumentNullException>(() => controller.Settings = null!);
    }

    [Fact]
    public void TheSettingsChoicesNameTheEnginesOwnInTheSameOrder()
    {
        Assert.Equal(Enum.GetNames<StereoMode>(), Enum.GetNames<StereoChoice>());
        Assert.Equal(Enum.GetNames<ReplayGainMode>(), Enum.GetNames<LoudnessChoice>());
        Assert.Equal(Enum.GetValues<StereoMode>().Select(value => (int)value), Enum.GetValues<StereoChoice>().Select(value => (int)value));
        Assert.Equal(Enum.GetValues<ReplayGainMode>().Select(value => (int)value), Enum.GetValues<LoudnessChoice>().Select(value => (int)value));
    }
}
