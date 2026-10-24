using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;

namespace Rex.Media.App;

public sealed partial class MainWindow
{
    /// <summary>The list's right-click menu, for the lines chosen when it opens.</summary>
    private void FillLibraryMenu(MenuFlyout menu, List<LibraryRow> chosen)
    {
        menu.Items.Clear();
        if (chosen.Count == 0)
        {
            menu.Items.Add(new MenuFlyoutItem { Text = "Choose something first", IsEnabled = false });
            return;
        }

        if (TryAddScopedAudiobookMenu(menu, chosen))
        {
            return;
        }

        if (chosen[0].Item is string folder)
        {
            menu.Items.Add(MenuItem("Play everything in it", "LibraryMenuPlay", PlayChosen));
            menu.Items.Add(MenuItem("Stop watching...", "LibraryMenuRemoveFolder", () => Forget(RemoveChosenFoldersAsync())));
            AddTranscriptionMenuItems(menu, EntriesOf([chosen[0]]), folder);
            return;
        }

        if (chosen[0].Item is NamedPlaylist playlist)
        {
            menu.Items.Add(MenuItem("Play", "LibraryMenuPlay", () => _player.PlayPlaylist(playlist.Id)));
            menu.Items.Add(MenuItem("Shuffle play", "LibraryMenuShuffleNamed", () => _player.PlayPlaylist(playlist.Id, shuffle: true)));
            menu.Items.Add(MenuItem("Add to the end of this playlist", null, () => _player.PlayPlaylist(playlist.Id, enqueue: true)));
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(MenuItem("Edit contents...", "LibraryMenuEditNamedPlaylist", () => Forget(EditPlaylistAsync(playlist.Id))));
            menu.Items.Add(MenuItem("Add files...", "LibraryMenuAddFilesNamedPlaylist", () => Forget(AddFilesToNamedPlaylistAsync(playlist.Id))));
            menu.Items.Add(MenuItem("Rename...", null, () => Forget(RenamePlaylistAsync(playlist.Id))));
            menu.Items.Add(MenuItem("Duplicate", null, () => _player.DuplicatePlaylist(playlist.Id)));
            menu.Items.Add(MenuItem("Delete...", null, () => Forget(DeletePlaylistAsync(playlist.Id))));
            return;
        }

        if (chosen is [{ Item: LibraryEntry { Kind: LibraryKind.Video } video }]
            && LibraryViews.AutoplayVideos(_library!.Entries, video) is { Count: > 1 } season
            && LibraryViews.EpisodeOf(video) is { } episode)
        {
            menu.Items.Add(MenuItem($"Play season {episode.Season} from here ({LibraryActions.Count(season.Count, "episode")})", "LibraryMenuPlay", PlayChosen));
            menu.Items.Add(MenuItem("Play only this episode", null, () => Play([video], 0)));
        }
        else
        {
            menu.Items.Add(MenuItem("Play", "LibraryMenuPlay", PlayChosen));
        }

        if (chosen is [{ Item: LibraryEntry { Kind: LibraryKind.Video } single }]
            && (_librarySource == LibrarySource.Movies || IsMovie(single)))
        {
            menu.Items.Add(MenuItem("Movie details", "LibraryMenuDetails", () => OpenMovieDetails(single)));
        }

        var selectedEntries = EntriesOf(chosen).DistinctBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase).ToList();
        menu.Items.Add(MenuItem(LibraryActions.MenuText("Play", selectedEntries.Count, tail: "next"), "LibraryMenuPlayNext",
            () => _player.PlayNextFromLibrary(selectedEntries)));
        menu.Items.Add(MenuItem(LibraryActions.MenuText("Add", selectedEntries.Count, tail: "to queue"), "LibraryMenuEnqueue", EnqueueChosen));
        var items = selectedEntries.Select(PlayerController.ItemOf).ToList();
        var addTo = new MenuFlyoutSubItem { Text = "Add to a playlist" };
        AutomationProperties.SetAutomationId(addTo, "LibraryMenuAddTo");
        foreach (var named in _player.NamedPlaylists)
        {
            var id = named.Id;
            addTo.Items.Add(MenuItem(named.Name, null, () => _player.AddToPlaylist(id, items)));
        }

        if (addTo.Items.Count > 0)
        {
            addTo.Items.Add(new MenuFlyoutSeparator());
        }

        addTo.Items.Add(MenuItem("New playlist...", "LibraryMenuAddToNew", () => Forget(NewPlaylistAsync(items))));
        menu.Items.Add(addTo);
        if (selectedEntries.Count > 0)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(MenuItem(LibraryActions.MenuText("Mark", selectedEntries.Count, tail: "as watched"), "LibraryMenuMarkWatched",
                () => _player.MarkWatched(selectedEntries.Select(entry => entry.Path).ToList())));
            menu.Items.Add(MenuItem(LibraryActions.MenuText("Mark", selectedEntries.Count, tail: "as unwatched"), "LibraryMenuMarkUnwatched",
                () => _player.MarkUnwatched(selectedEntries.Select(entry => entry.Path).ToList())));
            menu.Items.Add(MenuItem(LibraryActions.MenuText("Reset", selectedEntries.Count, tail: "progress"), "LibraryMenuResetProgress",
                () => _player.ResetProgress(selectedEntries.Select(entry => entry.Path).ToList())));
            menu.Items.Add(MenuItem(LibraryActions.MenuText("Copy", selectedEntries.Count, tail: "paths"), "LibraryMenuCopyPath",
                () => CopyLibraryPaths(selectedEntries)));
            menu.Items.Add(MenuItem(selectedEntries.Count == 1 ? "Reveal in File Explorer" : "Reveal files in File Explorer", "LibraryMenuOpenFolder",
                () => Forget(OpenLibraryFoldersAsync(selectedEntries))));
            AddEditDetailsMenuItem(menu, selectedEntries);
            AddListeningMenuItems(menu, selectedEntries, chosen);
            AddMovieChoiceMenuItems(menu, selectedEntries);
            AddTranscriptionMenuItems(menu, selectedEntries, null);
            menu.Items.Add(new MenuFlyoutSeparator());
            AddMetadataMenuItems(menu, chosen);

            menu.Items.Add(MenuItem(LibraryActions.MenuText("Remove", selectedEntries.Count, tail: "from library..."), "LibraryMenuRemove",
                () => Forget(RemoveLibraryEntriesAsync(selectedEntries))));
            menu.Items.Add(MenuItem(LibraryActions.MenuText("Delete", selectedEntries.Count, tail: "to Recycle Bin..."), "LibraryMenuDelete",
                () => Forget(DeleteLibraryEntriesAsync(selectedEntries))));
        }
    }

}
