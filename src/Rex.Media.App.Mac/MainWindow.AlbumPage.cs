using System.Globalization;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;

namespace Rex.Media.App.Mac;

/// <summary>
/// An album's page in the Mac library (LIB-05): the cover, the title, each of the album's artists
/// as a link to their page, the release's kind, year, songs and length, Play album and Add to Up
/// Next, then its numbered songs under disc headings for a set of several, each with Play and
/// Queue, the songs' genres and more by the same artist. <see cref="AlbumPage"/> decides; this
/// file draws. The Mac keeps no looked-up release facts yet, so only what the songs say is shown.
/// </summary>
public sealed partial class MainWindow
{
    private const double LibraryAlbumArtworkSize = 160;

    /// <summary>Opens the album's page over the current view; Back returns to that view.</summary>
    private bool OpenLibraryAlbum(LibraryGroup album)
    {
        if (_libraryWindow?.IsVisible != true || _mediaLibrary is null || _libraryPageHost is null)
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
    /// Gives a song's list row its ways to the album and artist pages without changing how it
    /// looks: clicking the artwork opens the album, and the row's menu (a secondary click, or
    /// VoiceOver's Show Menu on its Play button) offers the album and the song's first artist.
    /// </summary>
    private void LinkMusicRow(LibraryEntry entry, NSView row, NSButton play, NSImageView artwork)
    {
        var menu = new NSMenu();
        if (entry.Album is { Length: > 0 } albumName)
        {
            menu.AddItem(new NSMenuItem("Go to album: " + albumName, (_, _) => OpenLibraryAlbumOf(entry)));
            artwork.AddGestureRecognizer(new NSClickGestureRecognizer(() => OpenLibraryAlbumOf(entry)));
            artwork.ToolTip = "Open the album " + albumName;
        }

        if (ArtistNames.Learn([entry]).Of(entry) is [var artist, ..])
        {
            menu.AddItem(new NSMenuItem("Go to artist: " + artist, (_, _) => OpenLibraryArtistNamed(artist)));
        }

        if (menu.Count > 0)
        {
            row.Menu = menu;
            play.Menu = menu;
        }
    }

    /// <summary>The album's page: its cover and words, its songs by disc, then its genres and more by its artist.</summary>
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

        var cover = new NSImageView(new CGRect(0, 0, LibraryAlbumArtworkSize, LibraryAlbumArtworkSize))
        {
            ImageScaling = NSImageScale.ProportionallyUpOrDown,
        };
        ((NSView)cover).AccessibilityLabel = "Cover of " + album.Name;
        cover.WidthAnchor.ConstraintEqualTo((nfloat)LibraryAlbumArtworkSize).Active = true;
        cover.HeightAnchor.ConstraintEqualTo((nfloat)LibraryAlbumArtworkSize).Active = true;
        LoadLibraryAlbumArtwork(cover, songs.FirstOrDefault());

        var kicker = NSTextField.CreateLabel(AlbumPage.KindName(kind).ToUpperInvariant());
        kicker.TextColor = NSColor.SecondaryLabel;
        kicker.Font = NSFont.SystemFontOfSize(11, NSFontWeight.Semibold)!;
        var title = NSTextField.CreateLabel(album.Name);
        title.Font = NSFont.SystemFontOfSize(26, NSFontWeight.Semibold)!;
        title.LineBreakMode = NSLineBreakMode.TruncatingTail;
        title.WidthAnchor.ConstraintLessThanOrEqualTo(420).Active = true;
        var words = new List<NSView> { kicker, title };
        if (artists.Count > 0)
        {
            var links = new List<NSView>();
            foreach (var name in artists)
            {
                var link = LibraryLink(name, "Open the artist " + name, () => OpenLibraryArtistNamed(name));
                keyViews.Add(link);
                links.Add(link);
            }

            var byline = NSStackView.FromViews([.. links]);
            byline.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            byline.Spacing = 10;
            words.Add(byline);
        }

        var facts = NSTextField.CreateLabel(line);
        facts.TextColor = NSColor.SecondaryLabel;
        facts.AccessibilityLabel = "About this album: " + line;
        words.Add(facts);
        var playAlbum = NSButton.CreateButton("Play album", () =>
        {
            if (songs.Count > 0)
            {
                PlayLibraryEntry(songs[0], songs);
            }
        });
        playAlbum.Enabled = songs.Count > 0;
        ((NSView)playAlbum).AccessibilityLabel = "Play the album " + album.Name;
        var queueAlbum = NSButton.CreateButton("Add to Up Next", () => QueueLibraryEntries(songs, album.Name));
        queueAlbum.Enabled = songs.Count > 0;
        queueAlbum.ToolTip = "Add the whole album to Up Next without replacing what is playing";
        ((NSView)queueAlbum).AccessibilityLabel = "Add the album " + album.Name + " to Up Next";
        keyViews.Add(playAlbum);
        keyViews.Add(queueAlbum);
        var actions = NSStackView.FromViews([playAlbum, queueAlbum]);
        actions.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        actions.Spacing = 10;
        words.Add(actions);
        var wordColumn = NSStackView.FromViews([.. words]);
        wordColumn.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
        wordColumn.Alignment = NSLayoutAttribute.Leading;
        wordColumn.Spacing = 6;
        var hero = NSStackView.FromViews([cover, wordColumn]);
        hero.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        hero.Alignment = NSLayoutAttribute.CenterY;
        hero.Spacing = 18;
        views.Add(hero);

        if (songs.Count == 0)
        {
            var empty = NSTextField.CreateLabel("None of this album's songs are in your library now.");
            empty.TextColor = NSColor.SecondaryLabel;
            views.Add(empty);
        }

        foreach (var disc in discs)
        {
            if (disc.Number is { } number)
            {
                views.Add(LibraryHeading(AlbumPage.DiscName(number)));
            }

            foreach (var song in disc.Songs)
            {
                var featured = AlbumPage.Featured(song, artists, albumNames);
                var detail = ViewFacts.Join(
                    featured.Count == 0 ? null : "feat. " + string.Join(", ", featured),
                    song.Duration is { } length ? TimeText.Format(length, length) : null);
                views.Add(LibrarySongRow(song, songs, song.Track?.ToString(CultureInfo.CurrentCulture) ?? "", detail, keyViews));
            }
        }

        if (AlbumPage.Genres(album, null) is { Count: > 0 } genres)
        {
            var genreLine = NSTextField.CreateLabel(string.Join(" · ",
                genres.Select(genre => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(genre))));
            genreLine.TextColor = NSColor.SecondaryLabel;
            genreLine.Font = NSFont.SystemFontOfSize(12, NSFontWeight.Regular)!;
            genreLine.AccessibilityLabel = "Genres: " + genreLine.StringValue;
            views.Add(genreLine);
        }

        if (AlbumPage.MoreBy(album, LibraryMemo.Albums(all), libraryNames) is { Count: > 0 } more && artists is [var first, ..])
        {
            views.Add(LibraryHeading("More by " + first));
            foreach (var other in more.Take(MusicHome.RowSize))
            {
                views.Add(LibraryAlbumRow(other, other.Entries.Max(song => song.Year), keyViews));
            }
        }

        var summary = $"album={album.Name};artist={string.Join(", ", artists)};discs={discs.Count};tracks={songs.Count}";
        return LibraryDetailPageOf("Album: " + album.Name, summary, views, keyViews);
    }

    /// <summary>
    /// The cover: a picture kept beside the songs (<see cref="FolderArt"/>) when there is one, read
    /// off the window's thread; until then, and otherwise, the preview macOS has for the first song,
    /// which is its embedded artwork where the system reads one.
    /// </summary>
    private void LoadLibraryAlbumArtwork(NSImageView cover, LibraryEntry? first)
    {
        if (first is null)
        {
            return;
        }

        var preview = NSWorkspace.SharedWorkspace.IconForFile(first.Path);
        preview.Size = new CGSize(LibraryAlbumArtworkSize, LibraryAlbumArtworkSize);
        cover.Image = preview;
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
                        cover.Image = image;
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
    /// One song's line on a detail page: its number, its title as Play (the rest of
    /// <paramref name="selection"/> plays after it), a detail line and Queue.
    /// </summary>
    private NSView LibrarySongRow(LibraryEntry song, IReadOnlyList<LibraryEntry> selection, string number, string detail, List<NSView> keyViews)
    {
        var badge = NSTextField.CreateLabel(number);
        badge.TextColor = NSColor.SecondaryLabel;
        badge.Alignment = NSTextAlignment.Right;
        badge.WidthAnchor.ConstraintEqualTo(28).Active = true;
        var play = NSButton.CreateButton(song.Title, () => PlayLibraryEntry(song, selection));
        play.ToolTip = song.Path;
        play.Alignment = NSTextAlignment.Left;
        play.LineBreakMode = NSLineBreakMode.TruncatingTail;
        play.WidthAnchor.ConstraintGreaterThanOrEqualTo(250).Active = true;
        play.WidthAnchor.ConstraintLessThanOrEqualTo(370).Active = true;
        ((NSView)play).AccessibilityLabel = "Play: " + song.Title;
        var line = new List<NSView> { play };
        if (detail.Length > 0)
        {
            var words = NSTextField.CreateLabel(detail);
            words.TextColor = NSColor.SecondaryLabel;
            words.Font = NSFont.SystemFontOfSize(11, NSFontWeight.Regular)!;
            words.LineBreakMode = NSLineBreakMode.TruncatingTail;
            words.WidthAnchor.ConstraintLessThanOrEqualTo(370).Active = true;
            line.Add(words);
        }

        var column = NSStackView.FromViews([.. line]);
        column.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
        column.Alignment = NSLayoutAttribute.Leading;
        column.Spacing = 2;
        var queue = NSButton.CreateButton("Queue", () => QueueLibraryEntries([song], song.Title));
        queue.ToolTip = "Add to Up Next without replacing what is playing";
        ((NSView)queue).AccessibilityLabel = "Add " + song.Title + " to Up Next";
        keyViews.Add(play);
        keyViews.Add(queue);
        var row = NSStackView.FromViews([badge, column, queue]);
        row.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        row.Spacing = 12;
        return row;
    }

    /// <summary>One album's line on a detail page: its artwork and name, opening its page, and its year and songs.</summary>
    private NSView LibraryAlbumRow(LibraryGroup album, int? year, List<NSView> keyViews)
    {
        var artwork = new NSImageView(new CGRect(0, 0, 48, 48)) { ImageScaling = NSImageScale.ProportionallyUpOrDown };
        if (album.Entries.FirstOrDefault() is { } first)
        {
            artwork.Image = LibraryThumbnail(first);
        }

        ((NSView)artwork).AccessibilityLabel = "Artwork for " + album.Name;
        artwork.WidthAnchor.ConstraintEqualTo(48).Active = true;
        artwork.HeightAnchor.ConstraintEqualTo(48).Active = true;
        var open = LibraryLink(album.Name, "Open the album " + album.Name, () => OpenLibraryAlbum(FullLibraryAlbum(album)));
        keyViews.Add(open);
        var detail = NSTextField.CreateLabel(ViewFacts.Join(
            year?.ToString(CultureInfo.CurrentCulture),
            album.Entries.Count == 1 ? "1 song" : album.Entries.Count.ToString(CultureInfo.CurrentCulture) + " songs"));
        detail.TextColor = NSColor.SecondaryLabel;
        detail.Font = NSFont.SystemFontOfSize(11, NSFontWeight.Regular)!;
        var words = NSStackView.FromViews([open, detail]);
        words.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
        words.Alignment = NSLayoutAttribute.Leading;
        words.Spacing = 2;
        var row = NSStackView.FromViews([artwork, words]);
        row.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        row.Spacing = 14;
        return row;
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

    /// <summary>A borderless, link-coloured button that opens another page.</summary>
    private static NSButton LibraryLink(string title, string label, Action open)
    {
        var link = NSButton.CreateButton(title, open);
        link.Bordered = false;
        link.ContentTintColor = NSColor.Link;
        link.LineBreakMode = NSLineBreakMode.TruncatingTail;
        link.ToolTip = label;
        ((NSView)link).AccessibilityLabel = label;
        return link;
    }

    /// <summary>A section heading on a detail page.</summary>
    private static NSTextField LibraryHeading(string text)
    {
        var heading = NSTextField.CreateLabel(text);
        heading.Font = NSFont.SystemFontOfSize(15, NSFontWeight.Semibold)!;
        heading.AccessibilityLabel = text;
        return heading;
    }

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
