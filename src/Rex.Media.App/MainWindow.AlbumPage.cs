using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;

namespace Rex.Media.App;

/// <summary>
/// An album's page and the Songs table, the streaming-style way (LIB-05). The album keeps the
/// library's banner and virtualised list, made its own: the cover over a plate of the cover's own
/// colour, each artist a link, the release's kind, year, songs and length, Play, Shuffle, Add and a
/// "..." menu, then numbered tracks (the playing one marked, featured artists under the title, a
/// subtle bar of plays), disc headings for a set, and under the list the release facts a lookup
/// found and more by the same artist. <see cref="AlbumPage"/> decides; this file draws.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Whether the open view is the Songs table, whose lines carry its columns.</summary>
    private bool _songsTable;

    private string _shownMusicPage = "";
    private long _albumTintRequest;
    private StackPanel? _heroArtists;
    private Button? _heroMore;

    /// <summary>An album's page title line and its tracks, under disc headings for a set of several.</summary>
    private (string Subtitle, List<LibraryRowGroup> Sections) AlbumPageParts(LibraryGroup album)
    {
        var facts = MetadataAlbum(album.Detail, album.Entries.FirstOrDefault()?.Album ?? album.Name);
        var names = ArtistNames.Learn(album.Entries);
        var artists = AlbumPage.Artists(album, names);
        var subtitle = AlbumPage.Line(album, AlbumPage.Kind(album, facts), AlbumPage.Year(album, facts));
        return (subtitle, [.. AlbumPage.Discs(album).Select(disc => new LibraryRowGroup(disc.Number is { } number ? AlbumPage.DiscName(number) : "",
            disc.Songs.Select(song => TrackRow(song, album, artists, names))))]);
    }

    private MusicRow TrackRow(LibraryEntry song, LibraryGroup album, IReadOnlyList<string> artists, ArtistNames names)
    {
        var featured = AlbumPage.Featured(song, artists, names);
        var row = new MusicRow(song.Title, ArtistNames.Display(song.Artist), song.Duration is { } length ? TimeText.Format(length, length) : "", song,
            badge: song.Track?.ToString(CultureInfo.CurrentCulture) ?? "", isPlaying: LibraryViews.SameLocation(song.Path, _player.Playlist.Current?.Location))
        {
            Featured = featured.Count == 0 ? "" : "feat. " + string.Join(", ", featured),
            PlaysText = PlaysText(song.Plays),
            PlayShare = AlbumPage.PlayShare(song, album),
        };
        var (glyph, text) = TranscriptBadgeOf(song);
        row.SetTranscript(glyph, text);
        ShowDownload(row, song);
        return row;
    }

    /// <summary>A line of the Songs table: the song's own line with its artist, album and plays apart.</summary>
    private MusicRow SongRow(LibraryEntry song)
    {
        var plain = EntryRow(song);
        var row = new MusicRow(plain.Name, plain.PlainDetail, plain.Extra, song, badge: plain.Badge, isPlaying: plain.IsPlaying)
        {
            Artist = ArtistNames.Display(song.Artists ?? song.Artist) ?? "",
            ArtistLink = song.Artists?.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? song.Artist,
            Album = song.Album ?? "",
            PlaysText = PlaysText(song.Plays),
            PlaysNumber = song.Plays > 0 ? song.Plays.ToString("N0", CultureInfo.CurrentCulture) : "",
        };
        row.SetTranscript(plain.TranscriptGlyph, plain.TranscriptText);
        ShowDownload(row, song);
        return row;
    }

    private static string PlaysText(int plays) => plays switch { <= 0 => "", 1 => "1 play", _ => plays.ToString("N0", CultureInfo.CurrentCulture) + " plays" };

    /// <summary>
    /// What the album page and the Songs table add to the banner and list: the tint, links and menu,
    /// the facts and "More by" under the tracks, and the table's column headings. Anything else
    /// puts the banner and list back as they were. Redrawn only when what it shows has changed.
    /// </summary>
    private void ShowMusicPageExtras(LibraryGroup? album, bool songsTable)
    {
        var facts = album is null ? null : MetadataAlbum(album.Detail, album.Entries.FirstOrDefault()?.Album ?? album.Name);
        var key = album is null ? (songsTable ? "songs" : "")
            : "album\n" + album.Name + "\n" + album.Detail + "\n" + album.Entries.Count + "\n" + facts?.ToJson() + "\n" + MoreByKey();
        if (key == _shownMusicPage)
        {
            return;
        }

        _shownMusicPage = key;
        LibraryList.Header = songsTable ? SongsTableHeader() : null;
        if (album is null)
        {
            ++_albumTintRequest;
            _heroTint = null;
            ApplyHeroLook();
            LibraryList.Footer = null;
            if (_heroArtists is not null)
            {
                _heroArtists.Visibility = Visibility.Collapsed;
            }

            if (_heroMore is not null)
            {
                _heroMore.Visibility = Visibility.Collapsed;
            }

            return;
        }

        // The page wears its cover's colour as the plate under its words, once that colour is known.
        var request = ++_albumTintRequest;
        _heroTint = null;
        Forget(TintAlbumAsync());
        async Task TintAlbumAsync()
        {
            var tint = await HeaderTintAsync(album.Entries.FirstOrDefault(), null, CancellationToken.None);
            if (request == _albumTintRequest)
            {
                _heroTint = tint;
                ApplyHeroLook();
            }
        }

        var names = ArtistNames.Learn(_library?.Entries.Where(entry => entry.Kind == LibraryKind.Music) ?? []);
        ShowHeroArtists(AlbumPage.Artists(album, names));
        ShowHeroMore(album, AlbumPage.Artists(album, names));
        LibraryList.Footer = AlbumFooter(album, facts, names);
    }

    /// <summary>What "More by" depends on beyond the album: how many albums the library holds.</summary>
    private string MoreByKey() => LibraryMemo.Albums(_library?.Entries ?? []).Count.ToString(CultureInfo.InvariantCulture);

    /// <summary>Each of the album's artists as a link under its title, opening their page.</summary>
    private void ShowHeroArtists(IReadOnlyList<string> artists)
    {
        if (_heroArtists is null)
        {
            _heroArtists = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(-8, 0, 0, 0) };
            AutomationProperties.SetAutomationId(_heroArtists, "LibraryHeroArtists");
            Grid.SetRow(_heroArtists, 2);
            Grid.SetColumnSpan(_heroArtists, 2);
            LibraryHeroWords.Children.Add(_heroArtists);
        }

        _heroArtists.Children.Clear();
        foreach (var name in artists)
        {
            var link = new HyperlinkButton { Content = name, Padding = new Thickness(8, 2, 8, 2), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            AutomationProperties.SetAutomationId(link, "LibraryHeroArtist-" + name);
            link.Click += (_, _) => OpenArtistNamed(name);
            _heroArtists.Children.Add(link);
        }

        _heroArtists.Visibility = artists.Count > 0 && !_libraryHeaderCompact ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The "..." menu beside Play, Shuffle and Add: the album's artists, and its details and cover.</summary>
    private void ShowHeroMore(LibraryGroup album, IReadOnlyList<string> artists)
    {
        if (_heroMore is null)
        {
            _heroMore = new Button { Content = new FontIcon { Glyph = "", FontSize = 14 }, Padding = new Thickness(10, 7, 10, 7) };
            AutomationProperties.SetAutomationId(_heroMore, "LibraryMore");
            AutomationProperties.SetName(_heroMore, "More");
            ToolTipService.SetToolTip(_heroMore, "More");
            var actions = (Panel)LibraryPlay.Parent;
            actions.Children.Insert(actions.Children.IndexOf(LibraryEnqueue) + 1, _heroMore);
        }

        var menu = new MenuFlyout();
        menu.Items.Add(MenuItem(AlbumIsPinned(album) ? "Unpin from Music Home" : "Pin to Music Home",
            "LibraryMorePinAlbum", () => ToggleAlbumPin(album)));
        foreach (var name in artists)
        {
            menu.Items.Add(MenuItem("Go to " + name, "LibraryMoreArtist-" + name, () => OpenArtistNamed(name)));
        }

        if (album.Entries.FirstOrDefault()?.Album is { Length: > 0 } title && _settings.Metadata.Music)
        {
            if (menu.Items.Count > 0)
            {
                menu.Items.Add(new MenuFlyoutSeparator());
            }

            menu.Items.Add(MenuItem("Refresh details", "LibraryMoreRefresh", () => RefreshAlbumMetadata(album.Detail, title)));
            menu.Items.Add(MenuItem("Replace cover...", "LibraryMoreCover", () => Forget(ReplaceAlbumCoverAsync(album.Detail, title))));
        }

        _heroMore.Flyout = menu;
        _heroMore.Visibility = menu.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Opens the artist of that name's page, if the library has them.</summary>
    private void OpenArtistNamed(string name)
    {
        var key = ArtistNames.Key(name);
        if (_library is not null && LibraryMemo.Artists(_library.Entries).FirstOrDefault(artist => ArtistNames.Key(artist.Name) == key) is { } artist)
        {
            OpenMusicGroup(artist, artist: true);
        }
    }

    /// <summary>Under the tracks: the release date, the phonographic line and genres a lookup found (or the songs' own genres), then more by the artist.</summary>
    private StackPanel AlbumFooter(LibraryGroup album, Rex.Media.AppCore.Metadata.EnrichedAlbum? facts, ArtistNames names)
    {
        var footer = new StackPanel { Spacing = 4, Padding = new Thickness(6, 18, 6, 12) };
        AutomationProperties.SetAutomationId(footer, "AlbumFacts");
        AutomationProperties.SetName(footer, "About this album");
        var secondary = RexTheme.Brush("TextFillColorSecondaryBrush");
        foreach (var line in new[] { AlbumPage.Released(facts, CultureInfo.CurrentCulture), AlbumPage.Phonographic(facts) }.OfType<string>())
        {
            footer.Children.Add(new TextBlock { Text = line, Foreground = secondary, FontSize = 12 });
        }

        if (AlbumPage.Genres(album, facts) is { Count: > 0 } genres)
        {
            var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };
            AutomationProperties.SetAutomationId(chips, "AlbumGenres");
            foreach (var genre in genres)
            {
                chips.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(11),
                    Padding = new Thickness(10, 3, 10, 3),
                    Background = RexTheme.Brush("LayerFillColorDefaultBrush"),
                    Child = new TextBlock { Text = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(genre), FontSize = 12 },
                });
            }

            footer.Children.Add(chips);
        }

        var all = LibraryMemo.Albums(_library?.Entries ?? []);
        if (AlbumPage.MoreBy(album, all, names) is { Count: > 0 } more && AlbumPage.Artists(album, names) is [var first, ..])
        {
            var shelf = MusicShelf("More by " + first, "MoreBy", [.. more.Take(MusicHome.RowSize).Select(AlbumCard)]);
            shelf.Margin = new Thickness(0, 22, 0, 0);
            footer.Children.Add(shelf);
        }

        return footer;
    }

    /// <summary>
    /// The Songs table's column headings, in a list line of their own so they get exactly the lines'
    /// padding and columns; screen readers skip it, since each line names its own columns.
    /// </summary>
    private static ListViewItem SongsTableHeader()
    {
        var header = new Grid { ColumnSpacing = 14, Padding = new Thickness(0, 4, 0, 4) };
        AutomationProperties.SetAutomationId(header, "SongsTableHeader");
        foreach (var width in new[] { new GridLength(40), new GridLength(2, GridUnitType.Star), new GridLength(1, GridUnitType.Star), new GridLength(1, GridUnitType.Star), new GridLength(56), new GridLength(72), new GridLength(52) })
        {
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        }

        var secondary = RexTheme.Brush("TextFillColorSecondaryBrush");
        foreach (var (text, column, right) in new[] { ("Title", 1, false), ("Artist", 2, false), ("Album", 3, false), ("Plays", 4, true), ("Length", 6, true) })
        {
            var heading = new TextBlock { Text = text, Foreground = secondary, FontSize = 12, HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetAutomationId(heading, "SongsTableHeading-" + text);
            Grid.SetColumn(heading, column);
            header.Children.Add(heading);
        }

        var line = new ListViewItem { Content = header, IsHitTestVisible = false, IsTabStop = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 0 };
        AutomationProperties.SetAccessibilityView(line, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        return line;
    }

    /// <summary>A Songs line's artist link: the artist's page.</summary>
    internal void OnMusicArtistLink(object sender)
    {
        if (sender is FrameworkElement { DataContext: MusicRow { ArtistLink: { } artist } })
        {
            OpenArtistNamed(artist);
        }
    }

    /// <summary>A Songs line's album link: the album's page.</summary>
    internal void OnMusicAlbumLink(object sender)
    {
        if (sender is FrameworkElement { DataContext: MusicRow { Item: LibraryEntry { Album: { } name } song } } && _library is not null
            && LibraryMemo.Albums(_library.Entries).FirstOrDefault(album => album.Entries.Any(entry => LibraryViews.SameLocation(entry.Path, song.Path))) is { } found)
        {
            OpenMusicGroup(found, artist: false);
        }
    }
}
