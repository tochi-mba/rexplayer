using Rex.Media.AppCore.Updates;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

public sealed class UpdateFlowTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 14, 0, 0, TimeSpan.Zero);

    /// <summary>The window as it is when nothing stands in the way: windowed, paused, untouched for an hour.</summary>
    internal static readonly UpdateMoment Quiet = new(false, false, false, false, false, false, false, TimeSpan.FromHours(1));

    internal static UpdateOffer Offer(string version = "0.16.0", bool attention = false) =>
        new(Version.Parse(version), new Uri($"https://example.com/rexplayer-Setup-{version}.exe"),
            new Uri($"https://example.com/rexplayer-Setup-{version}.exe.sha256"),
            new Uri($"https://example.com/releases/v{version}"), $"rexplayer-Setup-{version}.exe")
        {
            Size = 1234,
            Notes = "- Faster",
            NeedsAttention = attention,
        };

    internal static PendingUpdate Pending(UpdateStage stage = UpdateStage.Ready, string version = "0.16.0") =>
        UpdateFlow.Found(Offer(version), UpdateMode.DownloadAndAsk, metered: false, asked: false) with { Stage = stage };

    private static TimeZoneInfo Fixed(double hours) =>
        TimeZoneInfo.CreateCustomTimeZone("Fixed", TimeSpan.FromHours(hours), "Fixed", "Fixed");

    /// <summary>A zone an hour ahead of UTC whose clocks go forward at 02:00 on 25 October and back at 03:00 on 1 November 2026.</summary>
    private static TimeZoneInfo Changing(int forwardAt = 2)
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, forwardAt, 0, 0), 10, 25),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 3, 0, 0), 11, 1));
        return TimeZoneInfo.CreateCustomTimeZone("Changing", TimeSpan.FromHours(1), "Changing", "Standard", "Summer", [rule]);
    }

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
        Assert.Equal(("0.16.0", "rexplayer-Setup-0.16.0.exe", 1234L, "- Faster"), (found.Version, found.InstallerName, found.Size, found.Notes));
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
        var newer = UpdateFlow.AfterCheck(Pending() with { Declined = true, Paused = true }, Offer("0.17.0"), "0.15.11", null, UpdateMode.DownloadAndAsk, false, false);
        Assert.True(newer.Discard);
        Assert.Equal(("0.17.0", UpdateStage.Downloading, false, false), (newer.Pending!.Version, newer.Pending.Stage, newer.Pending.Declined, newer.Pending.Paused));
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
        Assert.Equal(((DateTimeOffset?)null, true), (asked.Pending!.RetryAt, asked.Pending.Approved));
    }

    [Fact]
    public void ACheckTheUserAsksForKeepsTheirReminder()
    {
        // Check for updates used to clear "Later", so the card came straight back afterwards even
        // though the user had just put it off. The panel the check opens says everything now.
        var reminded = Pending() with { RemindAt = Now.AddHours(3) };
        var asked = UpdateFlow.AfterCheck(reminded, Offer(), "0.15.11", null, UpdateMode.DownloadAndAsk, false, true);
        Assert.Equal(Now.AddHours(3), asked.Pending!.RemindAt);
        Assert.Equal(UpdatePrompt.None, UpdateFlow.Prompt(asked.Pending, UpdateMode.DownloadAndAsk, Quiet, Now));
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
    public void ADownloadTheUserCancelledIsNotStartedAgainByACheck()
    {
        var cancelled = UpdateFlow.CancelDownload(Pending(UpdateStage.Downloading));
        Assert.Equal(UpdateStage.Available, UpdateFlow.AfterCheck(cancelled, Offer(), "0.15.11", null, UpdateMode.Automatic, false, false).Pending!.Stage);
        Assert.Equal(UpdateStage.Available, UpdateFlow.AfterCheck(cancelled, Offer(), "0.15.11", null, UpdateMode.Automatic, false, true).Pending!.Stage);
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
    public void AFailedInstallAsksAgainAndPutsTheSessionBackOnlyWhenItJustHappened()
    {
        var handed = Pending(UpdateStage.Installing) with { HandedOffAt = Now.AddMinutes(-2), Timing = UpdateTiming.Scheduled, InstallAt = Now, RemindAt = Now };

        var failed = UpdateFlow.AtStartup(handed, "0.15.11", Now, InstallEnd.Failed);
        Assert.Equal(StartupNews.InstallFailed, failed.News);
        Assert.True(failed.RestoreSession);
        Assert.False(failed.Discard);
        Assert.Equal(Pending() with { InstallFailures = 1 }, failed.Pending);
        Assert.Equal(failed, UpdateFlow.AtStartup(handed, "0.15.11", Now));

        // Long after, or a hand-off time in the future (the clock moved): not "just now".
        Assert.False(UpdateFlow.AtStartup(handed with { HandedOffAt = Now.AddHours(-1) }, "0.15.11", Now).RestoreSession);
        Assert.False(UpdateFlow.AtStartup(handed with { HandedOffAt = Now.AddHours(1) }, "0.15.11", Now).RestoreSession);
        Assert.False(UpdateFlow.AtStartup(handed with { HandedOffAt = null }, "0.15.11", Now).RestoreSession);

        var third = UpdateFlow.AtStartup(handed with { InstallFailures = UpdateFlow.MostInstallFailures - 1 }, "0.15.11", Now, InstallEnd.AppOpen);
        Assert.Equal(new StartupOutcome(null, StartupNews.InstallAbandoned, true, true), third);
    }

    [Fact]
    [Capability("TOOL-09")]
    public void ACancelledInstallIsNotAFailureButAutomaticModeAsksBeforeTryingAgain()
    {
        // Cancelling the installer used to count as a failure: three cancels set the update aside
        // as "failed 3 times". It is the user's choice, so the update stays on offer, asking.
        var handed = Pending(UpdateStage.Installing) with { HandedOffAt = Now.AddMinutes(-1), InstallFailures = UpdateFlow.MostInstallFailures - 1 };
        var cancelled = UpdateFlow.AtStartup(handed, "0.15.11", Now, InstallEnd.Cancelled);

        Assert.Equal((StartupNews.InstallCancelled, false, true), (cancelled.News, cancelled.Discard, cancelled.RestoreSession));
        Assert.Equal((UpdateStage.Ready, UpdateTiming.Ask, UpdateFlow.MostInstallFailures - 1, true, (DateTimeOffset?)null),
            (cancelled.Pending!.Stage, cancelled.Pending.Timing, cancelled.Pending.InstallFailures, cancelled.Pending.InstallCancelled, cancelled.Pending.HandedOffAt));
        Assert.False(UpdateFlow.InstallsUnasked(Pending() with { InstallCancelled = true }, UpdateMode.Automatic));
        Assert.Equal(UpdatePrompt.Ready, UpdateFlow.Prompt(Pending() with { InstallCancelled = true }, UpdateMode.Automatic, Quiet, Now));

        // A newer version is a new start.
        Assert.False(UpdateFlow.AfterCheck(cancelled.Pending, Offer("0.17.0"), "0.15.11", null, UpdateMode.Automatic, false, false).Pending!.InstallCancelled);
    }

    [Theory]
    [InlineData("rexplayer-Setup-0.16.0.exe 2", InstallEnd.Cancelled)]
    [InlineData("rexplayer-Setup-0.16.0.exe 5\r\n", InstallEnd.Cancelled)]
    [InlineData("rexplayer-Setup-0.16.0.exe 7", InstallEnd.AppOpen)]
    [InlineData("rexplayer-Setup-0.16.0.exe 8", InstallEnd.AppOpen)]
    [InlineData("rexplayer-Setup-0.16.0.exe 4", InstallEnd.Failed)]
    [InlineData("rexplayer-Setup-0.16.0.exe -1", InstallEnd.Failed)]
    [InlineData("rexplayer-Setup-0.16.0.exe 0", InstallEnd.Failed)]
    [InlineData("rexplayer-Setup-0.15.0.exe 2", InstallEnd.Unknown)]
    [InlineData("rexplayer-Setup-0.16.0.exe", InstallEnd.Unknown)]
    [InlineData("rexplayer-Setup-0.16.0.exe two", InstallEnd.Unknown)]
    [InlineData("", InstallEnd.Unknown)]
    [InlineData(null, InstallEnd.Unknown)]
    public void TheHandOffHelpersRecordSaysHowTheInstallerEnded(string? record, InstallEnd end)
    {
        Assert.Equal(end, UpdateFlow.InstallEndOf(record, Pending(UpdateStage.Installing)));
        Assert.Equal(InstallEnd.Unknown, UpdateFlow.InstallEndOf(record, null));
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
    public void TheCardWaitsOutPlayingVideoDialogsEditingAndOtherBanners()
    {
        var ready = Pending();
        Assert.Equal(UpdatePrompt.Ready, UpdateFlow.Prompt(ready, UpdateMode.DownloadAndAsk, Quiet, Now));
        Assert.Equal(UpdatePrompt.Ready, UpdateFlow.Prompt(ready, UpdateMode.DownloadAndAsk, Quiet with { FullScreen = true }, Now));
        Assert.Equal(UpdatePrompt.Ready, UpdateFlow.Prompt(ready, UpdateMode.DownloadAndAsk, Quiet with { Video = true }, Now));

        // Music playing in a window leaves the picture free: the card may show.
        Assert.Equal(UpdatePrompt.Ready, UpdateFlow.Prompt(ready, UpdateMode.DownloadAndAsk, Quiet with { Playing = true }, Now));

        // A video playing in a window: the card used to come up over it; only full screen held it back.
        Assert.Equal(UpdatePrompt.None, UpdateFlow.Prompt(ready, UpdateMode.DownloadAndAsk, Quiet with { Playing = true, Video = true }, Now));
        Assert.Equal(UpdatePrompt.None, UpdateFlow.Prompt(ready, UpdateMode.DownloadAndAsk, Quiet with { FullScreen = true, Playing = true }, Now));
        Assert.Equal(UpdatePrompt.None, UpdateFlow.Prompt(ready, UpdateMode.DownloadAndAsk, Quiet with { DialogOpen = true }, Now));
        Assert.Equal(UpdatePrompt.None, UpdateFlow.Prompt(ready, UpdateMode.DownloadAndAsk, Quiet with { PanelOpen = true }, Now));
        Assert.Equal(UpdatePrompt.None, UpdateFlow.Prompt(ready, UpdateMode.DownloadAndAsk, Quiet with { Editing = true }, Now));
        Assert.Equal(UpdatePrompt.None, UpdateFlow.Prompt(ready, UpdateMode.DownloadAndAsk, Quiet with { NoticeOpen = true }, Now));
        Assert.Equal(UpdatePrompt.None, UpdateFlow.Prompt(null, UpdateMode.DownloadAndAsk, Quiet, Now));
        Assert.Throws<ArgumentNullException>(() => UpdateFlow.Prompt(ready, UpdateMode.DownloadAndAsk, null!, Now));
    }

    [Fact]
    public void ACardAlreadyShowingOutlastsAnotherBannerAndEditingButNotWhatEndsTheOffer()
    {
        var ready = Pending();
        Assert.Equal(UpdatePrompt.Ready, UpdateFlow.Prompt(ready, UpdateMode.DownloadAndAsk, Quiet with { NoticeOpen = true, Editing = true }, Now, showing: true));
        Assert.Equal(UpdatePrompt.None, UpdateFlow.Prompt(ready, UpdateMode.DownloadAndAsk, Quiet with { DialogOpen = true }, Now, showing: true));
        Assert.Equal(UpdatePrompt.None, UpdateFlow.Prompt(ready, UpdateMode.DownloadAndAsk, Quiet with { Playing = true, Video = true }, Now, showing: true));
        Assert.Equal(UpdatePrompt.None, UpdateFlow.Prompt(null, UpdateMode.DownloadAndAsk, Quiet, Now, showing: true));
    }

    [Fact]
    public void TheCardOffersOnlyWhatNeedsAnAnswer()
    {
        Assert.Equal(UpdatePrompt.Available, UpdateFlow.Prompt(Pending(UpdateStage.Available), UpdateMode.NotifyOnly, Quiet, Now));
        Assert.Equal(UpdatePrompt.None, UpdateFlow.Prompt(UpdateFlow.CancelDownload(Pending(UpdateStage.Available)), UpdateMode.NotifyOnly, Quiet, Now));
        Assert.Equal(UpdatePrompt.None, UpdateFlow.Prompt(Pending(UpdateStage.Downloading), UpdateMode.DownloadAndAsk, Quiet, Now));
        Assert.Equal(UpdatePrompt.None, UpdateFlow.Prompt(Pending(UpdateStage.Installing), UpdateMode.DownloadAndAsk, Quiet, Now));
        Assert.Equal(UpdatePrompt.None, UpdateFlow.Prompt(Pending() with { Timing = UpdateTiming.OnClose }, UpdateMode.DownloadAndAsk, Quiet, Now));
        Assert.Equal(UpdatePrompt.None, UpdateFlow.Prompt(Pending() with { Timing = UpdateTiming.Scheduled }, UpdateMode.DownloadAndAsk, Quiet, Now));

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

        // The clock was put back a month: a reminder "five weeks away" is not believed.
        Assert.Equal(UpdatePrompt.Ready, UpdateFlow.Prompt(reminded, UpdateMode.DownloadAndAsk, Quiet, Now.AddDays(-30)));
    }

    [Fact]
    public void InstallingOnCloseOrTonightIsTheUsersChoice()
    {
        var onClose = UpdateFlow.WhenClosed(Pending() with { RemindAt = Now, InstallAt = Now });
        Assert.Equal((UpdateTiming.OnClose, (DateTimeOffset?)null, (DateTimeOffset?)null), (onClose.Timing, onClose.RemindAt, onClose.InstallAt));
        Assert.Throws<ArgumentNullException>(() => UpdateFlow.WhenClosed(null!));

        var evening = new DateTimeOffset(2026, 10, 12, 22, 0, 0, TimeSpan.FromHours(1));
        var tonight = UpdateFlow.ScheduledTonight(Pending() with { RemindAt = Now }, evening, Fixed(1));
        Assert.Equal((UpdateTiming.Scheduled, (DateTimeOffset?)null), (tonight.Timing, tonight.RemindAt));
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 3, 0, 0, TimeSpan.FromHours(1)), tonight.InstallAt);
        Assert.Throws<ArgumentNullException>(() => UpdateFlow.ScheduledTonight(null!, evening, Fixed(1)));
        Assert.Throws<ArgumentNullException>(() => UpdateFlow.Tonight(evening, null!));
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
        Assert.Equal(new DateTimeOffset(2026, 10, day, 3, 0, 0, TimeSpan.FromHours(-5)), UpdateFlow.Tonight(local, Fixed(-5)));

        // The same moment told in another offset is the same night.
        Assert.Equal(new DateTimeOffset(2026, 10, day, 3, 0, 0, TimeSpan.FromHours(-5)), UpdateFlow.Tonight(local.ToUniversalTime(), Fixed(-5)));
    }

    [Fact]
    [Capability("TOOL-09")]
    public void InstallingTonightKeepsToThreeByTheClockOnTheNightTheClocksChange()
    {
        // Chosen at 22:00 summer time the night the clocks go back: 3:00 that night is already
        // standard time. It used to be worked out in the evening's offset, an hour early.
        var zone = Changing();
        var evening = new DateTimeOffset(2026, 10, 31, 22, 0, 0, TimeSpan.FromHours(2));
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 3, 0, 0, TimeSpan.FromHours(1)), UpdateFlow.Tonight(evening, zone));

        // The night they go forward, 3:00 is summer time already.
        var spring = new DateTimeOffset(2026, 10, 24, 22, 0, 0, TimeSpan.FromHours(1));
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 3, 0, 0, TimeSpan.FromHours(2)), UpdateFlow.Tonight(spring, zone));

        // A night that skips 3:00 itself installs at 4:00.
        var skipped = Changing(forwardAt: 3);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 4, 0, 0, TimeSpan.FromHours(2)), UpdateFlow.Tonight(spring, skipped));
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
            Quiet with { Editing = true }, Quiet with { PanelOpen = true }, Quiet with { IdleFor = UpdateFlow.IdleBeforeInstall - TimeSpan.FromSeconds(1) },
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
        Assert.False(UpdateFlow.MayDownload(Pending(UpdateStage.Downloading) with { Approved = approved, Paused = true }, mode, cadence, metered, Now));
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

        var done = UpdateFlow.Downloaded(pending with { Paused = true });
        Assert.Equal((UpdateStage.Ready, 0, (DateTimeOffset?)null, (string?)null, false), (done.Stage, done.DownloadFailures, done.RetryAt, done.LastError, done.Paused));
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
    [Capability("TOOL-09")]
    public void TheSettingsAndConnectionDecideWhetherAnOfferDownloads()
    {
        // Switching to "Only tell me", turning checks off, or a connection becoming metered used to
        // leave a download nobody asked for "pending" with nothing running, the status saying it
        // would download in the background. It goes back to being on offer.
        var downloading = Pending(UpdateStage.Downloading);
        Assert.Equal(UpdateStage.Available, UpdateFlow.Settle(downloading, UpdateMode.NotifyOnly, UpdateCadence.Daily, false).Stage);
        Assert.Equal(UpdateStage.Available, UpdateFlow.Settle(downloading, UpdateMode.DownloadAndAsk, UpdateCadence.Off, false).Stage);
        Assert.Equal(UpdateStage.Available, UpdateFlow.Settle(downloading, UpdateMode.Automatic, UpdateCadence.Daily, true).Stage);
        Assert.Same(downloading, UpdateFlow.Settle(downloading, UpdateMode.DownloadAndAsk, UpdateCadence.Daily, false));

        // One the user asked for keeps going.
        var approved = downloading with { Approved = true };
        Assert.Same(approved, UpdateFlow.Settle(approved, UpdateMode.NotifyOnly, UpdateCadence.Off, true));

        // And the other way: an offer used to wait for the next daily check after the mode allowed
        // downloading or the connection stopped being metered.
        var available = Pending(UpdateStage.Available);
        Assert.Equal(UpdateStage.Downloading, UpdateFlow.Settle(available, UpdateMode.DownloadAndAsk, UpdateCadence.Daily, false).Stage);
        Assert.Same(available, UpdateFlow.Settle(available, UpdateMode.DownloadAndAsk, UpdateCadence.Daily, true));
        Assert.Same(available, UpdateFlow.Settle(available, UpdateMode.NotifyOnly, UpdateCadence.Daily, false));
        var declined = UpdateFlow.CancelDownload(downloading);
        Assert.Same(declined, UpdateFlow.Settle(declined, UpdateMode.Automatic, UpdateCadence.Daily, false));

        var ready = Pending();
        Assert.Same(ready, UpdateFlow.Settle(ready, UpdateMode.NotifyOnly, UpdateCadence.Off, true));
        Assert.Throws<ArgumentNullException>(() => UpdateFlow.Settle(null!, UpdateMode.Automatic, UpdateCadence.Daily, false));
    }

    [Fact]
    public void ADownloadThatIsNotRunningSaysWhy()
    {
        var downloading = Pending(UpdateStage.Downloading);
        Assert.Equal(DownloadHold.None, UpdateFlow.Hold(downloading, online: true, Now));
        Assert.Equal(DownloadHold.Offline, UpdateFlow.Hold(downloading, online: false, Now));
        Assert.Equal(DownloadHold.Paused, UpdateFlow.Hold(UpdateFlow.Pause(downloading), online: false, Now));

        var failed = UpdateFlow.DownloadFailed(downloading, "offline", Now);
        Assert.Equal(DownloadHold.Failed, UpdateFlow.Hold(failed, online: false, Now));
        Assert.Equal(DownloadHold.None, UpdateFlow.Hold(failed, online: true, Now.AddMinutes(2)));
        Assert.Throws<ArgumentNullException>(() => UpdateFlow.Hold(null!, true, Now));
    }

    [Fact]
    [Capability("TOOL-09")]
    public void PauseResumeRetryAndCancelDoWhatTheySay()
    {
        var downloading = Pending(UpdateStage.Downloading);
        var paused = UpdateFlow.Pause(downloading);
        Assert.True(paused.Paused);
        Assert.False(UpdateFlow.MayDownload(paused, UpdateMode.Automatic, UpdateCadence.Daily, false, Now));

        // Resuming is asking: it goes over a metered connection, and a failure's wait is dropped.
        var resumed = UpdateFlow.Resume(UpdateFlow.DownloadFailed(paused, "offline", Now));
        Assert.Equal((false, true, (DateTimeOffset?)null), (resumed.Paused, resumed.Approved, resumed.RetryAt));
        Assert.True(UpdateFlow.MayDownload(resumed, UpdateMode.NotifyOnly, UpdateCadence.Off, true, Now));

        var cancelled = UpdateFlow.CancelDownload(UpdateFlow.DownloadFailed(resumed, "offline", Now));
        Assert.Equal((UpdateStage.Available, true, false, false, 0, (string?)null), (cancelled.Stage, cancelled.Declined, cancelled.Approved, cancelled.Paused, cancelled.DownloadFailures, cancelled.LastError));
        Assert.Null(UpdateFlow.KeptFiles(cancelled));
        Assert.Same(downloading, UpdateFlow.KeptFiles(downloading));
        Assert.Null(UpdateFlow.KeptFiles(null));

        // Download after a cancel starts it again.
        var again = UpdateFlow.Approve(cancelled);
        Assert.Equal((UpdateStage.Downloading, false, true), (again.Stage, again.Declined, again.Approved));

        Assert.Throws<ArgumentNullException>(() => UpdateFlow.Pause(null!));
        Assert.Throws<ArgumentNullException>(() => UpdateFlow.Resume(null!));
        Assert.Throws<ArgumentNullException>(() => UpdateFlow.CancelDownload(null!));
    }

    [Fact]
    [Capability("TOOL-09")]
    public void ATickChecksWhenDueOtherwiseStartsOrStopsTheDownloadAndInstallsWhenIdle()
    {
        var clock = new UpdateClock(Pending(UpdateStage.Downloading), UpdateMode.DownloadAndAsk, UpdateCadence.Daily, Now.AddHours(-1), null, 0,
            Checking: false, Downloading: false, Online: true, Metered: false);

        Assert.Equal(new UpdatePlan(clock.Pending, false, false, true, false), UpdateFlow.Plan(clock, Quiet, Now));
        Assert.Equal(new UpdatePlan(clock.Pending, false, false, false, false), UpdateFlow.Plan(clock with { Downloading = true }, Quiet, Now));
        Assert.Equal(new UpdatePlan(clock.Pending, false, false, false, false), UpdateFlow.Plan(clock with { Online = false }, Quiet, Now));
        Assert.Equal(new UpdatePlan(clock.Pending, false, true, false, false), UpdateFlow.Plan(clock with { Downloading = true, Online = false }, Quiet, Now));

        // A metered connection: the download stops and the version goes back on offer.
        var metered = UpdateFlow.Plan(clock with { Downloading = true, Metered = true }, Quiet, Now);
        Assert.Equal((UpdateStage.Available, true, false), (metered.Pending!.Stage, metered.StopDownload, metered.StartDownload));

        // A check that is due comes first, and alone.
        Assert.Equal(new UpdatePlan(clock.Pending, true, false, false, false), UpdateFlow.Plan(clock with { LastCheck = Now.AddDays(-2) }, Quiet, Now));
        Assert.False(UpdateFlow.Plan(clock with { LastCheck = Now.AddDays(-2), Checking = true }, Quiet, Now).Check);

        var idle = clock with { Pending = Pending(), Mode = UpdateMode.Automatic };
        Assert.Equal(new UpdatePlan(idle.Pending, false, false, false, true), UpdateFlow.Plan(idle, Quiet, Now));
        Assert.False(UpdateFlow.Plan(idle, Quiet with { Playing = true }, Now).Install);
        Assert.Equal(new UpdatePlan(null, false, false, false, false), UpdateFlow.Plan(clock with { Pending = null }, Quiet, Now));
        Assert.Throws<ArgumentNullException>(() => UpdateFlow.Plan(null!, Quiet, Now));
        Assert.Throws<ArgumentNullException>(() => UpdateFlow.Plan(clock, null!, Now));
    }

    [Fact]
    [Capability("TOOL-09")]
    public void ADamagedDownloadIsFetchedAgainOnAMeteredConnectionOnlyWhenTheUserAsked()
    {
        // An install rexplayer started by itself found the file damaged. It used to mark the new
        // download approved, so it went ahead over a metered connection nobody had allowed.
        var ready = Pending() with { Approved = false };
        var again = UpdateFlow.Damaged(ready, asked: false);
        Assert.Equal((UpdateStage.Downloading, false), (again.Stage, again.Approved));
        Assert.False(UpdateFlow.MayDownload(again, UpdateMode.Automatic, UpdateCadence.Daily, metered: true, Now));
        Assert.True(UpdateFlow.MayDownload(again, UpdateMode.Automatic, UpdateCadence.Daily, metered: false, Now));

        // The user pressed Restart now: that is asking.
        Assert.True(UpdateFlow.Damaged(ready, asked: true).Approved);
        Assert.True(UpdateFlow.Damaged(ready with { Approved = true }, asked: false).Approved);
        Assert.Throws<ArgumentNullException>(() => UpdateFlow.Damaged(null!, false));
    }

    [Fact]
    public void TheWindowsAnswersChangeThePendingUpdateAsTheySay()
    {
        var waiting = UpdateFlow.DownloadFailed(Pending(UpdateStage.Available) with { RemindAt = Now, Paused = true }, "offline", Now);
        var approved = UpdateFlow.Approve(waiting);
        Assert.Equal((UpdateStage.Downloading, true, false, (DateTimeOffset?)null, (DateTimeOffset?)null), (approved.Stage, approved.Approved, approved.Paused, approved.RemindAt, approved.RetryAt));

        var retrying = Pending(UpdateStage.Downloading) with { RetryAt = Now.AddHours(1) };
        Assert.Null(UpdateFlow.Reconnected(retrying).RetryAt);
        var ready = Pending() with { RetryAt = Now };
        Assert.Same(ready, UpdateFlow.Reconnected(ready));

        var handed = UpdateFlow.HandedOff(Pending(), Now);
        Assert.Equal((UpdateStage.Installing, (DateTimeOffset?)Now), (handed.Stage, handed.HandedOffAt));
        Assert.Equal(StartupNews.Installed, UpdateFlow.AtStartup(handed, "0.16.0", Now).News);

        Assert.Throws<ArgumentNullException>(() => UpdateFlow.Approve(null!));
        Assert.Throws<ArgumentNullException>(() => UpdateFlow.Reconnected(null!));
        Assert.Throws<ArgumentNullException>(() => UpdateFlow.HandedOff(null!, Now));
    }

    [Fact]
    public void ADownloadAttemptLeavesTheUpdateReadyAsItWasOrWaitingToTryAgain()
    {
        var downloading = Pending(UpdateStage.Downloading) with { DownloadFailures = 2, RetryAt = Now, LastError = "offline" };
        Assert.Equal(UpdateFlow.Downloaded(downloading), UpdateFlow.AfterDownload(downloading, new DownloadReport(DownloadOutcome.Ready), Now));
        Assert.Same(downloading, UpdateFlow.AfterDownload(downloading, new DownloadReport(DownloadOutcome.Cancelled), Now));
        Assert.Same(downloading, UpdateFlow.AfterDownload(downloading, new DownloadReport(DownloadOutcome.Busy, "another window"), Now));
        var failed = UpdateFlow.AfterDownload(downloading, new DownloadReport(DownloadOutcome.Mismatch, "it did not match"), Now);
        Assert.Equal((3, "it did not match"), (failed.DownloadFailures, failed.LastError));
        Assert.Equal("unknown error", UpdateFlow.AfterDownload(downloading, new DownloadReport(DownloadOutcome.Failed), Now).LastError);
        Assert.Throws<ArgumentNullException>(() => UpdateFlow.AfterDownload(null!, new DownloadReport(DownloadOutcome.Ready), Now));
        Assert.Throws<ArgumentNullException>(() => UpdateFlow.AfterDownload(downloading, null!, Now));
    }

    [Fact]
    public void DaysAndTimesAreSaidTheWayPeopleSayThem()
    {
        var local = new DateTimeOffset(2026, 10, 12, 0, 30, 0, TimeSpan.FromHours(2));
        Assert.Equal("today", UpdateFlow.When(local, local));

        // 23:00 UTC on the 11th is already the 12th where the user is.
        Assert.Equal("today", UpdateFlow.When(new DateTimeOffset(2026, 10, 11, 23, 0, 0, TimeSpan.Zero), local));
        Assert.Equal("yesterday", UpdateFlow.When(local.AddDays(-1), local));
        Assert.Equal("tomorrow", UpdateFlow.When(local.AddDays(1), local));
        Assert.Equal("on 2 Oct 2026", UpdateFlow.When(local.AddDays(-10), local));
        Assert.Equal("01:00", UpdateFlow.Clock(new DateTimeOffset(2026, 10, 11, 23, 0, 0, TimeSpan.Zero), local));

        Assert.Equal("Updated to 0.16.0 yesterday.", UpdateFlow.LastUpdated("0.16.0", local.AddDays(-1), local));
        Assert.Null(UpdateFlow.LastUpdated(null, local, local));
        Assert.Null(UpdateFlow.LastUpdated("", local, local));
        Assert.Null(UpdateFlow.LastUpdated("0.16.0", null, local));
    }
}
