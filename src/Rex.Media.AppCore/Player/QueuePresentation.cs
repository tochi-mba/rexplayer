using Rex.Media.Engine;

namespace Rex.Media.AppCore.Player;

/// <summary>
/// What the queue panel says about the queue, and what it offers. The count and the now and next
/// line show only while the queue holds entries, since an empty queue says so once in its own
/// empty state; the drag hint shows only with two entries to reorder; the jump to the playing
/// entry is offered only while an entry is current; and the current entry's own line says whether
/// it is playing or paused, and nothing once the player has stopped, ended or failed.
/// </summary>
/// <param name="Count">"1 item", "12 items".</param>
/// <param name="Now">"Now playing: Sungba", "Current media outside the queue" or "Nothing playing".</param>
/// <param name="Next">"Up next: Terminator" or "No upcoming items".</param>
/// <param name="ShowsStatus">Whether the count and the now and next line show.</param>
/// <param name="ShowsDragHint">Whether the hint about dragging and Alt+Up/Down shows.</param>
/// <param name="CanJump">Whether "Jump to the playing item" has an entry to go to.</param>
/// <param name="CurrentState">"Playing" or "Paused" for the current entry's line, or null for none.</param>
public sealed record QueuePresentation(string Count, string Now, string Next, bool ShowsStatus, bool ShowsDragHint,
    bool CanJump, string? CurrentState)
{
    /// <summary>
    /// The panel for a queue of <paramref name="count"/> entries whose current entry is
    /// <paramref name="current"/> and whose next is <paramref name="next"/>, while the player holds
    /// <paramref name="playing"/> (which can be media from outside the queue) in <paramref name="state"/>.
    /// </summary>
    public static QueuePresentation Of(int count, PlaylistItem? current, PlaylistItem? next, PlaylistItem? playing, SessionState state)
    {
        var active = playing is not null && state is not (SessionState.Idle or SessionState.Ended or SessionState.Faulted);
        var now = current is not null && active ? "Now playing: " + current.Title
            : active ? "Current media outside the queue" : "Nothing playing";
        var currentState = current is null || !active ? null
            : state is SessionState.Paused or SessionState.Ready ? "Paused" : "Playing";
        return new(count == 1 ? "1 item" : $"{count} items", now,
            next is not null ? "Up next: " + next.Title : "No upcoming items",
            count > 0, count > 1, current is not null, currentState);
    }
}
