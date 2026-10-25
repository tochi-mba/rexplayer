using Rex.Media.AppCore.Updates;
using Rex.Media.Settings;

namespace Rex.Media.App.Mac;

/// <summary>
/// Check the owner-pinned release feed, download the universal Mac ZIP with resume
/// and SHA-256 verification, and leave installation as an explicit user action.
/// </summary>
public sealed partial class MainWindow
{
    private const string MacUpdateAsset = "rexplayer-mac-universal.zip";
    private NSWindow? _updatesWindow;
    private NSTextField? _updatesStatus;
    private NSPopUpButton? _updatesCadence;
    private NSButton? _updatesDownload;
    private NSButton? _updatesOpenFolder;
    private NSButton? _updatesRemind;
    private NSButton? _updatesSkip;
    private CancellationTokenSource? _updateCancellation;
    private readonly HttpClient _updateHttp = new() { Timeout = TimeSpan.FromMinutes(60) };
    private UpdateOffer? _macUpdateOffer;
    private bool _updateChecking;

    private void BeginMacUpdateChecks()
    {
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
            _updatesWindow = new NSWindow(new CGRect(0, 0, 590, 365),
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
                "is kept only when its download matches the published SHA-256 checksum. " +
                "Installation is currently manual.");
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
            var check = NSButton.CreateButton("Check now", CheckForMacUpdates);
            _updatesDownload = NSButton.CreateButton("Download & verify", DownloadMacUpdate);
            _updatesOpenFolder = NSButton.CreateButton("Open downloaded ZIP folder", () =>
                NSWorkspace.SharedWorkspace.OpenUrl(NSUrl.FromFilename(_roots.UpdateFolder)));
            _updatesRemind = NSButton.CreateButton("Remind me later", () =>
            {
                _macUpdateOffer = null;
                if (_updatesStatus is not null)
                    _updatesStatus.StringValue = "We'll check again on the next scheduled interval.";
                RefreshMacUpdateControls();
            });
            _updatesSkip = NSButton.CreateButton("Skip this version", () =>
            {
                if (_macUpdateOffer is not { } skipped) return;
                SaveMacUpdatePreferences(_settings with { SkippedUpdateVersion = skipped.Version.ToString() });
                _macUpdateOffer = null;
                if (_updatesStatus is not null)
                    _updatesStatus.StringValue = "Skipped version " + skipped.Version + ".";
                RefreshMacUpdateControls();
            });
            var controls = NSStackView.FromViews([check, _updatesDownload, _updatesOpenFolder]);
            controls.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            controls.Spacing = 9;
            var stack = NSStackView.FromViews([
                heading, intro, EffectChoiceRow("Automatic checks", _updatesCadence),
                _updatesStatus, controls,
                PlaybackToolsRow(_updatesRemind, _updatesSkip),
            ]);
            stack.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            stack.Alignment = NSLayoutAttribute.Leading;
            stack.Spacing = 17;
            stack.EdgeInsets = new NSEdgeInsets(22, 24, 22, 24);
            stack.TranslatesAutoresizingMaskIntoConstraints = false;
            var root = new NSView(new CGRect(0, 0, 590, 365));
            root.AddSubview(stack);
            stack.TopAnchor.ConstraintEqualTo(root.TopAnchor).Active = true;
            stack.BottomAnchor.ConstraintEqualTo(root.BottomAnchor).Active = true;
            stack.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor).Active = true;
            stack.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor).Active = true;
            intro.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
            _updatesStatus.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
            _updatesWindow.ContentView = root;
            _updatesWindow.Center();
        }

        _updatesCadence!.SelectItem((int)_settings.UpdateChecks);
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
            _updatesStatus!.StringValue = "Could not save update preferences: " + ex.Message;
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

                RefreshMacUpdateControls();
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

        var pending = UpdateFlow.Found(offer, UpdateMode.DownloadAndAsk, metered: false, asked: true);
        _updateCancellation = new CancellationTokenSource();
        _updatesStatus!.StringValue = "Downloading and verifying " + offer.InstallerName + "…";
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
                if (_updatesStatus is not null)
                {
                    _updatesStatus.StringValue = report.Outcome == DownloadOutcome.Ready
                        ? "Verified " + offer.Version + ". Open the ZIP folder to install it manually."
                        : "Update download did not complete: " + (report.Reason ?? report.Outcome.ToString());
                }

                RefreshMacUpdateControls();
            });
        });
    }

    private void RefreshMacUpdateControls()
    {
        if (_updatesDownload is null || _updatesOpenFolder is null)
        {
            return;
        }

        _updatesDownload.Enabled = _macUpdateOffer is not null && _updateCancellation is null;
        if (_updatesRemind is not null) _updatesRemind.Enabled = _macUpdateOffer is not null;
        if (_updatesSkip is not null) _updatesSkip.Enabled = _macUpdateOffer is not null;
        _updatesOpenFolder.Enabled = _macUpdateOffer is { } offer &&
            File.Exists(Path.Combine(_roots.UpdateFolder, offer.InstallerName));
    }

    private void DisposeMacUpdates()
    {
        _updateCancellation?.Cancel();
        _updatesWindow?.Close();
        _updatesWindow?.Dispose();
        _updatesWindow = null;
        // A running download owns the request until cancellation has been observed.
        // Avoid disposing HttpClient here while its background task may still be using it.
    }
}
