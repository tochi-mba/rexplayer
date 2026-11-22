using System.Globalization;
using Rex.Media.AppCore.Design;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;
using Rex.Media.Settings;

namespace Rex.Media.App.Mac;

/// <summary>
/// The list page's results, drawn as the Windows list draws them: a line of column headings in
/// tertiary caption text, then a line per item with its artwork, its title in primary text and the
/// rest in secondary text, the playing line in the signal colour, and the raised fill under the
/// line pointed at or chosen. Songs take the Songs table's columns; other media show their details
/// and length; Folders lists the watched folders. An empty view says what Windows says.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The share of a line the Songs table gives its artist and album columns each.</summary>
    private const double LibrarySongColumnShare = 0.18;

    /// <summary>The share of a line other media give their details.</summary>
    private const double LibraryDetailColumnShare = 0.34;

    /// <summary>Which kind of empty view a category is, and the words and ways out the shared rules give it.</summary>
    private LibraryEmptyState LibraryEmptyFor(int category, bool searching)
    {
        var view = category switch
        {
            6 => LibraryEmptyView.ContinueWatching,
            5 => LibraryEmptyView.RecentlyPlayed,
            7 => LibraryEmptyView.Folders,
            0 => LibraryEmptyView.Home,
            _ => LibraryEmptyView.Media,
        };
        return LibraryEmptyState.For(view, searching, (_mediaLibrary?.Folders.Count ?? 0) > 0, null,
            _libraryScanner?.IsBusy == true, _libraryScanner?.Problem);
    }

    /// <summary>Replaces the results with this page's lines, or with the empty state when there are none.</summary>
    private void RenderLibraryResults(IReadOnlyList<LibraryEntry> shown, IReadOnlyList<string> watchedFolders, int category,
        LibraryGrouping grouping, IReadOnlyList<LibrarySection> sections, bool showPlays, LibraryEmptyState? empty)
    {
        if (_libraryScroll is null || _libraryEmpty is null)
        {
            return;
        }

        _libraryFirstPlay = null;
        _libraryFirstQueue = null;
        _libraryChosenRow = null;
        _libraryResultKeyViews.Clear();
        _libraryRowTitles.Clear();
        _libraryMarkedPlaying = null;
        var lines = new List<NSView>();
        if (empty is not null)
        {
            ShowLibraryEmpty(empty);
        }
        else
        {
            _libraryEmpty.Hidden = true;
            _libraryScroll.Hidden = false;
        }

        if (watchedFolders.Count > 0)
        {
            lines.Add(LibraryHeaderLine([("Folder", 0, false)], folders: true));
            foreach (var folder in watchedFolders)
            {
                lines.Add(LibraryFolderLine(folder));
            }
        }
        else if (shown.Count > 0)
        {
            var songs = category == 1;
            lines.Add(songs
                ? LibraryHeaderLine([("Title", 0, false), ("Artist", LibrarySongColumnShare, false), ("Album", LibrarySongColumnShare, false),
                    .. showPlays ? new[] { ("Plays", -56.0, true) } : [], ("Length", -52.0, true)])
                : LibraryHeaderLine([("Title", 0, false), ("Details", LibraryDetailColumnShare, false), ("Length", -52.0, true)]));
            var sectionOf = new Dictionary<LibraryEntry, string>(ReferenceEqualityComparer.Instance);
            if (grouping != LibraryGrouping.None)
            {
                foreach (var section in sections)
                {
                    foreach (var entry in section.Entries)
                    {
                        sectionOf[entry] = section.Header;
                    }
                }
            }

            string? heading = null;
            foreach (var entry in shown)
            {
                if (sectionOf.TryGetValue(entry, out var header) && header != heading)
                {
                    heading = header;
                    var label = MacLibraryViews.Heading(header.Length > 0 ? header : " ");
                    var holder = new NSView { TranslatesAutoresizingMaskIntoConstraints = false };
                    holder.AddSubview(label);
                    label.LeadingAnchor.ConstraintEqualTo(holder.LeadingAnchor, MacTheme.Space(1)).Active = true;
                    label.TopAnchor.ConstraintEqualTo(holder.TopAnchor, MacTheme.Space(3)).Active = true;
                    label.BottomAnchor.ConstraintEqualTo(holder.BottomAnchor, -MacTheme.Space(0)).Active = true;
                    lines.Add(holder);
                }

                lines.Add(LibraryEntryLine(entry, songs, showPlays));
            }
        }

        var content = MacTheme.Stack(true, 0, [.. lines]);
        content.Alignment = NSLayoutAttribute.Width;
        content.Spacing = 2;
        content.EdgeInsets = new NSEdgeInsets(0, 0, MacTheme.Space(2), 0);
        var document = new TopAlignedLibraryDocument { TranslatesAutoresizingMaskIntoConstraints = false };
        document.AddSubview(content);
        content.TopAnchor.ConstraintEqualTo(document.TopAnchor).Active = true;
        content.LeadingAnchor.ConstraintEqualTo(document.LeadingAnchor).Active = true;
        content.TrailingAnchor.ConstraintEqualTo(document.TrailingAnchor).Active = true;
        content.BottomAnchor.ConstraintEqualTo(document.BottomAnchor).Active = true;
        _libraryScroll.DocumentView = document;
        // The lines are as wide as the page and as tall as they come, so they scroll only up and down.
        document.TopAnchor.ConstraintEqualTo(_libraryScroll.ContentView.TopAnchor).Active = true;
        document.LeadingAnchor.ConstraintEqualTo(_libraryScroll.ContentView.LeadingAnchor).Active = true;
        document.WidthAnchor.ConstraintEqualTo(_libraryScroll.ContentView.WidthAnchor).Active = true;
        MarkLibraryPlaying();
        WireLibraryKeyViews();
    }

    /// <summary>
    /// The column headings, laid out exactly as the lines under them: a share of the line for a
    /// proportional column, a negative width for a fixed one, nothing for the title, which takes
    /// what is left.
    /// </summary>
    private static NSView LibraryHeaderLine(IReadOnlyList<(string Text, double Width, bool Right)> columns, bool folders = false)
    {
        var line = new NSView { TranslatesAutoresizingMaskIntoConstraints = false };
        var views = new List<NSView> { LibraryColumnSpacer(folders ? 32 : 40) };
        foreach (var (text, _, right) in columns)
        {
            var heading = MacTheme.Label(text, 1, DesignRole.TextTertiary);
            heading.Alignment = right ? NSTextAlignment.Right : NSTextAlignment.Left;
            views.Add(heading);
        }

        views.Add(LibraryColumnSpacer(folders ? 120 : MacTheme.ControlSize));
        var row = MacTheme.Stack(false, 3, [.. views]);
        line.AddSubview(row);
        row.LeadingAnchor.ConstraintEqualTo(line.LeadingAnchor, MacTheme.Space(1)).Active = true;
        row.TrailingAnchor.ConstraintEqualTo(line.TrailingAnchor, -MacTheme.Space(0)).Active = true;
        row.TopAnchor.ConstraintEqualTo(line.TopAnchor, MacTheme.Space(0)).Active = true;
        row.BottomAnchor.ConstraintEqualTo(line.BottomAnchor, -MacTheme.Space(0)).Active = true;
        for (var index = 0; index < columns.Count; index++)
        {
            SizeLibraryColumn(views[index + 1], columns[index].Width, line);
        }

        // Screen readers skip the headings: each line names its own columns.
        line.AccessibilityElement = false;
        return line;
    }

    /// <summary>Gives a column its width: a share of <paramref name="line"/>, a fixed width, or (zero) the rest.</summary>
    private static void SizeLibraryColumn(NSView column, double width, NSView line)
    {
        if (width > 0)
        {
            column.WidthAnchor.ConstraintEqualTo(line.WidthAnchor, (nfloat)width).Active = true;
        }
        else if (width < 0)
        {
            column.WidthAnchor.ConstraintEqualTo((nfloat)(-width)).Active = true;
        }
        else
        {
            column.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Horizontal);
            column.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
        }
    }

    /// <summary>Empty space as wide as a column with nothing to head.</summary>
    private static NSView LibraryColumnSpacer(double width)
    {
        var spacer = new NSView { TranslatesAutoresizingMaskIntoConstraints = false };
        spacer.WidthAnchor.ConstraintEqualTo((nfloat)width).Active = true;
        spacer.HeightAnchor.ConstraintEqualTo(1).Active = true;
        return spacer;
    }

    /// <summary>
    /// One item's line: its artwork, its title (pressing it plays from here, with the rest of the
    /// view after it), its columns, and Add to Up Next at the end; the line's menu offers both and,
    /// for a song, its album and artist.
    /// </summary>
    private LibraryResultRow LibraryEntryLine(LibraryEntry entry, bool songs, bool showPlays)
    {
        void Play() => PlayLibraryEntry(entry, _libraryCurrentSelection);
        void Queue() => QueueLibraryEntry(entry);
        var line = new LibraryResultRow(Play, ChooseLibraryRow);
        var art = new LibraryArtView(40, MacTheme.Radius(0), LibraryGlyphOf(entry.Kind));
        if (entry.Kind != LibraryKind.Music)
        {
            art.Picture = LibraryThumbnail(entry);
        }

        ((NSView)art).AccessibilityLabel = entry.Kind switch
        {
            LibraryKind.Music => "Artwork for " + entry.Title,
            LibraryKind.Picture => "Thumbnail of " + entry.Title,
            _ => "Preview of " + entry.Title,
        };
        var title = new RexTextButton(entry.Title, "Play: " + entry.Title, Play, DesignRole.TextPrimary, MacTheme.Font(2));
        title.ToolTip = entry.Path;
        _libraryFirstPlay ??= title;
        _libraryResultKeyViews.Add(title);
        _libraryRowTitles.Add((entry.Path, title));

        var columns = new List<(NSView View, double Width)> { (title, 0) };
        if (songs)
        {
            columns.Add((LibrarySongLink(entry.Artist, entry.Artist is { Length: > 0 } artist ? () => OpenLibraryArtistNamed(artist) : null), LibrarySongColumnShare));
            columns.Add((LibrarySongLink(entry.Album, entry.Album is { Length: > 0 } ? () => OpenLibraryAlbumOf(entry) : null), LibrarySongColumnShare));
            if (showPlays)
            {
                columns.Add((LibraryCell(entry.Plays > 0 ? entry.Plays.ToString("N0", CultureInfo.CurrentCulture) : "", right: true), -56));
            }
        }
        else
        {
            var details = LibraryCell(LibraryEntryDescription(entry), right: false);
            details.ToolTip = details.StringValue;
            columns.Add((details, LibraryDetailColumnShare));
        }

        columns.Add((LibraryCell(entry.Duration is { } length ? TimeText.Format(length, length) : "", right: true), -52));
        var queue = MacTheme.IconButton("plus", "Add " + entry.Title + " to Up Next", Queue);
        queue.ToolTip = "Add to Up Next without replacing what is playing";
        _libraryFirstQueue ??= queue;
        _libraryResultKeyViews.Add(queue);

        var row = MacTheme.Stack(false, 3, [art, .. columns.Select(column => column.View), queue]);
        line.AddSubview(row);
        row.LeadingAnchor.ConstraintEqualTo(line.LeadingAnchor, MacTheme.Space(1)).Active = true;
        row.TrailingAnchor.ConstraintEqualTo(line.TrailingAnchor, -MacTheme.Space(0)).Active = true;
        row.CenterYAnchor.ConstraintEqualTo(line.CenterYAnchor).Active = true;
        line.HeightAnchor.ConstraintEqualTo((nfloat)LibraryRowHeight).Active = true;
        foreach (var (view, width) in columns)
        {
            SizeLibraryColumn(view, width, line);
        }

        var menu = new NSMenu();
        menu.AddItem(new NSMenuItem("Play", (_, _) => Play()));
        menu.AddItem(new NSMenuItem("Add to Up Next", (_, _) => Queue()));
        line.Menu = menu;
        title.Menu = menu;
        if (entry.Kind == LibraryKind.Music)
        {
            // A song leads to its album and artist: its artwork opens the album, and the line's
            // menu offers both pages.
            LinkMusicRow(entry, menu, art);
        }

        ((NSView)line).AccessibilityLabel = entry.Title;
        return line;
    }

    /// <summary>A song's artist or album: a secondary-text link to its page, left out of the Tab order as on Windows.</summary>
    private static NSView LibrarySongLink(string? text, Action? open)
    {
        if (string.IsNullOrWhiteSpace(text) || open is null)
        {
            return LibraryCell(text ?? "", right: false);
        }

        var link = new RexTextButton(text, text, open, DesignRole.TextSecondary, MacTheme.Font(2))
        {
            RefusesFirstResponder = true,
        };
        return link;
    }

    /// <summary>A column's text: secondary, one line, cut short with an ellipsis.</summary>
    private static RexLabel LibraryCell(string text, bool right)
    {
        var cell = MacTheme.Label(text, 2, DesignRole.TextSecondary);
        cell.Alignment = right ? NSTextAlignment.Right : NSTextAlignment.Left;
        cell.Font = right ? MacTheme.DigitFont(2) : MacTheme.Font(2);
        cell.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
        return cell;
    }

    /// <summary>A watched folder's line: its path, how many files it holds and Stop watching.</summary>
    private LibraryResultRow LibraryFolderLine(string folder)
    {
        var line = new LibraryResultRow(null, null);
        var art = new LibraryArtView(32, MacTheme.Radius(0), "folder");
        var name = MacTheme.Label(folder, 2, DesignRole.TextPrimary);
        name.LineBreakMode = NSLineBreakMode.TruncatingMiddle;
        name.ToolTip = folder;
        name.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Horizontal);
        name.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
        var files = _mediaLibrary?.Entries.Count(entry => Rex.Media.Library.MediaLibrary.Holds(folder, entry.Path)) ?? 0;
        var count = LibraryCell(LibraryActions.Count(files, "file"), right: false);
        var stop = MacTheme.TextButton("Stop watching", () =>
        {
            if (_mediaLibrary?.RemoveFolder(folder) == true)
            {
                _libraryScanner?.Request();
                RefreshLibrary(force: true);
            }
        }, RexButtonLook.Standard, label: "Stop watching folder: " + folder);
        stop.ToolTip = "Stop watching this folder without deleting its files";
        stop.WidthAnchor.ConstraintEqualTo(120).Active = true;
        _libraryResultKeyViews.Add(stop);
        var row = MacTheme.Stack(false, 3, art, name, count, stop);
        line.AddSubview(row);
        row.LeadingAnchor.ConstraintEqualTo(line.LeadingAnchor, MacTheme.Space(1)).Active = true;
        row.TrailingAnchor.ConstraintEqualTo(line.TrailingAnchor, -MacTheme.Space(0)).Active = true;
        row.CenterYAnchor.ConstraintEqualTo(line.CenterYAnchor).Active = true;
        line.HeightAnchor.ConstraintEqualTo((nfloat)LibraryRowHeight).Active = true;
        count.WidthAnchor.ConstraintEqualTo(line.WidthAnchor, (nfloat)0.16).Active = true;
        ((NSView)line).AccessibilityLabel = "Watched folder: " + folder;
        return line;
    }

    /// <summary>The placeholder glyph for a kind of media, as the Windows cards and lines use one.</summary>
    private static string LibraryGlyphOf(LibraryKind kind) => kind switch
    {
        LibraryKind.Video => "film",
        LibraryKind.Picture => "photo",
        _ => "music.note",
    };

    /// <summary>A click chose <paramref name="row"/>; the line chosen before lets go.</summary>
    private void ChooseLibraryRow(LibraryResultRow row)
    {
        if (_libraryChosenRow is not null && !ReferenceEquals(_libraryChosenRow, row))
        {
            _libraryChosenRow.Chosen = false;
        }

        row.Chosen = true;
        _libraryChosenRow = row;
    }

    /// <summary>Adds one item to Up Next, or asks for a rescan when its file has gone.</summary>
    private void QueueLibraryEntry(LibraryEntry entry)
    {
        if (!File.Exists(entry.Path))
        {
            _libraryScanner?.Request();
            Say("This file is missing. Rescanning your library.");
            return;
        }

        _player.EnqueueFromLibrary([entry]);
        Say("Added to Up Next: " + entry.Title);
    }

    /// <summary>Gives the playing item's title the signal colour, and every other title its own.</summary>
    private void MarkLibraryPlaying()
    {
        var playing = _player.Item?.Location ?? "";
        if (playing == _libraryMarkedPlaying)
        {
            return;
        }

        _libraryMarkedPlaying = playing;
        foreach (var (path, title) in _libraryRowTitles)
        {
            var now = playing.Length > 0 && LibraryViews.SameLocation(path, playing);
            if (now != (title.FixedColor is not null))
            {
                title.FixedColor = now ? MacTheme.Signal : null;
            }
        }
    }

    /// <summary>
    /// The empty state in the results' place: the library's mark (or a warning for a folder out of
    /// reach), the heading and words the shared rules give, and the ways out under them.
    /// </summary>
    private void ShowLibraryEmpty(LibraryEmptyState state)
    {
        if (_libraryEmpty is null || _libraryScroll is null)
        {
            return;
        }

        foreach (var old in _libraryEmpty.Subviews)
        {
            old.RemoveFromSuperview();
        }

        var badge = MacTheme.Panel(DesignRole.Raised, 3);
        badge.WidthAnchor.ConstraintEqualTo(64).Active = true;
        badge.HeightAnchor.ConstraintEqualTo(64).Active = true;
        var glyph = new NSImageView
        {
            Image = MacLibraryViews.Symbol(state.IsProblem ? "exclamationmark.triangle" : "books.vertical", 28),
            ContentTintColor = state.IsProblem ? MacTheme.Attention : MacTheme.Accent,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        badge.AddSubview(glyph);
        glyph.CenterXAnchor.ConstraintEqualTo(badge.CenterXAnchor).Active = true;
        glyph.CenterYAnchor.ConstraintEqualTo(badge.CenterYAnchor).Active = true;
        var views = new List<NSView> { badge };
        if (state.Heading is { } heading)
        {
            var title = MacTheme.WrappingLabel(heading, 4, DesignRole.TextPrimary, semibold: true);
            title.Alignment = NSTextAlignment.Center;
            title.AccessibilityLabel = heading;
            views.Add(title);
        }

        var words = MacTheme.WrappingLabel(state.Text, 3, DesignRole.TextPrimary);
        words.Alignment = NSTextAlignment.Center;
        views.Add(words);
        var buttons = new List<NSView>();
        var query = _librarySearch?.StringValue.Trim() ?? "";
        if (query.Length > 0)
        {
            buttons.Add(MacTheme.TextButton("Clear search", () =>
            {
                if (_librarySearch is not null)
                {
                    _librarySearch.StringValue = "";
                    ChangeLibraryView();
                    // The empty state's buttons go as results return; focus moves to a control that stays.
                    _window.MakeFirstResponder(_librarySearch);
                }
            }));
        }

        if (state.OffersRescan)
        {
            buttons.Add(MacTheme.TextButton("Try scanning again", () => _libraryScanner?.Request(), RexButtonLook.Standard, label: "Try scanning the library folder again"));
        }

        if (state.OffersFolderList)
        {
            buttons.Add(MacTheme.TextButton("Show folders", () => SelectLibrarySection(SidebarView.Folders), RexButtonLook.Standard, label: "Show the library's folders"));
        }

        if (state.OffersFolders)
        {
            // Where adding a folder is the way forward (an empty library), it is the primary action.
            buttons.Add(MacTheme.TextButton("Add a folder", AddLibraryFolder, state.Heading is null ? RexButtonLook.Standard : RexButtonLook.Accent));
            foreach (var (name, special) in new[] { ("Music", Environment.SpecialFolder.MyMusic), ("Videos", Environment.SpecialFolder.MyVideos), ("Pictures", Environment.SpecialFolder.MyPictures) })
            {
                var path = Environment.GetFolderPath(special);
                if (path.Length > 0 && Directory.Exists(path) &&
                    _mediaLibrary?.Folders.Any(known => Rex.Media.Library.MediaLibrary.Holds(known, path)) != true)
                {
                    var suggestion = MacTheme.TextButton("Add your " + name + " folder", () => AddSuggestedLibraryFolder(path, name));
                    suggestion.ToolTip = path;
                    buttons.Add(suggestion);
                }
            }
        }

        if (buttons.Count > 0)
        {
            var column = MacTheme.Stack(true, 1, [.. buttons]);
            column.Alignment = NSLayoutAttribute.CenterX;
            views.Add(column);
            _libraryResultKeyViews.AddRange(buttons);
        }

        var stack = MacTheme.Stack(true, 2, [.. views]);
        stack.Alignment = NSLayoutAttribute.CenterX;
        stack.SetCustomSpacing(MacTheme.Space(3), badge);
        _libraryEmpty.AddSubview(stack);
        stack.CenterXAnchor.ConstraintEqualTo(_libraryEmpty.CenterXAnchor).Active = true;
        stack.CenterYAnchor.ConstraintEqualTo(_libraryEmpty.CenterYAnchor).Active = true;
        stack.WidthAnchor.ConstraintLessThanOrEqualTo(440).Active = true;
        stack.WidthAnchor.ConstraintLessThanOrEqualTo(_libraryEmpty.WidthAnchor, 1, -48).Active = true;
        stack.TopAnchor.ConstraintGreaterThanOrEqualTo(_libraryEmpty.TopAnchor, MacTheme.Space(3)).Active = true;
        foreach (var view in views.Skip(1).OfType<RexLabel>())
        {
            view.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor).Active = true;
        }

        ((NSView)_libraryEmpty).AccessibilityLabel = state.Heading ?? state.Text;
        _libraryEmpty.Hidden = false;
        _libraryScroll.Hidden = true;
    }
}
