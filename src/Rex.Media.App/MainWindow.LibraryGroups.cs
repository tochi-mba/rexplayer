using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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

}
