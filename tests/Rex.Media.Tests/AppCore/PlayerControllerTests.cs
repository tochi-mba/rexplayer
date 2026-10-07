using Rex.Media.AppCore.Commands;
using Rex.Media.Audio;
using Rex.Media.AppCore.Player;
using Rex.Media.Engine;
using Rex.Media.IO;
using Rex.Media.Primitives;
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
            File.WriteAllText(Path.Combine(folder, "2.srt"), "1\n00:00:00,000 --> 00:00:01,000\nTwo\n");
            using var harness = new ControllerHarness(openSource: path => new FileByteSource(path), diskFolders: true);

            harness.Controller.Open([folder]);
            harness.PumpUntil(c => EndedOn(c, "2"));

            Assert.Equal(Expected((0, 400), (500, 400)), harness.Played());
            Assert.Equal(["1", "2"], harness.Messages);
            Assert.Equal("Two", Assert.Single(harness.Controller.SubtitlesAt(TimeSpan.FromMilliseconds(500))).Text);
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
    [Capability("AU-18")]
    public void TheSoundBeingHeardCanBeReadForAVisualisation()
    {
        using var harness = new ControllerHarness(settings: new PlayerSettings { TitleSeconds = 0 });
        harness.Files["a.wav"] = Count(0, 400);
        var (left, right) = (new float[64], new float[64]);
        Assert.False(harness.Controller.ReadSound(left, right));
        Assert.Equal(0, harness.Controller.SoundSampleRate);

        harness.Controller.Open(["a.wav"]);
        harness.PumpUntil(c => EndedOn(c, "a"));

        Assert.True(harness.Controller.ReadSound(left, right));
        Assert.True(harness.Controller.SoundSampleRate > 0);
        Assert.Contains(left, sample => sample != 0);
    }

    /// <summary>English, a French commentary and French sound, with English and French subtitles.</summary>
    private static byte[] ManyLanguages()
    {
        static byte[] Language(string code) => EbmlWriter.Text(Rex.Media.Containers.Matroska.MatroskaId.Language, code);
        static byte[] Subtitles(int number, string code) => EbmlWriter.Element(
            Rex.Media.Containers.Matroska.MatroskaId.TrackEntry,
            EbmlWriter.UInt(Rex.Media.Containers.Matroska.MatroskaId.TrackNumber, (ulong)number),
            EbmlWriter.UInt(Rex.Media.Containers.Matroska.MatroskaId.TrackType, 17),
            EbmlWriter.Text(Rex.Media.Containers.Matroska.MatroskaId.CodecId, "S_TEXT/UTF8"),
            Language(code));
        var blocks = Enumerable.Range(1, 3).Select(track => Rex.Media.Tests.Containers.MatroskaCraftedTests.Simple(track, 0, true, Pcm.Int16(new float[320]))).ToArray();
        return Rex.Media.Tests.Containers.MatroskaCraftedTests.Mkv(
            Rex.Media.Tests.Containers.MatroskaCraftedTests.Tracks(
                Rex.Media.Tests.Containers.MatroskaCraftedTests.PcmTrack(1, Language("eng")),
                Rex.Media.Tests.Containers.MatroskaCraftedTests.PcmTrack(2, Language("fre"), EbmlWriter.Text(Rex.Media.Containers.Matroska.MatroskaId.Name, "Commentary")),
                Rex.Media.Tests.Containers.MatroskaCraftedTests.PcmTrack(3, Language("fre")),
                Subtitles(4, "eng"),
                Subtitles(5, "fre")),
            Rex.Media.Tests.Containers.MatroskaCraftedTests.Cluster(0, blocks));
    }

    [Fact]
    [Capability("AU-21")]
    public void SoundAndSubtitlesFollowThePreferredLanguages()
    {
        using var harness = new ControllerHarness(autoPlay: false, settings: new PlayerSettings { TitleSeconds = 0, AudioLanguages = "fr", SubtitleLanguages = "original" });
        harness.Files["film.mkv"] = ManyLanguages();
        var controller = harness.Controller;

        // French sound, passing over the commentary; subtitles in the film's own language, English.
        controller.Open(["film.mkv"]);
        harness.PumpUntil(c => c.State == SessionState.Ready && c.AudioTrack is not null);
        Assert.Equal(3, controller.AudioTrack);
        Assert.True(Languages.Same("en", controller.Subtitles!.Language));

        // No German sound, so the media's own choice; French subtitles before English.
        controller.Settings = controller.Settings with { AudioLanguages = "de", SubtitleLanguages = "fr, en" };
        controller.Open(["film.mkv"]);
        harness.PumpUntil(c => c.State == SessionState.Ready && c.AudioTrack is not null);
        Assert.Equal(1, controller.AudioTrack);
        Assert.True(Languages.Same("fr", controller.Subtitles!.Language));
    }

    [Fact]
    [Capability("AU-21")]
    public void AFileBesideTheMediaInAPreferredLanguageIsChosen()
    {
        using var harness = new ControllerHarness(autoPlay: false, settings: new PlayerSettings { TitleSeconds = 0, SubtitleLanguages = "fr" }, sidecars: new() { ["a.wav"] = ["a.en.srt", "a.fr.srt"] });
        harness.Files["a.wav"] = Count(0, 400);
        harness.Files["a.en.srt"] = Srt("1\n00:00:01,000 --> 00:00:03,000\nHello\n");
        harness.Files["a.fr.srt"] = Srt("1\n00:00:01,000 --> 00:00:03,000\nBonjour\n");

        harness.Controller.Open(["a.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Ready);

        Assert.Equal("a.fr.srt", harness.Controller.Subtitles!.Name);
        Assert.Equal("fr", harness.Controller.Subtitles.Language);
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

    [Fact]
    public void SpeedCommandsStepThroughTheSpeedsAndHoldForTheNextItem()
    {
        using var harness = new ControllerHarness(autoPlay: false, settings: new PlayerSettings { TitleSeconds = 0 });
        harness.Files["a.wav"] = Count(0, 400);
        harness.Files["b.wav"] = Count(0, 400);
        var controller = harness.Controller;

        var seen = new List<double>();
        foreach (var command in new[] { CommandCatalog.Faster, CommandCatalog.Faster, CommandCatalog.SlightlyFaster, CommandCatalog.Slower, CommandCatalog.NormalSpeed, CommandCatalog.SlightlySlower })
        {
            Assert.True(controller.Execute(command));
            seen.Add(controller.Speed);
        }

        Assert.Equal([1.25, 1.5, 1.6, 1.5, 1, 0.9], seen);
        Assert.Equal("Speed 0.9×", harness.Messages[^1]);

        controller.SetSpeed(99);
        Assert.Equal(4, controller.Speed);
        Assert.True(controller.Execute(CommandCatalog.Faster));
        Assert.Equal(4, controller.Speed);
        controller.SetSpeed(double.NaN);
        Assert.Equal(1, controller.Speed);
        controller.SetSpeed(0.1);
        Assert.True(controller.Execute(CommandCatalog.Slower));
        Assert.Equal(0.25, controller.Speed);

        controller.Open(["a.wav", "b.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Ready);
        controller.SetSpeed(2);
        Assert.True(controller.Execute(CommandCatalog.Next));
        harness.PumpUntil(c => c.State == SessionState.Ready && c.Item?.Title == "b");
        Assert.Equal(2, controller.Speed);
    }

    [Fact]
    public void AudioDelayMovesInStepsHoldsForTheNextItemAndResets()
    {
        using var harness = new ControllerHarness(autoPlay: false, settings: new PlayerSettings { TitleSeconds = 0 });
        harness.Files["a.wav"] = Count(0, 400);
        var controller = harness.Controller;

        Assert.True(controller.Execute(CommandCatalog.AudioLater));
        Assert.True(controller.Execute(CommandCatalog.AudioLater));
        Assert.True(controller.Execute(CommandCatalog.AudioEarlier));
        Assert.Equal(TimeSpan.FromMilliseconds(50), controller.AudioDelay);
        Assert.Equal("Audio delay 50 ms", harness.Messages[^1]);

        controller.Open(["a.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Ready);
        controller.SetAudioDelay(TimeSpan.FromSeconds(30));
        Assert.Equal(TimeSpan.FromSeconds(10), controller.AudioDelay);
        controller.SetAudioDelay(TimeSpan.FromSeconds(-30));
        Assert.Equal(TimeSpan.FromSeconds(-10), controller.AudioDelay);
        Assert.True(controller.Execute(CommandCatalog.ResetAudioDelay));
        Assert.Equal(TimeSpan.Zero, controller.AudioDelay);
    }

    private static byte[] Srt(string text) => System.Text.Encoding.UTF8.GetBytes(text);

    [Fact]
    [Capability("SUB-16")]
    [Capability("PB-20")]
    public void SubtitlesBesideTheMediaShowAndTheirCommandsWork()
    {
        using var harness = new ControllerHarness(autoPlay: false, settings: new PlayerSettings { TitleSeconds = 0 }, sidecars: new() { ["a.wav"] = ["a.srt"] });
        harness.Files["a.wav"] = Count(0, 8000 * 10);
        harness.Files["a.srt"] = Srt("1\n00:00:01,000 --> 00:00:03,000\nHello\n\n2\n00:00:05,000 --> 00:00:06,000\nAgain\n");
        harness.Files["extra.srt"] = Srt("1\n00:00:01,000 --> 00:00:02,000\nExtra\n");
        harness.Files["junk.srt"] = [1, 2, 3];
        var controller = harness.Controller;
        var changes = 0;
        controller.SubtitlesChanged += (_, _) => changes++;

        controller.Open(["a.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Ready);
        Assert.Equal("a.srt", controller.Subtitles!.Name);
        Assert.Equal(["Hello"], controller.SubtitlesAt(TimeSpan.FromSeconds(2)).Select(cue => cue.Text));
        Assert.Empty(controller.SubtitlesNow);

        Assert.True(controller.Execute(CommandCatalog.SubtitlesLater));
        Assert.True(controller.Execute(CommandCatalog.SubtitlesLater));
        Assert.Equal(TimeSpan.FromMilliseconds(100), controller.SubtitleDelay);
        Assert.Equal(["Hello"], controller.SubtitlesAt(TimeSpan.FromSeconds(3.05)).Select(cue => cue.Text));
        Assert.True(controller.Execute(CommandCatalog.SubtitlesEarlier));
        Assert.True(controller.Execute(CommandCatalog.ResetSubtitleDelay));
        controller.SetSubtitleDelay(TimeSpan.FromMinutes(1));
        Assert.Equal(TimeSpan.FromSeconds(10), controller.SubtitleDelay);
        controller.SetSubtitleDelay(TimeSpan.FromMinutes(-1));
        Assert.Equal(TimeSpan.FromSeconds(-10), controller.SubtitleDelay);

        Assert.True(controller.Execute(CommandCatalog.CycleSubtitles));
        Assert.Null(controller.Subtitles);
        Assert.Empty(controller.SubtitlesAt(TimeSpan.FromSeconds(2)));
        Assert.True(controller.Execute(CommandCatalog.CycleSubtitles));
        Assert.Equal("a.srt", controller.Subtitles!.Name);
        Assert.True(controller.Execute(CommandCatalog.ToggleSubtitles));
        Assert.Null(controller.Subtitles);
        Assert.True(controller.Execute(CommandCatalog.ToggleSubtitles));
        Assert.Equal("a.srt", controller.Subtitles!.Name);

        Assert.True(controller.AddSubtitles("extra.srt"));
        Assert.Equal(["extra.srt", "a.srt"], controller.SubtitleTracks.Select(track => track.Name));
        Assert.Equal("Subtitles: extra.srt", harness.Messages[^1]);
        Assert.False(controller.AddSubtitles("junk.srt"));
        Assert.False(controller.AddSubtitles("missing.srt"));
        Assert.Equal("missing.srt is not a subtitle file rexplayer can read.", harness.Messages[^1]);
        Assert.True(changes > 8);
        Assert.Throws<ArgumentNullException>(() => controller.AddSubtitles(null!));
    }

    [Fact]
    [Capability("SUB-16")]
    public void SubtitlesDroppedWithAFilmPlayWithIt()
    {
        using var harness = new ControllerHarness(autoPlay: false, settings: new PlayerSettings { TitleSeconds = 0 }, sidecars: new() { ["a.wav"] = ["a.srt"] });
        harness.Files["a.wav"] = Count(0, 400);
        harness.Files["a.srt"] = Srt("1\n00:00:01,000 --> 00:00:03,000\nBeside\n");
        harness.Files["Dropped.SRT"] = Srt("1\n00:00:01,000 --> 00:00:03,000\nDropped\n");
        var controller = harness.Controller;

        controller.Drop(["Dropped.SRT"]);
        Assert.Equal(PlayerController.NothingToSubtitle, harness.Messages[^1]);
        Assert.Null(controller.Item);

        controller.Drop(["a.wav", "Dropped.SRT"]);
        harness.PumpUntil(c => c.State == SessionState.Ready);
        Assert.Equal(["Dropped.SRT", "a.srt"], controller.SubtitleTracks.Select(track => track.Name));
        Assert.Equal(["Dropped"], controller.SubtitlesAt(TimeSpan.FromSeconds(2)).Select(cue => cue.Text));

        // A subtitle file on its own joins what is playing.
        controller.ShowSubtitles(null);
        controller.Drop(["a.srt"], enqueue: true);
        Assert.Equal(["Beside"], controller.SubtitlesAt(TimeSpan.FromSeconds(2)).Select(cue => cue.Text));
        Assert.Equal(3, controller.SubtitleTracks.Count);
        Assert.Throws<ArgumentNullException>(() => controller.Drop(null!));
    }

    [Fact]
    [Capability("OSD-04")]
    public void ASecondTrackShowsBesideTheFirst()
    {
        using var harness = new ControllerHarness(autoPlay: false, settings: new PlayerSettings { TitleSeconds = 0 }, sidecars: new() { ["a.wav"] = ["a.en.srt", "a.fr.srt"] });
        harness.Files["a.wav"] = Count(0, 400);
        harness.Files["a.en.srt"] = Srt("1\n00:00:01,000 --> 00:00:03,000\nHello\n");
        harness.Files["a.fr.srt"] = Srt("1\n00:00:01,000 --> 00:00:03,000\nBonjour\n");
        var controller = harness.Controller;
        var changes = 0;
        controller.SubtitlesChanged += (_, _) => changes++;

        controller.Open(["a.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Ready);
        Assert.Null(controller.SecondarySubtitles);
        Assert.Empty(controller.SecondarySubtitlesAt(TimeSpan.FromSeconds(2)));

        // The main track is passed over: the only other one is French, then off.
        Assert.True(controller.Execute(CommandCatalog.CycleSecondarySubtitles));
        Assert.Equal("a.fr.srt", controller.SecondarySubtitles!.Name);
        Assert.Equal("Second subtitles: a.fr.srt", harness.Messages[^1]);
        Assert.Equal(["Bonjour"], controller.SecondarySubtitlesAt(TimeSpan.FromSeconds(2)).Select(cue => cue.Text));
        Assert.Equal(["Hello"], controller.SubtitlesAt(TimeSpan.FromSeconds(2)).Select(cue => cue.Text));
        Assert.True(controller.Execute(CommandCatalog.CycleSecondarySubtitles));
        Assert.Null(controller.SecondarySubtitles);
        Assert.Equal("Second subtitles off", harness.Messages[^1]);

        // Making the second track the main one stops showing it twice.
        controller.ShowSecondarySubtitles(controller.SubtitleTracks[1]);
        controller.ShowSubtitles(controller.SubtitleTracks[1]);
        Assert.Null(controller.SecondarySubtitles);
        controller.ShowSecondarySubtitles(controller.SubtitleTracks[0]);
        controller.ShowSubtitles(null);
        Assert.Equal("a.en.srt", controller.SecondarySubtitles!.Name);
        Assert.True(changes >= 6);
    }

    [Fact]
    public void SubtitleFilesWithoutAMarkAreReadInTheChosenCodePage()
    {
        using var harness = new ControllerHarness(autoPlay: false, settings: new PlayerSettings { TitleSeconds = 0, SubtitleCodePage = 1251 }, sidecars: new() { ["a.wav"] = ["a.srt"] });
        harness.Files["a.wav"] = Count(0, 400);
        harness.Files["a.srt"] = [.. System.Text.Encoding.ASCII.GetBytes("1\n00:00:01,000 --> 00:00:03,000\n"), 0xC4, 0xE0];

        harness.Controller.Open(["a.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Ready);

        Assert.Equal("\u0414\u0430", Assert.Single(harness.Controller.SubtitlesAt(TimeSpan.FromSeconds(2))).Text);
    }

    [Fact]
    public void ToggleWithNothingChosenBeforeShowsTheFirstTrack()
    {
        using var harness = new ControllerHarness(autoPlay: false, settings: new PlayerSettings { TitleSeconds = 0 }, sidecars: new() { ["a.wav"] = ["a.srt"] });
        harness.Files["a.wav"] = Count(0, 400);
        harness.Files["a.srt"] = Srt("1\n00:00:01,000 --> 00:00:03,000\nHello\n");
        var controller = harness.Controller;

        Assert.True(controller.Execute(CommandCatalog.ToggleSubtitles));
        Assert.Null(controller.Subtitles);

        controller.Open(["a.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Ready);
        controller.ShowSubtitles(null);
        controller.Open(["a.wav"]);
        harness.PumpUntil(c => c.State == SessionState.Ready);
        controller.ShowSubtitles(null);
        Assert.True(controller.Execute(CommandCatalog.ToggleSubtitles));
        Assert.Equal("a.srt", controller.Subtitles!.Name);
    }

    [Fact]
    public void TheMediasOwnDefaultSubtitlesShowAsTheirCuesArrive()
    {
        using var harness = new ControllerHarness(settings: new PlayerSettings { TitleSeconds = 0 });
        var track = EbmlWriter.Element(Rex.Media.Containers.Matroska.MatroskaId.TrackEntry, EbmlWriter.UInt(Rex.Media.Containers.Matroska.MatroskaId.TrackNumber, 3), EbmlWriter.UInt(Rex.Media.Containers.Matroska.MatroskaId.TrackType, 17), EbmlWriter.Text(Rex.Media.Containers.Matroska.MatroskaId.CodecId, "S_TEXT/UTF8"), EbmlWriter.Text(Rex.Media.Containers.Matroska.MatroskaId.Language, "eng"));
        harness.Files["film.mkv"] = Rex.Media.Tests.Containers.MatroskaCraftedTests.Mkv(
            Rex.Media.Tests.Containers.MatroskaCraftedTests.Tracks(Rex.Media.Tests.Containers.MatroskaCraftedTests.PcmTrack(1), track),
            Rex.Media.Tests.Containers.MatroskaCraftedTests.Cluster(0, Rex.Media.Tests.Containers.MatroskaCraftedTests.Simple(1, 0, true, Pcm.Int16(new float[800])), Rex.Media.Tests.Containers.MatroskaCraftedTests.Simple(3, 10, true, Srt("Inside"))));

        harness.Controller.Open(["film.mkv"]);
        harness.PumpUntil(c => EndedOn(c, "film"));

        Assert.Equal("eng", harness.Controller.Subtitles!.Name);
        Assert.Equal(["Inside"], harness.Controller.SubtitlesAt(TimeSpan.FromSeconds(1)).Select(cue => cue.Text));
    }

    [Fact]
    public void TheNextItemOfAGaplessRunBringsItsOwnSubtitles()
    {
        using var harness = new ControllerHarness(settings: new PlayerSettings { TitleSeconds = 0 }, sidecars: new() { ["b.wav"] = ["b.srt"] });
        harness.Files["a.wav"] = Count(0, 400);
        harness.Files["b.wav"] = Count(0, 400);
        harness.Files["b.srt"] = Srt("1\n00:00:00,000 --> 00:00:01,000\nB\n");

        var seen = new List<string>();
        harness.Controller.SubtitlesChanged += (_, _) => seen.AddRange(harness.Controller.SubtitleTracks.Select(track => track.Name));

        harness.Controller.Open(["a.wav", "b.wav", "https://example.com/c.mp3"]);
        harness.PumpUntil(c => c.Item?.Title == "c");

        Assert.Empty(harness.Controller.SubtitleTracks);
        Assert.Contains("b.srt", seen);
    }
}
