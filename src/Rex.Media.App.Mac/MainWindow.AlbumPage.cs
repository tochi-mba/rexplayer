using System.Globalization;
using Rex.Media.AppCore.Design;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;

namespace Rex.Media.App.Mac;

/// <summary>
/// An album's page in the Mac library (LIB-05), laid out as the Windows album page: a banner with
/// the cover, the release's kind as its kicker, the title, each of the album's artists as a link to
/// their page, the year, songs and length, and Play, Shuffle and Add to queue; then its numbered
/// songs under disc headings for a set of several, the songs' genres, and more by the same artist
/// as a row of cards. <see cref="AlbumPage"/> decides; this file draws. The Mac keeps no looked-up
/// release facts yet, so only what the songs say is shown.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The side of a card's artwork on a shelf, as Windows draws its music cards.</summary>
    private const double LibraryCardArtworkSize = 144;

    /// <summary>Opens the album's page over the current view; Back returns to that view.</summary>
    private bool OpenLibraryAlbum(LibraryGroup album)
    {
        if (!LibraryShown || _mediaLibrary is null || _libraryPageHost is null)
        {
            return false;
        }

        ShowLibraryPage(BuildLibraryAlbumPage(album));
        return true;
    }

    /// <summary>Opens the page of the album <paramref name="song"/> is on, if the catalogue has it.</summary>
    private bool OpenLibraryAlbumOf(LibraryEntry song)
    {
        if (_mediaLibrary is null)
        {
            return false;
        }

        var album = LibraryMemo.Albums(_mediaLibrary.Entries).FirstOrDefault(candidate =>
            candidate.Entries.Any(entry => LibraryViews.SameLocation(entry.Path, song.Path)));
        if (album is null)
        {
            Say("This song is no longer in your library. Refresh the library and try again.");
            return false;
        }

        return OpenLibraryAlbum(album);
    }

    /// <summary>
    /// Gives a song's line its ways to the album and artist pages: clicking the artwork opens the
    /// album, and the line's menu (a secondary click, or VoiceOver's Show Menu on its title) offers
    /// the album and the song's first artist.
    /// </summary>
    private void LinkMusicRow(LibraryEntry entry, NSMenu menu, NSView artwork)
    {
        var first = true;
        if (entry.Album is { Length: > 0 } albumName)
        {
            menu.AddItem(NSMenuItem.SeparatorItem);
            first = false;
            menu.AddItem(new NSMenuItem("Go to album: " + albumName, (_, _) => OpenLibraryAlbumOf(entry)));
            artwork.AddGestureRecognizer(new NSClickGestureRecognizer(() => OpenLibraryAlbumOf(entry)));
            artwork.ToolTip = "Open the album " + albumName;
        }

        if (ArtistNames.Learn([entry]).Of(entry) is [var artist, ..])
        {
            if (first)
            {
                menu.AddItem(NSMenuItem.SeparatorItem);
            }

            menu.AddItem(new NSMenuItem("Go to artist: " + artist, (_, _) => OpenLibraryArtistNamed(artist)));
        }
    }

    /// <summary>The album's page: its banner, its songs by disc, then its genres and more by its artist.</summary>
    private LibraryDetailPage BuildLibraryAlbumPage(LibraryGroup album)
    {
        var all = _mediaLibrary?.Entries ?? [];
        var libraryNames = ArtistNames.Learn(all.Where(entry => entry.Kind == LibraryKind.Music));
        var albumNames = ArtistNames.Learn(album.Entries);
        var artists = AlbumPage.Artists(album, libraryNames);
        var kind = AlbumPage.Kind(album, null);
        var line = AlbumPage.Line(album, kind, AlbumPage.Year(album, null));
        var discs = AlbumPage.Discs(album);
        IReadOnlyList<LibraryEntry> songs = [.. discs.SelectMany(disc => disc.Songs)];
        var keyViews = new List<NSView>();
        var views = new List<NSView>();

        var hero = BuildLibraryHero("opticaldisc");
        ((NSView)hero.Art).AccessibilityLabel = "Cover of " + album.Name;
        LoadLibraryAlbumArtwork(hero.Art, songs.FirstOrDefault());
        hero.Kicker.Text = AlbumPage.KindName(kind).ToUpperInvariant();
        hero.Title.StringValue = album.Name;
        hero.Summary.StringValue = line;
        hero.Summary.AccessibilityLabel = "About this album: " + line;
        if (artists.Count > 0)
        {
            var links = new List<NSView>();
            foreach (var name in artists)
            {
                var link = MacLibraryViews.Link(name, "Open the artist " + name, () => OpenLibraryArtistNamed(name), 3);
                keyViews.Add(link);
                links.Add(link);
            }

            hero.Words.InsertArrangedSubview(MacTheme.Stack(false, 2, [.. links]), 2);
        }

        AddLibraryHeroActions(hero, songs, "the album " + album.Name, keyViews);

        if (songs.Count == 0)
        {
            views.Add(MacTheme.Label("None of this album's songs are in your library now.", 2, DesignRole.TextSecondary));
        }

        foreach (var disc in discs)
        {
            if (disc.Number is { } number)
            {
                views.Add(MacLibraryViews.Heading(AlbumPage.DiscName(number)));
            }

            foreach (var song in disc.Songs)
            {
                var featured = AlbumPage.Featured(song, artists, albumNames);
                views.Add(LibrarySongRow(song, songs, song.Track?.ToString(CultureInfo.CurrentCulture) ?? "",
                    featured.Count == 0 ? "" : "feat. " + string.Join(", ", featured),
                    song.Duration is { } length ? TimeText.Format(length, length) : "", keyViews));
            }
        }

        if (AlbumPage.Genres(album, null) is { Count: > 0 } genres)
        {
            var chips = genres.Select(genre => (NSView)LibraryChip(CultureInfo.CurrentCulture.TextInfo.ToTitleCase(genre))).ToArray();
            var row = MacTheme.Stack(false, 0, chips);
            row.Spacing = 6;
            ((NSView)row).AccessibilityLabel = "Genres: " + string.Join(", ", genres);
            views.Add(LibraryLeading(row));
        }

        if (AlbumPage.MoreBy(album, LibraryMemo.Albums(all), libraryNames) is { Count: > 0 } more && artists is [var first, ..])
        {
            views.Add(MacLibraryViews.Heading("More by " + first));
            views.Add(LibraryShelf(more.Take(MusicHome.RowSize).Select(other =>
                LibraryAlbumCard(other, other.Entries.Max(song => song.Year), keyViews))));
        }

        var summary = $"album={album.Name};artist={string.Join(", ", artists)};discs={discs.Count};tracks={songs.Count}";
        return LibraryDetailPageOf("Album: " + album.Name, summary, hero, views, keyViews);
    }

    /// <summary>A banner's Play, Shuffle and Add to queue for <paramref name="songs"/>, named for <paramref name="what"/>.</summary>
    private void AddLibraryHeroActions(LibraryHero hero, IReadOnlyList<LibraryEntry> songs, string what, List<NSView> keyViews)
    {
        var play = MacTheme.TextButton("Play", () =>
        {
            if (songs.Count > 0)
            {
                PlayLibraryEntry(songs[0], songs);
            }
        }, RexButtonLook.Accent, "play.fill", "Play " + what);
        var shuffle = MacTheme.TextButton("Shuffle", () =>
        {
            if (songs.Count > 0)
            {
                _player.Playlist.Shuffle = true;
                PlayLibraryEntry(songs[Random.Shared.Next(songs.Count)], songs);
                Say("Shuffle on");
            }
        }, RexButtonLook.Standard, "shuffle", "Shuffle " + what);
        var queue = MacTheme.TextButton("Add to queue", () => QueueLibraryEntries(songs, what), RexButtonLook.Standard, "plus",
            "Add " + what + " to Up Next");
        queue.ToolTip = "Add to Up Next without replacing what is playing";
        play.Enabled = shuffle.Enabled = queue.Enabled = songs.Count > 0;
        hero.Actions.AddArrangedSubview(play);
        hero.Actions.AddArrangedSubview(shuffle);
        hero.Actions.AddArrangedSubview(queue);
        keyViews.AddRange([play, shuffle, queue]);
    }

    /// <summary>
    /// The cover: a picture kept beside the songs (<see cref="FolderArt"/>) when there is one, read
    /// off the window's thread; until then, and otherwise, the tile's glyph.
    /// </summary>
    private void LoadLibraryAlbumArtwork(LibraryArtView cover, LibraryEntry? first)
    {
        if (first is null)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                return FolderArt.Find(first.Path) is { } picture ? File.ReadAllBytes(picture) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Warning(LogSource, "Album artwork could not be read: " + ex.Message);
                return null;
            }
        }).ContinueWith(read =>
        {
            if (!read.IsCompletedSuccessfully || read.Result is not { Length: > 0 } bytes)
            {
                return;
            }

            OnWindowThread(() =>
            {
                try
                {
                    using var data = NSData.FromArray(bytes);
                    var image = new NSImage(data);
                    if (image.IsValid)
                    {
                        cover.Picture = image;
                    }
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    _log.Warning(LogSource, "Album artwork could not be displayed: " + ex.Message);
                }
            });
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// One song's line on a detail page, as Windows draws an album's track line: its number, its
    /// title (pressing it plays it, with the rest of <paramref name="selection"/> after it) over a
    /// secondary line, its length in even figures, and Add to Up Next.
    /// </summary>
    private LibraryResultRow LibrarySongRow(LibraryEntry song, IReadOnlyList<LibraryEntry> selection, string number, string under, string extra, List<NSView> keyViews)
    {
        void Play() => PlayLibraryEntry(song, selection);
        void Queue() => QueueLibraryEntries([song], song.Title);
        var line = new LibraryResultRow(Play, ChooseLibraryRow);
        var badge = MacTheme.Label(number, 2, DesignRole.TextSecondary);
        badge.Font = MacTheme.DigitFont(2);
        badge.Alignment = NSTextAlignment.Center;
        badge.WidthAnchor.ConstraintEqualTo(32).Active = true;
        var title = new RexTextButton(song.Title, "Play: " + song.Title, Play, DesignRole.TextPrimary, MacTheme.Font(2));
        title.ToolTip = song.Path;
        var words = new List<NSView> { title };
        if (under.Length > 0)
        {
            var detail = MacTheme.Label(under, 1, DesignRole.TextSecondary);
            detail.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
            words.Add(detail);
        }

        var column = MacTheme.Stack(true, 0, [.. words]);
        column.Spacing = 1;
        column.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Horizontal);
        column.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
        var length = LibraryCell(extra, right: true);
        length.WidthAnchor.ConstraintEqualTo(72).Active = true;
        var queue = MacTheme.IconButton("plus", "Add " + song.Title + " to Up Next", Queue);
        queue.ToolTip = "Add to Up Next without replacing what is playing";
        keyViews.Add(title);
        keyViews.Add(queue);
        var row = MacTheme.Stack(false, 3, badge, column, length, queue);
        line.AddSubview(row);
        row.LeadingAnchor.ConstraintEqualTo(line.LeadingAnchor, MacTheme.Space(1)).Active = true;
        row.TrailingAnchor.ConstraintEqualTo(line.TrailingAnchor, -MacTheme.Space(0)).Active = true;
        row.CenterYAnchor.ConstraintEqualTo(line.CenterYAnchor).Active = true;
        line.HeightAnchor.ConstraintEqualTo((nfloat)LibraryRowHeight).Active = true;
        var menu = new NSMenu();
        menu.AddItem(new NSMenuItem("Play", (_, _) => Play()));
        menu.AddItem(new NSMenuItem("Add to Up Next", (_, _) => Queue()));
        line.Menu = menu;
        title.Menu = menu;
        ((NSView)line).AccessibilityLabel = song.Title;
        MarkLibraryPlayingTitle(song.Path, title);
        return line;
    }

    /// <summary>Gives a detail page's title the signal colour when it is the item playing.</summary>
    private void MarkLibraryPlayingTitle(string path, RexTextButton title)
    {
        if (_player.Item?.Location is { } playing && LibraryViews.SameLocation(path, playing))
        {
            title.FixedColor = MacTheme.Signal;
        }
    }

    /// <summary>
    /// An album as a card on a shelf, as Windows draws its music cards: the artwork, its name
    /// (which opens its page) and its year and songs under it.
    /// </summary>
    private NSView LibraryAlbumCard(LibraryGroup album, int? year, List<NSView> keyViews)
    {
        var art = new LibraryArtView(LibraryCardArtworkSize, MacTheme.Radius(2), "opticaldisc");
        ((NSView)art).AccessibilityLabel = "Artwork for " + album.Name;
        LoadLibraryAlbumArtwork(art, album.Entries.FirstOrDefault());
        void Open() => OpenLibraryAlbum(FullLibraryAlbum(album));
        art.AddGestureRecognizer(new NSClickGestureRecognizer(Open));
        var name = new RexTextButton(album.Name, "Open the album " + album.Name, Open, DesignRole.TextPrimary, MacTheme.Font(2, semibold: true));
        keyViews.Add(name);
        var detail = MacTheme.Label(ViewFacts.Join(
            year?.ToString(CultureInfo.CurrentCulture),
            album.Entries.Count == 1 ? "1 song" : album.Entries.Count.ToString(CultureInfo.CurrentCulture) + " songs"), 1, DesignRole.TextSecondary);
        return LibraryCard(art, name, detail);
    }

    /// <summary>A card's column: the artwork over its name and, if any, the line under it, as wide as the artwork.</summary>
    private static NSView LibraryCard(LibraryArtView art, NSView name, NSView? detail)
    {
        var card = MacTheme.Stack(true, 1, detail is null ? [art, name] : [art, name, detail]);
        card.SetCustomSpacing(2, name);
        card.WidthAnchor.ConstraintEqualTo((nfloat)LibraryCardArtworkSize).Active = true;
        name.WidthAnchor.ConstraintLessThanOrEqualTo(card.WidthAnchor).Active = true;
        if (detail is not null)
        {
            detail.WidthAnchor.ConstraintLessThanOrEqualTo(card.WidthAnchor).Active = true;
        }

        return card;
    }

    /// <summary>A row of cards that scrolls sideways when it is wider than the page.</summary>
    private static NSView LibraryShelf(IEnumerable<NSView> cards)
    {
        var row = MacTheme.Stack(false, 3, [.. cards]);
        row.Alignment = NSLayoutAttribute.Top;
        row.EdgeInsets = new NSEdgeInsets(0, MacTheme.Space(1), 0, MacTheme.Space(1));
        var document = new TopAlignedLibraryDocument { TranslatesAutoresizingMaskIntoConstraints = false };
        document.AddSubview(row);
        MacTheme.Fill(document, row);
        var scroll = new NSScrollView
        {
            HasHorizontalScroller = true,
            AutohidesScrollers = true,
            DrawsBackground = false,
            BorderType = NSBorderType.NoBorder,
            TranslatesAutoresizingMaskIntoConstraints = false,
            DocumentView = document,
        };
        document.TopAnchor.ConstraintEqualTo(scroll.ContentView.TopAnchor).Active = true;
        document.LeadingAnchor.ConstraintEqualTo(scroll.ContentView.LeadingAnchor).Active = true;
        document.HeightAnchor.ConstraintEqualTo(scroll.ContentView.HeightAnchor).Active = true;
        scroll.HeightAnchor.ConstraintEqualTo((nfloat)(LibraryCardArtworkSize + 56)).Active = true;
        return scroll;
    }

    /// <summary>A small rounded label on the raised fill: a genre under an album's songs.</summary>
    private static NSView LibraryChip(string text)
    {
        var chip = MacTheme.Panel(DesignRole.Raised, 2);
        var label = MacTheme.Label(text, 1, DesignRole.TextPrimary);
        chip.AddSubview(label);
        label.LeadingAnchor.ConstraintEqualTo(chip.LeadingAnchor, 10).Active = true;
        label.TrailingAnchor.ConstraintEqualTo(chip.TrailingAnchor, -10).Active = true;
        label.TopAnchor.ConstraintEqualTo(chip.TopAnchor, 3).Active = true;
        label.BottomAnchor.ConstraintEqualTo(chip.BottomAnchor, -3).Active = true;
        return chip;
    }

    /// <summary><paramref name="view"/> at the start of a full-width line, at its own width.</summary>
    private static NSView LibraryLeading(NSView view)
    {
        var holder = new NSView { TranslatesAutoresizingMaskIntoConstraints = false };
        view.TranslatesAutoresizingMaskIntoConstraints = false;
        holder.AddSubview(view);
        view.TopAnchor.ConstraintEqualTo(holder.TopAnchor).Active = true;
        view.BottomAnchor.ConstraintEqualTo(holder.BottomAnchor).Active = true;
        view.LeadingAnchor.ConstraintEqualTo(holder.LeadingAnchor, MacTheme.Space(1)).Active = true;
        view.TrailingAnchor.ConstraintLessThanOrEqualTo(holder.TrailingAnchor).Active = true;
        return holder;
    }

    /// <summary>
    /// The whole album behind an artist page's grouping, which may hold only that artist's songs
    /// on it; the grouping itself when the catalogue no longer files it so.
    /// </summary>
    private LibraryGroup FullLibraryAlbum(LibraryGroup album) =>
        _mediaLibrary is null ? album
            : LibraryMemo.Albums(_mediaLibrary.Entries).FirstOrDefault(candidate =>
                string.Equals(candidate.Name, album.Name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.Detail, album.Detail, StringComparison.OrdinalIgnoreCase)) ?? album;

    /// <summary>Adds <paramref name="entries"/> to Up Next, or asks for a rescan when any has gone missing.</summary>
    private void QueueLibraryEntries(IReadOnlyList<LibraryEntry> entries, string name)
    {
        if (entries.Count == 0)
        {
            return;
        }

        if (entries.Any(entry => !File.Exists(entry.Path)))
        {
            _libraryScanner?.Request();
            Say("A file is missing. Rescanning your library.");
            return;
        }

        _player.EnqueueFromLibrary(entries);
        Say("Added to Up Next: " + name);
    }
}
