using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;
using Rex.Media.AppCore.Library;
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
    private static readonly (LibrarySource Source, LibrarySourceItem Item)[] LibrarySourceItems =
    [
        (LibrarySource.Home, new("\uE80F", "Home")),
        (LibrarySource.Songs, new("\uE8D6", "Songs")),
        (LibrarySource.Albums, new("\uE93C", "Albums")),
        (LibrarySource.Artists, new("\uE77B", "Artists")),
        (LibrarySource.Genres, new("\uE8EC", "Genres")),
        (LibrarySource.Movies, new("\uE714", "Movies")),
        (LibrarySource.TvShows, new("\uE80F", "TV Shows")),
        (LibrarySource.Videos, new("\uE8FD", "All videos")),
        (LibrarySource.Pictures, new("\uE91B", "Pictures")),
        (LibrarySource.ContinueWatching, new("\uE768", "Continue watching")),
        (LibrarySource.RecentlyPlayed, new("\uE81C", "Recently played")),
        (LibrarySource.RecentlyAdded, new("\uE710", "Recently added")),
        (LibrarySource.Playlists, new("\uE8FD", "Playlists")),
        (LibrarySource.Folders, new("\uE8B7", "Folders")),
    ];

    private static readonly (LibrarySort Sort, string Name)[] SortNames =
    [
        (LibrarySort.Natural, "Natural order"), (LibrarySort.Title, "Title"), (LibrarySort.Artist, "Artist"), (LibrarySort.Album, "Album"),
        (LibrarySort.Year, "Year"), (LibrarySort.Added, "Date added"), (LibrarySort.Played, "Last played"), (LibrarySort.MostPlayed, "Most played"),
        (LibrarySort.Length, "Length"),
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
    private bool _settingCardSize;

    private void WireLibraryView()
    {
        LibraryCardSize.Value = 180;
    }

    /// <summary>The key a view's choices are kept under: its name, or the kind of group opened in it, or the search.</summary>
    private string ViewKey(bool searching) => searching ? "Search" : _librarySeason is not null ? "TvShows season" : _libraryGroup is not null ? _librarySource + " group" : _librarySource.ToString();

    /// <summary>How the view is shown: as the user last set it, else as suits what it holds.</summary>
    private LibraryViewChoice ViewChoice(string key)
    {
        if (_settings.LibraryViews.TryGetValue(key, out var kept))
        {
            return kept;
        }

        return key switch
        {
            "Songs" or "RecentlyPlayed" or "RecentlyAdded" or "Search" or "Albums group" or "Artists group" or "Genres group" => new LibraryViewChoice(LibraryLook.List),
            "Pictures" => new LibraryViewChoice(LibraryLook.Grid, CardSize: 200, Grouping: LibraryGrouping.Folder),
            "Movies" or "TvShows" => new LibraryViewChoice(LibraryLook.Grid, CardSize: 200),
            "Videos" or "ContinueWatching" or "TvShows group" or "TvShows season" => new LibraryViewChoice(LibraryLook.Grid, CardSize: 180),
            _ => new LibraryViewChoice(LibraryLook.Grid),
        };
    }

    private void KeepViewChoice(LibraryViewChoice choice)
    {
        _settings = _settings with { LibraryViews = new Dictionary<string, LibraryViewChoice>(_settings.LibraryViews, StringComparer.Ordinal) { [_libraryViewKey] = choice.Normalize() } };
        RememberLater();
        _shownLibrary = "";
        ShowLibrary();
    }

    /// <summary>Brings the open view in line with the library; the list is replaced only when what it shows has changed.</summary>
    private void ShowLibrary()
    {
        if (_library is null || !LibraryOpen)
        {
            return;
        }

        var entries = _library.Entries;
        var search = LibrarySearch.Text.Trim();
        var source = search.Length > 0 ? (LibrarySource?)null : _librarySource;
        _libraryViewKey = ViewKey(search.Length > 0);
        var choice = ViewChoice(_libraryViewKey);
        var home = source == LibrarySource.Home;
        LibraryHome.Visibility = home ? Visibility.Visible : Visibility.Collapsed;
        LibraryList.Visibility = home ? Visibility.Collapsed : Visibility.Visible;
        if (home)
        {
            ShowHome(entries);
            return;
        }

        string kicker, title, subtitle;
        List<LibraryRowGroup> sections;
        var kind = LibraryKind.Music;
        var options = true;
        var groupsView = false;
        if (search.Length > 0)
        {
            (kicker, title) = ("SEARCH", search);
            var found = LibraryViews.Search(entries, search);
            sections = EntrySections(found, choice);
            subtitle = Count(found.Count, "result");
        }
        else if (_libraryGroup is { } opened)
        {
            // The group again, as the library has it now.
            var fresh = Groups(_librarySource, entries).FirstOrDefault(g => g.Name == opened.Name && g.Detail == opened.Detail);
            _libraryGroup = fresh;
            kicker = _librarySource switch { LibrarySource.Albums => "ALBUM", LibrarySource.Artists => "ARTIST", LibrarySource.TvShows when _librarySeason is not null => "SEASON", LibrarySource.TvShows => "TV SHOW", _ => "GENRE" };
            title = fresh?.Name ?? opened.Name;
            var members = fresh?.Entries ?? [];
            if (_librarySource == LibrarySource.TvShows)
            {
                kind = LibraryKind.Video;
                if (_librarySeason is { } openedSeason)
                {
                    var season = LibraryViews.TvSeasons(fresh ?? opened).FirstOrDefault(item => item.Name == openedSeason.Name);
                    _librarySeason = season;
                    title = season is null ? title : title + " · " + season.Name;
                    subtitle = season?.Detail ?? "No episodes";
                    sections = EntrySections(season?.Entries ?? [], choice);
                }
                else
                {
                    var seasons = LibraryViews.TvSeasons(fresh ?? opened);
                    sections = [new LibraryRowGroup("", seasons.Select(GroupRow))];
                    subtitle = fresh?.Detail ?? "No seasons";
                    groupsView = true;
                }
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
                LibrarySource.Songs => LibraryViews.Songs(entries),
                LibrarySource.Movies => LibraryViews.Movies(entries),
                LibrarySource.Videos => LibraryViews.Videos(entries),
                LibrarySource.Pictures => LibraryViews.Pictures(entries),
                LibrarySource.ContinueWatching => LibraryViews.ContinueWatching(entries, _player.LeftAt),
                LibrarySource.RecentlyPlayed => LibraryViews.RecentlyPlayed(entries),
                LibrarySource.RecentlyAdded => LibraryViews.RecentlyAdded(entries),
                _ => null,
            };
            if (list is not null)
            {
                sections = EntrySections(list, choice);
                kind = _librarySource switch { LibrarySource.Movies or LibrarySource.Videos or LibrarySource.ContinueWatching => LibraryKind.Video, LibrarySource.Pictures => LibraryKind.Picture, _ => LibraryKind.Music };
                subtitle = Count(list.Count, _librarySource switch { LibrarySource.Pictures => "picture", LibrarySource.Movies => "movie", LibrarySource.Videos or LibrarySource.ContinueWatching => "video", _ => "item" })
                    + (kind == LibraryKind.Picture ? "" : " \u00B7 " + Total(list));
            }
            else if (_librarySource is LibrarySource.Albums or LibrarySource.Artists or LibrarySource.Genres or LibrarySource.TvShows)
            {
                groupsView = true;
                var groups = LibraryArrangement.Sort(Groups(_librarySource, entries), choice.Sort, choice.Descending);
                sections = choice.Grouping == LibraryGrouping.Letter
                    ? [.. groups.GroupBy(group => FirstLetter(group.Name)).OrderBy(group => group.Key == "#").ThenBy(group => group.Key, StringComparer.CurrentCulture).Select(group => new LibraryRowGroup(group.Key, group.Select(GroupRow)))]
                    : [new LibraryRowGroup("", groups.Select(GroupRow))];
                kind = _librarySource == LibrarySource.TvShows ? LibraryKind.Video : LibraryKind.Music;
                subtitle = Count(groups.Count, _librarySource switch { LibrarySource.Albums => "album", LibrarySource.Artists => "artist", LibrarySource.TvShows => "show", _ => "genre" });
            }
            else
            {
                options = false;
                var rows = _librarySource == LibrarySource.Playlists
                    ? _player.NamedPlaylists.Select(p => new LibraryRow(p.Name, null, Count(p.Items.Count, "item"), p)).ToList()
                    : [.. _library.Folders.Select(folder => new LibraryRow(folder, Count(entries.Count(entry => MediaLibrary.Holds(folder, entry.Path)), "file"), "", folder))];
                sections = [new LibraryRowGroup("", rows)];
                subtitle = _librarySource == LibrarySource.Playlists ? Count(rows.Count, "playlist") : Count(rows.Count, "folder") + " watched";
            }
        }

        _libraryRows = [.. sections.SelectMany(section => section)];
        var glyph = LibrarySourceItems.First(pair => pair.Source == _librarySource).Item.Glyph;
        ShowHero(kicker, title, subtitle, search.Length > 0 ? "\uE721" : glyph, _libraryRows.Select(row => PictureEntry(row.Item)).FirstOrDefault(entry => entry is not null));
        ShowViewOptions(options, choice, groupsView, kind);
        LibraryBack.Visibility = _libraryGroup is not null && search.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var folders = source == LibrarySource.Folders;
        LibraryFolderActions.Visibility = folders ? Visibility.Visible : Visibility.Collapsed;
        LibraryPlay.Visibility = LibraryShuffle.Visibility = LibraryEnqueue.Visibility = folders || source == LibrarySource.Playlists || (source == LibrarySource.TvShows && _libraryGroup is null) ? Visibility.Collapsed : Visibility.Visible;
        ShowLibraryEmpty(_libraryRows.Count == 0, search.Length > 0, source);

        var look = SetLibraryLook(options ? choice : new LibraryViewChoice(LibraryLook.List), _libraryRows.FirstOrDefault()?.Item, options);
        var grouped = sections.Count > 1 || (sections.Count == 1 && sections[0].Header.Length > 0);
        var shown = look + "\n" + title + "\n" + grouped + "\n" + string.Join("\n", sections.Select(section => section.Header + ":" + string.Join("\n", section.Select(row => row.Name + "|" + row.Detail + "|" + row.Extra))));
        if (shown == _shownLibrary)
        {
            return;
        }

        CancelLibraryPictures();
        _shownLibrary = shown;
        LibraryList.GroupStyle.Clear();
        if (grouped)
        {
            LibraryList.GroupStyle.Add(new GroupStyle { HeaderTemplate = (DataTemplate)Root.Resources["LibraryGroupHeaderTemplate"], HidesIfEmpty = true });
            LibraryList.ItemsSource = new CollectionViewSource { IsSourceGrouped = true, Source = sections }.View;
        }
        else
        {
            LibraryList.ItemsSource = _libraryRows;
        }
    }

    /// <summary>A list of entries, ordered and grouped as the view's choices say.</summary>
    private List<LibraryRowGroup> EntrySections(IReadOnlyList<LibraryEntry> entries, LibraryViewChoice choice) =>
        [.. LibraryArrangement.Group(LibraryArrangement.Sort(entries, choice.Sort, choice.Descending), choice.Grouping, DateTime.UtcNow)
            .Select(section => new LibraryRowGroup(section.Header, section.Entries.Select(EntryRow)))];

    private static string FirstLetter(string name) => name.TrimStart().FirstOrDefault() is var first && char.IsLetter(first) ? char.ToUpper(first, CultureInfo.CurrentCulture).ToString() : "#";

    /// <summary>How long it all lasts together: "48 min", "3 h 12 min".</summary>
    private static string Total(IEnumerable<LibraryEntry> entries)
    {
        var total = TimeSpan.FromTicks(entries.Sum(entry => entry.Duration?.Ticks ?? 0));
        return total.TotalHours >= 1 ? $"{(int)total.TotalHours} h {total.Minutes} min" : $"{Math.Max(total.Minutes, total > TimeSpan.Zero ? 1 : 0)} min";
    }

    /// <summary>The banner: kicker, title and what the view holds, with the first item's picture in front and filling the banner behind.</summary>
    private void ShowHero(string kicker, string title, string subtitle, string glyph, LibraryEntry? art)
    {
        LibraryKicker.Text = kicker;
        LibraryTitle.Text = title;
        LibrarySubtitle.Text = subtitle;
        LibraryHeroGlyph.Glyph = glyph;
        var key = title + "\n" + art?.Path;
        if (key == _shownHero)
        {
            return;
        }

        _shownHero = key;
        LibraryHeroArt.Source = null;
        LibraryHeroBackdrop.Source = null;
        if (art is not null)
        {
            _ = ShowPictureAsync(art.Path, art.Kind, art.Duration, picture =>
            {
                if (_shownHero == key)
                {
                    (LibraryHeroArt.Source, LibraryHeroBackdrop.Source) = (picture, picture);
                }
            }, CancellationToken.None);
        }
    }

    /// <summary>The layout, order and grouping menus, each ticking the choice in use, and the card size.</summary>
    private void ShowViewOptions(bool shown, LibraryViewChoice choice, bool groupsView, LibraryKind kind)
    {
        foreach (var control in new FrameworkElement[] { LibraryLookButton, LibrarySortButton, LibraryGroupButton, LibrarySizePanel })
        {
            control.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        }

        if (!shown)
        {
            return;
        }

        LibraryLookButton.Content = choice.Look switch { LibraryLook.List => "List", LibraryLook.Wall => "Big cards", _ => "Cards" };
        LibraryLookButton.Flyout = Choices("LibraryLook", [(LibraryLook.List, "List"), (LibraryLook.Grid, "Cards"), (LibraryLook.Wall, "Big cards")], choice.Look, look => KeepViewChoice(choice with { Look = look }));

        // Each view offers the orders and groups that mean something for what it holds.
        var sorts = groupsView
            ? new[] { LibrarySort.Natural, LibrarySort.Title, LibrarySort.Artist, LibrarySort.Year, LibrarySort.Added, LibrarySort.Played, LibrarySort.MostPlayed, LibrarySort.Length }
            : kind switch
            {
                LibraryKind.Picture => [LibrarySort.Natural, LibrarySort.Title, LibrarySort.Added],
                LibraryKind.Video => [LibrarySort.Natural, LibrarySort.Title, LibrarySort.Added, LibrarySort.Played, LibrarySort.MostPlayed, LibrarySort.Length],
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
        LibraryGroupButton.Flyout = Choices("LibraryGroup", [.. groupings.Select(grouping => (grouping, GroupingNames.First(pair => pair.Grouping == grouping).Name))], choice.Grouping, grouping => KeepViewChoice(choice with { Grouping = grouping }));
        LibrarySizePanel.Visibility = choice.Look == LibraryLook.List ? Visibility.Collapsed : Visibility.Visible;
        _settingCardSize = true;
        LibraryCardSize.Value = choice.CardSize;
        _settingCardSize = false;
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
        var kind = sample switch { LibraryEntry entry => entry.Kind, LibraryGroup => _librarySource == LibrarySource.TvShows ? LibraryKind.Video : LibraryKind.Music, _ => (LibraryKind?)null };
        var size = choice.Look == LibraryLook.Wall ? Math.Min(LibraryViewChoice.LargestCard * 1.4, choice.CardSize * 1.6) : choice.CardSize;
        var (template, width, height, name) = !media || kind is null ? ("LibraryGenericTemplate", 0.0, 0.0, "Library items, list")
            : choice.Look == LibraryLook.List ? ("LibrarySongTemplate", 0.0, 0.0, "Compact list")
            : kind == LibraryKind.Video && (_librarySource == LibrarySource.Movies || (_librarySource == LibrarySource.TvShows && _libraryGroup is null)) ? ("LibraryMovieTemplate", size + 12, (size * 1.5) + 68, "Movies and TV shows, poster grid")
            : kind == LibraryKind.Video ? ("LibraryVideoTemplate", (size * 1.45) + 12, (size * 1.45 * 9 / 16) + 64, "Episodes, thumbnail grid")
            : kind == LibraryKind.Picture ? ("LibraryPhotoTemplate", size + 8, (size * 0.75) + 8, "Pictures, gallery grid")
            : ("LibraryBrowseTemplate", size + 12, size + 78, "Cover grid");
        var look = $"{template}|{width:0}|{height:0}";
        if (look == _shownLook)
        {
            return look;
        }

        _shownLook = look;
        LibraryList.ItemTemplate = (DataTemplate)Root.Resources[template];
        LibraryList.ItemsPanel = width == 0
            ? (ItemsPanelTemplate)Root.Resources["LibraryListPanel"]
            : (ItemsPanelTemplate)XamlReader.Load(string.Create(CultureInfo.InvariantCulture,
                $"""<ItemsPanelTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><ItemsWrapGrid Orientation="Horizontal" ItemWidth="{width:0}" ItemHeight="{height:0}" /></ItemsPanelTemplate>"""));
        AutomationProperties.SetName(LibraryList, name);
        return look;
    }

    /// <summary>Home: shelves of what to play next, each with a way to see all of it.</summary>
    private void ShowHome(IReadOnlyList<LibraryEntry> entries)
    {
        var music = entries.Count(entry => entry.Kind == LibraryKind.Music);
        var videos = entries.Count(entry => entry.Kind == LibraryKind.Video);
        var pictures = entries.Count(entry => entry.Kind == LibraryKind.Picture);
        var recent = LibraryViews.RecentlyAdded(entries);
        ShowHero("YOUR LIBRARY", "Home", string.Join(" \u00B7 ", new[] { Count(music, "song"), Count(videos, "video"), Count(pictures, "picture") }), "\uE80F",
            recent.FirstOrDefault(entry => entry.Kind != LibraryKind.Picture) ?? recent.FirstOrDefault());
        ShowViewOptions(false, ViewChoice("Home"), false, LibraryKind.Music);
        LibraryBack.Visibility = Visibility.Collapsed;
        LibraryFolderActions.Visibility = Visibility.Collapsed;
        LibraryPlay.Visibility = LibraryShuffle.Visibility = LibraryEnqueue.Visibility = Visibility.Collapsed;
        _libraryRows = [];
        ShowLibraryEmpty(entries.Count == 0, false, LibrarySource.Home);

        var shelves = new (string Title, LibrarySource See, IReadOnlyList<LibraryRow> Rows, bool Wide)[]
        {
            ("Jump back in", LibrarySource.ContinueWatching, [.. LibraryViews.ContinueWatching(entries, _player.LeftAt).Take(20).Select(EntryRow)], true),
            ("Recently played", LibrarySource.RecentlyPlayed, [.. LibraryViews.RecentlyPlayed(entries).Where(entry => entry.Kind == LibraryKind.Music).Take(20).Select(EntryRow)], false),
            ("Your most played", LibrarySource.Songs, [.. entries.Where(entry => entry.Kind == LibraryKind.Music && entry.Plays > 0).OrderByDescending(entry => entry.Plays).Take(20).Select(EntryRow)], false),
            ("New in your library", LibrarySource.RecentlyAdded, [.. recent.Where(entry => entry.Kind == LibraryKind.Music).Take(20).Select(EntryRow)], false),
            ("Albums", LibrarySource.Albums, [.. LibraryArrangement.Sort(LibraryViews.Albums(entries), LibrarySort.Added, false).Take(20).Select(GroupRow)], false),
            ("Movies", LibrarySource.Movies, [.. LibraryViews.Movies(recent).Take(20).Select(EntryRow)], false),
            ("TV Shows", LibrarySource.TvShows, [.. LibraryViews.TvShows(entries).Take(20).Select(GroupRow)], false),
            ("Videos", LibrarySource.Videos, [.. recent.Where(entry => entry.Kind == LibraryKind.Video).Take(20).Select(EntryRow)], true),
            ("Pictures", LibrarySource.Pictures, [.. recent.Where(entry => entry.Kind == LibraryKind.Picture).Take(24).Select(EntryRow)], false),
        };
        _homeShelfRows = [.. shelves.SelectMany(shelf => shelf.Rows)];
        var shown = string.Join("\n", shelves.Select(shelf => shelf.Title + ":" + string.Join(",", shelf.Rows.Select(row => row.Name + row.Extra))));
        if (shown == _shownLibrary)
        {
            return;
        }

        CancelLibraryPictures();
        _shownLibrary = shown;
        LibraryShelves.Children.Clear();
        foreach (var (shelfTitle, see, rows, wide) in shelves.Where(shelf => shelf.Rows.Count > 0))
        {
            LibraryShelves.Children.Add(Shelf(shelfTitle, see, rows, wide));
        }
    }

    /// <summary>One shelf: its title and a way to see all of it, over a row of cards that scrolls sideways.</summary>
    private StackPanel Shelf(string title, LibrarySource see, IReadOnlyList<LibraryRow> rows, bool wide)
    {
        var shelf = new StackPanel { Spacing = 6 };
        var heading = new Grid();
        heading.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] });
        var all = new HyperlinkButton { Content = "See all", HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetAutomationId(all, "LibraryShelf-" + see);
        all.Click += (_, _) => LibrarySources.SelectedIndex = Array.FindIndex(LibrarySourceItems, pair => pair.Source == see);
        heading.Children.Add(all);
        shelf.Children.Add(heading);

        var picture = rows.FirstOrDefault()?.Item is LibraryEntry { Kind: LibraryKind.Picture };
        var posters = see is LibrarySource.Movies or LibrarySource.TvShows;
        var (width, height, template) = wide ? (240.0, 200.0, "LibraryVideoTemplate") : picture ? (190.0, 150.0, "LibraryPhotoTemplate") : posters ? (178.0, 298.0, "LibraryMovieTemplate") : (170.0, 236.0, "LibraryBrowseTemplate");
        var cards = new ListView
        {
            ItemsSource = rows,
            ItemTemplate = (DataTemplate)Root.Resources[template],
            ItemsPanel = (ItemsPanelTemplate)XamlReader.Load("""<ItemsPanelTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><ItemsStackPanel Orientation="Horizontal" /></ItemsPanelTemplate>"""),
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = true,
            Height = height + 16,
            ItemContainerStyle = CardStyle(width, height),
        };
        ScrollViewer.SetHorizontalScrollMode(cards, ScrollMode.Enabled);
        ScrollViewer.SetHorizontalScrollBarVisibility(cards, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollMode(cards, ScrollMode.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(cards, ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(cards, title);
        AutomationProperties.SetAutomationId(cards, "LibraryShelfCards-" + see);
        cards.ContainerContentChanging += OnLibraryRowShown;
        cards.ItemClick += (_, e) => OpenFromShelf(see, rows, (LibraryRow)e.ClickedItem);
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

    /// <summary>A card on a shelf: an album opens, a song or video plays with the rest of its shelf after it.</summary>
    private void OpenFromShelf(LibrarySource see, IReadOnlyList<LibraryRow> rows, LibraryRow row)
    {
        switch (row.Item)
        {
            case LibraryGroup group:
                _librarySource = see;
                _librarySeason = null;
                _libraryGroup = group;
                LibrarySources.SelectedIndex = Array.FindIndex(LibrarySourceItems, pair => pair.Source == see);
                _libraryGroup = group;
                ShowLibrary();
                break;
            case LibraryEntry entry:
                var entries = EntriesOf(rows);
                Play(entries, entries.FindIndex(item => string.Equals(item.Path, entry.Path, StringComparison.OrdinalIgnoreCase)));
                break;
        }
    }

    /// <summary>Plays the chosen items, or the whole view, in shuffled order.</summary>
    private void OnLibraryShuffle(object sender, RoutedEventArgs e)
    {
        var entries = EntriesOf(ChosenRows());
        if (entries.Count == 0)
        {
            Say("There is nothing here to play yet.");
            return;
        }

        _player.Playlist.Shuffle = true;
        Play(entries, Random.Shared.Next(entries.Count));
        Say("Shuffle on");
    }

    /// <summary>A card shows its play mark while pointed at.</summary>
    private void OnCardPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        SetCardPlay(sender, 1);
        if (sender is FrameworkElement { DataContext: LibraryRow row })
        {
            PrioritizeLibraryPicture(row);
        }
    }

    private void OnCardGettingFocus(object sender, Microsoft.UI.Xaml.Input.GettingFocusEventArgs e)
    {
        SetCardPlay(sender, 1);
        if (sender is FrameworkElement { DataContext: LibraryRow row })
        {
            PrioritizeLibraryPicture(row);
        }
    }

    private void OnCardPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => SetCardPlay(sender, 0);

    private static void SetCardPlay(object card, double opacity)
    {
        if (card is FrameworkElement element && element.FindName("CardPlay") is UIElement play)
        {
            play.OpacityTransition ??= new ScalarTransition { Duration = TimeSpan.FromMilliseconds(120) };
            play.Opacity = opacity;
        }
    }
}
