using Rex.Media.AppCore.Automation;

namespace Rex.Media.App.Mac;

/// <summary>Native keyboard traversal for the transport, timeline and audio regions.</summary>
public sealed partial class MainWindow
{
    private NSObject? _macKeyMonitor;
    private IReadOnlyList<NSView> _mainKeyViews = [];

    private void SetMainKeyViews(params NSView[] views) => _mainKeyViews = views;

    private void StartMacKeyboardTraversal()
    {
        // A local monitor sees the key before a slider or text field consumes it. Handle
        // F6 only for our player window, leaving every other app and panel untouched.
        _macKeyMonitor = NSEvent.AddLocalMonitorForEventsMatchingMask(NSEventMask.KeyDown, e =>
        {
            if (e.KeyCode != 97 || e.WindowNumber != _window.WindowNumber)
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
            PlaceChrome();
        }

        RevealFullScreenController();

        // F6 moves between the three principal player regions, while Tab continues
        // through controls within a region. The first F6 focuses transport. The library and
        // Up Next keep a Tab loop of their own inside the window, so while either shows it is
        // a region too: otherwise the keyboard could never reach it.
        List<NSView> regions = [_playPause, _seek, _volume];
        if (LibraryShown && _librarySearch is not null)
        {
            regions.Insert(0, _librarySearch);
        }

        if (QueueShown && _queueTable is not null)
        {
            regions.Add(_queueTable);
        }

        var focused = _window.FirstResponder as NSView;
        var active = regions.FindIndex(view => focused is not null && (ReferenceEquals(view, focused) || focused.IsDescendantOf(view)));
        var next = active < 0 ? (reverse ? regions.Count - 1 : 0)
            : (active + (reverse ? regions.Count - 1 : 1)) % regions.Count;
        _window.MakeFirstResponder(regions[next]);
    }

    private void StopMacKeyboardTraversal()
    {
        if (_macKeyMonitor is null) return;
        NSEvent.RemoveMonitor(_macKeyMonitor);
        _macKeyMonitor.Dispose();
        _macKeyMonitor = null;
    }

    /// <summary>Exercise the native Tab loop and F6 region cycle used by keyboard-only journeys.</summary>
    private AutomationReply ProbeMacKeyboardTraversal()
    {
        if (_mainKeyViews.Count == 0 || _mainKeyViews.Where((view, index) =>
                view.NextKeyView?.Handle != _mainKeyViews[(index + 1) % _mainKeyViews.Count].Handle).Any())
        {
            return AutomationReply.Failed("The main player key-view loop is incomplete.");
        }

        _window.MakeFirstResponder(_playPause);
        MoveMacFocus(reverse: false);
        if (_window.FirstResponder?.Handle != _seek.Handle)
        {
            return AutomationReply.Failed("F6 did not move from transport to timeline.");
        }

        MoveMacFocus(reverse: false);
        if (_window.FirstResponder?.Handle != _volume.Handle)
        {
            return AutomationReply.Failed("F6 did not move from timeline to audio.");
        }

        MoveMacFocus(reverse: true);
        var reversed = _window.FirstResponder?.Handle == _seek.Handle;
        _window.MakeFirstResponder(_playPause);
        return reversed
            ? new AutomationReply { Ok = true, Text = $"tab={_mainKeyViews.Count};regions=3" }
            : AutomationReply.Failed("Shift-F6 did not move back to the timeline.");
    }
}
