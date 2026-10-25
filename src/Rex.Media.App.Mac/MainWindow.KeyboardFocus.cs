using AppKit;
using Foundation;

namespace Rex.Media.App.Mac;

/// <summary>Native keyboard traversal for the transport, timeline and audio regions.</summary>
public sealed partial class MainWindow
{
    private NSObject? _macKeyMonitor;

    private void StartMacKeyboardTraversal()
    {
        // A local monitor sees the key before a slider or text field consumes it. Handle
        // F6 only for our player window, leaving every other app and panel untouched.
        _macKeyMonitor = NSEvent.AddLocalMonitorForEventsMatchingMask(NSEventMask.KeyDown, e =>
        {
            if (e.KeyCode != 97 || e.Window != _window)
            {
                return e;
            }

            var reverse = (e.ModifierFlags & NSEventModifierMask.ShiftKeyMask) != 0;
            MoveMacFocus(reverse);
            return null!;
        });
    }

    private void MoveMacFocus(bool reverse)
    {
        if (_minimalInterface)
        {
            _minimalInterface = false;
            if (_chrome is not null) _chrome.Hidden = false;
        }

        // F6 moves between the three principal player regions, while Tab continues
        // through controls within a region. The first F6 focuses transport.
        NSView[] regions = [_playPause, _seek, _volume];
        var active = Array.FindIndex(regions, view => ReferenceEquals(view, _window.FirstResponder));
        var next = active < 0 ? (reverse ? regions.Length - 1 : 0)
            : (active + (reverse ? regions.Length - 1 : 1)) % regions.Length;
        _window.MakeFirstResponder(regions[next]);
    }

    private void StopMacKeyboardTraversal()
    {
        if (_macKeyMonitor is null) return;
        NSEvent.RemoveMonitor(_macKeyMonitor);
        _macKeyMonitor.Dispose();
        _macKeyMonitor = null;
    }
}
