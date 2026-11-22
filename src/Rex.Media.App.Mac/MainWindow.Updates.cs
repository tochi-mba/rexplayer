using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Design;
using Rex.Media.AppCore.Updates;
using Rex.Media.Settings;

namespace Rex.Media.App.Mac;

/// <summary>
/// Check the owner-pinned release feed, download the universal Mac ZIP with resume and SHA-256
/// verification, and install it now, on close, or tonight through a clean process hand-off.
/// </summary>
public sealed partial class MainWindow
{
    private const string MacUpdateAsset = "rexplayer-mac-universal.zip";

    /// <summary>
    /// A stand-in release feed the operator chose over the published one (the update smoke test's
    /// own server); assets are then accepted from that feed's origin alone.
    /// </summary>
    private static readonly Uri? UpdateFeedOverride = ReadUpdateFeedOverride();

    private static Uri? ReadUpdateFeedOverride() =>
        Uri.TryCreate(Environment.GetEnvironmentVariable("REXPLAYER_UPDATE_FEED"), UriKind.Absolute, out var feed)
            ? feed : null;
    private NSWindow? _updatesWindow;
    private RexLabel? _updatesStatus;
    private NSProgressIndicator? _updatesProgress;
    private RexPanel? _updatesOfferCard;
    private RexLabel? _updatesOfferTitle;
    private NSPopUpButton? _updatesCadence;
    private NSPopUpButton? _updatesMode;
    private RexTextView? _updatesNotes;
    private RexButton? _updatesDownload;
    private RexButton? _updatesInstallNow;
    private RexButton? _updatesInstallOnClose;
    private RexButton? _updatesInstallTonight;
    private RexButton? _updatesRemind;
    private RexButton? _updatesSkip;
    private CancellationTokenSource? _updateCancellation;
    private readonly HttpClient _updateHttp = new() { Timeout = TimeSpan.FromMinutes(60) };
    private UpdateOffer? _macUpdateOffer;
    private PendingUpdate? _macPending;
    private bool _updateChecking;
    private bool _installHandedOff;
    private NSTimer? _updateInstallTimer;

    private void BeginMacUpdateChecks()
    {
        _macPending = _settings.PendingUpdate?.InstallerName == MacUpdateAsset
            ? _settings.PendingUpdate : null;
        var startup = UpdateFlow.AtStartup(_macPending, RunningVersion, DateTimeOffset.UtcNow);
        SaveMacPending(startup.Pending);
        if (_macPending is { } saved)
        {
            _macUpdateOffer = new UpdateOffer(Version.Parse(saved.Version), new Uri(saved.InstallerUrl),
                new Uri(saved.ChecksumUrl), new Uri(saved.ReleasePage), saved.InstallerName)
            {
                Notes = saved.Notes,
                Size = saved.Size,
                NeedsAttention = saved.NeedsAttention,
            };
        }
        _updateInstallTimer = NSTimer.CreateRepeatingScheduledTimer(TimeSpan.FromMinutes(1), _ =>
            InstallScheduledMacUpdate());
        if (_settings.UpdateChecks == UpdateCadence.Off)
        {
            return;
        }

        _ = NSTimer.CreateScheduledTimer(UpdateFlow.StartupDelay, _ =>
        {
            if (UpdateCheck.IsDue(_settings.UpdateChecks, _settings.LastUpdateCheck,
                    DateTimeOffset.UtcNow))
            {
                CheckForMacUpdates();
            }
        });
    }

    /// <summary>
    /// The software-updates window as a settings page, in the Windows Updates section's and update
    /// card's words: a card with the version running, the state and a progress bar while a
    /// download runs; a card for the version on offer with its notes and what to do about it; then
    /// how often to look and what to do when a version is found.
    /// </summary>
    private void ShowMacUpdates()
    {
        if (_updatesWindow is null)
        {
            _updatesWindow = new NSWindow(new CGRect(0, 0, 680, 680),
                NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable,
                NSBackingStore.Buffered, false)
            {
                Title = "Software updates - rexplayer",
                MinSize = new CGSize(560, 460),
                AutorecalculatesKeyViewLoop = true,
            };
            _updatesWindow.ReleaseWhenClosed(false);
            MacTheme.Style(_updatesWindow);
            var page = new SettingsPage("Software updates",
                "Only releases from the rexplayer GitHub repository are accepted. A ZIP " +
                "is kept only when its download matches the published SHA-256 checksum.");
            _updatesStatus = MacTheme.WrappingLabel("Check for a newer version.", 2, DesignRole.TextSecondary);
            _updatesProgress = new NSProgressIndicator
            {
                Style = NSProgressIndicatorStyle.Bar,
                Indeterminate = false,
                MinValue = 0,
                MaxValue = 100,
                Hidden = true,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            ((NSView)_updatesProgress).AccessibilityLabel = "Download progress";
            _updatesCadence = MacSettings.Choices(["Never", "Daily", "Weekly"]);
            _updatesCadence.Activated += (_, _) =>
            {
                var candidate = _settings with { UpdateChecks = (UpdateCadence)
                    (int)_updatesCadence.IndexOfSelectedItem };
                SaveMacUpdatePreferences(candidate);
            };
            _updatesMode = MacSettings.Choices(["Install it by itself when rexplayer is closed or not in use",
                "Download it, then ask me when to install", "Only tell me about it"]);
            _updatesMode.Activated += (_, _) => SaveMacUpdatePreferences(_settings with
            {
                UpdateMode = (UpdateMode)(int)_updatesMode.IndexOfSelectedItem,
            });
            var check = MacTheme.TextButton("Check for updates now", CheckForMacUpdates);
            _updatesDownload = MacTheme.TextButton("Download", DownloadMacUpdate, RexButtonLook.Accent, label: "Download and verify the update");
            _updatesInstallNow = MacTheme.TextButton("Restart and install now", () => InstallMacUpdate(relaunch: true), RexButtonLook.Accent);
            _updatesInstallOnClose = MacTheme.TextButton("Install when I close rexplayer", ScheduleMacUpdateOnClose);
            _updatesInstallTonight = MacTheme.TextButton("Install tonight", ScheduleMacUpdateTonight);
            _updatesRemind = MacTheme.TextButton("Remind me tomorrow", () =>
            {
                if (_macPending is { } pending)
                {
                    SaveMacPending(UpdateFlow.Remind(pending, RemindChoice.Tomorrow, DateTimeOffset.UtcNow));
                }
                _macUpdateOffer = null;
                if (_updatesStatus is not null)
                    _updatesStatus.StringValue = "We'll check again on the next scheduled interval.";
                RefreshMacUpdateControls();
            });
            _updatesSkip = MacTheme.TextButton("Skip this version", () =>
            {
                if (_macPending is not { } skipped) return;
                SaveMacUpdatePreferences(_settings with { SkippedUpdateVersion = skipped.Version });
                SaveMacPending(null);
                _macUpdateOffer = null;
                if (_updatesStatus is not null)
                    _updatesStatus.StringValue = "Skipped version " + skipped.Version + ".";
                RefreshMacUpdateControls();
            });
            _updatesNotes = new RexTextView { Text = "Release notes appear here when an update is available." };
            ((NSView)_updatesNotes).AccessibilityLabel = "What's new";
            var status = MacTheme.Stack(true, 1, _updatesStatus, _updatesProgress);
            status.EdgeInsets = new NSEdgeInsets(0, 0, MacTheme.Space(3), 0);
            _updatesStatus.WidthAnchor.ConstraintEqualTo(status.WidthAnchor).Active = true;
            _updatesProgress.WidthAnchor.ConstraintEqualTo(status.WidthAnchor).Active = true;
            _updatesOfferTitle = MacTheme.Label("", 3, semibold: true);
            var notes = _updatesNotes.InScroller(160);
            var offer = MacTheme.Stack(true, 2, _updatesOfferTitle, notes,
                MacSettings.Buttons(_updatesDownload, _updatesInstallNow, _updatesInstallOnClose, _updatesInstallTonight),
                MacSettings.Buttons(_updatesRemind, _updatesSkip));
            notes.WidthAnchor.ConstraintEqualTo(offer.WidthAnchor).Active = true;
            _updatesOfferCard = MacTheme.Panel(DesignRole.Surface, 2, hairline: true);
            MacTheme.Fill(_updatesOfferCard, offer, (double)MacTheme.Space(3));
            page.Add(
                MacSettings.Card(null,
                    MacSettings.Row("rexplayer " + RunningVersion, "The version on this Mac.", check),
                    status),
                _updatesOfferCard,
                MacSettings.Card(null,
                    MacSettings.Row("Look for new versions", null, _updatesCadence),
                    MacSettings.Row("When a new version is found", null, _updatesMode)));
            var scroll = MacSettings.Scroller();
            _updatesWindow.ContentView = scroll;
            MacSettings.Show(scroll, page);
            _updatesWindow.Center();
        }

        _updatesCadence!.SelectItem((int)_settings.UpdateChecks);
        _updatesMode!.SelectItem((int)_settings.UpdateMode);
        if (_updatesNotes is not null && _macPending is { } pending)
            _updatesNotes.Text = pending.Notes;
        RefreshMacUpdateControls();
        _updatesWindow.MakeKeyAndOrderFront(null);
    }

    private void SaveMacUpdatePreferences(PlayerSettings settings)
    {
        try
        {
            SettingsStore.Save(_roots.SettingsPath, settings);
            _settings = settings;
            _player.Settings = settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning(LogSource, "Could not save update preferences: " + ex.Message);
            if (_updatesStatus is not null)
                _updatesStatus.StringValue = "Could not save update preferences: " + ex.Message;
        }
    }

    private void CheckForMacUpdates()
    {
        if (_updateChecking)
        {
            return;
        }

        _updateChecking = true;
        if (_updatesStatus is not null)
        {
            _updatesStatus.StringValue = "Checking the rexplayer release feed…";
        }

        _ = CheckMacUpdatesAsync();
    }

    private async Task CheckMacUpdatesAsync()
    {
        try
        {
            _updateHttp.DefaultRequestHeaders.UserAgent.ParseAdd("rexplayer-mac/1");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var json = await _updateHttp.GetStringAsync(UpdateFeedOverride ?? UpdateCheck.LatestRelease, deadline.Token);
            var offer = UpdateCheck.Offer(json, RunningVersion, MacUpdateAsset, UpdateFeedOverride);
            OnWindowThread(() =>
            {
                _macUpdateOffer = offer is not null &&
                    offer.Version.ToString() == _settings.SkippedUpdateVersion ? null : offer;
                if (_macUpdateOffer is { } found && (_macPending is null || _macPending.Version != found.Version.ToString()))
                {
                    SaveMacPending(UpdateFlow.Found(found, _settings.UpdateMode, metered: false,
                        asked: false));
                }
                SaveMacUpdatePreferences(_settings with { LastUpdateCheck = DateTimeOffset.UtcNow });
                if (_updatesStatus is not null)
                {
                    _updatesStatus.StringValue = offer is null
                        ? "No newer universal Mac archive was published."
                        : _macUpdateOffer is null
                            ? "The newest version was skipped in your update preferences."
                            : "rexplayer " + offer.Version + " is available (" +
                                (offer.Size / (1024 * 1024)) + " MB).";
                }

                if (_updatesNotes is not null)
                {
                    _updatesNotes.Text = _macUpdateOffer?.Notes is { Length: > 0 } notes
                        ? notes : "This release did not include notes.";
                }

                RefreshMacUpdateControls();
                if (_macUpdateOffer is not null && _settings.UpdateMode != UpdateMode.NotifyOnly
                    && _macPending?.Stage != UpdateStage.Ready)
                {
                    DownloadMacUpdate();
                }
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            _log.Warning(LogSource, "Mac release check failed: " + ex.Message);
            OnWindowThread(() =>
            {
                if (_updatesStatus is not null)
                {
                    _updatesStatus.StringValue = "Could not check the release feed: " + ex.Message;
                }
            });
        }
        finally
        {
            OnWindowThread(() => _updateChecking = false);
        }
    }

    private void DownloadMacUpdate()
    {
        if (_macUpdateOffer is not { } offer || _updateCancellation is not null)
        {
            return;
        }

        var pending = _macPending?.Version == offer.Version.ToString()
            ? _macPending with { Stage = UpdateStage.Downloading, Approved = true }
            : UpdateFlow.Found(offer, _settings.UpdateMode, metered: false, asked: true);
        SaveMacPending(pending);
        _updateCancellation = new CancellationTokenSource();
        if (_updatesStatus is not null)
            _updatesStatus.StringValue = "Downloading and verifying " + offer.InstallerName + "…";
        RefreshMacUpdateControls();
        var cancellation = _updateCancellation;
        _ = Task.Run(() =>
        {
            DownloadReport report;
            try
            {
                var downloader = new UpdateDownloader(_updateHttp, _roots.UpdateFolder)
                {
                    Busy = () => _player.IsPlaying,
                };
                report = downloader.Download(pending, (completed, total) =>
                {
                    if (total > 0 && completed % (2 * 1024 * 1024) < 65536)
                    {
                        OnWindowThread(() =>
                        {
                            if (_updatesStatus is not null)
                            {
                                _updatesStatus.StringValue =
                                    "Downloaded " + (completed * 100 / total) + "%; verifying SHA-256 before offering the ZIP.";
                            }

                            if (_updatesProgress is not null)
                            {
                                _updatesProgress.DoubleValue = completed * 100.0 / total;
                            }
                        });
                    }
                }, cancellation.Token);
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException or UnauthorizedAccessException)
            {
                report = new DownloadReport(DownloadOutcome.Failed, ex.Message);
            }

            OnWindowThread(() =>
            {
                _updateCancellation?.Dispose();
                _updateCancellation = null;
                var completed = UpdateFlow.AfterDownload(pending, report, DateTimeOffset.UtcNow);
                SaveMacPending(completed);
                if (_updatesStatus is not null)
                {
                    _updatesStatus.StringValue = report.Outcome == DownloadOutcome.Ready
                        ? "Verified " + offer.Version + ". Choose when to install it."
                        : "Update download did not complete: " + (report.Reason ?? report.Outcome.ToString());
                }

                RefreshMacUpdateControls();
                InstallScheduledMacUpdate();
            });
        });
    }

    private void RefreshMacUpdateControls()
    {
        if (_updatesDownload is null || _updatesInstallNow is null)
        {
            return;
        }

        var ready = _macPending is { Stage: UpdateStage.Ready } pending
            && UpdateDownloader.IsReady(_roots.UpdateFolder, pending);
        _updatesDownload.Enabled = _macUpdateOffer is not null && !ready && _updateCancellation is null;
        _updatesInstallNow.Enabled = ready;
        if (_updatesInstallOnClose is not null) _updatesInstallOnClose.Enabled = ready;
        if (_updatesInstallTonight is not null) _updatesInstallTonight.Enabled = ready;
        if (_updatesRemind is not null) _updatesRemind.Enabled = _macPending is not null;
        if (_updatesSkip is not null) _updatesSkip.Enabled = _macPending is not null;
        ShowMacUpdateCards(ready);
    }

    /// <summary>
    /// Shows the offer card only while there is a version to decide about, titled by that version,
    /// with only the actions that apply now, and the progress bar only while a download runs.
    /// </summary>
    private void ShowMacUpdateCards(bool ready)
    {
        if (_updatesProgress is not null)
        {
            _updatesProgress.Hidden = _updateCancellation is null;
            if (_updateCancellation is null)
            {
                _updatesProgress.DoubleValue = 0;
            }
        }

        var version = _macPending?.Version ?? _macUpdateOffer?.Version.ToString();
        if (_updatesOfferCard is not null)
        {
            _updatesOfferCard.Hidden = version is null;
        }

        if (_updatesOfferTitle is not null && version is not null)
        {
            _updatesOfferTitle.StringValue = ready ? $"rexplayer {version} is ready" : $"rexplayer {version} is available";
        }

        if (_updatesDownload is not null)
        {
            _updatesDownload.Hidden = ready;
        }

        foreach (var install in new[] { _updatesInstallNow, _updatesInstallOnClose, _updatesInstallTonight })
        {
            if (install is not null)
            {
                install.Hidden = !ready;
            }
        }
    }

    private void SaveMacPending(PendingUpdate? pending)
    {
        _macPending = pending;
        SaveMacUpdatePreferences(_settings with { PendingUpdate = pending });
    }

    private void ScheduleMacUpdateOnClose()
    {
        if (_macPending is not { Stage: UpdateStage.Ready } pending) return;
        SaveMacPending(UpdateFlow.WhenClosed(pending));
        _updatesStatus!.StringValue = "rexplayer " + pending.Version + " will install when the app closes.";
        RefreshMacUpdateControls();
    }

    private void ScheduleMacUpdateTonight()
    {
        if (_macPending is not { Stage: UpdateStage.Ready } pending) return;
        var scheduled = UpdateFlow.ScheduledTonight(pending, DateTimeOffset.UtcNow, TimeZoneInfo.Local);
        SaveMacPending(scheduled);
        _updatesStatus!.StringValue = "rexplayer " + pending.Version + " will install tonight after 3:00 when playback is idle.";
        RefreshMacUpdateControls();
    }

    /// <summary>
    /// Installs a scheduled or automatic update only when the shared rule says rexplayer is idle:
    /// nothing playing, not full screen, no sheet or update window open, and no input for a while.
    /// </summary>
    private void InstallScheduledMacUpdate()
    {
        if (!_installHandedOff
            && UpdateFlow.InstallWhileOpen(_macPending, _settings.UpdateMode, MacUpdateMoment(), DateTimeOffset.UtcNow))
        {
            InstallMacUpdate(relaunch: true);
        }
    }

    private UpdateMoment MacUpdateMoment() => new(
        Playing: _player.IsPlaying || _player.State == Rex.Media.Engine.SessionState.Opening,
        Video: false,
        FullScreen: IsFullScreen,
        DialogOpen: NSApplication.SharedApplication.ModalWindow is not null || _window.AttachedSheet is not null,
        Editing: false,
        NoticeOpen: false,
        PanelOpen: _updatesWindow?.IsVisible == true,
        IdleFor: TimeSpan.FromSeconds(CGEventSource.GetSecondsSinceLastEventType(
            CGEventSourceStateID.CombinedSession, unchecked((CGEventType)uint.MaxValue))));

    private void InstallMacUpdate(bool relaunch)
    {
        if (_installHandedOff || _macPending is not { Stage: UpdateStage.Ready } pending
            || !UpdateDownloader.IsReady(_roots.UpdateFolder, pending))
        {
            return;
        }

        var saved = false;
        try
        {
            var archive = UpdateDownloader.InstallerPath(_roots.UpdateFolder, pending);
            var staged = MacUpdateInstaller.Stage(archive, _roots.UpdateFolder);
            if (relaunch)
            {
                _player.SaveForUpdate("Home", LibraryShown,
                    _librarySearch?.StringValue ?? "", null, null, null,
                    QueueShown, targetVersion: pending.Version, resumePaused: true);
                saved = true;
            }

            MacUpdateInstaller.HandOff(staged, NSBundle.MainBundle.BundlePath, _roots.UpdateFolder, relaunch);
            _installHandedOff = true;
            SaveMacPending(UpdateFlow.HandedOff(pending, DateTimeOffset.UtcNow));
            _log.Info(LogSource, "Handed rexplayer " + pending.Version + " to the Mac update helper.");
            if (relaunch)
            {
                NSApplication.SharedApplication.Terminate(null);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
            or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            if (saved) _player.Memory.Updating = null;
            _log.Warning(LogSource, "The Mac update could not start: " + ex.Message);
            if (_updatesStatus is not null) _updatesStatus.StringValue = "The update could not start: " + ex.Message;
        }
    }

    /// <summary>
    /// CI-only update smoke actions (TOOL-09): the same checks, downloads and installs the buttons
    /// run, driven against the stand-in feed REXPLAYER_UPDATE_FEED names.
    /// </summary>
    private AutomationReply MacUpdateSmokeAction(string? action)
    {
        if (Environment.GetEnvironmentVariable("REXPLAYER_MAC_UPDATE_TESTS") != "1")
        {
            return AutomationReply.Failed("Set REXPLAYER_MAC_UPDATE_TESTS=1 to drive the Mac updater.");
        }

        switch (action)
        {
            case "check":
                CheckForMacUpdates();
                break;
            case "download":
                DownloadMacUpdate();
                break;
            case "install-now":
                // Reply before the hand-off: installing saves the session and ends this process.
                _ = NSTimer.CreateScheduledTimer(TimeSpan.FromMilliseconds(50), _ => InstallMacUpdate(relaunch: true));
                break;
            case "status":
                break;
            default:
                return AutomationReply.Failed("Unknown Mac update action: " + action);
        }

        var ready = _macPending is { Stage: UpdateStage.Ready } pending
            && UpdateDownloader.IsReady(_roots.UpdateFolder, pending);
        return new AutomationReply
        {
            Ok = true,
            Text = $"version={RunningVersion};offer={_macUpdateOffer?.Version.ToString() ?? "none"};" +
                $"stage={_macPending?.Stage.ToString() ?? "none"};ready={ready};handedOff={_installHandedOff}",
        };
    }

    private void DisposeMacUpdates()
    {
        if (!_installHandedOff && UpdateFlow.InstallOnClose(_macPending, _settings.UpdateMode, null, DateTimeOffset.UtcNow))
        {
            InstallMacUpdate(relaunch: false);
        }

        _updateInstallTimer?.Invalidate();
        _updateInstallTimer?.Dispose();
        _updateInstallTimer = null;
        _updateCancellation?.Cancel();
        _updatesWindow?.Close();
        _updatesWindow?.Dispose();
        _updatesWindow = null;
        // A running download owns the request until cancellation has been observed.
        // Avoid disposing HttpClient here while its background task may still be using it.
    }
}
