using Rex.Media.AppCore.Automation;

namespace Rex.Media.App.Mac;

/// <summary>The touch-sized, translucent controller shown over full-screen playback.</summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan FullScreenControllerDelay = TimeSpan.FromSeconds(1.5);

    private void RevealFullScreenController()
    {
        if (!IsFullScreen || _chrome is null) return;
        _chrome.Hidden = false;
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
        return focusable && !_chrome.Hidden && _fullScreenHideTimer is not null
            ? new AutomationReply { Ok = true, Text = "seek;transport;volume;tracks;effects;leave;hide=1.5;opacity=0.88" }
            : AutomationReply.Failed("The full-screen controller is incomplete.");
    }

    private void DisposeFullScreenController()
    {
        _fullScreenHideTimer?.Invalidate();
        _fullScreenHideTimer?.Dispose();
        _fullScreenHideTimer = null;
    }
}
