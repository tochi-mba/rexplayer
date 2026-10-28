using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;
using Rex.Media.AppCore.Library;
using static Rex.Media.AppCore.Library.LibraryActions;
using Rex.Media.Library;
using Rex.Media.Settings;

namespace Rex.Media.App;

/// <summary>
/// How the library looks (LIB-05). A sidebar of views with their icons, Home first: shelves of what
/// to play next. Every other view opens under a banner of its artwork (sharp in front, filling the
/// banner behind), its name and what it holds, with Play, Shuffle and Add. Under it, the view's own
/// layout (a list, a grid of cards or a wall of big ones), order, grouping under headings, and card
/// size, each kept for that view. Cards show a play mark when pointed at.
/// </summary>
public sealed partial class MainWindow
{
    private const string MostPlayedShelf = "Your most played";
    private const string DownloadingShelf = "Downloading";

    private static readonly (LibrarySource Source, LibrarySourceItem Item)[] LibrarySourceItems =
    [
        (LibrarySource.Home, new("\uE80F", "Home")),
        (LibrarySource.Music, new("\uE189", "Music")),
        (LibrarySource.Songs, new("\uE8D6", "Songs")),
        (LibrarySource.Albums, new("\uE93C", "Albums")),
        (LibrarySource.Artists, new("\uE77B", "Artists")),
        (LibrarySource.Genres, new("\uE8EC", "Genres")),
        (LibrarySource.Audiobooks, new("\uE82D", "Audiobooks")),
        (LibrarySource.Authors, new("\uE77B", "Authors")),
        (LibrarySource.Podcasts, new("\uE720", "Podcasts")),
        (LibrarySource.Recordings, new("\uE1D6", "Recordings")),
        (LibrarySource.Movies, new("\uE714", "Movies")),
        (LibrarySource.TvShows, new("\uE80F", "TV Shows")),
        (LibrarySource.OtherVideos, new("\uE722", "Other videos")),
        (LibrarySource.Videos, new("\uE8FD", "All videos")),
        (LibrarySource.Pictures, new("\uE91B", "Pictures")),
        (LibrarySource.ContinueWatching, new("\uE768", "Continue watching")),
        (LibrarySource.Completed, new("\uE73E", "Completed")),
        (LibrarySource.RecentlyPlayed, new("\uE81C", "Recently played")),
        (LibrarySource.RecentlyAdded, new("\uE710", "Recently added")),
        (LibrarySource.Playlists, new("\uE8FD", "Playlists")),
        (LibrarySource.Folders, new("\uE8B7", "Folders")),
    ];

    private static readonly (LibrarySort Sort, string Name)[] SortNames =
    [
        (LibrarySort.Natural, "Natural order"), (LibrarySort.Title, "Title"), (LibrarySort.Artist, "Artist"), (LibrarySort.Album, "Album"),
        (LibrarySort.Year, "Year"), (LibrarySort.Added, "Date added"), (LibrarySort.Played, "Last played"), (LibrarySort.MostPlayed, "Most played"),
        (LibrarySort.Length, "Duration"), (LibrarySort.Modified, "Date modified"),
        (LibrarySort.Size, "File size"), (LibrarySort.Path, "Folder and path"),
    ];

    private static readonly (LibraryGrouping Grouping, string Name)[] GroupingNames =
    [
        (LibraryGrouping.None, "No groups"), (LibraryGrouping.Letter, "First letter"), (LibraryGrouping.Artist, "Artist"), (LibraryGrouping.Album, "Album"),
        (LibraryGrouping.Genre, "Genre"), (LibraryGrouping.Year, "Year"), (LibraryGrouping.Decade, "Decade"), (LibraryGrouping.Folder, "Folder"),
        (LibraryGrouping.Added, "Date added"), (LibraryGrouping.Length, "Length"),
    ];

    private List<LibraryRow> _libraryRows = [];
    private List<LibraryRow> _homeShelfRows = [];
    private string _libraryViewKey = "";
    private string _shownLook = "";
    private string _shownHero = "";
    private string _selectionView = "";
    private long _heroRequest;
    private bool _settingCardSize;
    private readonly LibrarySearchText _librarySearchText = new();
    /// <summary>A view opened by a shelf's "See all" in the shelf's order, until the user leaves it or keeps a choice of their own.</summary>
    private (LibrarySource Source, LibraryViewChoice Choice)? _shelfChoice;

    private void WireLibraryView()
    {
        LibraryCardSize.Value = 180;
        _places = new LibraryPlaces(_settings.LibraryPlaces);
        WireLibraryHeaderScrolling();
    }

    /// <summary>The key a view's choices are kept under: its name, or the kind of group opened in it, or the search.</summary>
    private string ViewKey(bool searching) => searching ? "Search"
        : _librarySeason is not null ? _librarySource switch
        {
            LibrarySource.Artists => "Artists album",
            LibrarySource.Authors => "Authors book",
            _ => "TvShows page",
        }
        : _libraryGroup is not null ? _librarySource + " group" : _librarySource.ToString();

    /// <summary>How the view is shown: as the user last set it, else as suits what it holds.</summary>
    private LibraryViewChoice ViewChoice(string key)
    {
        if (_shelfChoice is { } shelf && key == shelf.Source.ToString())
        {
            return shelf.Choice;
        }

        return LibraryViewDefaults.Resolve(key, _settings.LibraryViews.GetValueOrDefault(key));
    }

    private void KeepViewChoice(LibraryViewChoice choice)
    {
        var previous = ViewChoice(_libraryViewKey);
        var orderChanged = previous.Sort != choice.Sort || previous.Descending != choice.Descending;
        choice = LibraryViewDefaults.Keep(previous, choice);
        _shelfChoice = null;
        _settings = _settings with { LibraryViews = new Dictionary<string, LibraryViewChoice>(_settings.LibraryViews, StringComparer.Ordinal) { [_libraryViewKey] = choice.Normalize() } };
        RememberLater();
        _shownLibrary = "";
        ShowLibrary();
        // A changed sort starts at its first row, not the old scroll anchor.
        if (orderChanged)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_closed && LibraryList.Visibility == Visibility.Visible && LibraryList.Items.Count > 0)
                {
                    LibraryList.ScrollIntoView(LibraryList.Items[0]);
                }
            });
        }
    }

    /// <summary>Brings the open view in line with the library; the list is replaced only when what it shows has changed.</summary>
    private void ShowLibrary()
    {
        if (_library is null || !LibraryOpen)
        {
            return;
        }

        BuildLibrary();
        ShowDownloads();
    }

    private void BuildLibrary()
    {
        var library = _library;
        if (library is null)
        {
            return;
        }

        StartMetadataWhenAllowed();
        if (UpdateSections(immediate: false))
        {
            return;
        }

        var entries = library.Entries;
        var search = _librarySearchText.Build(LibrarySearch?.Text ?? string.Empty);
        ResolveShowPage(search);
        var selectionView = $"{_librarySource}\n{_libraryGroup?.Name}\n{(_librarySource == LibrarySource.Albums ? _libraryGroup?.Detail : null)}\n{_librarySeason?.Name}\n{search}";
        var sameRoute = selectionView == _selectionView;
        if (!sameRoute)
        {
            SetLibraryHeaderCompact(false);
        }
        var selected = sameRoute
            ? SelectedLibraryRows().Select(SelectionIdentity).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];
        // Built again around the same things, the list stays anchored on the line in view.
        var anchors = sameRoute ? AnchorCandidates() : [];
        _selectionView = selectionView;
        _placeKey = LibraryPlaces.Key(_librarySource.ToString(), _libraryGroup?.Name,
            _librarySource == LibrarySource.Albums ? _libraryGroup?.Detail : null, _librarySeason?.Name, search);
        if (ShowDetailsPage(search, entries))
        {
            return;
        }

        var source = search.Length > 0 ? (LibrarySource?)null : _librarySource;
        _libraryViewKey = ViewKey(search.Length > 0);
        var choice = ViewChoice(_libraryViewKey);
        var home = source == LibrarySource.Home;
        var musicHome = source == LibrarySource.Music;
        _songsTable = source == LibrarySource.Songs && _libraryGroup is null;
        if (!home && !musicHome)
        {
            // Progress of an audiobook is a book-wide state. Keep the file-level
            // filters for folder/download/kind but defer progress until grouping.
            var effective = source is LibrarySource.Audiobooks or LibrarySource.Authors
                ? choice with { Filter = LibraryFilter.All } : choice;
            entries = LibraryArrangement.Filter(entries, effective, _player.LeftAt);
        }

        // The music views, and Home's music shelves, never hold audiobooks, podcasts or recordings.
        if (home || source is LibrarySource.Songs or LibrarySource.Albums or LibrarySource.Artists or LibrarySource.Genres)
        {
            entries = WithoutListening(entries);
        }
        var browse = !home && search.Length == 0 && BrowseOffered && choice.Look == LibraryLook.Browse;
        if (!browse)
        {
            EndBrowseHeroRotation();
        }
        LibraryHome.Visibility = home || browse || musicHome ? Visibility.Visible : Visibility.Collapsed;
        LibraryList.Visibility = home || browse || musicHome ? Visibility.Collapsed : Visibility.Visible;
        LibraryCollage.Visibility = Visibility.Collapsed;
        LibraryHero.Visibility = browse || musicHome ? Visibility.Collapsed : Visibility.Visible;
        ApplySmallHomeViewport(home);
        if (home || browse || musicHome)
        {
            ShowMusicPageExtras(null, songsTable: false);
        }

        if (home)
        {
            ShowHome(entries, sameRoute);
            return;
        }

        if (musicHome)
        {
            ShowMusicHome(entries, sameRoute);
            return;
        }

        if (browse)
        {
            LibraryPlay.Visibility = LibraryShuffle.Visibility = LibraryEnqueue.Visibility = Visibility.Collapsed;
            ShowStreamingBrowse(entries, choice, sameRoute);
            return;
        }

        string kicker, title, subtitle;
        List<LibraryRowGroup> sections;
        var kind = LibraryKind.Music;
        var options = true;
        var groupsView = false;
        var artistPage = false;
        LibraryGroup? albumPage = null;
        if (search.Length > 0)
        {
            (kicker, title) = ("SEARCH", search);
            var found = FoundFor(entries, search);
            // Search stays global for individual media, but a search made from a grouped
            // library view must also offer matching artists, albums, genres or shows as
            // navigable results. Otherwise finding an artist can only play one of their
            // songs and there is no artist page to return from.
            var matchingGroups = _librarySource is LibrarySource.Artists or LibrarySource.Albums or LibrarySource.Genres or LibrarySource.Authors
                or LibrarySource.Audiobooks or LibrarySource.Podcasts or LibrarySource.TvShows
                ? SearchGroups(_librarySource, entries).Where(group => GroupMatches(group, search)).ToList()
                : [];
            sections = matchingGroups.Count == 0
                ? EntrySections(found, choice)
                : [new LibraryRowGroup("", matchingGroups.Select(GroupRow))];
            subtitle = Count(matchingGroups.Count == 0 ? found.Count : matchingGroups.Count, "result");
        }
        else if (_librarySource == LibrarySource.Completed)
        {
            (kicker, title, subtitle, sections, groupsView) = CompletedView(entries);
        }
        else if (IsListening(_librarySource))
        {
            (kicker, title, subtitle, sections, groupsView) = ListeningView(entries);
        }
        else if (_libraryGroup is { } opened)
        {
            // The group again, as the library has it now.
            var fresh = LibraryViews.RefreshGroup(Groups(_librarySource, entries), opened, _librarySource == LibrarySource.Albums);
            _libraryGroup = fresh;
            kicker = _librarySource switch { LibrarySource.Albums => "ALBUM", LibrarySource.Artists when _librarySeason is not null => "ALBUM", LibrarySource.Artists => "ARTIST", LibrarySource.TvShows => "TV SHOW", _ => "GENRE" };
            title = fresh.Name;
            var members = fresh.Entries;
            if (_librarySource == LibrarySource.TvShows)
            {
                kind = LibraryKind.Video;
                if (_librarySeason is { } openedSeason)
                {
                    var season = LibraryViews.RefreshGroup(LibraryViews.TvSeasons(fresh), openedSeason);
                    _librarySeason = season;
                    subtitle = ShowPage.Subtitle(fresh, season);
                    sections = EpisodeSections(season.Entries, choice);
                }
                else
                {
                    var seasons = LibraryViews.TvSeasons(fresh);
                    sections = [new LibraryRowGroup("", seasons.Select(GroupRow))];
                    subtitle = Count(seasons.Count, "season");
                    groupsView = true;
                }
            }
            else if (_librarySource == LibrarySource.Artists)
            {
                // The artist page: their most played songs, then their albums, newest first, then the
                // albums of others they are on; an album opened from it shows its own track list,
                // with Back returning to the artist.
                var albums = ArtistPages.Albums(fresh);
                var featuredOn = ArtistPages.FeaturedOn(fresh);
                if (_librarySeason is { } openedAlbum)
                {
                    var album = LibraryViews.RefreshGroup([.. albums.Concat(featuredOn).Select(artistAlbum => artistAlbum.Album)], openedAlbum, detailIdentifiesGroup: true);
                    _librarySeason = album;
                    kicker = "ALBUM";
                    title = album.Name;
                    albumPage = album;
                    (subtitle, sections) = AlbumPageParts(album);
                }
                else
                {
                    artistPage = true;
                    var top = ArtistPages.TopSongs(fresh);
                    sections = [];
                    if (top.Count > 0)
                    {
                        sections.Add(new LibraryRowGroup("Top songs", top.Select(EntryRow)));
                    }

                    sections.Add(new LibraryRowGroup("Albums", albums.Select(ArtistAlbumRow)));
                    if (featuredOn.Count > 0)
                    {
                        sections.Add(new LibraryRowGroup("Featured on", featuredOn.Select(ArtistAlbumRow)));
                    }
                    subtitle = string.Join(" \u00B7 ", new[] { Count(albums.Count, "album"), Count(members.Count, "song"), Total(members) }.Where(part => !string.IsNullOrEmpty(part)));
                }
            }
            else if (_librarySource == LibrarySource.Albums)
            {
                albumPage = fresh;
                (subtitle, sections) = AlbumPageParts(fresh);
            }
            else
            {
                sections = EntrySections(members, choice);
                var year = members.Select(entry => entry.Year).Where(year => year is not null).DefaultIfEmpty().Max();
                subtitle = string.Join(" \u00B7 ", new[] { fresh?.Detail, year?.ToString(CultureInfo.CurrentCulture), Count(members.Count, "song"), Total(members) }.Where(part => !string.IsNullOrEmpty(part)));
            }
        }
        else
        {
            title = LibrarySourceItems.First(pair => pair.Source == _librarySource).Item.Name;
            kicker = "LIBRARY";
            IReadOnlyList<LibraryEntry>? list = _librarySource switch
            {
                LibrarySource.Songs => LibraryMemo.Songs(entries),
                LibrarySource.Movies => LibraryViews.Movies(entries, MovieLookupOf),
                LibrarySource.OtherVideos => LibraryViews.OtherVideos(entries, MovieLookupOf),
                LibrarySource.Videos => LibraryMemo.Videos(entries),
                LibrarySource.Pictures => LibraryMemo.Pictures(entries),
                LibrarySource.ContinueWatching => LibraryViews.ContinueWatching(entries, _player.LeftAt),
                LibrarySource.RecentlyPlayed => LibraryViews.RecentlyPlayed(entries, ExistingRecent()),
                LibrarySource.RecentlyAdded => LibraryMemo.RecentlyAdded(entries),
                _ => null,
            };
            if (list is not null)
            {
                if (_librarySource == LibrarySource.RecentlyPlayed)
                {
                    sections = [new LibraryRowGroup("", list.Select(EntryRow))];
                    options = false;
                }
                else
                {
                    sections = _librarySource == LibrarySource.Songs ? SongSections(list, choice) : EntrySections(list, choice);
                }

                kind = _librarySource switch { LibrarySource.Movies or LibrarySource.OtherVideos or LibrarySource.Videos or LibrarySource.ContinueWatching => LibraryKind.Video, LibrarySource.Pictures => LibraryKind.Picture, _ => LibraryKind.Music };
                subtitle = Count(list.Count, _librarySource switch { LibrarySource.Pictures => "picture", LibrarySource.Movies => "movie", LibrarySource.OtherVideos or LibrarySource.Videos or LibrarySource.ContinueWatching => "video", _ => "item" })
                    + (kind == LibraryKind.Picture ? "" : " \u00B7 " + Total(list));
            }
            else if (_librarySource is LibrarySource.Albums or LibrarySource.Artists or LibrarySource.Genres or LibrarySource.TvShows)
            {
                groupsView = true;
                var groups = LibraryArrangement.Sort(Groups(_librarySource, entries), choice.Sort, choice.Descending);
                sections = choice.Grouping == LibraryGrouping.Letter
                    ? [.. groups.GroupBy(group => LibraryArrangement.LetterOf(group.Name)).OrderBy(group => group.Key == "#").ThenBy(group => group.Key, StringComparer.CurrentCulture).Select(group => new LibraryRowGroup(group.Key, group.Select(GroupRow)))]
                    : [new LibraryRowGroup("", groups.Select(GroupRow))];
                kind = _librarySource == LibrarySource.TvShows ? LibraryKind.Video : LibraryKind.Music;
                subtitle = Count(groups.Count, _librarySource switch { LibrarySource.Albums => "album", LibrarySource.Artists => "artist", LibrarySource.TvShows => "show", _ => "genre" });
            }
            else
            {
                options = false;
                var rows = _librarySource == LibrarySource.Playlists
                    ? _player.NamedPlaylists.Select(p => new LibraryRow(p.Name,
                        SavedPlaylistFacts.For(p, path => library.Entry(path)).Label,
                        Count(p.Items.Count, "item"), p)).ToList()
                    : [.. library.Folders.Select(folder => new LibraryRow(folder, Count(entries.Count(entry => MediaLibrary.Holds(folder, entry.Path)), "file"), "", folder))];
                sections = [new LibraryRowGroup("", rows)];
                subtitle = _librarySource == LibrarySource.Playlists ? Count(rows.Count, "playlist") : Count(rows.Count, "folder") + " watched";
            }
        }

        var rowsForView = sections.SelectMany(section => section).ToList();
        var collage = options && choice.Look == LibraryLook.Collage;
        LibraryList.Visibility = collage ? Visibility.Collapsed : Visibility.Visible;
        LibraryCollage.Visibility = collage ? Visibility.Visible : Visibility.Collapsed;
        var glyph = LibrarySourceItems.First(pair => pair.Source == _librarySource).Item.Glyph;
        // A show's page wears the show's poster, never its first episode's frame.
        var showArt = search.Length == 0 && _librarySource == LibrarySource.TvShows && _libraryGroup is { Entries: [var first, ..] } ? first : null;
        ShowHero(kicker, title, subtitle, search.Length > 0 ? "\uE721" : glyph,
            showArt ?? rowsForView.Select(row => PictureEntry(row.Item)).FirstOrDefault(entry => entry is not null),
            showArt is null ? PictureArt.Natural : PictureArt.ShowPoster,
            artistPage ? ArtistPhotoPathIfKnown(title) : null);
        ShowViewOptions(options && !artistPage && albumPage is null, choice, groupsView, kind);
        ShowMusicPageExtras(search.Length == 0 ? albumPage : null, _songsTable && choice.Look == LibraryLook.List);
        if (search.Length == 0
            && (_librarySource == LibrarySource.Audiobooks ? _libraryGroup : _librarySource == LibrarySource.Authors ? _librarySeason : null) is { } book)
        {
            ShowTitleExtras("", TitleSections.BookSynopsis(MetadataBook(book)), credit: null,
                trailer: null, poster: false, backdrop: null);
        }
        else if (search.Length == 0 && _librarySource == LibrarySource.Podcasts
            && _libraryGroup is { } localPodcast)
        {
            ShowTitleExtras(localPodcast.Detail is { Length: > 0 } creator
                    ? "Artist tag: " + creator + " · Local episodes only"
                    : "Local episodes only",
                synopsis: null, credit: null, trailer: null, poster: false, backdrop: null);
            ShowText(LibraryOverview, PodcastDescription(localPodcast));
        }
        else if (artistPage)
        {
            var artist = MetadataArtist(title);
            ShowTitleExtras(Rex.Media.AppCore.Library.TitleSections.ArtistFacts(artist),
                Rex.Media.AppCore.Library.TitleSections.ArtistBiography(artist), credit: null,
                trailer: null, poster: false, backdrop: null);
            LibraryHeroArtBox.CornerRadius = new CornerRadius(75);
        }
        else
        {
            LibraryHeroArtBox.CornerRadius = new CornerRadius(12);
            if (_librarySource != LibrarySource.TvShows)
            {
                ShowText(LibraryFacts, "");
                ShowText(LibraryOverview, "");
                LibraryCredit.Visibility = Visibility.Collapsed;
            }
        }

        // Searching the Authors directory does not change _librarySource, only
        // the effective route (source becomes null). Search results can still
        // be author cards; their primary action must remain navigation.
        var authorDirectory = _librarySource == LibrarySource.Authors
            && _libraryGroup is null && _librarySeason is null;
        if (_librarySource == LibrarySource.Pictures && search.Length == 0)
        {
            LibraryPlayText.Text = "Start slideshow";
            ToolTipService.SetToolTip(LibraryPlay,
                "Play the selected photographs, or all visible photographs, in gallery order. Pause and Next control the show.");
        }
        else if (authorDirectory)
        {
            LibraryPlayText.Text = "Open author";
            ToolTipService.SetToolTip(LibraryPlay, "Open one author's books, then choose a book to play.");
        }
        else
        {
            // A picture slideshow's text and tooltip must not leak into a later
            // audiobook, music or author view when the same button is reused.
            LibraryPlayText.Text = "Play";
            ToolTipService.SetToolTip(LibraryPlay, source switch
            {
                LibrarySource.Audiobooks when _libraryGroup is null => "Play one selected audiobook in chapter order.",
                LibrarySource.Audiobooks => "Resume this book or play a selected chapter.",
                LibrarySource.Authors when _librarySeason is null => "Play one selected book from this author.",
                LibrarySource.Authors => "Resume this book or play a selected chapter.",
                _ => "Play the chosen items, or all of them (Enter)",
            });
        }

        ShowLibraryBack(_libraryGroup is not null && search.Length == 0);
        var folders = source == LibrarySource.Folders;
        LibraryFolderActions.Visibility = folders ? Visibility.Visible : Visibility.Collapsed;
        var noPlayback = folders || source == LibrarySource.Playlists
            || (source == LibrarySource.TvShows && _libraryGroup is null);
        LibraryPlay.Visibility = noPlayback ? Visibility.Collapsed : Visibility.Visible;
        // An author's books and a book's chapters are ordered listening tasks,
        // not a collection of tracks to shuffle. The book queue is scoped.
        LibraryShuffle.Visibility = noPlayback || authorDirectory
            || source is LibrarySource.Authors or LibrarySource.Audiobooks
            ? Visibility.Collapsed : Visibility.Visible;
        LibraryEnqueue.Visibility = noPlayback || authorDirectory
            ? Visibility.Collapsed : Visibility.Visible;
        ShowLibraryEmpty(rowsForView.Count == 0, search.Length > 0, source, options ? choice : null);

        // Rebuild the scroll anchor only after the new layout is realized.
        var previousLook = _shownLook;
        var look = SetLibraryLook(options && !artistPage && albumPage is null ? choice : new LibraryViewChoice(LibraryLook.List), rowsForView.FirstOrDefault()?.Item, options);
        var grouped = sections.Count > 1 || (sections.Count == 1 && sections[0].Header.Length > 0);
        var shown = look + "\n" + title + "\n" + grouped + "\n" + string.Join("\n", sections.Select(section => section.Header + ":" + string.Join("\n", section.Select(RowIdentity))));
        if (shown == _shownLibrary)
        {
            return;
        }

        CancelLibraryPictures();
        _libraryRows = rowsForView;
        _shownLibrary = shown;
        _shownLibraryGroups = grouped ? sections : [];
        _shownLibraryGroupScope = grouped ? _placeKey + "\n" + choice.Grouping : "";
        if (grouped && _collapsedLibraryGroups.TryGetValue(_shownLibraryGroupScope, out var collapsed))
        {
            foreach (var section in sections.Where(section => collapsed.Contains(section.Header)))
            {
                section.SetExpanded(false);
            }
        }
        if (collage)
        {
            // ItemsView keeps selection, focus and virtualized rows. Never build all card visuals
            // at once or replace the collection on every playback-position update.
            LibraryList.ItemsSource = null;
            LibraryList.GroupStyle.Clear();
            LibraryCollage.ItemTemplate ??= LibraryTemplate("LibraryCollageTemplate");
            LibraryCollage.ItemsSource = _libraryRows;
            RestoreLibrarySelection(selected, collage: true);
            if (previousLook == look)
            {
                HoldSurvivingAnchor(anchors, collage: true);
            }
            return;
        }

        LibraryCollage.ItemsSource = null;
        LibraryList.GroupStyle.Clear();
        if (grouped)
        {
            LibraryList.GroupStyle.Add(new GroupStyle { HeaderTemplate = (DataTemplate)Root.Resources["LibraryGroupHeaderTemplate"], HidesIfEmpty = false });
            LibraryList.ItemsSource = new CollectionViewSource { IsSourceGrouped = true, Source = sections }.View;
        }
        else
        {
            LibraryList.ItemsSource = _libraryRows;
        }

        RestoreLibrarySelection(selected, collage: false);
        if (previousLook == look)
        {
            HoldSurvivingAnchor(anchors, collage: false);
        }
    }

    /// <summary>Updates every visible representation when playback changes without moving the view.</summary>
    private void ShowCurrentLibraryItem()
    {
        var current = _player.Playlist.Current?.Location;
        foreach (var row in _libraryRows.Concat(_homeShelfRows))
        {
            row.SetPlaying(row.Item is LibraryEntry entry && LibraryViews.SameLocation(entry.Path, current));
        }
    }

    /// <summary>A list of entries, ordered and grouped as the view's choices say.</summary>
    private List<LibraryRowGroup> EntrySections(IReadOnlyList<LibraryEntry> entries, LibraryViewChoice choice) =>
        [.. LibraryArrangement.Group(LibraryArrangement.Sort(entries, choice.Sort, choice.Descending), choice.Grouping, DateTime.UtcNow)
            .Select(section => new LibraryRowGroup(section.Header, section.Entries.Select(entry => _songsTable ? SongRow(entry) : EntryRow(entry))))];

    /// <summary>An album's line on its artist's page: artwork, name, year, how many songs and how long.</summary>
    private static LibraryRow ArtistAlbumRow(ArtistAlbum album) => new(album.Album.Name,
        string.Join(" · ", new[] { album.Year?.ToString(CultureInfo.CurrentCulture), Count(album.Album.Entries.Count, "song") }.Where(part => part is not null)),
        Total(album.Album.Entries), album.Album, hasPicture: true, picturePlaceholder: "♪");

    private static string FirstLetter(string name) => name.TrimStart().FirstOrDefault() is var first && char.IsLetter(first) ? char.ToUpper(first, CultureInfo.CurrentCulture).ToString() : "#";

    /// <summary>How long it all lasts together: "48 min", "3 h 12 min".</summary>
    private static string Total(IEnumerable<LibraryEntry> entries)
    {
        var total = TimeSpan.FromTicks(entries.Sum(entry => entry.Duration?.Ticks ?? 0));
        return total.TotalHours >= 1 ? $"{(int)total.TotalHours} h {total.Minutes} min" : $"{Math.Max(total.Minutes, total > TimeSpan.Zero ? 1 : 0)} min";
    }

    /// <summary>The banner: kicker, title and what the view holds, with the first item's picture in front and filling the banner behind.</summary>
    /// <summary>The layout, order and grouping menus, each ticking the choice in use, and the card size.</summary>
    private void ShowViewOptions(bool shown, LibraryViewChoice choice, bool groupsView, LibraryKind kind)
    {
        foreach (var control in new FrameworkElement[] { LibraryLookButton, LibrarySortButton, LibraryGroupButton, LibraryFilterButton, LibrarySizePanel })
        {
            control.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        }

        if (!shown)
        {
            return;
        }

        LibraryLookButton.Content = choice.Look switch { LibraryLook.List => "List", LibraryLook.Wall => "Big cards", LibraryLook.Collage => "Collage", LibraryLook.Browse => "Browse", _ => "Cards" };
        var looks = new List<(LibraryLook Look, string Name)> { (LibraryLook.List, "List"), (LibraryLook.Grid, "Cards"), (LibraryLook.Wall, "Big cards"), (LibraryLook.Collage, "Collage") };
        if (BrowseOffered)
        {
            looks.Insert(0, (LibraryLook.Browse, "Browse"));
        }

        LibraryLookButton.Flyout = Choices("LibraryLook", looks,
            choice.Look, look => KeepViewChoice(choice with { Look = look, Grouping = look == LibraryLook.Collage ? LibraryGrouping.None : choice.Grouping }));

        // Each view offers the orders and groups that mean something for what it holds.
        var sorts = groupsView
            ? new[] { LibrarySort.Natural, LibrarySort.Title, LibrarySort.Artist, LibrarySort.Year, LibrarySort.Added, LibrarySort.Played, LibrarySort.MostPlayed, LibrarySort.Length }
            : kind switch
            {
                LibraryKind.Picture => [LibrarySort.Natural, LibrarySort.Title, LibrarySort.Added, LibrarySort.Modified, LibrarySort.Size, LibrarySort.Path],
                LibraryKind.Video => [LibrarySort.Natural, LibrarySort.Title, LibrarySort.Added, LibrarySort.Modified, LibrarySort.Size, LibrarySort.Path, LibrarySort.Played, LibrarySort.MostPlayed, LibrarySort.Length],
                _ => SortNames.Select(pair => pair.Sort).ToArray(),
            };
        var groupings = groupsView
            ? new[] { LibraryGrouping.None, LibraryGrouping.Letter }
            : kind switch
            {
                LibraryKind.Picture => [LibraryGrouping.None, LibraryGrouping.Folder, LibraryGrouping.Added],
                LibraryKind.Video => [LibraryGrouping.None, LibraryGrouping.Letter, LibraryGrouping.Folder, LibraryGrouping.Added, LibraryGrouping.Length],
                _ => GroupingNames.Select(pair => pair.Grouping).ToArray(),
            };
        var sortName = SortNames.First(pair => pair.Sort == choice.Sort).Name;
        LibrarySortButton.Content = (choice.Descending ? "\u2191 " : "\u2193 ") + sortName;
        var sortMenu = Choices("LibrarySort", [.. sorts.Select(sort => (sort, SortNames.First(pair => pair.Sort == sort).Name))], choice.Sort, sort => KeepViewChoice(choice with { Sort = sort }));
        sortMenu.Items.Add(new MenuFlyoutSeparator());
        var reverse = new ToggleMenuFlyoutItem { Text = "Reverse the order", IsChecked = choice.Descending };
        AutomationProperties.SetAutomationId(reverse, "LibrarySort-Reverse");
        reverse.Click += (_, _) => KeepViewChoice(choice with { Descending = !choice.Descending });
        sortMenu.Items.Add(reverse);
        LibrarySortButton.Flyout = sortMenu;
        LibraryGroupButton.Content = GroupingNames.First(pair => pair.Grouping == choice.Grouping).Name;
        var groupMenu = Choices("LibraryGroup", [.. groupings.Select(grouping => (grouping, GroupingNames.First(pair => pair.Grouping == grouping).Name))], choice.Grouping,
            grouping => KeepViewChoice(choice with { Grouping = grouping, Look = grouping != LibraryGrouping.None && choice.Look == LibraryLook.Collage ? LibraryLook.Grid : choice.Look }));
        if (choice.Grouping != LibraryGrouping.None)
        {
            groupMenu.Items.Add(new MenuFlyoutSeparator());
            var collapseAll = new MenuFlyoutItem { Text = "Collapse all groups" };
            AutomationProperties.SetAutomationId(collapseAll, "LibraryGroup-CollapseAll");
            collapseAll.Click += (_, _) => SetAllLibraryGroupsExpanded(false);
            groupMenu.Items.Add(collapseAll);
            var expandAll = new MenuFlyoutItem { Text = "Expand all groups" };
            AutomationProperties.SetAutomationId(expandAll, "LibraryGroup-ExpandAll");
            expandAll.Click += (_, _) => SetAllLibraryGroupsExpanded(true);
            groupMenu.Items.Add(expandAll);
        }

        LibraryGroupButton.Flyout = groupMenu;

        var filterMenu = new MenuFlyout();
        filterMenu.Items.Add(FilterGroup("Progress", "LibraryFilterProgress",
            [(LibraryFilter.All, "All progress"), (LibraryFilter.Unwatched, "Unwatched"),
                (LibraryFilter.InProgress, "In progress"), (LibraryFilter.Finished, "Finished")],
            choice.Filter, value => KeepViewChoice(choice with { Filter = value })));
        filterMenu.Items.Add(FilterGroup("Media type", "LibraryFilterKind",
            [(LibraryKindFilter.Any, "All media"), (LibraryKindFilter.Video, "Videos"),
                (LibraryKindFilter.Music, "Music"), (LibraryKindFilter.Picture, "Pictures")],
            choice.Kinds, value => KeepViewChoice(choice with { Kinds = value })));
        var folders = new MenuFlyoutSubItem { Text = "Folder" };
        AutomationProperties.SetAutomationId(folders, "LibraryFilterFolder");
        var allFolders = new RadioMenuFlyoutItem { Text = "All folders", GroupName = "LibraryFilterFolder", IsChecked = choice.Folder is null };
        AutomationProperties.SetAutomationId(allFolders, "LibraryFilterFolder-All");
        allFolders.Click += (_, _) => KeepViewChoice(choice with { Folder = null });
        folders.Items.Add(allFolders);
        if (_library is not null)
        {
            var index = 0;
            foreach (var folder in _library.Folders)
            {
                var selected = folder;
                var item = new RadioMenuFlyoutItem
                {
                    Text = selected,
                    GroupName = "LibraryFilterFolder",
                    IsChecked = string.Equals(selected, choice.Folder, StringComparison.OrdinalIgnoreCase),
                };
                AutomationProperties.SetAutomationId(item, "LibraryFilterFolder-" + index++);
                item.Click += (_, _) => KeepViewChoice(choice with { Folder = selected });
                folders.Items.Add(item);
            }
        }

        filterMenu.Items.Add(folders);
        filterMenu.Items.Add(FilterGroup("Download", "LibraryFilterDownload",
            [.. new[] { LibraryDownloadFilter.Any, LibraryDownloadFilter.Complete, LibraryDownloadFilter.Downloading }.Select(value => (value, LibraryArrangement.DownloadName(value)))],
            choice.Download, value => KeepViewChoice(choice with { Download = value })));
        filterMenu.Items.Add(new MenuFlyoutSeparator());
        var resetFilters = new MenuFlyoutItem { Text = "Clear filters", IsEnabled = choice.IsFiltered };
        AutomationProperties.SetAutomationId(resetFilters, "LibraryFilterClear");
        resetFilters.Click += (_, _) => KeepViewChoice(choice.Unfiltered());
        filterMenu.Items.Add(resetFilters);
        LibraryFilterButton.Content = LibraryArrangement.FilterLabel(choice);
        // A screen reader hears which filters are on, as the button shows them.
        AutomationProperties.SetName(LibraryFilterButton, choice.IsFiltered ? "Filter: " + LibraryArrangement.FilterLabel(choice) : "Filter by progress, type, folder or download");
        LibraryFilterButton.Flyout = filterMenu;

        LibrarySizePanel.Visibility = choice.Look == LibraryLook.List ? Visibility.Collapsed : Visibility.Visible;
        _settingCardSize = true;
        LibraryCardSize.Value = choice.CardSize;
        _settingCardSize = false;
    }

    /// <summary>Builds a quick-filter submenu with the current value ticked.</summary>
    private static MenuFlyoutSubItem FilterGroup<T>(string label, string id, IReadOnlyList<(T Value, string Name)> choices, T current, Action<T> chosen)
        where T : struct, Enum
    {
        var group = new MenuFlyoutSubItem { Text = label };
        AutomationProperties.SetAutomationId(group, id);
        foreach (var (value, name) in choices)
        {
            var item = new RadioMenuFlyoutItem { Text = name, GroupName = id, IsChecked = EqualityComparer<T>.Default.Equals(value, current) };
            AutomationProperties.SetAutomationId(item, id + "-" + value);
            item.Click += (_, _) => chosen(value);
            group.Items.Add(item);
        }

        return group;
    }

    /// <summary>A menu of choices, the one in use ticked; choosing one calls <paramref name="chosen"/>.</summary>
    private static MenuFlyout Choices<T>(string id, IReadOnlyList<(T Value, string Name)> choices, T current, Action<T> chosen)
        where T : struct, Enum
    {
        var menu = new MenuFlyout();
        foreach (var (value, name) in choices)
        {
            var item = new RadioMenuFlyoutItem { Text = name, GroupName = id, IsChecked = EqualityComparer<T>.Default.Equals(value, current) };
            AutomationProperties.SetAutomationId(item, id + "-" + value);
            item.Click += (_, _) => chosen(value);
            menu.Items.Add(item);
        }

        return menu;
    }

    private void OnLibraryCardSizeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_settingCardSize && _libraryViewKey.Length > 0 && Math.Abs(e.NewValue - e.OldValue) >= 1)
        {
            KeepViewChoice(ViewChoice(_libraryViewKey) with { CardSize = (int)e.NewValue });
        }
    }

    /// <summary>
    /// Lays the list out as <paramref name="choice"/> says, in the cards that suit what it shows:
    /// square covers for music, wide frames for video, photos for pictures; gives the layout's name.
    /// </summary>
    private string SetLibraryLook(LibraryViewChoice choice, object? sample, bool media)
    {
        if (choice.Look == LibraryLook.Collage && media)
        {
            var collageKey = $"Collage|{choice.CardSize}";
            if (collageKey != _shownLook)
            {
                _shownLook = collageKey;
                if (LibraryCollage.Layout is LinedFlowLayout flow)
                {
                    flow.LineHeight = choice.CardSize;
                }

                AutomationProperties.SetName(LibraryCollage, "Justified collage of library artwork");
            }

            return collageKey;
        }

        var kind = sample switch { LibraryEntry entry => entry.Kind, LibraryGroup => _librarySource == LibrarySource.TvShows ? LibraryKind.Video : LibraryKind.Music, _ => (LibraryKind?)null };
        var size = choice.Look == LibraryLook.Wall ? Math.Min(LibraryViewChoice.LargestCard * 1.4, choice.CardSize * 1.6) : choice.CardSize;
        var (template, width, height, name) = !media || kind is null ? ("LibraryGenericTemplate", 0.0, 0.0, "Library items, list")
            : choice.Look == LibraryLook.List && kind == LibraryKind.Video && _librarySeason is not null ? ("LibraryEpisodeTemplate", 0.0, 0.0, "Episodes, list")
            : choice.Look == LibraryLook.List && _shownMusicPage.StartsWith("album", StringComparison.Ordinal) ? ("MusicTrackTemplate", 0.0, 0.0, "Album tracks, list")
            : choice.Look == LibraryLook.List && _songsTable ? ("MusicSongRowTemplate", 0.0, 0.0, "Songs, table")
            : choice.Look == LibraryLook.List ? ("LibrarySongTemplate", 0.0, 0.0, "Compact list")
            : kind == LibraryKind.Video && (_librarySource == LibrarySource.Movies || (_librarySource == LibrarySource.TvShows && _libraryGroup is null)) ? ("LibraryMovieTemplate", size + 12, (size * 1.5) + 68, "Movies and TV shows, poster grid")
            : kind == LibraryKind.Video ? ("LibraryVideoTemplate", (size * 1.45) + 12, (size * 1.45 * 9 / 16) + 64, "Episodes, thumbnail grid")
            : kind == LibraryKind.Picture ? ("LibraryPhotoTemplate", size + 8, (size * 0.75) + 52, "Pictures, gallery grid")
            : ("LibraryBrowseTemplate", size + 12, size + 78, "Cover grid");
        var look = $"{template}|{width:0}|{height:0}";
        if (look == _shownLook)
        {
            return look;
        }

        _shownLook = look;
        LibraryList.ItemTemplate = LibraryTemplate(template);
        LibraryList.ItemsPanel = width == 0
            ? (ItemsPanelTemplate)Root.Resources["LibraryListPanel"]
            : (ItemsPanelTemplate)XamlReader.Load(string.Create(CultureInfo.InvariantCulture,
                $"""<ItemsPanelTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><ItemsWrapGrid Orientation="Horizontal" ItemWidth="{width:0}" ItemHeight="{height:0}" /></ItemsPanelTemplate>"""));
        AutomationProperties.SetName(LibraryList, name);
        return look;
    }

    /// <summary>Home: shelves of what to play next, each with a way to see all of it.</summary>
    private void ShowHome(IReadOnlyList<LibraryEntry> entries, bool sameRoute)
    {
        var (music, videos, pictures) = LibraryMemo.Counts(entries);
        var recent = LibraryMemo.RecentlyAdded(entries);
        ShowHero("YOUR LIBRARY", "Home", string.Join(" \u00B7 ", new[] { Count(music, "song"), Count(videos, "video"), Count(pictures, "picture") }), "\uE80F",
            recent.FirstOrDefault(entry => entry.Kind != LibraryKind.Picture) ?? recent.FirstOrDefault());
        ShowViewOptions(false, ViewChoice("Home"), false, LibraryKind.Music);
        LibraryBack.Visibility = Visibility.Collapsed;
        LibraryFolderActions.Visibility = Visibility.Collapsed;
        LibraryPlay.Visibility = LibraryShuffle.Visibility = LibraryEnqueue.Visibility = Visibility.Collapsed;
        _libraryRows = [];
        ShowLibraryEmpty(entries.Count == 0, false, LibrarySource.Home);

        var downloading = HomeShelves.Downloading(entries, 20);
        var albums = LibraryMemo.Albums(entries);
        var pinned = new List<LibraryGroup>();
        foreach (var path in _settings.PinnedAlbumPaths)
        {
            var album = albums.FirstOrDefault(group => group.Entries.Any(song =>
                string.Equals(song.Path, path, StringComparison.OrdinalIgnoreCase)));
            if (album is not null && !pinned.Contains(album))
            {
                pinned.Add(album);
            }
        }

        (string Title, LibrarySource See, IReadOnlyList<LibraryRow> Rows, bool Wide)[] shelves =
        [
            (DownloadingShelf, Enum.Parse<LibrarySource>(HomeShelves.SeeAllDownloading(downloading, ViewChoice).View), [.. downloading.Select(EntryRow)], downloading is [{ Kind: LibraryKind.Video }, ..]),
            ("Pinned albums", LibrarySource.Albums, [.. pinned.Take(20).Select(GroupRow)], false),
            ("Jump back in", LibrarySource.ContinueWatching, [.. LibraryViews.ContinueWatching(entries, _player.LeftAt).Take(20).Select(EntryRow)], true),
            .. ListeningShelvesForHome(),
            ("Recently played", LibrarySource.RecentlyPlayed, [.. LibraryViews.RecentlyPlayed(entries, ExistingRecent()).Where(entry => entry.Kind == LibraryKind.Music).Take(20).Select(EntryRow)], false),
            (MostPlayedShelf, LibrarySource.Songs, [.. LibraryMemo.MostPlayed(entries).Select(EntryRow)], false),
            ("New in your library", LibrarySource.RecentlyAdded, [.. recent.Where(entry => entry.Kind == LibraryKind.Music).Take(20).Select(EntryRow)], false),
            ("Albums", LibrarySource.Albums, [.. LibraryMemo.AlbumsByAdded(entries).Take(20).Select(GroupRow)], false),
            ("Movies", LibrarySource.Movies, [.. LibraryViews.Movies(recent, MovieLookupOf).Take(20).Select(EntryRow)], false),
            ("TV Shows", LibrarySource.TvShows, [.. LibraryMemo.TvShows(entries).Take(20).Select(GroupRow)], false),
            ("Other videos", LibrarySource.OtherVideos, [.. LibraryViews.RecentlyAdded(LibraryViews.OtherVideos(entries, MovieLookupOf)).Take(20).Select(EntryRow)], true),
            ("Videos", LibrarySource.Videos, [.. recent.Where(entry => entry.Kind == LibraryKind.Video).Take(20).Select(EntryRow)], true),
            ("Pictures", LibrarySource.Pictures, [.. recent.Where(entry => entry.Kind == LibraryKind.Picture).Take(24).Select(EntryRow)], false),
        ];
        // Background scanning or local tag edits can change an existing
        // album, file title or credit without changing its path identity.
        var shown = string.Join("\n", shelves.Select(shelf => shelf.Title + ":" + string.Join(",", shelf.Rows.Select(row =>
            RowIdentity(row) + "|" + row.Name + "|" + row.Detail + "|" + row.PicturePlaceholder))));
        if (shown == _shownLibrary)
        {
            return;
        }

        CancelLibraryPictures();
        _shownLibrary = shown;
        // Preserve the current shelf scroll position when the route has not changed.
        var place = sameRoute ? CapturePlace() : null;
        PlaceHomeShelves(shown, shelves);

        if (place is not null)
        {
            ApplyPlaceAfterLayout(place);
        }
    }

    /// <summary>One shelf: its title and a way to see all of it, over a row of cards that scrolls sideways.</summary>
    private StackPanel Shelf(string title, LibrarySource see, IReadOnlyList<LibraryRow> rows, bool wide, bool seeAll = true, string? id = null)
    {
        id ??= see.ToString();
        var shelf = new StackPanel { Spacing = 6 };
        var heading = new Grid();
        heading.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] });
        if (seeAll)
        {
            var all = new HyperlinkButton { Content = "See all", HorizontalAlignment = HorizontalAlignment.Right };
            AutomationProperties.SetAutomationId(all, "LibraryShelf-" + id);
            all.Click += (_, _) =>
            {
                // The most played and downloading shelves open their view in their own order and
                // filter, so the view starts with the shelf.
                _shelfChoice = title == MostPlayedShelf ? (see, HomeShelves.SeeAllMostPlayed(ViewChoice(see.ToString())))
                    : title == DownloadingShelf ? (see, HomeShelves.SeeAllDownloading(HomeShelves.Downloading(_library!.Entries, 20), ViewChoice).Choice)
                    : null;
                LibrarySources.SelectedIndex = Array.FindIndex(LibrarySourceItems, pair => pair.Source == see);
            };
            heading.Children.Add(all);
        }

        shelf.Children.Add(heading);

        var picture = rows.FirstOrDefault()?.Item is LibraryEntry { Kind: LibraryKind.Picture };
        var posters = see is LibrarySource.Movies or LibrarySource.TvShows;
        var (width, height, template) = wide ? (240.0, 200.0, "LibraryVideoTemplate") : picture ? (190.0, 150.0, "LibraryPhotoTemplate") : posters ? (178.0, 298.0, "LibraryMovieTemplate") : (170.0, 236.0, "LibraryBrowseTemplate");
        var cards = new ListView
        {
            ItemsSource = rows,
            ItemTemplate = LibraryTemplate(template),
            ItemsPanel = (ItemsPanelTemplate)XamlReader.Load("""<ItemsPanelTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><ItemsStackPanel Orientation="Horizontal" /></ItemsPanelTemplate>"""),
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = _settings.MediaOpenClickMode == MediaOpenClick.SingleClick,
            Height = height + 16,
            ItemContainerStyle = CardStyle(width, height),
        };
        ScrollViewer.SetHorizontalScrollMode(cards, ScrollMode.Enabled);
        ScrollViewer.SetHorizontalScrollBarVisibility(cards, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollMode(cards, ScrollMode.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(cards, ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(cards, title);
        AutomationProperties.SetAutomationId(cards, "LibraryShelfCards-" + id);
        cards.ContainerContentChanging += OnLibraryRowShown;
        if (_settings.MediaOpenClickMode == MediaOpenClick.SingleClick)
        {
            cards.ItemClick += (_, e) => OpenFromShelf(see, rows, (LibraryRow)e.ClickedItem);
        }
        else
        {
            cards.DoubleTapped += (_, e) =>
            {
                if (TappedRow(e.OriginalSource) is { } row)
                {
                    OpenFromShelf(see, rows, row);
                }
            };
        }

        shelf.Children.Add(cards);
        return shelf;
    }

    private static Style CardStyle(double width, double height)
    {
        var style = new Style(typeof(ListViewItem));
        style.Setters.Add(new Setter(FrameworkElement.WidthProperty, width));
        style.Setters.Add(new Setter(FrameworkElement.HeightProperty, height));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 4, 0)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Stretch));
        return style;
    }

    /// <summary>
    /// A card on a shelf: an album opens, a song or video plays with the rest of its shelf after it;
    /// on a details page, a movie opens its own page.
    /// </summary>
    private void OpenFromShelf(LibrarySource see, IReadOnlyList<LibraryRow> rows, LibraryRow row)
    {
        switch (row.Item)
        {
            case LibraryEntry movie when movie.Kind == LibraryKind.Video && IsMovie(movie):
                OpenMovieDetails(movie);
                break;
            case LibraryGroup book when see == LibrarySource.Audiobooks:
                // Home's "Continue listening" goes on with the book.
                PlayBook(book, null);
                break;
            case LibraryGroup group:
                KeepLibraryParent();
                _places.RememberSearch(see.ToString(), "");
                _changingLibraryRoute = true;
                _librarySource = see;
                _librarySeason = null;
                _libraryGroup = group;
                LibrarySources.SelectedIndex = Array.FindIndex(LibrarySourceItems, pair => pair.Source == see);
                _changingLibraryRoute = false;
                _libraryGroup = group;
                ShowLibraryAtPlace();
                break;
            case LibraryEntry entry when entry.Kind == LibraryKind.Music && ShelfOf(entry) == AudioShelf.Podcasts:
                // Continue listening and New episodes can mix several local shows in one shelf.
                // Choosing an episode must not queue every other show's entries beside it.
                PlayPodcastEpisode(entry);
                break;
            case LibraryEntry entry:
                var entries = EntriesOf(rows);
                Play(entries, entries.FindIndex(item => string.Equals(item.Path, entry.Path, StringComparison.OrdinalIgnoreCase)), see is LibrarySource.ContinueWatching or LibrarySource.Audiobooks);
                break;
        }
    }

    /// <summary>What tells one row from another when a view is rebuilt; a download's progress is not in it, since the row shows that as it changes.</summary>
    private static string RowIdentity(LibraryRow row) => $"{PictureEntry(row.Item)?.Path}|{row.Name}|{row.PlainDetail}|{row.Extra}|{row.Progress:R}|{row.Summary}";

    /// <summary>
    /// A library item template by key: the window's own list templates, else the show page's episode
    /// lines or the card layouts, each dictionary created the first time one of its templates is needed.
    /// </summary>
    private DataTemplate LibraryTemplate(string key)
    {
        if (Root.Resources.TryGetValue(key, out var own))
        {
            return (DataTemplate)own;
        }

        if (key.StartsWith("Music", StringComparison.Ordinal))
        {
            return MusicTemplate(key);
        }

        ResourceDictionary templates = key.StartsWith("LibraryEpisode", StringComparison.Ordinal)
            ? _episodeTemplates ??= new LibraryEpisodeTemplates()
            : _cardTemplates ??= new LibraryCardTemplates(this);
        return (DataTemplate)templates[key];
    }

    private LibraryCardTemplates? _cardTemplates;

}
