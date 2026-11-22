using Rex.Media.AppCore.Design;

namespace Rex.Media.App.Mac;

/// <summary>
/// How the library pane is built: the navigation column beside the page panel, the list page with
/// its banner, search, row of view options, results and pager, and the banner every page shares.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The pane: the navigation column at the left, the page panel inset beside it.</summary>
    private void BuildLibraryPane(NSView host)
    {
        var root = new LibraryPaneView(HideLibrary, OnLibraryPaneResized);
        var navigation = BuildLibrarySidebar();
        var panel = MacTheme.Panel(DesignRole.Surface, 3, hairline: true);
        var pages = BuildLibraryPageHost(BuildLibraryListPage());
        MacTheme.Fill(panel, pages);

        // Back to player closes the pane from any of its pages, so it sits on the panel itself.
        var close = MacTheme.IconButton("xmark", "Back to player", HideLibrary);
        _libraryBackButton = close;
        panel.AddSubview(close);
        close.TopAnchor.ConstraintEqualTo(panel.TopAnchor, MacTheme.Space(1)).Active = true;
        close.TrailingAnchor.ConstraintEqualTo(panel.TrailingAnchor, -MacTheme.Space(1)).Active = true;

        root.AddSubview(navigation);
        root.AddSubview(panel);
        navigation.TopAnchor.ConstraintEqualTo(root.TopAnchor).Active = true;
        navigation.BottomAnchor.ConstraintEqualTo(root.BottomAnchor).Active = true;
        navigation.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor).Active = true;
        panel.TopAnchor.ConstraintEqualTo(root.TopAnchor, MacTheme.Space(2)).Active = true;
        panel.BottomAnchor.ConstraintEqualTo(root.BottomAnchor, -MacTheme.Space(2)).Active = true;
        panel.LeadingAnchor.ConstraintEqualTo(navigation.TrailingAnchor, MacTheme.Space(2)).Active = true;
        panel.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor, -MacTheme.Space(2)).Active = true;
        _libraryRoot = root;
        MacTheme.Fill(host, root);
        host.LayoutSubtreeIfNeeded();
    }

    /// <summary>
    /// The list page: the banner, the search across the page, the row of view options with the
    /// scan status at its end, the results (or the empty state in their place) and the pager.
    /// </summary>
    private NSView BuildLibraryListPage()
    {
        var hero = BuildLibraryHero("house");
        _libraryListHero = hero;
        _libraryPlayAll = MacTheme.TextButton("Play", () => PlayLibrarySelection(shuffled: false), RexButtonLook.Accent, "play.fill",
            "Play the chosen view");
        _libraryShuffleAll = MacTheme.TextButton("Shuffle", () => PlayLibrarySelection(shuffled: true), RexButtonLook.Standard, "shuffle",
            "Play the chosen view, shuffled");
        _libraryQueueAll = MacTheme.TextButton("Add to queue", () => QueueLibraryEntries(_libraryCurrentSelection, "this view"),
            RexButtonLook.Standard, "plus", "Add the view to the end of Up Next");
        hero.Actions.AddArrangedSubview(_libraryPlayAll);
        hero.Actions.AddArrangedSubview(_libraryShuffleAll);
        hero.Actions.AddArrangedSubview(_libraryQueueAll);

        // The Folders view's own actions, as Windows shows them in the banner there.
        var addFolders = MacTheme.TextButton("Add folders...", AddLibraryFolder, RexButtonLook.Standard, label: "Add folders to the library");
        _libraryRescanButton = MacTheme.TextButton("Rescan folders", () => _libraryScanner?.Request(), RexButtonLook.Standard,
            label: "Rescan watched library folders");
        var backUp = MacTheme.TextButton("Back up...", BackUpLibrary, RexButtonLook.Standard,
            label: "Keep the library, its play counts and your playlists in a folder you choose");
        var restore = MacTheme.TextButton("Restore backup...", RestoreLibraryBackup, RexButtonLook.Standard,
            label: "Add a backup's folders, play counts and playlists to the library");
        _libraryFolderActionKeys.AddRange([addFolders, _libraryRescanButton, backUp, restore]);
        _libraryFolderActions = MacTheme.Stack(false, 1, addFolders, _libraryRescanButton, backUp, restore);
        hero.Actions.AddArrangedSubview(_libraryFolderActions);
        _libraryMore = MacTheme.IconButton("ellipsis", "More library actions", ShowLibraryMoreMenu);
        hero.Actions.AddArrangedSubview(_libraryMore);

        _librarySearch = new NSSearchField
        {
            PlaceholderString = "Search the library",
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        _librarySearch.Changed += (_, _) => ChangeLibraryView();
        _librarySearch.Activated += (_, _) => ChangeLibraryView();
        _librarySearch.ToolTip = "Search by title, artist, album or filename";
        _librarySearch.AccessibilityLabel = "Search library";

        _librarySort = LibraryChoice("Sort order", "What the view is ordered by", LibrarySortNames.Select(item => item.Name));
        _libraryGrouping = LibraryChoice("Grouping", "What the view is gathered under", LibraryGroupingNames.Select(item => item.Name));
        _libraryCategory = LibraryChoice("Media category", "Filter this view by media type",
            ["All media", "Music", "Videos", "Pictures", "Recently added", "Recently played", "Continue watching", "Folders"]);

        _librarySummary = MacTheme.Label("", 1, DesignRole.TextSecondary);
        _librarySummary.SetContentCompressionResistancePriority(200, NSLayoutConstraintOrientation.Horizontal);
        _librarySummary.SetContentHuggingPriorityForOrientation(200, NSLayoutConstraintOrientation.Horizontal);
        _librarySummary.Alignment = NSTextAlignment.Right;
        var options = MacTheme.Stack(false, 1, _librarySort, _libraryGrouping, _libraryCategory, _librarySummary);

        _libraryScroll = new NSScrollView
        {
            HasVerticalScroller = true,
            AutohidesScrollers = true,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        _libraryScroll.DrawsBackground = false;
        _libraryScroll.BorderType = NSBorderType.NoBorder;
        _libraryScroll.AccessibilityLabel = "Library results";
        _libraryEmpty = new NSView { TranslatesAutoresizingMaskIntoConstraints = false, Hidden = true };
        var results = new NSView { TranslatesAutoresizingMaskIntoConstraints = false };
        MacTheme.Fill(results, _libraryScroll);
        MacTheme.Fill(results, _libraryEmpty);

        _libraryPreviousPage = MacTheme.TextButton("Previous", () =>
        {
            if (_libraryPageIndex > 0)
            {
                _libraryPageIndex--;
                RefreshLibrary();
                ScrollLibraryToStart();
            }
        }, RexButtonLook.Standard, "chevron.left", "Previous results page");
        _libraryNextPage = MacTheme.TextButton("Next", () =>
        {
            _libraryPageIndex++;
            RefreshLibrary();
            ScrollLibraryToStart();
        }, RexButtonLook.Standard, "chevron.right", "Next results page");
        _libraryPageSummary = MacTheme.Label("", 1, DesignRole.TextSecondary);
        _libraryPager = MacTheme.Stack(false, 2, _libraryPreviousPage, _libraryPageSummary, _libraryNextPage);

        var page = MacTheme.Stack(true, 2,
            hero.View,
            LibraryInset(_librarySearch, MacTheme.Space(4)),
            LibraryInset(options, MacTheme.Space(4)),
            LibraryInset(results, 18),
            _libraryPagerHolder = LibraryInset(_libraryPager, MacTheme.Space(4), centred: true));
        page.Alignment = NSLayoutAttribute.Width;
        page.Distribution = NSStackViewDistribution.Fill;
        page.EdgeInsets = new NSEdgeInsets(0, 0, MacTheme.Space(2), 0);
        page.DetachesHiddenViews = true;
        results.Superview!.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Vertical);
        results.Superview.SetContentCompressionResistancePriority(1, NSLayoutConstraintOrientation.Vertical);
        results.HeightAnchor.ConstraintGreaterThanOrEqualTo(120).Active = true;
        return page;
    }

    /// <summary>A stock pop-up for one of the view options, named for VoiceOver.</summary>
    private NSPopUpButton LibraryChoice(string label, string tip, IEnumerable<string> names)
    {
        var choice = new NSPopUpButton(new CGRect(0, 0, 160, 28), false) { TranslatesAutoresizingMaskIntoConstraints = false };
        foreach (var name in names)
        {
            choice.AddItem(name);
        }

        choice.Activated += (_, _) => ChangeLibraryView();
        choice.ToolTip = tip;
        ((NSView)choice).AccessibilityLabel = label;
        return choice;
    }

    /// <summary><paramref name="view"/> in a holder that keeps it <paramref name="inset"/> from each side.</summary>
    private static NSView LibraryInset(NSView view, nfloat inset, bool centred = false)
    {
        var holder = new NSView { TranslatesAutoresizingMaskIntoConstraints = false };
        view.TranslatesAutoresizingMaskIntoConstraints = false;
        holder.AddSubview(view);
        view.TopAnchor.ConstraintEqualTo(holder.TopAnchor).Active = true;
        view.BottomAnchor.ConstraintEqualTo(holder.BottomAnchor).Active = true;
        if (centred)
        {
            view.CenterXAnchor.ConstraintEqualTo(holder.CenterXAnchor).Active = true;
            view.LeadingAnchor.ConstraintGreaterThanOrEqualTo(holder.LeadingAnchor, inset).Active = true;
        }
        else
        {
            view.LeadingAnchor.ConstraintEqualTo(holder.LeadingAnchor, inset).Active = true;
            view.TrailingAnchor.ConstraintEqualTo(holder.TrailingAnchor, -inset).Active = true;
        }

        return holder;
    }

    /// <summary>
    /// A banner as Windows draws one: the artwork square (Back above it when there is somewhere to
    /// go back to) beside the kicker, the title, the summary and the actions, set on the page.
    /// </summary>
    private static LibraryHero BuildLibraryHero(string glyph, bool round = false)
    {
        var art = new LibraryArtView(LibraryHeroArtworkSize,
            round ? (nfloat)(LibraryHeroArtworkSize / 2) : MacTheme.Radius(2), glyph);
        var aside = MacTheme.Stack(true, 2, art);
        aside.Alignment = NSLayoutAttribute.Leading;
        aside.DetachesHiddenViews = true;
        var kicker = MacTheme.Kicker("");
        var title = MacTheme.Label("", 2, DesignRole.TextPrimary);
        title.Font = NSFont.SystemFontOfSize(40, NSFontWeight.Semibold)!;
        title.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
        var summary = MacTheme.Label("", 2, DesignRole.TextSecondary);
        summary.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
        var actions = MacTheme.Stack(false, 1);
        actions.DetachesHiddenViews = true;
        var words = MacTheme.Stack(true, 1, kicker, title, summary, actions);
        words.SetCustomSpacing(MacTheme.Space(0), kicker);
        words.SetCustomSpacing(MacTheme.Space(2), summary);
        words.DetachesHiddenViews = true;
        var row = MacTheme.Stack(false, 4, aside, words);
        row.Alignment = NSLayoutAttribute.Bottom;
        row.EdgeInsets = new NSEdgeInsets(20, MacTheme.Space(4), 8, MacTheme.Space(4) + (nfloat)MacTheme.ControlSize);
        words.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
        return new LibraryHero(row, aside, art, kicker, title, summary, words, actions);
    }
}
