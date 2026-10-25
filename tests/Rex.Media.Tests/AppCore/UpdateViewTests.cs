using Rex.Media.AppCore.Updates;
using Rex.Media.Settings;
using Rex.Media.TestKit;
using static Rex.Media.Tests.AppCore.UpdateFlowTests;

namespace Rex.Media.Tests.AppCore;

public sealed class UpdateViewTests
{
    private const long Megabyte = 1048576;

    /// <summary>15:00 on 12 October where the user is (an hour ahead of UTC).</summary>
    private static readonly DateTimeOffset Local = new(2026, 10, 12, 15, 0, 0, TimeSpan.FromHours(1));

    private static UpdateSituation Situation(PendingUpdate? pending = null) =>
        new("0.15.11", pending, UpdateMode.DownloadAndAsk, Checking: false, Downloading: false, Transfer: null,
            Online: true, Metered: false, LastCheck: null, CheckProblem: null, Open: false, Local);

    private static UpdatePanel Panel(PendingUpdate? pending = null, Func<UpdateSituation, UpdateSituation>? change = null) =>
        UpdateView.Panel((change ?? (s => s))(Situation(pending)));

    [Fact]
    [Capability("TOOL-09")]
    public void ABackgroundDownloadIsQuietAndSaysHowFarItHasGot()
    {
        // The update bar used to cover the picture with "Downloading rexplayer 0.17.0" over
        // "Downloading rexplayer 0.17.0: 15%", with no size, no time left and no word that it
        // carries on by itself. Now it is a small indicator whose tooltip says all of that.
        var transfer = new TransferProgress(42 * Megabyte, 280 * Megabyte, TimeSpan.FromSeconds(70));
        var panel = Panel(Pending(UpdateStage.Downloading, "0.19.0"), s => s with { Downloading = true, Transfer = transfer });

        Assert.Equal(UpdateState.Downloading, panel.State);
        Assert.Equal("Downloading rexplayer 0.19.0", panel.Title);
        Assert.Equal("15%, 42 of 280 MB, about 1 minute left", panel.Status);
        Assert.Equal("Keeps downloading in the background. You can keep watching.", panel.Note);
        Assert.Equal(UpdateCommands.Pause | UpdateCommands.Cancel, panel.Commands);
        Assert.Equal(UpdateCommands.None, panel.Primary);
        Assert.Equal((UpdateTone.Accent, 0.15, false, true), (panel.Tone, panel.Progress, panel.Busy, panel.Shown));
        Assert.Equal("Downloading rexplayer 0.19.0 in the background: 15%, 42 of 280 MB, about 1 minute left", panel.Line);
        Assert.Equal("Downloading rexplayer 0.19.0 in the background.", panel.Spoken);

        var indicator = UpdateView.Indicator(panel, open: false);
        Assert.Equal(new UpdateIndicatorView(true, UpdateGlyph.Download, UpdateBadge.None, 0.15, false, panel.Line), indicator);
    }

    [Fact]
    public void ADownloadStartingOrOfUnknownSizeSpinsAndOneBeingCheckedSaysSo()
    {
        var downloading = Pending(UpdateStage.Downloading, "0.19.0");
        var starting = Panel(downloading, s => s with { Downloading = true });
        Assert.Equal(("Starting the download...", true, (double?)null), (starting.Status, starting.Busy, starting.Progress));
        Assert.Equal("Downloading rexplayer 0.19.0 in the background.", starting.Line);

        // Allowed and about to start (the next tick starts it): the same words.
        Assert.Equal(starting, Panel(downloading));

        var unknown = Panel(downloading, s => s with { Downloading = true, Transfer = new TransferProgress(5 * Megabyte, 0) });
        Assert.Equal(("5.0 MB so far", true), (unknown.Status, unknown.Busy));

        var checking = Panel(downloading, s => s with { Downloading = true, Transfer = new TransferProgress(9, 9) });
        Assert.Equal((UpdateState.Verifying, "Checking the download...", true, UpdateCommands.Cancel), (checking.State, checking.Status, checking.Busy, checking.Commands));
    }

    [Fact]
    [Capability("TOOL-09")]
    public void APausedOfflineOrFailedDownloadSaysWhyAndWhatCanBeDone()
    {
        var downloading = Pending(UpdateStage.Downloading, "0.19.0");

        var paused = Panel(UpdateFlow.Pause(downloading));
        Assert.Equal((UpdateState.Paused, "Download paused", UpdateTone.Neutral), (paused.State, paused.Title, paused.Tone));
        Assert.Equal((UpdateCommands.Resume | UpdateCommands.Cancel, UpdateCommands.Resume), (paused.Commands, paused.Primary));
        Assert.Equal(UpdateGlyph.Paused, UpdateView.Indicator(paused, false).Glyph);

        var offline = Panel(downloading, s => s with { Online = false });
        Assert.Equal((UpdateState.Offline, "rexplayer 0.19.0 downloads once you are online.", UpdateCommands.Cancel), (offline.State, offline.Status, offline.Commands));

        // A failure uses error styling, says why in plain words, and offers Retry.
        var failed = Panel(UpdateFlow.DownloadFailed(downloading, "the connection stopped sending", Local.AddMinutes(4)));
        Assert.Equal((UpdateState.Failed, "The download stopped", UpdateTone.Error), (failed.State, failed.Title, failed.Tone));
        Assert.Equal("rexplayer 0.19.0 could not be downloaded: the connection stopped sending. It tries again today at 15:05.", failed.Status);
        Assert.Equal((UpdateCommands.Retry | UpdateCommands.Cancel, UpdateCommands.Retry), (failed.Commands, failed.Primary));
        Assert.Equal(UpdateBadge.Critical, UpdateView.Indicator(failed, false).Badge);
        Assert.Contains("unknown error", Panel(UpdateFlow.DownloadFailed(downloading, "x", Local) with { LastError = null }).Status, StringComparison.Ordinal);
    }

    [Fact]
    [Capability("TOOL-09")]
    public void AReadyUpdateOffersRestartNowAndLater()
    {
        var ready = Panel(Pending(UpdateStage.Ready, "0.19.0"));
        Assert.Equal((UpdateState.Ready, "rexplayer 0.19.0 is ready", "Restart to update; you will come back to the same moment."), (ready.State, ready.Title, ready.Status));
        Assert.Equal((UpdateCommands.Restart | UpdateCommands.Later, UpdateCommands.Restart, true), (ready.Commands, ready.Primary, ready.OffersTiming));
        Assert.Equal((UpdateTone.Success, (string?)null), (ready.Tone, ready.Note));
        Assert.Equal(new UpdateIndicatorView(true, UpdateGlyph.Ready, UpdateBadge.Success, null, false, "rexplayer 0.19.0 is ready. Restart to update; you will come back to the same moment."),
            UpdateView.Indicator(ready, false));

        Assert.Equal("It installs when you close rexplayer.", Panel(UpdateFlow.WhenClosed(Pending())).Status);
        Assert.Equal("It installs by itself when rexplayer is closed or not in use.", Panel(Pending(), s => s with { Mode = UpdateMode.Automatic }).Status);
        Assert.Equal("It installs tomorrow at 03:00 if rexplayer is not in use, or when you close it.",
            Panel(UpdateFlow.ScheduledTonight(Pending(), Local, TimeZoneInfo.CreateCustomTimeZone("x", TimeSpan.FromHours(1), "x", "x"))).Status);
        Assert.Equal("It installs tonight if rexplayer is not in use, or when you close it.", Panel(Pending() with { Timing = UpdateTiming.Scheduled }).Status);
        Assert.Equal("This release asks you to read what's new before installing.", Panel(Pending() with { NeedsAttention = true }).Note);
    }

    [Fact]
    public void AnAvailableVersionSaysWhyItIsNotDownloaded()
    {
        var available = Pending(UpdateStage.Available, "0.19.0") with { Size = 280 * Megabyte };
        var panel = Panel(available);
        Assert.Equal((UpdateState.Available, "rexplayer 0.19.0 is available", "Download it in the background, then choose when to install it."), (panel.State, panel.Title, panel.Status));
        Assert.Equal(("Download size: 280 MB.", UpdateCommands.Download | UpdateCommands.Later, UpdateCommands.Download, false), (panel.Note, panel.Commands, panel.Primary, panel.OffersTiming));
        Assert.Equal((UpdateBadge.Attention, true), (UpdateView.Indicator(panel, false).Badge, UpdateView.Indicator(panel, false).Visible));

        Assert.Equal("You are on a metered connection, so it has not been downloaded.", Panel(available, s => s with { Metered = true }).Status);
        Assert.Equal("Download it in the background, then choose when to install it.", Panel(available, s => s with { Metered = true, Mode = UpdateMode.NotifyOnly }).Status);
        Assert.Equal("Download size: 280 MB. This release asks you to read what's new before installing.", Panel(available with { NeedsAttention = true }).Note);
        Assert.Null(Panel(available with { Size = 0 }).Note);

        // A cancelled download stays on offer in the panel but the indicator goes.
        var declined = Panel(UpdateFlow.CancelDownload(available));
        Assert.Equal("You cancelled its download. Download it whenever it suits you.", declined.Status);
        Assert.False(UpdateView.Indicator(declined, open: false).Visible);
        Assert.True(UpdateView.Indicator(declined, open: true).Visible);
    }

    [Fact]
    [Capability("TOOL-09")]
    public void ACheckTheUserAskedForShowsItsLiveStateAndAFailureOffersToCheckAgain()
    {
        var checking = Panel(null, s => s with { Checking = true });
        Assert.Equal((UpdateState.Checking, "Checking for updates", true), (checking.State, checking.Title, checking.Busy));
        Assert.Equal(new UpdateIndicatorView(false, UpdateGlyph.Checking, UpdateBadge.None, null, true, checking.Line), UpdateView.Indicator(checking, open: false));
        Assert.True(UpdateView.Indicator(checking, open: true).Visible);

        // An automatic check does not flicker a pending update's panel; one the user asked for shows.
        Assert.Equal(UpdateState.Ready, Panel(Pending(), s => s with { Checking = true }).State);
        Assert.Equal(UpdateState.Checking, Panel(Pending(), s => s with { Checking = true, Open = true }).State);
        Assert.Equal(UpdateState.Downloading, Panel(Pending(UpdateStage.Downloading), s => s with { Checking = true, Open = true, Downloading = true }).State);

        var upToDate = Panel(null, s => s with { LastCheck = Local.AddMinutes(-10) });
        Assert.Equal((UpdateState.UpToDate, "rexplayer is up to date", "You have rexplayer 0.15.11. Last checked today at 14:50."), (upToDate.State, upToDate.Title, upToDate.Status));
        Assert.Equal((UpdateGlyph.UpToDate, false), (UpdateView.Indicator(upToDate, false).Glyph, UpdateView.Indicator(upToDate, false).Visible));
        Assert.Equal("You have rexplayer 0.15.11.", Panel().Status);

        var failed = Panel(null, s => s with { CheckProblem = UpdateView.CheckProblem(true, 429, false) });
        Assert.Equal((UpdateState.CheckFailed, UpdateTone.Error, "The update server is busy. rexplayer tries again later."), (failed.State, failed.Tone, failed.Status));
        Assert.Equal((UpdateCommands.CheckAgain, UpdateCommands.CheckAgain), (failed.Commands, failed.Primary));
        Assert.Equal((UpdateGlyph.Checking, UpdateBadge.Critical), (UpdateView.Indicator(failed, true).Glyph, UpdateView.Indicator(failed, true).Badge));
    }

    [Theory]
    [InlineData(false, null, false, "You are offline. rexplayer looks again once you are connected.")]
    [InlineData(true, null, true, "The update server did not answer in time. rexplayer tries again later.")]
    [InlineData(true, 403, false, "The update server is busy. rexplayer tries again later.")]
    [InlineData(true, 500, false, "The update server answered with an error (500). rexplayer tries again later.")]
    [InlineData(true, null, false, "rexplayer could not reach the update server. rexplayer tries again later.")]
    public void AFailedCheckSaysWhyInPlainWords(bool online, int? status, bool timedOut, string words)
    {
        Assert.Equal(words, UpdateView.CheckProblem(online, status, timedOut));
    }

    [Fact]
    public void InstallingSaysWhatHappensNext()
    {
        var installing = Panel(Pending(UpdateStage.Installing, "0.19.0"));
        Assert.Equal((UpdateState.Installing, "Installing rexplayer 0.19.0", true, UpdateCommands.None), (installing.State, installing.Title, installing.Busy, installing.Commands));
        Assert.Equal((UpdateGlyph.Ready, UpdateBadge.None, true), (UpdateView.Indicator(installing, false).Glyph, UpdateView.Indicator(installing, false).Badge, UpdateView.Indicator(installing, false).Visible));
    }

    [Fact]
    [Capability("TOOL-09")]
    public void TheCardIsOneLineWithOneMainAction()
    {
        var ready = UpdateView.Card(UpdatePrompt.Ready, Pending(UpdateStage.Ready, "0.19.0"), metered: false);
        Assert.Equal(new UpdateCardView(UpdatePrompt.Ready, "0.19.0", "rexplayer 0.19.0 is ready. Restart to update; you will come back to the same moment.", "Restart now"), ready);

        var available = Pending(UpdateStage.Available, "0.19.0") with { Size = 280 * Megabyte };
        Assert.Equal("rexplayer 0.19.0 (280 MB) is available. Download it in the background, then choose when to install it.", UpdateView.Card(UpdatePrompt.Available, available, false)!.Message);
        Assert.Equal("rexplayer 0.19.0 is available. You are on a metered connection, so it has not been downloaded. This release asks you to read what's new first.",
            UpdateView.Card(UpdatePrompt.Available, available with { Size = 0, NeedsAttention = true }, true)!.Message);
        Assert.Equal("Download", UpdateView.Card(UpdatePrompt.Available, available, false)!.Action);

        Assert.Null(UpdateView.Card(UpdatePrompt.None, available, false));
        Assert.Null(UpdateView.Card(UpdatePrompt.Ready, null, false));
    }

    [Fact]
    public void ACardForANewerVersionIsNotTheCardForTheOlderOne()
    {
        // The bar offered 0.16.0; a check found 0.16.1 while it was open. The bar used to be left
        // naming 0.16.0. Cards are compared whole, so the newer one is drawn.
        var older = UpdateView.Card(UpdatePrompt.Available, Pending(UpdateStage.Available, "0.16.0"), false);
        var newer = UpdateView.Card(UpdatePrompt.Available, Pending(UpdateStage.Available, "0.16.1"), false);
        Assert.NotEqual(older, newer);
        Assert.Equal(newer, UpdateView.Card(UpdatePrompt.Available, Pending(UpdateStage.Available, "0.16.1"), false));
    }

    [Fact]
    [Capability("TOOL-09")]
    public void AScreenReaderHearsChangesOfStateNotEveryPercent()
    {
        var downloading = Pending(UpdateStage.Downloading, "0.19.0");
        var at15 = Panel(downloading, s => s with { Downloading = true, Transfer = new TransferProgress(15, 100) });
        var at16 = Panel(downloading, s => s with { Downloading = true, Transfer = new TransferProgress(16, 100) });
        var ready = Panel(Pending(UpdateStage.Ready, "0.19.0"));

        Assert.Equal("Downloading rexplayer 0.19.0 in the background.", UpdateView.Announcement(null, at15, open: false));
        Assert.Null(UpdateView.Announcement(at15, at16, open: false));
        Assert.Equal(ready.Line, UpdateView.Announcement(at16, ready, open: false));

        // A newer version in the same state is news.
        Assert.NotNull(UpdateView.Announcement(ready, Panel(Pending(UpdateStage.Ready, "0.19.1")), open: false));

        // What an automatic check found is said only when it found something.
        var upToDate = Panel();
        Assert.Null(UpdateView.Announcement(null, upToDate, open: false));
        Assert.Equal(upToDate.Line, UpdateView.Announcement(null, upToDate, open: true));
        Assert.Throws<ArgumentNullException>(() => UpdateView.Announcement(null, null!, true));
    }

    [Fact]
    public void RemindersAndStartupNewsHavePlainWords()
    {
        Assert.Equal("rexplayer reminds you about 0.16.0 in an hour.", UpdateView.Reminded(RemindChoice.InAnHour, "0.16.0"));
        Assert.Equal("rexplayer reminds you about 0.16.0 tomorrow.", UpdateView.Reminded(RemindChoice.Tomorrow, "0.16.0"));
        Assert.Equal("rexplayer reminds you about 0.16.0 next week.", UpdateView.Reminded(RemindChoice.NextWeek, "0.16.0"));

        Assert.Equal(new UpdateNotice("rexplayer 0.16.0 was not installed", "The installer was cancelled, so rexplayer 0.15.2 is still here and nothing was lost.", false),
            UpdateView.News(StartupNews.InstallCancelled, InstallEnd.Cancelled, "0.16.0", "0.15.2"));
        Assert.Equal(new UpdateNotice("rexplayer 0.16.0 did not install",
                "The installer could not close rexplayer. rexplayer 0.15.2 is still here and nothing was lost. The log (Help, Open the log folder) has the details.", true),
            UpdateView.News(StartupNews.InstallFailed, InstallEnd.AppOpen, "0.16.0", "0.15.2"));
        Assert.StartsWith("The installer stopped before it finished.", UpdateView.News(StartupNews.InstallFailed, InstallEnd.Unknown, "0.16.0", "0.15.2")!.Message, StringComparison.Ordinal);
        Assert.Equal(new UpdateNotice("The update could not be installed", "It failed 3 times, so rexplayer 0.15.2 set it aside. A later check offers it again; the log has the details.", true),
            UpdateView.News(StartupNews.InstallAbandoned, InstallEnd.Failed, null, "0.15.2"));
        Assert.Null(UpdateView.News(StartupNews.Installed, InstallEnd.Unknown, "0.16.0", "0.16.0"));
        Assert.Null(UpdateView.News(StartupNews.None, InstallEnd.Unknown, null, "0.16.0"));
    }

    [Fact]
    public void NothingIsNoView()
    {
        Assert.Throws<ArgumentNullException>(() => UpdateView.Panel(null!));
        Assert.Throws<ArgumentNullException>(() => UpdateView.Indicator(null!, false));
    }
}
