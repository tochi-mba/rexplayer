namespace Rex.Media.AppCore.Player;

/// <summary>Only follow playback in the queue when the playing entry changes or the pane opens.</summary>
public static class QueueScrollPolicy
{
    public static bool ShouldFollow(bool visible, bool justOpened, PlaylistItem? previouslyShown, PlaylistItem? current) =>
        visible && current is not null && (justOpened || !ReferenceEquals(previouslyShown, current));
}
