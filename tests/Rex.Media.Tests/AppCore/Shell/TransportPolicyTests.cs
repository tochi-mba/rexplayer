using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Player;
using Rex.Media.AppCore.Shell;
using Rex.Media.Engine;
using Rex.Media.IO;
using Rex.Media.TestKit;
using Rex.Media.TestKit.Damage;

namespace Rex.Media.Tests.AppCore.Shell;

/// <summary>The transport, media-key and shell policies are independent of the Windows shell.</summary>
public sealed class TransportPolicyTests
{
    private static TransportInput Input(SessionState state = SessionState.Playing, bool playing = true,
        TransportMedia media = TransportMedia.Video, string? previous = "Earlier",
        string? following = "Later", string? episode = null, bool restarts = false) =>
        new(state, playing, true, media, "Current", previous, following, episode, restarts);

    [Fact]
    public void RealPlayerTransportAndMediaCardFollowTheCurrentPlaylistItem()
    {
        using var harness = new ControllerHarness(autoPlay: false);
        var player = harness.Controller;
        Assert.Throws<ArgumentNullException>(() => TransportAvailability.Read(null!));
        Assert.Throws<ArgumentNullException>(() => TransportAvailability.MediaOf(null!));
        Assert.Throws<ArgumentNullException>(() => MediaCard.For(null!));
        Assert.Equal(TransportMedia.None, TransportAvailability.MediaOf(player));
        Assert.Equal(TransportAvailability.Nothing, TransportAvailability.Decide(TransportAvailability.Read(player)));
        Assert.Null(MediaCard.For(player));

        harness.Files["first.wav"] = ControllerHarness.Count(0, 4000);
        harness.Files["second.wav"] = ControllerHarness.Count(3000, 4000);
        player.Open(["first.wav", "second.wav"]);
        harness.PumpUntil(c => c.Item?.Location == "first.wav" && c.State == SessionState.Ready);

        var first = TransportAvailability.Read(player);
        Assert.True(first.HasQueue);
        Assert.Equal(TransportMedia.Audio, first.Media);
        Assert.Equal("second", first.FollowingTitle);
        Assert.Null(first.EarlierTitle);
        Assert.Equal("first", MediaCard.For(player)!.Title);
        Assert.Equal(MediaCardKind.Music, MediaCard.For(player)!.Kind);

        player.Execute(CommandCatalog.Next);
        harness.PumpUntil(c => c.Item?.Location == "second.wav" && c.State == SessionState.Ready);
        var second = TransportAvailability.Read(player);
        Assert.Equal("first", second.EarlierTitle);
        Assert.Null(second.FollowingTitle);
        Assert.Equal(TransportMedia.Audio, second.Media);

        player.Stop();
        harness.Pump();
        Assert.Equal(TransportMedia.None, TransportAvailability.MediaOf(player));
    }

    [Fact]
    public void OpeningPictureAndNonSeekableMediaUsesTheRightKind()
    {
        using (var pictures = new ControllerHarness(autoPlay: false))
        {
            pictures.Files["poster.png"] = ControllerHarness.Count(0, 1200);
            pictures.Controller.Open(["poster.png"]);
            Assert.Equal(TransportMedia.Picture, TransportAvailability.MediaOf(pictures.Controller));
            Assert.Equal(MediaCardKind.Image, MediaCard.For(pictures.Controller)!.Kind);
        }

        using (var stream = new ControllerHarness(autoPlay: false, openSource: path =>
            new ForwardOnlySource(ControllerHarness.Count(0, 4000), path)))
        {
            stream.Controller.Open(["live.wav"]);
            stream.PumpUntil(c => c.State == SessionState.Ready);
            Assert.Equal(TransportMedia.Stream, TransportAvailability.MediaOf(stream.Controller));
            Assert.Equal(MediaCardKind.Music, MediaCard.For(stream.Controller)!.Kind);
        }

        using (var video = new ControllerHarness(autoPlay: false, pictures: true))
        {
            video.Files["film.mkv"] = HealthyMedia.Matroska(2).Bytes;
            video.Controller.Open(["film.mkv"]);
            video.PumpUntil(c => c.State == SessionState.Ready);
            Assert.Equal(TransportMedia.Video, TransportAvailability.MediaOf(video.Controller));
            Assert.Equal(MediaCardKind.Video, MediaCard.For(video.Controller)!.Kind);
        }
    }

    private sealed class ForwardOnlySource(byte[] data, string name) : IByteSource
    {
        public string Name => name;
        public long? Length => null;
        public bool CanSeek => false;
        public int Read(long position, Span<byte> destination, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (position < 0 || position >= data.Length)
            {
                return 0;
            }

            var count = Math.Min(destination.Length, data.Length - (int)position);
            data.AsSpan((int)position, count).CopyTo(destination);
            return count;
        }

        public void Dispose()
        {
        }
    }

    [Fact]
    public void EmptyTransportHasNoActionsAndAnItemExposesOnlyUsefulCommands()
    {
        var empty = TransportAvailability.Decide(new TransportInput(
            SessionState.Idle, false, false, TransportMedia.None, "", null, null, null, false));
        Assert.Equal(TransportAvailability.Nothing, empty);
        Assert.False(empty.PlayPause.IsVisible);
        Assert.Null(TransportAvailability.CommandFor(TransportRequest.PlayPause, empty));

        var view = TransportAvailability.Decide(Input());
        Assert.True(view.Previous.IsEnabled);
        Assert.True(view.Next.IsEnabled);
        Assert.True(view.Stop.IsEnabled);
        Assert.True(view.ShowsPause);
        Assert.Equal(TransportStatus.Playing, view.Status);
        Assert.Equal(TransportBadge.None, view.Badge);
        Assert.Equal(CommandCatalog.Previous, TransportAvailability.CommandFor(TransportRequest.Previous, view));
        Assert.Equal(CommandCatalog.Next, TransportAvailability.CommandFor(TransportRequest.Next, view));
        Assert.Equal(CommandCatalog.Stop, TransportAvailability.CommandFor(TransportRequest.Stop, view));
        Assert.Equal(CommandCatalog.PlayPause, TransportAvailability.CommandFor(TransportRequest.Pause, view));
        Assert.Null(TransportAvailability.CommandFor(TransportRequest.Play, view));

        var stopped = TransportAvailability.Decide(Input(SessionState.Idle, false, TransportMedia.Audio, null, null));
        Assert.False(stopped.Stop.IsEnabled);
        Assert.False(stopped.Previous.IsVisible);
        Assert.False(stopped.Next.IsVisible);
        Assert.Equal(TransportStatus.Stopped, stopped.Status);
        Assert.Equal("Play", stopped.PlayPause.Tooltip);
        Assert.Equal(CommandCatalog.PlayPause, TransportAvailability.CommandFor(TransportRequest.Play, stopped));
        Assert.Null(TransportAvailability.CommandFor(TransportRequest.Pause, stopped));
        Assert.Null(TransportAvailability.CommandFor(TransportRequest.Stop, stopped));
        Assert.Null(TransportAvailability.CommandFor(TransportRequest.Next, stopped));
        Assert.Null(TransportAvailability.CommandFor((TransportRequest)999, stopped));
        Assert.Null(TransportAvailability.CommandFor(TransportRequest.Previous, stopped));
    }

    [Fact]
    public void PlayStatusAndTooltipsFollowTheItemAndNotTheShell()
    {
        Assert.Equal("Pause the slideshow", TransportAvailability.Decide(Input(media: TransportMedia.Picture)).PlayPause.Tooltip);
        Assert.Equal("Play the slideshow", TransportAvailability.Decide(Input(playing: false, media: TransportMedia.Picture)).PlayPause.Tooltip);
        Assert.Equal("Play again", TransportAvailability.Decide(Input(SessionState.Ended, false)).PlayPause.Tooltip);
        Assert.Equal("Try again", TransportAvailability.Decide(Input(SessionState.Faulted, false)).PlayPause.Tooltip);
        Assert.Equal(TransportBadge.Failed, TransportAvailability.Decide(Input(SessionState.Faulted, false)).Badge);
        Assert.Equal(TransportBadge.Paused, TransportAvailability.Decide(Input(SessionState.Paused, false)).Badge);
        Assert.Equal(TransportBadge.Paused, TransportAvailability.Decide(Input(SessionState.Ready, false)).Badge);
        Assert.Equal(TransportStatus.Changing, TransportAvailability.Decide(Input(SessionState.Opening)).Status);
        Assert.Equal(TransportStatus.Playing, TransportAvailability.Decide(Input(SessionState.Buffering)).Status);
        Assert.Equal(TransportStatus.Playing, TransportAvailability.Decide(Input(SessionState.Seeking)).Status);
        Assert.Equal(TransportStatus.Paused, TransportAvailability.Decide(Input(SessionState.Ready)).Status);
        Assert.Contains("Back to the start", TransportAvailability.Decide(Input(restarts: true)).Previous.Tooltip, StringComparison.Ordinal);

        var episode = TransportAvailability.Decide(Input(following: null, episode: @"C:\Series\Show.S01E02.mkv"));
        Assert.True(episode.Next.IsEnabled);
        Assert.Equal(CommandCatalog.NextEpisode, episode.Next.Command);
        Assert.Equal(CommandCatalog.PlayPause, TransportAvailability.CommandFor(TransportRequest.PlayPause, episode));
    }

    [Fact]
    public void TransportTooltipsAreBoundedAndHidePrivatePaths()
    {
        Assert.Equal("short", TransportAvailability.Fit("short"));
        Assert.Equal(TransportAvailability.MaxTooltip, TransportAvailability.Fit(new string('x', 400)).Length);
        Assert.Throws<ArgumentNullException>(() => TransportAvailability.Fit(null!));
        var text = new string('x', 255) + "\ud83d\ude00" + new string('y', 20);
        var cut = TransportAvailability.Fit(text);
        Assert.False(char.IsHighSurrogate(cut[^4]));

        var privateTitle = TransportAvailability.Decide(Input(previous: Path.GetFullPath("private/film.mp4")));
        Assert.Equal("Previous", privateTitle.Previous.Tooltip);
    }

    [Fact]
    public void MediaKeysAreHandledOnceEvenWhenWindowsEchoesThem()
    {
        Assert.Equal(TransportRequest.Next, MediaKeyEcho.RequestOfKey(0xB0));
        Assert.Equal(TransportRequest.Previous, MediaKeyEcho.RequestOfKey(0xB1));
        Assert.Equal(TransportRequest.Stop, MediaKeyEcho.RequestOfKey(0xB2));
        Assert.Equal(TransportRequest.PlayPause, MediaKeyEcho.RequestOfKey(0xB3));
        Assert.Null(MediaKeyEcho.RequestOfKey(0x30));

        var echo = new MediaKeyEcho();
        Assert.False(echo.IsEcho(TransportRequest.PlayPause, TimeSpan.Zero));
        echo.Handled(TransportRequest.PlayPause, TimeSpan.FromSeconds(1));
        Assert.False(echo.IsEcho(TransportRequest.Play, TimeSpan.Zero));
        Assert.False(echo.IsEcho(TransportRequest.Next, TimeSpan.FromSeconds(1)));
        Assert.True(echo.IsEcho(TransportRequest.Pause, TimeSpan.FromSeconds(1.1)));
        Assert.False(echo.IsEcho(TransportRequest.Pause, TimeSpan.FromSeconds(1.2)));
        echo.Handled(TransportRequest.Previous, TimeSpan.FromSeconds(2));
        Assert.False(echo.IsEcho(TransportRequest.Previous, TimeSpan.FromSeconds(3)));
        echo.Handled(TransportRequest.Stop, TimeSpan.FromSeconds(4));
        Assert.True(echo.IsEcho(TransportRequest.Stop, TimeSpan.FromSeconds(4.5)));

        for (var i = 0; i < 10; i++)
        {
            echo.Handled(TransportRequest.Next, TimeSpan.FromSeconds(10 + i));
        }

        Assert.True(echo.IsEcho(TransportRequest.Next, TimeSpan.FromSeconds(19)));
    }

    [Fact]
    public void TaskbarProgressBelongsToBackgroundWorkNotPlaybackPosition()
    {
        Assert.Equal(TaskbarProgressView.Off, TaskbarProgress.Choose(null, null, false));
        Assert.Equal(new TaskbarProgressView(TaskbarProgressState.Normal, 25), TaskbarProgress.Choose(0.25, 0.9, true));
        Assert.Equal(new TaskbarProgressView(TaskbarProgressState.Paused, 100), TaskbarProgress.Choose(null, 3, true));
        Assert.Equal(new TaskbarProgressView(TaskbarProgressState.Normal, 0), TaskbarProgress.Choose(null, -1, false));
        Assert.Equal(new TaskbarProgressView(TaskbarProgressState.Normal, 0), TaskbarProgress.Choose(double.NaN, null, false));
    }

    [Fact]
    public void WindowsTimelineNeedsOnlyMeaningfulChangesAndPeriodicUpdates()
    {
        var policy = new TimelineThrottle();
        var duration = TimeSpan.FromMinutes(2);
        Assert.True(policy.ShouldPublish(TimeSpan.Zero, duration, true, true, 1, TimeSpan.Zero));
        Assert.False(policy.ShouldPublish(TimeSpan.FromMilliseconds(500), duration, true, true, 1, TimeSpan.FromMilliseconds(500)));
        Assert.True(policy.ShouldPublish(TimeSpan.FromSeconds(1), duration, true, true, 1, TimeSpan.FromSeconds(1)));
        Assert.True(policy.ShouldPublish(TimeSpan.FromSeconds(20), duration, true, true, 1, TimeSpan.FromSeconds(1.1)));
        Assert.True(policy.ShouldPublish(TimeSpan.FromSeconds(20), duration, true, false, 1, TimeSpan.FromSeconds(1.2)));
        Assert.False(policy.ShouldPublish(TimeSpan.FromSeconds(20), duration, true, false, 1, TimeSpan.FromSeconds(1.3)));
        Assert.True(policy.ShouldPublish(TimeSpan.FromSeconds(20), duration, true, false, 2, TimeSpan.FromSeconds(1.4)));
        Assert.True(policy.ShouldPublish(TimeSpan.FromSeconds(20), duration, false, false, 2, TimeSpan.FromSeconds(1.5)));
        Assert.True(policy.ShouldPublish(TimeSpan.FromSeconds(20), TimeSpan.Zero, false, false, 2, TimeSpan.FromSeconds(1.6)));
        policy.Reset();
        Assert.True(policy.ShouldPublish(TimeSpan.Zero, duration, true, false, 1, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void JumpListKeepsOnlyAllowedRecentFilesAndSafeTasks()
    {
        var file = @"C:\Videos\film one.mkv";
        var duplicate = @"c:\videos\FILM ONE.mkv";
        var another = @"C:\Videos\other.mkv";
        var recent = new[] { file, duplicate, @"C:\Gone\missing.mkv", "../relative.mkv", another, "https://media.invalid/stream" };
        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { JumpListPlan.QuoteArgument(another)! };
        var menu = JumpListPlan.Plan(recent, true, removed, 4, path => !path.Contains("missing", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, menu.Recent.Count);
        Assert.Equal(3, menu.Tasks.Count);
        Assert.Equal(JumpListPlan.MaxRecent, 10);
        Assert.Null(JumpListPlan.QuoteArgument(""));
        Assert.Null(JumpListPlan.QuoteArgument("relative.mkv"));
        Assert.Null(JumpListPlan.QuoteArgument("file\"bad.mkv"));
        Assert.Null(JumpListPlan.QuoteArgument("bad\nfile.mkv"));
        Assert.EndsWith(new string((char)92, 2) + "\"", JumpListPlan.QuoteArgument(@"C:\Video\")!);
        Assert.NotNull(JumpListPlan.QuoteArgument(@"\\host\share\film.mkv"));
        Assert.Throws<ArgumentNullException>(() => JumpListPlan.QuoteArgument(null!));
        Assert.Throws<ArgumentNullException>(() => JumpListPlan.Plan(null!, true, removed, 3, _ => true));
        Assert.Throws<ArgumentNullException>(() => JumpListPlan.Plan(recent, true, null!, 3, _ => true));
        Assert.Throws<ArgumentNullException>(() => JumpListPlan.Plan(recent, true, removed, 3, null!));
        var noHistory = JumpListPlan.Plan(recent, false, removed, 10, _ => true);
        Assert.Empty(noHistory.Recent);
        Assert.Equal(2, noHistory.Tasks.Count);
        var few = JumpListPlan.Plan(recent, true, removed, 1, _ => true);
        Assert.Single(few.Recent);

        var command = JumpListPlan.TaskCommands[0].Command;
        var parsed = JumpListPlan.ParseLaunch([file, JumpListPlan.CommandSwitch, command, another]);
        Assert.Equal([file], parsed.Files);
        Assert.Equal(command, parsed.Command);
        Assert.Null(JumpListPlan.ParseLaunch([file]).Command);
        Assert.Null(JumpListPlan.ParseLaunch([file, JumpListPlan.CommandSwitch, "unknown"]).Command);
        Assert.Null(JumpListPlan.ParseLaunch([file, JumpListPlan.CommandSwitch]).Command);
        Assert.Throws<ArgumentNullException>(() => JumpListPlan.ParseLaunch(null!));
    }

    [Fact]
    public void MediaCardLeavesPrivateLocationsOffTheLockScreen()
    {
        Assert.Null(MediaCard.Describe(TransportMedia.None, "", null, null, null, false));
        Assert.Null(MediaCard.Describe(TransportMedia.Unknown, "", null, null, null, false));
        Assert.Equal(MediaCardKind.Music, MediaCard.Describe(TransportMedia.Audio, "Track", "Artist", null, null, false)!.Kind);
        Assert.Equal(MediaCardKind.Video, MediaCard.Describe(TransportMedia.Video, "Film", "", null, null, true)!.Kind);
        Assert.Equal(MediaCardKind.Image, MediaCard.Describe(TransportMedia.Picture, "Picture", "", null, null, false)!.Kind);
        Assert.Equal(MediaCardKind.Video, MediaCard.Describe(TransportMedia.Stream, "Stream", "", null, null, true)!.Kind);
        Assert.Equal(MediaCardKind.Music, MediaCard.Describe(TransportMedia.Stream, "Stream", "", null, null, false)!.Kind);
        Assert.Equal("", MediaCard.Shown(Path.GetFullPath("Secret/file.mp4")));
        Assert.Equal("", MediaCard.Shown(@"\\host\share\file.mp4"));
        Assert.Equal("", MediaCard.Shown("https://example.test/stream"));
        Assert.Equal("Name", MediaCard.Shown(" Name "));
        Assert.Equal(3, MediaCard.TrackNumberOf("3/12"));
        Assert.Equal(2, MediaCard.TrackNumberOf("2"));
        Assert.Equal(0, MediaCard.TrackNumberOf("x"));
        Assert.Equal(0, MediaCard.TrackNumberOf("0"));
        Assert.Throws<ArgumentNullException>(() => MediaCard.Shown(null!));
        Assert.Throws<ArgumentNullException>(() => MediaCard.TrackNumberOf(null!));
        Assert.Throws<ArgumentNullException>(() => MediaCard.Describe(TransportMedia.Audio, null!, null, null, null, false));
        Assert.Equal("SHOW, season 2, episode 3", MediaCard.Episode(new EpisodeIdentity("SHOW", 2, 3)));
        Assert.Equal("SHOW, season 2, episodes 3 to 4", MediaCard.Episode(new EpisodeIdentity("SHOW", 2, 3, 4)));
        Assert.Throws<ArgumentNullException>(() => MediaCard.Episode(null!));
    }
}
