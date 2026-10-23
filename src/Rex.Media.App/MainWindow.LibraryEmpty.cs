using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Library;
using Rex.Media.Library;
using Rex.Media.Settings;

namespace Rex.Media.App;

public sealed partial class MainWindow
{
    /// <summary>
    /// Shows or hides the words of an empty view and the ways out under them. They are kept, so a
    /// scan starting or finishing updates them (its status changes no entries), and the buttons are
    /// rebuilt only when what the view says changes.
    /// </summary>
    private void ShowLibraryEmpty(bool empty, bool searching, LibrarySource? source, LibraryViewChoice? choice = null)
    {
        _emptyView = empty ? (searching, source, choice) : null;
        ShowLibraryEmptyState();
    }

    private void ShowLibraryEmptyState()
    {
        if (_emptyView is not { } empty)
        {
            _shownEmpty = null;
            LibraryEmpty.Visibility = Visibility.Collapsed;
            LibraryFolderButtons.Children.Clear();
            return;
        }

        var (searching, source, choice) = empty;
        var view = source switch
        {
            LibrarySource.ContinueWatching => LibraryEmptyView.ContinueWatching,
            LibrarySource.RecentlyPlayed => LibraryEmptyView.RecentlyPlayed,
            LibrarySource.Playlists => LibraryEmptyView.Playlists,
            LibrarySource.Folders => LibraryEmptyView.Folders,
            _ => LibraryEmptyView.Media,
        };
        var state = LibraryEmptyState.For(view, searching, _library!.Folders.Count > 0,
            choice is { IsFiltered: true } ? LibraryArrangement.FilterLabel(choice) : null, _scanner?.Status.Length > 0);
        LibraryEmpty.Visibility = Visibility.Visible;
        if (state == _shownEmpty)
        {
            return;
        }

        _shownEmpty = state;
        LibraryEmptyText.Text = state.Text;
        LibraryFolderButtons.Children.Clear();
        if (searching)
        {
            var clearSearch = new Button { Content = "Clear search" };
            AutomationProperties.SetAutomationId(clearSearch, "LibraryEmptyClearSearch");
            clearSearch.Click += (_, _) => LibrarySearch.Text = "";
            LibraryFolderButtons.Children.Add(clearSearch);
        }

        if (state.OffersClearFilters)
        {
            var clear = new Button { Content = "Clear filters" };
            AutomationProperties.SetAutomationId(clear, "LibraryEmptyClearFilters");
            clear.Click += (_, _) => KeepViewChoice(ViewChoice(_libraryViewKey).Unfiltered());
            LibraryFolderButtons.Children.Add(clear);
        }

        if (!state.OffersFolders)
        {
            return;
        }

        var add = new Button { Content = "Add folders..." };
        AutomationProperties.SetAutomationId(add, "LibraryEmptyAddFolder");
        add.Click += OnLibraryAddFolder;
        LibraryFolderButtons.Children.Add(add);

        // The usual places, offered, never added unasked.
        foreach (var (name, folder) in new[] { ("Music", Environment.SpecialFolder.MyMusic), ("Videos", Environment.SpecialFolder.MyVideos), ("Pictures", Environment.SpecialFolder.MyPictures) })
        {
            var path = Environment.GetFolderPath(folder);
            if (path.Length > 0 && Directory.Exists(path) && !_library.Folders.Any(known => MediaLibrary.Holds(known, path)))
            {
                var suggestion = new Button { Content = "Add your " + name + " folder" };
                AutomationProperties.SetAutomationId(suggestion, "LibraryAdd" + name);
                ToolTipService.SetToolTip(suggestion, path);
                suggestion.Click += (_, _) => AddLibraryFolder(path);
                LibraryFolderButtons.Children.Add(suggestion);
            }
        }
    }

}
