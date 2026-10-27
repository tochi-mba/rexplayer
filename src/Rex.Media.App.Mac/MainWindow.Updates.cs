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
    private NSWindow? _updatesWindow;
    private NSTextField? _updatesStatus;
    private NSPopUpButton? _updatesCadence;
    private NSPopUpButton? _updatesMode;
    private NSTextView? _updatesNotes;
    private NSButton? _updatesDownload;
    private NSButton? _updatesInstallNow;
    private NSButton? _updatesInstallOnClose;
    private NSButton? _updatesInstallTonight;
    private NSButton? _updatesRemind;
    private NSButton? _updatesSkip;
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

    private void ShowMacUpdates()
    {
        if (_updatesWindow is null)
        {
            _updatesWindow = new NSWindow(new CGRect(0, 0, 650, 590),
                NSWindowStyle.Titled | NSWindowStyle.Closable,
                NSBackingStore.Buffered, false)
            {
                Title = "Software updates - rexplayer",
            };
            _updatesWindow.ReleaseWhenClosed(false);
            var heading = NSTextField.CreateLabel("Software updates");
            heading.Font = NSFont.SystemFontOfSize(23, NSFontWeight.Semibold)!;
            var intro = NSTextField.CreateWrappingLabel(
                "Only releases from the rexplayer GitHub repository are accepted. A ZIP " +
                "is kept only when its download matches the published SHA-256 checksum.");
            intro.TextColor = NSColor.SecondaryLabel;
            _updatesStatus = NSTextField.CreateWrappingLabel("Check for a newer version.");
            _updatesStatus.TextColor = NSColor.SecondaryLabel;
            _updatesCadence = EffectChoices(["Off", "Daily", "Weekly"]);
            _updatesCadence.Activated += (_, _) =>
            {
                var candidate = _settings with { UpdateChecks = (UpdateCadence)
                    (int)_updatesCadence.IndexOfSelectedItem };
                SaveMacUpdatePreferences(candidate);
            };
            _updatesMode = EffectChoices(["Install automatically", "Download, then ask", "Notify only"]);
            _updatesMode.Activated += (_, _) => SaveMacUpdatePreferences(_settings with
            {
                UpdateMode = (UpdateMode)(int)_updatesMode.IndexOfSelectedItem,
            });
            var check = NSButton.CreateButton("Check now", CheckForMacUpdates);
            _updatesDownload = NSButton.CreateButton("Download & verify", DownloadMacUpdate);
            _updatesInstallNow = NSButton.CreateButton("Install now", () => InstallMacUpdate(relaunch: true));
            _updatesInstallOnClose = NSButton.CreateButton("Install when rexplayer closes", ScheduleMacUpdateOnClose);
            _updatesInstallTonight = NSButton.CreateButton("Install tonight", ScheduleMacUpdateTonight);
            _updatesRemind = NSButton.CreateButton("Remind me later", () =>
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
            _updatesSkip = NSButton.CreateButton("Skip this version", () =>
            {
                if (_macPending is not { } skipped) return;
                SaveMacUpdatePreferences(_settings with { SkippedUpdateVersion = skipped.Version });
                SaveMacPending(null);
                _macUpdateOffer = null;
                if (_updatesStatus is not null)
                    _updatesStatus.StringValue = "Skipped version " + skipped.Version + ".";
                RefreshMacUpdateControls();
            });
            _updatesNotes = new NSTextView(new CGRect(0, 0, 590, 180))
            {
                Editable = false,
                Selectable = true,
                Font = NSFont.SystemFontOfSize(12, NSFontWeight.Regular)!,
                Value = "Release notes appear here when an update is available.",
            };
            var notesScroll = new NSScrollView(new CGRect(0, 0, 590, 180))
            {
                HasVerticalScroller = true,
                DocumentView = _updatesNotes,
            };
            notesScroll.HeightAnchor.ConstraintGreaterThanOrEqualTo(140).Active = true;
            var controls = NSStackView.FromViews([check, _updatesDownload]);
            controls.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            controls.Spacing = 9;
            var install = NSStackView.FromViews([_updatesInstallNow, _updatesInstallOnClose, _updatesInstallTonight]);
            install.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            install.Spacing = 9;
            var stack = NSStackView.FromViews([
                heading, intro, EffectChoiceRow("Automatic checks", _updatesCadence),
                EffectChoiceRow("When an update is found", _updatesMode),
                _updatesStatus, controls, notesScroll, install,
                PlaybackToolsRow(_updatesRemind, _updatesSkip),
            ]);
            stack.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            stack.Alignment = NSLayoutAttribute.Leading;
            stack.Spacing = 17;
            stack.EdgeInsets = new NSEdgeInsets(22, 24, 22, 24);
            stack.TranslatesAutoresizingMaskIntoConstraints = false;
            var root = new NSView(new CGRect(0, 0, 650, 590));
            root.AddSubview(stack);
            stack.TopAnchor.ConstraintEqualTo(root.TopAnchor).Active = true;
            stack.BottomAnchor.ConstraintEqualTo(root.BottomAnchor).Active = true;
            stack.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor).Active = true;
            stack.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor).Active = true;
            intro.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
            _updatesStatus.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
            notesScroll.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
            _updatesWindow.ContentView = root;
            _updatesWindow.Center();
        }

        _updatesCadence!.SelectItem((int)_settings.UpdateChecks);
        _updatesMode!.SelectItem((int)_settings.UpdateMode);
        if (_updatesNotes is not null && _macPending is { } pending)
            _updatesNotes.Value = pending.Notes;
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
            var json = await _updateHttp.GetStringAsync(UpdateCheck.LatestRelease, deadline.Token);
            var offer = UpdateCheck.Offer(json, RunningVersion, MacUpdateAsset);
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
                    _updatesNotes.Value = _macUpdateOffer?.Notes is { Length: > 0 } notes
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

    private void InstallScheduledMacUpdate()
    {
        if (_installHandedOff || _player.IsPlaying || _macPending is not { Stage: UpdateStage.Ready } pending)
        {
            return;
        }

        var due = pending.Timing == UpdateTiming.Scheduled && pending.InstallAt <= DateTimeOffset.UtcNow;
        var automatic = UpdateFlow.InstallsUnasked(pending, _settings.UpdateMode);
        if (due || automatic)
        {
            InstallMacUpdate(relaunch: true);
        }
    }

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
                _player.SaveForUpdate("Home", _libraryWindow?.IsVisible == true,
                    _librarySearch?.StringValue ?? "", null, null, null,
                    _queueWindow?.IsVisible == true, targetVersion: pending.Version, resumePaused: true);
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

    private void DisposeMacUpdates()
    {
        if (!_installHandedOff && _macPending is { Stage: UpdateStage.Ready } pending
            && (pending.Timing is UpdateTiming.OnClose or UpdateTiming.Scheduled
                || UpdateFlow.InstallsUnasked(pending, _settings.UpdateMode)))
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
