// The window's layout assigns these fields; until it does, the compiler sees them as never set.
#pragma warning disable CS0649
namespace Rex.Media.App.Mac;

/// <summary>
/// The parts of the player window that other pages sit in, as the Windows window has them: the
/// library takes the stage's place above the transport bar, and Up Next is a pane at the right.
/// The window's layout makes the two hosts; the library and the queue each fill their own and ask
/// here to be shown or hidden, so neither needs a window of its own.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The width of the Up Next pane, as on Windows.</summary>
    private const double QueuePaneWidth = 344;

    /// <summary>Covers the stage, above the transport bar; hidden until the library is shown.</summary>
    private RexPanel? _libraryHost;

    /// <summary>The pane at the right of the stage and the library; hidden until Up Next is shown.</summary>
    private RexPanel? _queueHost;

    /// <summary>The queue pane's width: <see cref="QueuePaneWidth"/> while shown, nothing while hidden.</summary>
    private NSLayoutConstraint? _queueHostWidth;

    /// <summary>The transport bar's library and queue toggles, lit while their page shows.</summary>
    private RexButton? _shellLibraryToggle;
    private RexButton? _shellQueueToggle;

    /// <summary>Whether the library is showing in the window.</summary>
    private bool LibraryShown => _libraryHost is { Hidden: false };

    /// <summary>Whether Up Next is showing in the window.</summary>
    private bool QueueShown => _queueHost is { Hidden: false };

    /// <summary>Shows or hides the library over the stage; playback carries on behind it.</summary>
    private void SetLibraryShown(bool shown)
    {
        if (_libraryHost is null || _libraryHost.Hidden == !shown)
        {
            return;
        }

        _libraryHost.Hidden = !shown;
        RefreshShellToggles();
    }

    /// <summary>Shows or hides the Up Next pane at the right.</summary>
    private void SetQueueShown(bool shown)
    {
        if (_queueHost is null || _queueHost.Hidden == !shown)
        {
            return;
        }

        _queueHost.Hidden = !shown;
        if (_queueHostWidth is not null)
        {
            _queueHostWidth.Constant = shown ? (nfloat)QueuePaneWidth : 0;
        }

        RefreshShellToggles();
    }

    /// <summary>Lights the transport bar's toggles for the pages that are showing.</summary>
    private void RefreshShellToggles()
    {
        if (_shellLibraryToggle is not null)
        {
            _shellLibraryToggle.On = LibraryShown;
        }

        if (_shellQueueToggle is not null)
        {
            _shellQueueToggle.On = QueueShown;
        }
    }
}
