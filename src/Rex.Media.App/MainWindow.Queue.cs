using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Player;

namespace Rex.Media.App;

/// <summary>
/// The queue panel's header and its playing line, as <see cref="QueuePresentation"/> decides: one
/// status line, hidden while the queue is empty, and buttons that show only while they can act.
/// </summary>
public sealed partial class MainWindow
{
    private void ShowQueueHeader(QueuePresentation queue)
    {
        PlaylistQueueCount.Text = queue.Count;
        PlaylistQueueNow.Text = queue.Now;
        PlaylistQueueNext.Text = queue.Next;
        var status = queue.ShowsStatus ? Visibility.Visible : Visibility.Collapsed;
        PlaylistQueueCount.Visibility = status;
        PlaylistQueueStatus.Visibility = status;
        PlaylistEmpty.Visibility = queue.ShowsStatus ? Visibility.Collapsed : Visibility.Visible;
        PlaylistDragHint.Visibility = queue.ShowsDragHint ? Visibility.Visible : Visibility.Collapsed;
        PlaylistJumpNow.IsEnabled = queue.CanJump;
        if (_player.Playlist.CurrentIndex is >= 0 and var current && current < _playlistRows.Count)
        {
            _playlistRows[current].SetState(queue.CurrentState);
        }
    }

    /// <summary>
    /// Undo reorder, Undo removal and Jump show only while they can act: whatever enables one
    /// shows it, and one that is spent goes, handing the keyboard focus to the queue it acted on.
    /// </summary>
    private void OnQueueActionUsableChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var action = (Control)sender;
        var usable = action.IsEnabled;
        if (!usable && action.FocusState != FocusState.Unfocused)
        {
            PlaylistView.Focus(FocusState.Programmatic);
        }

        action.Visibility = usable ? Visibility.Visible : Visibility.Collapsed;
    }
}
