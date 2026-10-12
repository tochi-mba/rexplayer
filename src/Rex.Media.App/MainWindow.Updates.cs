using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;
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

    private void ShowNotice(string title, string message, Button? action, InfoBarSeverity severity)
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
    /// <param name="anyVersion">
    /// The installer gave up moments ago and reopened this same version: the saved session is put
    /// back anyway, so a cancelled update costs the user nothing.
    /// </param>
    private bool RestoreUpdatedWorkspace(bool anyVersion)
    {
        UpdateSession? saved;
        try
        {
            saved = _player.RestoreAfterUpdate(anyVersion ? null : Version);
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
        Say(anyVersion ? "Back where you left off." : "Back where you left off after the update.");
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

    /// <summary>
    /// What happens once the window is up: learn how the last update went, restore an upgrade or
    /// offer crash recovery, then start the updater's clock.
    /// </summary>
    private void AfterGreeting(ResumePoint? crashed, bool openedSomething)
    {
        var news = ReconcileUpdate();
        var upgraded = !openedSomething && RestoreUpdatedWorkspace(news.RestoreSession);
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

        ShowUpdateNews(news.News);
        StartUpdates();
    }
}
