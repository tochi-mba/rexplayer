using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;
using Rex.Media.AppCore.Updates;
using Rex.Media.Engine;
using Rex.Media.Video;

namespace Rex.Media.App;

public sealed partial class MainWindow
{
    private static readonly string ResumePath = Path.Combine(App.DataRoot, "resume.json");
    private bool _installingUpdate;
    private UpdateWindowState? _pendingUpdateWindowState;
    private PlaylistItem? _pendingUpdateItem;

    /// <summary>
    /// Offers, in the banner at the foot of the picture, to carry on where a run that did not close
    /// cleanly left off (WIN-14). Answering either way clears the marker.
    /// </summary>
    private void OfferResume(ResumePoint point)
    {
        var resume = new Button { Content = "Resume" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(resume, "ResumeButton");
        resume.Click += (_, _) =>
        {
            Notice.IsOpen = false;
            _player.Resume(point.Location, TimeSpan.FromSeconds(point.PositionSeconds));
        };
        ShowNotice("rexplayer closed unexpectedly", $"Carry on with {point.Title} at {TimeText.Format(TimeSpan.FromSeconds(point.PositionSeconds))}?", resume, InfoBarSeverity.Informational);
        Notice.Closed += (_, _) => TryClearResumeMarker();
    }

    /// <summary>Notes what is playing and where, for <see cref="OfferResume"/> after a crash.</summary>
    private void RememberWhereWeAre()
    {
        _player.RememberPosition();
        try
        {
            if (_player.IsPlaying && _player.Item is { } item)
            {
                ResumeMarker.Write(ResumePath, new ResumePoint(item.Location, _player.Title, _player.Position.TotalSeconds));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.Log.Warning(LogSource, "The resume point could not be saved: " + ex.Message);
        }
    }

    private static void TryClearResumeMarker()
    {
        try
        {
            ResumeMarker.Clear(ResumePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.Log.Warning(LogSource, "The resume point could not be cleared: " + ex.Message);
        }
    }

    /// <summary>
    /// Looks for a newer release (TOOL-09): on the schedule the user chose, or when they ask.
    /// Nothing is downloaded until they choose Install.
    /// </summary>
    private async Task CheckForUpdatesAsync(bool asked)
    {
        UpdateOffer? offer;
        try
        {
            using var http = UpdateClient();
            var json = await http.GetStringAsync(UpdateCheck.LatestRelease);
            offer = UpdateCheck.Offer(json, Version);
            _settings = _settings with { LastUpdateCheck = DateTimeOffset.UtcNow };
            SaveSettings();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            App.Log.Info(LogSource, "Could not look for updates: " + ex.Message);
            if (asked)
            {
                Say("Could not reach the release page: " + ex.Message);
            }

            return;
        }

        if (offer is null)
        {
            if (asked)
            {
                Say($"rexplayer {Version} is the newest version.");
            }

            return;
        }

        var install = new Button { Content = "Install" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(install, "InstallUpdateButton");
        install.Click += async (_, _) =>
        {
            Notice.IsOpen = false;
            await InstallAsync(offer);
        };
        ShowNotice($"rexplayer {offer.Version} is available", "Your queue, playback position and open library view will be restored when rexplayer reopens.", install, InfoBarSeverity.Success);
    }

    /// <summary>
    /// Downloads the installer and its checksum, and hands the verified file to the updater. The
    /// updater waits for this process to close cleanly before starting installation, so neither the
    /// shell nor the installer blocks this window or has to force it down.
    /// </summary>
    private async Task InstallAsync(UpdateOffer offer)
    {
        if (_installingUpdate || _closed)
        {
            return;
        }

        _installingUpdate = true;
        try
        {
            await DownloadAndInstallAsync(offer);
        }
        finally
        {
            _installingUpdate = false;
        }
    }

    private async Task DownloadAndInstallAsync(UpdateOffer offer)
    {
        Say($"Downloading rexplayer {offer.Version}\u2026");
        var folder = Path.Combine(Path.GetTempPath(), "rexplayer-update", Guid.NewGuid().ToString("N"));
        var installer = Path.Combine(folder, offer.InstallerName);
        try
        {
            Directory.CreateDirectory(folder);
            using var http = UpdateClient();
            http.Timeout = TimeSpan.FromMinutes(10);
            var checksum = await http.GetStringAsync(offer.Checksum);
            await using (var file = File.Create(installer))
            {
                await using var download = await http.GetStreamAsync(offer.Installer);
                await download.CopyToAsync(file);
            }

            var verified = await Task.Run(() =>
            {
                using var file = File.OpenRead(installer);
                return UpdateCheck.Verify(file, checksum, offer.InstallerName);
            });
            if (!verified)
            {
                App.Log.Warning(LogSource, $"The download of {offer.InstallerName} did not match its checksum.");
                Say("The download did not match its published checksum, so nothing was installed.");
                return;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            Say("The update could not be downloaded: " + ex.Message);
            return;
        }

        if (_closed)
        {
            return;
        }

        Say($"Starting the rexplayer {offer.Version} update\u2026");
        var handoffSaved = false;
        try
        {
            // Store the one-time hand-off *before* starting the updater. It waits for us to
            // close; if starting it fails, remove the hand-off and leave the window open.
            _player.SaveForUpdate(_librarySource.ToString(), LibraryOpen, LibrarySearch.Text,
                _libraryGroup?.Name, _libraryGroup?.Detail, _librarySeason?.Name,
                PlaylistPane.Visibility == Microsoft.UI.Xaml.Visibility.Visible,
                new UpdateWindowState(_aspect.Name, _crop.Name, _view.Zoom,
                    _view.CenterX, _view.CenterY, IsFullScreen, _showRemaining),
                targetVersion: offer.Version.ToString());
            handoffSaved = true;
            var updater = Path.Combine(AppContext.BaseDirectory, "rexupdate.exe");
            var stagedUpdater = Path.Combine(folder, "rexupdate.exe");
            var handoffLog = Path.Combine(App.DataRoot, "logs", "update.log");
            var parent = Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await Task.Run(() =>
            {
                File.Copy(updater, stagedUpdater, overwrite: true);
                using var process = Process.Start(new ProcessStartInfo(stagedUpdater)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    ArgumentList = { parent, installer, handoffLog },
                });
                if (process is null)
                {
                    throw new InvalidOperationException("Windows did not start the update hand-off.");
                }
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            if (handoffSaved)
            {
                try
                {
                    _player.Memory.Updating = null;
                }
                catch (Exception clearError) when (clearError is IOException or UnauthorizedAccessException)
                {
                    App.Log.Warning(LogSource, "The abandoned update marker could not be cleared: " + clearError.Message);
                }
            }

            App.Log.Warning(LogSource, "The update hand-off could not start: " + ex.Message);
            Say("The installer is ready, but the update could not start: " + ex.Message);
            return;
        }

        App.Log.Info(LogSource, $"Handed rexplayer {offer.Version} to the updater; closing cleanly.");
        Close();
    }

    private static HttpClient UpdateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"rexplayer/{Version}");
        return http;
    }

    private void ShowNotice(string title, string message, Button action, InfoBarSeverity severity)
    {
        Notice.Title = title;
        Notice.Message = message;
        Notice.ActionButton = action;
        Notice.Severity = severity;
        Notice.IsOpen = true;
    }

    /// <summary>
    /// Puts the user's open workspace back after a verified in-app upgrade. Unknown views, removed
    /// albums and missing folders degrade to the containing library view instead of crashing.
    /// Ordinary launches and files intentionally opened from Explorer are unaffected.
    /// </summary>
    private bool RestoreUpdatedWorkspace()
    {
        UpdateSession? saved;
        try
        {
            saved = _player.RestoreAfterUpdate(Version);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            App.Log.Warning(LogSource, "The previous update session could not be restored: " + ex.Message);
            return false;
        }

        if (saved is null)
        {
            return false;
        }

        PlaylistPane.Visibility = saved.PlaylistVisible
            ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        PlaylistButton.IsChecked = saved.PlaylistVisible;
        if (Enum.TryParse<LibrarySource>(saved.LibrarySource, out var source) && Enum.IsDefined(source))
        {
            var index = Array.FindIndex(LibrarySourceItems, item => item.Source == source);
            if (index >= 0)
            {
                LibrarySources.SelectedIndex = index;
                _librarySource = source;
                if (saved.GroupName is { Length: > 0 } && _library is { } library)
                {
                    _libraryGroup = Groups(source, library.Entries).FirstOrDefault(group =>
                        group.Name == saved.GroupName && group.Detail == saved.GroupDetail);
                    if (_libraryGroup is not null && saved.SeasonName is { Length: > 0 })
                    {
                        _librarySeason = LibraryViews.TvSeasons(_libraryGroup)
                            .FirstOrDefault(season => season.Name == saved.SeasonName);
                    }
                }
            }
        }

        if (saved.Search.Length > 0)
        {
            LibrarySearch.Text = saved.Search[..Math.Min(saved.Search.Length, 256)];
        }

        if (saved.LibraryVisible)
        {
            SetLibraryOpen(true);
        }

        // There is no incoming video to restore geometry onto for a stopped/idle session.
        // Without this guard stale zoom could unexpectedly affect media opened later.
        _pendingUpdateWindowState = saved.Active ? saved.WindowState : null;
        _pendingUpdateItem = _player.Item;
        if (saved.WindowState is { } display)
        {
            _showRemaining = display.ShowRemaining;
            SetFullScreen(display.FullScreen);
            if (saved.Active)
            {
                RestoreUpdatedPictureView();
            }
        }

        ShowPlaylist();
        ShowPosition();
        Say("Back where you left off after the update.");
        return true;
    }

    /// <summary>
    /// Apply restored picture geometry when the decoded stream supplies its dimensions.
    /// Opening is asynchronous, so applying zoom immediately after the upgrade would erase it:
    /// HasVideo is still false at that point. An item that cannot open drops stale geometry.
    /// </summary>
    private void RestoreUpdatedPictureView()
    {
        if (_pendingUpdateWindowState is not { } saved)
        {
            return;
        }

        if (!ReferenceEquals(_pendingUpdateItem, _player.Item)
            || _player.State is SessionState.Idle or SessionState.Faulted or SessionState.Ended
            || _player.Info is not null && !HasVideo)
        {
            _pendingUpdateWindowState = null;
            _pendingUpdateItem = null;
            return;
        }

        if (!HasVideo)
        {
            return;
        }

        _pendingUpdateWindowState = null;
        _pendingUpdateItem = null;
        _aspect = VideoGeometry.AspectRatios.FirstOrDefault(preset => preset.Name == saved.Aspect)
            ?? VideoGeometry.AspectRatios[0];
        _crop = VideoGeometry.Crops.FirstOrDefault(preset => preset.Name == saved.Crop)
            ?? VideoGeometry.Crops[0];
        ApplyShape();
        SetView(new PictureView(saved.Zoom, saved.CenterX, saved.CenterY));
    }

    /// <summary>What happens once the window is up: restore an upgrade, or offer crash recovery, then check for updates.</summary>
    private void AfterGreeting(ResumePoint? crashed, bool openedSomething)
    {
        var upgraded = !openedSomething && RestoreUpdatedWorkspace();
        if (openedSomething && _player.Memory.Updating is not null)
        {
            // Explicitly opening media wins over a pending upgrade recovery. Do not let
            // the old workspace override a later ordinary launch either.
            try
            {
                _player.Memory.Updating = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                App.Log.Warning(LogSource, "The outdated update hand-off could not be cleared: " + ex.Message);
            }
        }

        if (!upgraded && crashed is not null && !openedSomething)
        {
            OfferResume(crashed);
        }
        else
        {
            TryClearResumeMarker();
        }

        if (UpdateCheck.IsDue(_settings.UpdateChecks, _settings.LastUpdateCheck, DateTimeOffset.UtcNow))
        {
            _ = CheckForUpdatesAsync(asked: false);
        }
    }
}
