using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.Engine;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Metadata;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;
using Rex.Media.Primitives;

namespace Rex.Media.App;

/// <summary>
/// Movies against Other videos (<see cref="MovieRules"/>): what the movie lookups said about each
/// video, read from memory only, and the person's own "This is a movie" and "Not a movie", kept with
/// their other detail choices in the library's store so rescans and cleared metadata never undo them.
/// Nothing here is stored or worked out ahead: each view asks as it is built.
/// <para>
/// Other videos sits in the sidebar between TV Shows and All videos, a plain card grid with no
/// billboard. All videos stays the whole set (Movies, TV shows and Other videos together), because
/// it is where a file is found whatever it turned out to be; Other videos only takes the clutter
/// out of Movies.
/// </para>
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>What the lookups said about a video's title, from memory only: safe on the window's thread.</summary>
    private MovieLookup MovieLookupOf(LibraryEntry entry)
    {
        var metadata = _settings.Metadata;
        if (!metadata.Movies || entry.Kind != LibraryKind.Video || MetadataPolicy.Excludes(metadata.Excluded, entry.Path))
        {
            return MovieLookup.NotSearched;
        }

        var key = MetadataMatch.MovieKey(LibraryViews.MovieTitleOf(entry), LibraryViews.MovieYearOf(entry));
        lock (_metadataGate)
        {
            return !_metadataMovies.TryGetValue(key, out var movie) ? MovieLookup.NotSearched
                : movie.Found ? MovieLookup.Matched : MovieLookup.Unmatched;
        }
    }

    /// <summary>The section a standalone video is in now: Movies or Other videos.</summary>
    private LibrarySource SectionOf(LibraryEntry video) =>
        MovieRules.IsMovie(video, MovieLookupOf) ? LibrarySource.Movies : LibrarySource.OtherVideos;

    private PlaylistItem? _shownVersionItem;
    private MediaInfo? _shownVersionInfo;
    private string? _shownVersionSignature;

    /// <summary>
    /// Clear stale movie-version actions before an item/profile switch without
    /// abandoning keyboard focus on a removed menu item.
    /// </summary>
    private void ClearMovieVersionMenu()
    {
        if (MovieVersionButton.Visibility == Visibility.Visible && Content.XamlRoot is { } xamlRoot)
        {
            var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(xamlRoot);
            if (ReferenceEquals(focused, MovieVersionButton)
                || (focused is MenuFlyoutItemBase menuItem && MovieVersionMenu.Items.Contains(menuItem)))
            {
                PlayPauseButton.Focus(FocusState.Programmatic);
            }
        }

        MovieVersionMenu.Hide();
        MovieVersionButton.Visibility = Visibility.Collapsed;
        MovieVersionMenu.Items.Clear();
        _shownVersionItem = null;
        _shownVersionInfo = null;
        _shownVersionSignature = null;
    }

    /// <summary>Show alternate local encodings only for the same film and release year.</summary>
    private void ShowMovieVersions()
    {
        if (_stageView.Profile != MediaStageProfile.Movie
            || _player.Info?.FirstTrack(Rex.Media.Primitives.MediaKind.Video) is null
            || _player.Item?.Location is not { } playing || _library?.Entry(playing) is not { } current
            || _player.Playlist.CurrentIndex < 0 || _player.State is SessionState.Idle or SessionState.Faulted)
        {
            if (_shownVersionItem is not null || MovieVersionMenu.Items.Count > 0)
            {
                ClearMovieVersionMenu();
            }

            return;
        }

        var editions = LibraryViews.MovieVersions(_library.Entries, current);
        if (editions.Count < 2)
        {
            if (_shownVersionItem is not null || MovieVersionMenu.Items.Count > 0)
            {
                ClearMovieVersionMenu();
            }

            return;
        }

        // Playing/paused/position updates must not replace an open flyout.
        // Rebuild only when the playing stream or the available encodings change.
        var signature = string.Join("\n", editions.Select(edition =>
            edition.Path + "\t" + LibraryViews.MovieResolution(edition)));
        if (ReferenceEquals(_player.Item, _shownVersionItem)
            && ReferenceEquals(_player.Info, _shownVersionInfo)
            && string.Equals(signature, _shownVersionSignature, StringComparison.Ordinal))
        {
            return;
        }

        ClearMovieVersionMenu();
        // Bind actions to the stream that actually created the flyout. Even if
        // a later film shares a release name, a queued click from this menu
        // must not switch an encoding of the replacement item.
        var sourceItem = _player.Item;
        var sourceInfo = _player.Info;
        foreach (var (index, edition) in editions.Index())
        {
            var selected = LibraryViews.SameLocation(edition.Path, playing);
            var label = LibraryViews.MovieResolution(edition);
            var item = new ToggleMenuFlyoutItem
            {
                Text = label + " · " + Path.GetFileName(edition.Path),
                MinWidth = 44,
                MinHeight = 44,
                IsChecked = selected,
                IsEnabled = !selected,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(item, $"MovieVersion-{index}");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, label + (selected ? ", currently playing" : ", play this version"));
            ToolTipService.SetToolTip(item, edition.Path);
            item.Click += (_, _) => SwitchMovieVersion(edition.Path, sourceItem, sourceInfo);
            MovieVersionMenu.Items.Add(item);
        }

        _shownVersionItem = _player.Item;
        _shownVersionInfo = _player.Info;
        _shownVersionSignature = signature;
        MovieVersionButton.Content = LibraryViews.MovieResolution(current);
        MovieVersionButton.Visibility = Visibility.Visible;
    }

    private void SwitchMovieVersion(string path, PlaylistItem? sourceItem, MediaInfo? sourceInfo)
    {
        if (!ReferenceEquals(_player.Item, sourceItem)
            || !ReferenceEquals(_player.Info, sourceInfo)
            || _stageView.Profile != MediaStageProfile.Movie
            || _player.Info?.FirstTrack(Rex.Media.Primitives.MediaKind.Video) is null
            || _player.Item?.Location is not { } playing || _library?.Entry(playing) is not { } current)
        {
            return;
        }

        // Re-evaluate at selection time: the scan may have removed or reclassified a file
        // since the flyout was opened. An unrelated video can never be switched in.
        var target = LibraryViews.MovieVersions(_library.Entries, current)
            .FirstOrDefault(entry => LibraryViews.SameLocation(entry.Path, path));
        if (target is null || !_player.SwitchLibraryVersion(target))
        {
            Say("That movie version is no longer available.");
        }
    }

    /// <summary>"This is a movie" and "Not a movie" for the chosen videos that are not TV episodes, whichever would move something.</summary>
    private void AddMovieChoiceMenuItems(MenuFlyout menu, IReadOnlyList<LibraryEntry> entries)
    {
        // Only videos outside a show: a special or an extra that joined one stays with it.
        var standalone = LibraryViews.Standalone(_library!.Entries).Select(entry => entry.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var videos = entries.Where(entry => standalone.Contains(entry.Path)).ToList();
        if (videos.Count == 0)
        {
            return;
        }

        AddFindFilmMenuItem(menu, videos);
        var movies = videos.Count(entry => SectionOf(entry) == LibrarySource.Movies);
        if (movies < videos.Count)
        {
            menu.Items.Add(MenuItem("This is a movie", "LibraryMenuIsMovie", () => ChooseMovie(videos, movie: true)));
        }

        if (movies > 0)
        {
            menu.Items.Add(MenuItem("Not a movie", "LibraryMenuNotMovie", () => ChooseMovie(videos, movie: false)));
        }
    }

    /// <summary>Keeps the person's choice over the rules; the view moves the videos at once and keeps its place.</summary>
    private void ChooseMovie(IReadOnlyList<LibraryEntry> videos, bool movie)
    {
        _library!.SetOverride([.. videos.Select(video => video.Path)], new DetailOverride { Movie = movie });
        var name = videos.Count == 1 ? LibraryViews.MovieTitleOf(videos[0]) : $"{videos.Count} videos";
        Say(movie ? $"{name} moved to Movies" : $"{name} moved to Other videos");
    }
}
