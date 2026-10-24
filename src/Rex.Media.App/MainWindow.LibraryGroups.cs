using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Library;
using Rex.Media.Library;

namespace Rex.Media.App;

/// <summary>Expand and collapse virtualised library groups without changing playback or grouping.</summary>
public sealed partial class MainWindow
{
    // Collapse state belongs to a route and grouping choice, never to a row, so it survives sort,
    // filter, refresh and navigation without affecting the same letter on another library screen.
    private readonly Dictionary<string, HashSet<string>> _collapsedLibraryGroups = new(StringComparer.Ordinal);
    private IReadOnlyList<LibraryRowGroup> _shownLibraryGroups = [];
    private string _shownLibraryGroupScope = "";

    /// <summary>Folds the group whose header was invoked, by mouse, touch, keyboard or UI Automation.</summary>
    private void OnLibraryGroupToggle(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LibraryRowGroup group })
        {
            SetLibraryGroupExpanded(group, !group.IsExpanded);
        }
    }

    private void SetLibraryGroupExpanded(LibraryRowGroup group, bool expanded)
    {
        if (group.IsExpanded == expanded || !_shownLibraryGroups.Contains(group))
        {
            return;
        }

        if (!_collapsedLibraryGroups.TryGetValue(_shownLibraryGroupScope, out var collapsed))
        {
            _collapsedLibraryGroups[_shownLibraryGroupScope] = collapsed = new HashSet<string>(StringComparer.Ordinal);
        }

        if (expanded)
        {
            collapsed.Remove(group.Header);
        }
        else
        {
            collapsed.Add(group.Header);
        }

        group.SetExpanded(expanded);
    }

    /// <summary>One quick choice for large grouped views, without recreating the virtualised list.</summary>
    private void SetAllLibraryGroupsExpanded(bool expanded)
    {
        foreach (var group in _shownLibraryGroups)
        {
            SetLibraryGroupExpanded(group, expanded);
        }
    }


    /// <summary>Sorting, layout changes and scan refreshes retain the selection of surviving items.</summary>
    private void RestoreLibrarySelection(HashSet<string> selected, bool collage)
    {
        for (var index = 0; index < _libraryRows.Count; index++)
        {
            var row = _libraryRows[index];
            if (!selected.Contains(SelectionIdentity(row)))
            {
                continue;
            }

            if (collage)
            {
                LibraryCollage.Select(index);
            }
            else if (LibraryList.Items.IndexOf(row) >= 0)
            {
                // A collapsed group's remembered rows are intentionally not selectable.
                LibraryList.SelectedItems.Add(row);
            }
        }
    }

    /// <summary>Selects the current item and asks the virtualised view to bring it on screen.</summary>
    private void RevealLibraryRow(string path)
    {
        var index = _libraryRows.FindIndex(row => row.Item is LibraryEntry entry && LibraryViews.SameLocation(entry.Path, path));
        if (index < 0 && _librarySource is LibrarySource.Audiobooks or LibrarySource.Authors)
        {
            // For a chaptered file, reveal the last chapter mark at or before the
            // live playhead, not the first chapter in that physical file.
            var playing = LibraryViews.SameLocation(_player.Playlist.Current?.Location, path);
            var position = playing ? _player.Position : TimeSpan.Zero;
            index = _libraryRows.FindLastIndex(row => row.Item is BookChapter chapter
                && LibraryViews.SameLocation(chapter.Part.Path, path)
                && chapter.Start <= position);
            if (index < 0)
            {
                index = _libraryRows.FindIndex(row => row.Item is BookChapter chapter
                    && LibraryViews.SameLocation(chapter.Part.Path, path));
            }
        }
        if (index < 0)
        {
            return;
        }

        var row = _libraryRows[index];
        OnWindowThread(() =>
        {
            // A request to reveal the playing item must open its group before scrolling.
            if (_shownLibraryGroups.FirstOrDefault(group => !group.IsExpanded && group.AllRows.Contains(row)) is { } folded)
            {
                SetLibraryGroupExpanded(folded, true);
            }

            if (LibraryCollage.Visibility == Visibility.Visible)
            {
                LibraryCollage.Select(index);
                LibraryCollage.StartBringItemIntoView(index, new BringIntoViewOptions());
            }
            else
            {
                LibraryList.SelectedItem = row;
                LibraryList.ScrollIntoView(row, ScrollIntoViewAlignment.Leading);
            }
        });
    }

}
