using Rex.Media.AppCore.Automation;

namespace Rex.Media.App.Mac;

/// <summary>The touch-sized, translucent controller shown over full-screen playback.</summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan FullScreenControllerDelay = TimeSpan.FromSeconds(1.5);

    /// <summary>How opaque the bar is while it floats over full-screen playback.</summary>
    private const double FullScreenControllerOpacity = 0.88;

    private readonly List<NSObject> _fullScreenWatches = [];

    /// <summary>The stage ends at the bar's top while the bar sits under it.</summary>
    private NSLayoutConstraint? _stageToBar;

    /// <summary>The stage reaches the window's bottom while the bar floats over it or is hidden.</summary>
    private NSLayoutConstraint? _stageToBottom;

    /// <summary>Follows the window into and out of full screen, where the bar floats over the picture.</summary>
    private void WatchFullScreen()
    {
        _fullScreenWatches.Add(NSWindow.Notifications.ObserveDidEnterFullScreen(_window, (_, _) =>
        {
            PlaceChrome();
            ShowState();
            RevealFullScreenController();
        }));
        _fullScreenWatches.Add(NSWindow.Notifications.ObserveDidExitFullScreen(_window, (_, _) =>
        {
            PlaceChrome();
            ShowState();
        }));
    }

    /// <summary>
    /// Puts the bar where the window's state wants it: under the stage normally; over the picture,
    /// translucent, in full screen; gone in the minimal interface, the stage taking its room.
    /// </summary>
    private void PlaceChrome()
    {
        if (_chrome is null || _stageToBar is null || _stageToBottom is null) return;
        var floating = IsFullScreen || _minimalInterface;
        if (floating)
        {
            _stageToBar.Active = false;
            _stageToBottom.Active = true;
        }
        else
        {
            _stageToBottom.Active = false;
            _stageToBar.Active = true;
        }

        _chrome.AlphaValue = IsFullScreen ? (nfloat)FullScreenControllerOpacity : 1;
        if (!IsFullScreen)
        {
            _chrome.Hidden = _minimalInterface;
        }
    }

    private void RevealFullScreenController()
    {
        if (!IsFullScreen || _chrome is null) return;
        _chrome.Hidden = false;
        ShowFullScreenButtons();
        _fullScreenHideTimer?.Invalidate();
        _fullScreenHideTimer?.Dispose();
        _fullScreenHideTimer = NSTimer.CreateScheduledTimer(FullScreenControllerDelay, _ =>
        {
            _fullScreenHideTimer?.Dispose();
            _fullScreenHideTimer = null;
            var focused = _window.FirstResponder as NSView;
            if (IsFullScreen && (focused is null || !_mainKeyViews.Contains(focused)))
            {
                if (_chrome is not null) _chrome.Hidden = true;
            }
        });
    }

    private AutomationReply ProbeFullScreenController()
    {
        if (!IsFullScreen || _chrome is null || _fullScreenTracks is null ||
            _fullScreenEffects is null || _fullScreenLeave is null)
        {
            return AutomationReply.Failed("Enter full screen before checking its controller.");
        }

        RevealFullScreenController();
        var focusable = _mainKeyViews.Contains(_seek) && _mainKeyViews.Contains(_playPause)
            && _mainKeyViews.Contains(_volume) && _mainKeyViews.Contains(_fullScreenTracks)
            && _mainKeyViews.Contains(_fullScreenEffects) && _mainKeyViews.Contains(_fullScreenLeave);
        var shown = !_fullScreenTracks.Hidden && !_fullScreenEffects.Hidden && !_fullScreenLeave.Hidden;
        return focusable && shown && !_chrome.Hidden && _fullScreenHideTimer is not null
            ? new AutomationReply { Ok = true, Text = "seek;transport;volume;tracks;effects;leave;hide=1.5;opacity=0.88" }
            : AutomationReply.Failed("The full-screen controller is incomplete.");
    }

    private void DisposeFullScreenController()
    {
        _fullScreenHideTimer?.Invalidate();
        _fullScreenHideTimer?.Dispose();
        _fullScreenHideTimer = null;
        foreach (var watch in _fullScreenWatches)
        {
            watch.Dispose();
        }

        _fullScreenWatches.Clear();
    }
}
