using Rex.Media.AppCore.Updates;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

public sealed class UpdateFlowTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 14, 0, 0, TimeSpan.Zero);

    /// <summary>The window as it is when nothing stands in the way: windowed, paused, untouched for an hour.</summary>
    private static readonly UpdateMoment Quiet = new(false, false, false, false, false, TimeSpan.FromHours(1));

    private static UpdateOffer Offer(string version = "0.16.0", bool attention = false) =>
        new(Version.Parse(version), new Uri($"https://example.com/rexplayer-Setup-{version}.exe"),
            new Uri($"https://example.com/rexplayer-Setup-{version}.exe.sha256"),
            new Uri($"https://example.com/releases/v{version}"), $"rexplayer-Setup-{version}.exe")
        {
            Size = 1234,
            Notes = "• Faster",
            NeedsAttention = attention,
        };

    private static PendingUpdate Pending(UpdateStage stage = UpdateStage.Ready, string version = "0.16.0") =>
        UpdateFlow.Found(Offer(version), UpdateMode.DownloadAndAsk, metered: false, asked: false) with { Stage = stage };

    [Fact]
    public void OnlyReleaseVersionsAreUpdated()
    {
        Assert.True(UpdateFlow.Applies("0.15.11"));
        Assert.False(UpdateFlow.Applies("0.16.0-dev"));
        Assert.False(UpdateFlow.Applies(""));
    }

    [Theory]
    [InlineData(UpdateMode.Automatic, false, false, UpdateStage.Downloading)]
    [InlineData(UpdateMode.DownloadAndAsk, false, false, UpdateStage.Downloading)]
    [InlineData(UpdateMode.NotifyOnly, false, false, UpdateStage.Available)]
    [InlineData(UpdateMode.Automatic, true, false, UpdateStage.Available)]
    [InlineData(UpdateMode.DownloadAndAsk, true, false, UpdateStage.Available)]
    [InlineData(UpdateMode.NotifyOnly, true, true, UpdateStage.Downloading)]
    [InlineData(UpdateMode.DownloadAndAsk, true, true, UpdateStage.Downloading)]
    [Capability("TOOL-09")]
    public void AFoundVersionDownloadsOnlyWhenTheModeAndConnectionAllowOrTheUserAsked(UpdateMode mode, bool metered, bool asked, UpdateStage stage)
    {
        var found = UpdateFlow.Found(Offer(), mode, metered, asked);

        Assert.Equal(stage, found.Stage);
        Assert.Equal(asked, found.Approved);
        Assert.Equal(("0.16.0", "rexplayer-Setup-0.16.0.exe", 1234L, "• Faster"), (found.Version, found.InstallerName, found.Size, found.Notes));
        Assert.Equal("https://example.com/rexplayer-Setup-0.16.0.exe", found.InstallerUrl);
        Assert.Equal("https://example.com/rexplayer-Setup-0.16.0.exe.sha256", found.ChecksumUrl);
        Assert.Equal("https://example.com/releases/v0.16.0", found.ReleasePage);
        Assert.Equal(found, found.Normalize());
        Assert.Throws<ArgumentNullException>(() => UpdateFlow.Found(null!, mode, metered, asked));
    }

    [Fact]
    public void ACheckFindingNothingNewerDropsWhateverWasPending()
    {
        var outcome = UpdateFlow.AfterCheck(Pending(), null, "0.15.11", null, UpdateMode.DownloadAndAsk, false, false);
        Assert.Equal(new CheckOutcome(null, true, "rexplayer 0.15.11 is the newest version."), outcome);

        Assert.Equal(new CheckOutcome(null, false, "rexplayer 0.15.11 is the newest version."),
            UpdateFlow.AfterCheck(null, null, "0.15.11", null, UpdateMode.DownloadAndAsk, false, false));
    }

    [Fact]
    public void AnInstallingUpdateIsLeftAloneByACheck()
    {
        var installing = Pending(UpdateStage.Installing);
        Assert.Equal(new CheckOutcome(installing, false, "Installing rexplayer 0.16.0..."),
            UpdateFlow.AfterCheck(installing, Offer("0.17.0"), "0.15.11", null, UpdateMode.DownloadAndAsk, false, true));
    }

    [Fact]
    public void ASkippedVersionIsNotOfferedUnlessTheUserAsks()
    {
        var skipped = UpdateFlow.AfterCheck(Pending(UpdateStage.Available), Offer(), "0.15.11", "0.16.0", UpdateMode.DownloadAndAsk, false, false);
        Assert.Equal(new CheckOutcome(null, true, "rexplayer 0.16.0 is available; you chose to skip it."), skipped);
        Assert.False(UpdateFlow.AfterCheck(null, Offer(), "0.15.11", "0.16.0", UpdateMode.DownloadAndAsk, false, false).Discard);

        var asked = UpdateFlow.AfterCheck(null, Offer(), "0.15.11", null, UpdateMode.DownloadAndAsk, false, true);
        Assert.Equal(UpdateStage.Downloading, asked.Pending!.Stage);

        // A newer version than the skipped one is offered.
        Assert.NotNull(UpdateFlow.AfterCheck(null, Offer("0.17.0"), "0.15.11", "0.16.0", UpdateMode.DownloadAndAsk, false, false).Pending);
    }

    [Fact]
    public void ANewerOrWithdrawnReleaseReplacesThePendingOne()
    {
        var newer = UpdateFlow.AfterCheck(Pending(), Offer("0.17.0"), "0.15.11", null, UpdateMode.DownloadAndAsk, false, false);
        Assert.True(newer.Discard);
        Assert.Equal(("0.17.0", UpdateStage.Downloading), (newer.Pending!.Version, newer.Pending.Stage));
        Assert.Equal("Downloading rexplayer 0.17.0...", newer.Message);

        // 0.16.0 was withdrawn: the latest is 0.15.12 again, still newer than what runs.
        var withdrawn = UpdateFlow.AfterCheck(Pending(), Offer("0.15.12"), "0.15.11", null, UpdateMode.NotifyOnly, false, false);
        Assert.True(withdrawn.Discard);
        Assert.Equal(("0.15.12", UpdateStage.Available), (withdrawn.Pending!.Version, withdrawn.Pending.Stage));
        Assert.Equal("rexplayer 0.15.12 is available.", withdrawn.Message);
    }

    [Fact]
    public void TheSameVersionKeepsItsProgressAndTakesTheReleasesNewDetails()
    {
        var ready = Pending() with { RemindAt = Now.AddHours(3), Timing = UpdateTiming.OnClose };
        var offer = Offer(attention: true) with { Notes = "Breaking change", Size = 99 };

        var unasked = UpdateFlow.AfterCheck(ready, offer, "0.15.11", null, UpdateMode.DownloadAndAsk, false, false);
        Assert.False(unasked.Discard);
        Assert.Equal(ready with { Notes = "Breaking change", Size = 99, NeedsAttention = true }, unasked.Pending);
        Assert.Equal("rexplayer 0.16.0 is ready to install.", unasked.Message);

        var asked = UpdateFlow.AfterCheck(ready with { RetryAt = Now }, offer, "0.15.11", null, UpdateMode.DownloadAndAsk, false, true);
        Assert.Equal((null, null, true), (asked.Pending!.RemindAt, asked.Pending.RetryAt, asked.Pending.Approved));
    }

    [Theory]
    [InlineData(UpdateMode.DownloadAndAsk, false, false, UpdateStage.Downloading)]
    [InlineData(UpdateMode.DownloadAndAsk, true, false, UpdateStage.Available)]
    [InlineData(UpdateMode.NotifyOnly, false, false, UpdateStage.Available)]
    [InlineData(UpdateMode.NotifyOnly, false, true, UpdateStage.Downloading)]
    public void AnAvailableVersionStartsDownloadingOnceAllowed(UpdateMode mode, bool metered, bool asked, UpdateStage stage)
    {
        var outcome = UpdateFlow.AfterCheck(Pending(UpdateStage.Available), Offer(), "0.15.11", null, mode, metered, asked);
        Assert.Equal(stage, outcome.Pending!.Stage);
    }

    [Fact]
    public void AStartWithNothingPendingOrOnADeveloperBuildChangesNothing()
    {
        Assert.Equal(new StartupOutcome(null, StartupNews.None, false, false), UpdateFlow.AtStartup(null, "0.15.11", Now));
        var installing = Pending(UpdateStage.Installing);
        Assert.Equal(new StartupOutcome(installing, StartupNews.None, false, false), UpdateFlow.AtStartup(installing, "dev", Now));
    }

    [Fact]
    [Capability("TOOL-09")]
    public void AStartAfterTheHandOffSeesWhetherTheUpdateInstalled()
    {
        var handed = Pending(UpdateStage.Installing) with { HandedOffAt = Now.AddMinutes(-1) };

        Assert.Equal(new StartupOutcome(null, StartupNews.Installed, true, false), UpdateFlow.AtStartup(handed, "0.16.0", Now));

        // The user installed it, or something newer, some other way: nothing to say, the files go.
        Assert.Equal(new StartupOutcome(null, StartupNews.None, true, false), UpdateFlow.AtStartup(Pending(), "0.16.0", Now));
        Assert.Equal(new StartupOutcome(null, StartupNews.None, true, false), UpdateFlow.AtStartup(Pending(UpdateStage.Downloading), "0.17.0", Now));

        // Still pending and not handed over: carries on as it was.
        var downloading = Pending(UpdateStage.Downloading);
        Assert.Equal(new StartupOutcome(downloading, StartupNews.None, false, false), UpdateFlow.AtStartup(downloading, "0.15.11", Now));
    }

    [Fact]
    public void ACancelledOrFailedInstallAsksAgainAndPutsTheSessionBackOnlyWhenItJustHappened()
    {
        var handed = Pending(UpdateStage.Installing) with { HandedOffAt = Now.AddMinutes(-2), Timing = UpdateTiming.Scheduled, InstallAt = Now, RemindAt = Now };

        var failed = UpdateFlow.AtStartup(handed, "0.15.11", Now);
        Assert.Equal(StartupNews.InstallFailed, failed.News);
        Assert.True(failed.RestoreSession);
        Assert.False(failed.Discard);
        Assert.Equal(Pending() with { InstallFailures = 1 }, failed.Pending);

        // Long after, or a hand-off time in the future (the clock moved): not "just now".
        Assert.False(UpdateFlow.AtStartup(handed with { HandedOffAt = Now.AddHours(-1) }, "0.15.11", Now).RestoreSession);
        Assert.False(UpdateFlow.AtStartup(handed with { HandedOffAt = Now.AddHours(1) }, "0.15.11", Now).RestoreSession);
        Assert.False(UpdateFlow.AtStartup(handed with { HandedOffAt = null }, "0.15.11", Now).RestoreSession);

        var third = UpdateFlow.AtStartup(handed with { InstallFailures = UpdateFlow.MostInstallFailures - 1 }, "0.15.11", Now);
        Assert.Equal(new StartupOutcome(null, StartupNews.InstallAbandoned, true, true), third);
    }

    [Theory]
    [InlineData(UpdateMode.Automatic, UpdateTiming.Ask, false, 0, true)]
    [InlineData(UpdateMode.Automatic, UpdateTiming.Ask, true, 0, false)]
    [InlineData(UpdateMode.Automatic, UpdateTiming.Ask, false, 1, false)]
    [InlineData(UpdateMode.Automatic, UpdateTiming.OnClose, false, 0, false)]
    [InlineData(UpdateMode.DownloadAndAsk, UpdateTiming.Ask, false, 0, false)]
    [InlineData(UpdateMode.NotifyOnly, UpdateTiming.Ask, false, 0, false)]
    public void AutomaticModeInstallsUnaskedOnlyAnOrdinaryReleaseThatHasNotFailed(UpdateMode mode, UpdateTiming timing, bool attention, int failures, bool unasked)
    {
        Assert.Equal(unasked, UpdateFlow.InstallsUnasked(Pending() with { Timing = timing, NeedsAttention = attention, InstallFailures = failures }, mode));
        Assert.Throws<ArgumentNullException>(() => UpdateFlow.InstallsUnasked(null!, mode));
    }

    [Fact]
    [Capability("TOOL-09")]
    public void TheBarWaitsOutFullScreenPlaybackDialogsEditingAndOtherBanners()
    {
        var ready = Pending();
        Assert.Equal(UpdatePrompt.Ready, UpdateFlow.Prompt(ready, UpdateMode.DownloadAndAsk, Quiet, Now));
        Assert.Equal(UpdatePrompt.Ready, UpdateFlow.Prompt(ready, UpdateMode.DownloadAndAsk, Quiet with { Playing = true }, Now));
        Assert.Equal(UpdatePrompt.Ready, UpdateFlow.Prompt(ready, UpdateMode.DownloadAndAsk, Quiet with { FullScreen = true }, Now));
        Assert.Equal(UpdatePrompt.None, UpdateFlow.Prompt(ready, UpdateMode.DownloadAndAsk, Quiet with { FullScreen = true, Playing = true }, Now));
        Assert.Equal(UpdatePrompt.None, UpdateFlow.Prompt(ready, UpdateMode.DownloadAndAsk, Quiet with { DialogOpen = true }, Now));
        Assert.Equal(UpdatePrompt.None, UpdateFlow.Prompt(ready, UpdateMode.DownloadAndAsk, Quiet with { Editing = true }, Now));
        Assert.Equal(UpdatePrompt.None, UpdateFlow.Prompt(ready, UpdateMode.DownloadAndAsk, Quiet with { NoticeOpen = true }, Now));
        Assert.Equal(UpdatePrompt.None, UpdateFlow.Prompt(null, UpdateMode.DownloadAndAsk, Quiet, Now));
        Assert.Throws<ArgumentNullException>(() => UpdateFlow.Prompt(ready, UpdateMode.DownloadAndAsk, null!, Now));

        // Check for updates shows it over anything but a dialog.
        Assert.Equal(UpdatePrompt.Ready, UpdateFlow.Prompt(ready, UpdateMode.DownloadAndAsk, Quiet with { FullScreen = true, Playing = true, Editing = true }, Now, asked: true));
        Assert.Equal(UpdatePrompt.None, UpdateFlow.Prompt(ready, UpdateMode.DownloadAndAsk, Quiet with { DialogOpen = true }, Now, asked: true));
    }

    [Fact]
    public void TheBarOffersOnlyWhatNeedsAnAnswer()
    {
        Assert.Equal(UpdatePrompt.Available, UpdateFlow.Prompt(Pending(UpdateStage.Available), UpdateMode.NotifyOnly, Quiet, Now));
        Assert.Equal(UpdatePrompt.None, UpdateFlow.Prompt(Pending(UpdateStage.Downloading), UpdateMode.DownloadAndAsk, Quiet, Now));
        Assert.Equal(UpdatePrompt.None, UpdateFlow.Prompt(Pending(UpdateStage.Installing), UpdateMode.DownloadAndAsk, Quiet, Now));
        Assert.Equal(UpdatePrompt.None, UpdateFlow.Prompt(Pending() with { Timing = UpdateTiming.OnClose }, UpdateMode.DownloadAndAsk, Quiet, Now));
        Assert.Equal(UpdatePrompt.Ready, UpdateFlow.Prompt(Pending() with { Timing = UpdateTiming.OnClose }, UpdateMode.DownloadAndAsk, Quiet, Now, asked: true));

        // Automatic mode installs by itself, unless the release asks for attention.
        Assert.Equal(UpdatePrompt.None, UpdateFlow.Prompt(Pending(), UpdateMode.Automatic, Quiet, Now));
        Assert.Equal(UpdatePrompt.Ready, UpdateFlow.Prompt(Pending() with { NeedsAttention = true }, UpdateMode.Automatic, Quiet, Now));
    }

    [Fact]
    public void ARemindedVersionWaitsUntilItsTimeUnlessTheClockWentBack()
    {
        var reminded = UpdateFlow.Remind(Pending() with { Timing = UpdateTiming.Scheduled, InstallAt = Now }, RemindChoice.Tomorrow, Now);
        Assert.Equal((UpdateTiming.Ask, (DateTimeOffset?)null, (DateTimeOffset?)Now.AddDays(1)), (reminded.Timing, reminded.InstallAt, reminded.RemindAt));
        Assert.Equal(Now.AddHours(1), UpdateFlow.Remind(Pending(), RemindChoice.InAnHour, Now).RemindAt);
        Assert.Equal(Now.AddDays(7), UpdateFlow.Remind(Pending(), RemindChoice.NextWeek, Now).RemindAt);
        Assert.Throws<ArgumentNullException>(() => UpdateFlow.Remind(null!, RemindChoice.Tomorrow, Now));

        Assert.Equal(UpdatePrompt.None, UpdateFlow.Prompt(reminded, UpdateMode.DownloadAndAsk, Quiet, Now.AddHours(23)));
        Assert.Equal(UpdatePrompt.Ready, UpdateFlow.Prompt(reminded, UpdateMode.DownloadAndAsk, Quiet, Now.AddDays(1)));
        Assert.Equal(UpdatePrompt.Ready, UpdateFlow.Prompt(reminded, UpdateMode.DownloadAndAsk, Quiet, Now.AddHours(1), asked: true));

        // The clock was put back a month: a reminder "five weeks away" is not believed.
        Assert.Equal(UpdatePrompt.Ready, UpdateFlow.Prompt(reminded, UpdateMode.DownloadAndAsk, Quiet, Now.AddDays(-30)));
    }

    [Fact]
    public void InstallingOnCloseOrTonightIsTheUsersChoice()
    {
        var onClose = UpdateFlow.WhenClosed(Pending() with { RemindAt = Now, InstallAt = Now });
        Assert.Equal((UpdateTiming.OnClose, (DateTimeOffset?)null, (DateTimeOffset?)null), (onClose.Timing, onClose.RemindAt, onClose.InstallAt));
        Assert.Throws<ArgumentNullException>(() => UpdateFlow.WhenClosed(null!));

        var local = new DateTimeOffset(2026, 10, 12, 22, 0, 0, TimeSpan.FromHours(1));
        var tonight = UpdateFlow.ScheduledTonight(Pending() with { RemindAt = Now }, local);
        Assert.Equal((UpdateTiming.Scheduled, (DateTimeOffset?)null), (tonight.Timing, tonight.RemindAt));
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 3, 0, 0, TimeSpan.FromHours(1)), tonight.InstallAt);
        Assert.Throws<ArgumentNullException>(() => UpdateFlow.ScheduledTonight(null!, local));
    }

    [Theory]
    [InlineData(22, 0, 13)]
    [InlineData(1, 0, 12)]
    [InlineData(2, 30, 13)]
    [InlineData(2, 29, 12)]
    [InlineData(3, 0, 13)]
    public void TonightIsTheNextThreeInTheMorningAtLeastHalfAnHourAway(int hour, int minute, int day)
    {
        var local = new DateTimeOffset(2026, 10, 12, hour, minute, 0, TimeSpan.FromHours(-5));
        Assert.Equal(new DateTimeOffset(2026, 10, day, 3, 0, 0, TimeSpan.FromHours(-5)), UpdateFlow.Tonight(local));
    }

    [Fact]
    [Capability("TOOL-09")]
    public void InstallingWhileOpenNeedsItsTimeAndAnIdleWindow()
    {
        var scheduled = Pending() with { Timing = UpdateTiming.Scheduled, InstallAt = Now.AddHours(-1) };
        Assert.True(UpdateFlow.InstallWhileOpen(scheduled, UpdateMode.DownloadAndAsk, Quiet, Now));
        Assert.False(UpdateFlow.InstallWhileOpen(scheduled with { InstallAt = Now.AddHours(1) }, UpdateMode.DownloadAndAsk, Quiet, Now));
        Assert.True(UpdateFlow.InstallWhileOpen(scheduled with { InstallAt = Now.AddDays(10) }, UpdateMode.DownloadAndAsk, Quiet, Now));
        Assert.True(UpdateFlow.InstallWhileOpen(scheduled with { InstallAt = null }, UpdateMode.DownloadAndAsk, Quiet, Now));

        foreach (var busy in new[]
        {
            Quiet with { Playing = true }, Quiet with { FullScreen = true }, Quiet with { DialogOpen = true },
            Quiet with { Editing = true }, Quiet with { IdleFor = UpdateFlow.IdleBeforeInstall - TimeSpan.FromSeconds(1) },
        })
        {
            Assert.False(UpdateFlow.InstallWhileOpen(scheduled, UpdateMode.Automatic, busy, Now));
        }

        // A banner does not stop an idle install; nothing to install, or only asking, does.
        Assert.True(UpdateFlow.InstallWhileOpen(scheduled, UpdateMode.DownloadAndAsk, Quiet with { NoticeOpen = true }, Now));
        Assert.False(UpdateFlow.InstallWhileOpen(null, UpdateMode.Automatic, Quiet, Now));
        Assert.False(UpdateFlow.InstallWhileOpen(Pending(UpdateStage.Downloading), UpdateMode.Automatic, Quiet, Now));
        Assert.True(UpdateFlow.InstallWhileOpen(Pending(), UpdateMode.Automatic, Quiet, Now));
        Assert.False(UpdateFlow.InstallWhileOpen(Pending(), UpdateMode.DownloadAndAsk, Quiet, Now));
        Assert.False(UpdateFlow.InstallWhileOpen(Pending() with { NeedsAttention = true }, UpdateMode.Automatic, Quiet, Now));
        Assert.Throws<ArgumentNullException>(() => UpdateFlow.InstallWhileOpen(scheduled, UpdateMode.Automatic, null!, Now));
    }

    [Fact]
    public void ClosingInstallsWhatTheUserOrAutomaticModeChoseUnlessAnotherWindowJustDid()
    {
        Assert.True(UpdateFlow.InstallOnClose(Pending() with { Timing = UpdateTiming.OnClose }, UpdateMode.NotifyOnly, null, Now));
        Assert.True(UpdateFlow.InstallOnClose(Pending() with { Timing = UpdateTiming.Scheduled }, UpdateMode.DownloadAndAsk, null, Now));
        Assert.True(UpdateFlow.InstallOnClose(Pending(), UpdateMode.Automatic, null, Now));
        Assert.False(UpdateFlow.InstallOnClose(Pending(), UpdateMode.DownloadAndAsk, null, Now));
        Assert.False(UpdateFlow.InstallOnClose(Pending(UpdateStage.Downloading) with { Timing = UpdateTiming.OnClose }, UpdateMode.Automatic, null, Now));
        Assert.False(UpdateFlow.InstallOnClose(null, UpdateMode.Automatic, null, Now));

        var other = Pending(UpdateStage.Installing) with { HandedOffAt = Now.AddMinutes(-1) };
        Assert.False(UpdateFlow.InstallOnClose(Pending(), UpdateMode.Automatic, other, Now));
        Assert.True(UpdateFlow.InstallOnClose(Pending(), UpdateMode.Automatic, other with { HandedOffAt = Now.AddHours(-1) }, Now));
        Assert.True(UpdateFlow.InstallOnClose(Pending(), UpdateMode.Automatic, other with { HandedOffAt = Now.AddHours(1) }, Now));
        Assert.True(UpdateFlow.InstallOnClose(Pending(), UpdateMode.Automatic, other with { HandedOffAt = null }, Now));
        Assert.True(UpdateFlow.InstallOnClose(Pending(), UpdateMode.Automatic, Pending(), Now));
    }

    [Theory]
    [InlineData(UpdateMode.DownloadAndAsk, UpdateCadence.Daily, false, false, true)]
    [InlineData(UpdateMode.Automatic, UpdateCadence.Weekly, false, false, true)]
    [InlineData(UpdateMode.DownloadAndAsk, UpdateCadence.Daily, true, false, false)]
    [InlineData(UpdateMode.NotifyOnly, UpdateCadence.Daily, false, false, false)]
    [InlineData(UpdateMode.DownloadAndAsk, UpdateCadence.Off, false, false, false)]
    [InlineData(UpdateMode.NotifyOnly, UpdateCadence.Off, true, true, true)]
    public void ADownloadRunsWhenApprovedOrAllowedByModeCadenceAndConnection(UpdateMode mode, UpdateCadence cadence, bool metered, bool approved, bool may)
    {
        Assert.Equal(may, UpdateFlow.MayDownload(Pending(UpdateStage.Downloading) with { Approved = approved }, mode, cadence, metered, Now));
    }

    [Fact]
    public void FailedDownloadsWaitLongerEachTimeUpToHalfADay()
    {
        var pending = Pending(UpdateStage.Downloading);
        var waits = new List<TimeSpan>();
        for (var i = 0; i < 8; i++)
        {
            pending = UpdateFlow.DownloadFailed(pending, "the disk is full", Now);
            waits.Add(pending.RetryAt!.Value - Now);
        }

        Assert.Equal([1, 5, 15, 60, 240, 720, 720, 720], waits.Select(wait => (int)wait.TotalMinutes));
        Assert.Equal((8, "the disk is full"), (pending.DownloadFailures, pending.LastError));
        Assert.False(UpdateFlow.MayDownload(pending, UpdateMode.Automatic, UpdateCadence.Daily, false, Now));
        Assert.True(UpdateFlow.MayDownload(pending, UpdateMode.Automatic, UpdateCadence.Daily, false, Now.AddHours(12)));

        // A retry further away than any wait means the clock went back.
        Assert.True(UpdateFlow.MayDownload(pending, UpdateMode.Automatic, UpdateCadence.Daily, false, Now.AddDays(-2)));
        Assert.False(UpdateFlow.MayDownload(null, UpdateMode.Automatic, UpdateCadence.Daily, false, Now));
        Assert.False(UpdateFlow.MayDownload(Pending(), UpdateMode.Automatic, UpdateCadence.Daily, false, Now));

        var done = UpdateFlow.Downloaded(pending);
        Assert.Equal((UpdateStage.Ready, 0, (DateTimeOffset?)null, (string?)null), (done.Stage, done.DownloadFailures, done.RetryAt, done.LastError));
        Assert.Throws<ArgumentNullException>(() => UpdateFlow.DownloadFailed(null!, "x", Now));
        Assert.Throws<ArgumentNullException>(() => UpdateFlow.Downloaded(null!));
    }

    [Fact]
    [Capability("TOOL-09")]
    public void AutomaticChecksFollowTheCadenceAndBackOffWhileOffline()
    {
        var yesterday = Now.AddDays(-2);
        Assert.True(UpdateFlow.CheckDue(UpdateCadence.Daily, yesterday, null, 0, Now));
        Assert.False(UpdateFlow.CheckDue(UpdateCadence.Off, yesterday, null, 0, Now));
        Assert.False(UpdateFlow.CheckDue(UpdateCadence.Daily, Now.AddHours(-2), null, 0, Now));

        Assert.False(UpdateFlow.CheckDue(UpdateCadence.Daily, yesterday, Now.AddMinutes(-4), 1, Now));
        Assert.True(UpdateFlow.CheckDue(UpdateCadence.Daily, yesterday, Now.AddMinutes(-5), 1, Now));
        Assert.False(UpdateFlow.CheckDue(UpdateCadence.Daily, yesterday, Now.AddMinutes(-14), 2, Now));
        Assert.False(UpdateFlow.CheckDue(UpdateCadence.Daily, yesterday, Now.AddMinutes(-59), 3, Now));
        Assert.False(UpdateFlow.CheckDue(UpdateCadence.Daily, yesterday, Now.AddHours(-3), 9, Now));
        Assert.True(UpdateFlow.CheckDue(UpdateCadence.Daily, yesterday, Now.AddHours(-4), 9, Now));
        Assert.True(UpdateFlow.CheckDue(UpdateCadence.Daily, yesterday, Now.AddMinutes(-1), 0, Now));
        Assert.True(UpdateFlow.CheckDue(UpdateCadence.Daily, yesterday, Now.AddHours(1), 3, Now));
        Assert.True(UpdateFlow.StartupDelay >= TimeSpan.FromSeconds(30) && UpdateFlow.StartupDelay <= TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void ThePhaseIsWhatTheUserSees()
    {
        Assert.Equal(UpdatePhase.Idle, UpdateFlow.Phase(null, false, false));
        Assert.Equal(UpdatePhase.Checking, UpdateFlow.Phase(null, true, false));
        Assert.Equal(UpdatePhase.Downloading, UpdateFlow.Phase(Pending(UpdateStage.Downloading), true, true));
        Assert.Equal(UpdatePhase.Downloading, UpdateFlow.Phase(Pending(UpdateStage.Downloading), false, false));
        Assert.Equal(UpdatePhase.Failed, UpdateFlow.Phase(Pending(UpdateStage.Downloading) with { DownloadFailures = 1 }, false, false));
        Assert.Equal(UpdatePhase.Available, UpdateFlow.Phase(Pending(UpdateStage.Available), false, false));
        Assert.Equal(UpdatePhase.Ready, UpdateFlow.Phase(Pending(), false, false));
        Assert.Equal(UpdatePhase.Scheduled, UpdateFlow.Phase(Pending() with { Timing = UpdateTiming.Scheduled }, false, false));
        Assert.Equal(UpdatePhase.Installing, UpdateFlow.Phase(Pending(UpdateStage.Installing), false, false));
    }

    [Fact]
    [Capability("TOOL-09")]
    public void EveryPhaseHasPlainWords()
    {
        var local = new DateTimeOffset(2026, 10, 12, 15, 0, 0, TimeSpan.FromHours(1));
        string Say(UpdatePhase phase, PendingUpdate? pending = null, double? percent = null, DateTimeOffset? last = null, UpdateMode mode = UpdateMode.DownloadAndAsk) =>
            UpdateFlow.Status(phase, pending, mode, "0.15.11", percent, last, local);

        Assert.Equal("This is rexplayer 0.15.11.", Say(UpdatePhase.Idle));
        Assert.Equal("rexplayer 0.15.11 is up to date. Last checked today.", Say(UpdatePhase.Idle, last: Now));
        Assert.Equal("Checking for updates...", Say(UpdatePhase.Checking));
        Assert.Equal("rexplayer 0.16.0 is available.", Say(UpdatePhase.Available, Pending(UpdateStage.Available)));
        Assert.Equal("Downloading rexplayer 0.16.0: 42%", Say(UpdatePhase.Downloading, Pending(UpdateStage.Downloading), 42.4));
        Assert.Equal("Downloading rexplayer 0.16.0: 100%", Say(UpdatePhase.Downloading, Pending(UpdateStage.Downloading), 140));
        Assert.Equal("rexplayer 0.16.0 will download in the background.", Say(UpdatePhase.Downloading, Pending(UpdateStage.Downloading)));
        Assert.Equal("rexplayer 0.16.0 could not be downloaded: the disk is full. Trying again today at 15:05.",
            Say(UpdatePhase.Failed, UpdateFlow.DownloadFailed(Pending(UpdateStage.Downloading), "the disk is full", Now.AddMinutes(4))));
        Assert.Equal("rexplayer 0.15.11 could not be downloaded: unknown error. Trying again later.", Say(UpdatePhase.Failed));
        Assert.Equal("rexplayer 0.16.0 is ready to install.", Say(UpdatePhase.Ready, Pending()));
        Assert.Equal("rexplayer 0.16.0 will install when you close rexplayer.", Say(UpdatePhase.Ready, UpdateFlow.WhenClosed(Pending())));
        Assert.Equal("rexplayer 0.16.0 will install when rexplayer is closed or not in use.", Say(UpdatePhase.Ready, Pending(), mode: UpdateMode.Automatic));
        Assert.Equal("rexplayer 0.15.11 is ready to install.", Say(UpdatePhase.Ready, mode: UpdateMode.Automatic));
        Assert.Equal("rexplayer 0.16.0 will install tomorrow at 03:00 when rexplayer is not in use, or when you close it.",
            Say(UpdatePhase.Scheduled, UpdateFlow.ScheduledTonight(Pending(), local)));
        Assert.Equal("rexplayer 0.16.0 will install tonight when rexplayer is not in use, or when you close it.",
            Say(UpdatePhase.Scheduled, Pending() with { Timing = UpdateTiming.Scheduled }));
        Assert.Equal("Installing rexplayer 0.16.0...", Say(UpdatePhase.Installing, Pending(UpdateStage.Installing)));
    }

    [Fact]
    public void DaysAreSaidTheWayPeopleSayThem()
    {
        var local = new DateTimeOffset(2026, 10, 12, 0, 30, 0, TimeSpan.FromHours(2));
        Assert.Equal("today", UpdateFlow.When(local, local));

        // 23:00 UTC on the 11th is already the 12th where the user is.
        Assert.Equal("today", UpdateFlow.When(new DateTimeOffset(2026, 10, 11, 23, 0, 0, TimeSpan.Zero), local));
        Assert.Equal("yesterday", UpdateFlow.When(local.AddDays(-1), local));
        Assert.Equal("tomorrow", UpdateFlow.When(local.AddDays(1), local));
        Assert.Equal("on 2 Oct 2026", UpdateFlow.When(local.AddDays(-10), local));

        Assert.Equal("Updated to 0.16.0 yesterday.", UpdateFlow.LastUpdated("0.16.0", local.AddDays(-1), local));
        Assert.Null(UpdateFlow.LastUpdated(null, local, local));
        Assert.Null(UpdateFlow.LastUpdated("", local, local));
        Assert.Null(UpdateFlow.LastUpdated("0.16.0", null, local));
    }

    [Fact]
    public void ThePromptSaysWhatEachAnswerKeeps()
    {
        Assert.Equal(("rexplayer 0.16.0 is ready to install", "Restart now and rexplayer reopens with your queue, place and view, paused."),
            UpdateFlow.PromptText(UpdatePrompt.Ready, Pending(), false));
        Assert.Equal(("rexplayer 0.16.0 is available", "Download it in the background and choose when to install it."),
            UpdateFlow.PromptText(UpdatePrompt.Available, Pending(UpdateStage.Available), false));
        Assert.Equal(("rexplayer 0.16.0 is available", "You are on a metered connection, so it has not been downloaded. This release asks you to read what's new before installing."),
            UpdateFlow.PromptText(UpdatePrompt.Available, Pending(UpdateStage.Available) with { NeedsAttention = true }, true));
        Assert.Throws<ArgumentNullException>(() => UpdateFlow.PromptText(UpdatePrompt.Ready, null!, false));
    }
}
