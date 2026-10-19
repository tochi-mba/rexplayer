using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Rex.Media.AppCore.Library;
using Rex.Media.Library;

namespace Rex.Media.App;

/// <summary>
/// Quick actions on a library song line (LIB-05): Play and Add to queue appear on the line the
/// pointer is over or the keyboard focuses, the same two ways a card shows its play mark, and act
/// on that line alone. The buttons never take focus themselves, so arrowing through the list keeps
/// its rhythm and the focused line always shows its actions.
/// </summary>
public sealed partial class MainWindow
{
    private readonly HashSet<SelectorItem> _rowFocusWired = [];

    /// <summary>Wires a list container once; recycled containers keep their wiring for every line they show.</summary>
    private void WireRowFocus(SelectorItem? container)
    {
        if (container is not null && _rowFocusWired.Add(container))
        {
            container.GotFocus += OnRowContainerGotFocus;
            container.LostFocus += OnRowContainerLostFocus;
        }
    }

    /// <summary>Keyboard focus behaves exactly like hover: the line's actions show and its artwork jumps the queue.</summary>
    private void OnRowContainerGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is SelectorItem { ContentTemplateRoot: FrameworkElement root } container)
        {
            SetCardPlay(root, 1);
            if (container.Content is LibraryRow row)
            {
                PrioritizeLibraryPicture(row);
            }
        }
    }

    private void OnRowContainerLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is SelectorItem { ContentTemplateRoot: FrameworkElement root })
        {
            SetCardPlay(root, 0);
        }
    }

    /// <summary>Plays the line: a song with the rest of its view after it, an album or genre line on its own.</summary>
    private void OnRowPlay(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LibraryRow row })
        {
            if (row.Item is LibraryGroup)
            {
                Play(EntriesOf([row]), 0);
            }
            else
            {
                Activate(row);
            }
        }
    }

    /// <summary>Adds the line's media to the end of the playlist and says so.</summary>
    private void OnRowQueue(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LibraryRow row } && EntriesOf([row]) is { Count: > 0 } entries)
        {
            _player.EnqueueFromLibrary(entries);
            Say(entries.Count == 1 ? $"Added {row.Name} to the playlist" : $"Added {LibraryActions.Count(entries.Count, "item")} to the playlist");
        }
    }
}
