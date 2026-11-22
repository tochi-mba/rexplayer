using Rex.Media.AppCore.Design;

namespace Rex.Media.App.Mac;

/// <summary>
/// The parts every tool window is laid out with, as the Windows shell lays out its settings: a page
/// with a heading, cards on it, and rows in the cards with the words at the left and the control at
/// the right. Controls stay stock AppKit; only what surrounds them comes from <see cref="MacTheme"/>.
/// </summary>
internal static class MacToolViews
{
    /// <summary>The least height of a row, the size of a touch target as on Windows.</summary>
    private const double RowHeight = MacTheme.ControlSize;

    /// <summary>
    /// A tool window: titled, closable, kept when closed so it can be shown again, painted in the
    /// canvas colour and centred.
    /// </summary>
    public static NSWindow ToolWindow(string title, double width, double height, double minWidth = 0, double minHeight = 0,
        bool resizable = true, bool miniaturizable = false)
    {
        var style = NSWindowStyle.Titled | NSWindowStyle.Closable;
        if (resizable) style |= NSWindowStyle.Resizable;
        if (miniaturizable) style |= NSWindowStyle.Miniaturizable;
        var window = new NSWindow(new CGRect(0, 0, width, height), style, NSBackingStore.Buffered, false)
        {
            Title = title,
        };
        if (minWidth > 0 && minHeight > 0)
        {
            window.MinSize = new CGSize(minWidth, minHeight);
        }

        window.ReleaseWhenClosed(false);
        MacTheme.Style(window);
        return window;
    }

    /// <summary>
    /// A page: its title at the title size, an optional description in secondary text, then
    /// <paramref name="content"/> down the page at full width, padded by the window margin.
    /// </summary>
    public static NSStackView ToolPage(string title, string? description, params NSView[] content) =>
        ToolPage(ToolTitle(title), description, content);

    /// <summary>A page's title, for a page whose title changes: the media it is about.</summary>
    public static RexLabel ToolTitle(string title)
    {
        var heading = MacTheme.Label(title, 4, DesignRole.TextPrimary, semibold: true);
        heading.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
        return heading;
    }

    /// <summary>A page under <paramref name="heading"/>, made by <see cref="ToolTitle"/>.</summary>
    public static NSStackView ToolPage(RexLabel heading, string? description, params NSView[] content)
    {
        var views = new List<NSView> { heading };
        if (description is not null)
        {
            views.Add(MacTheme.WrappingLabel(description, 2, DesignRole.TextSecondary));
        }

        views.AddRange(content);
        var page = MacTheme.Stack(true, 3, [.. views]);
        page.EdgeInsets = new NSEdgeInsets(MacTheme.Space(4), MacTheme.Space(4), MacTheme.Space(4), MacTheme.Space(4));
        page.SetCustomSpacing(MacTheme.Space(1), heading);
        foreach (var view in views)
        {
            Stretch(page, view, MacTheme.Space(4) * 2);
        }

        return page;
    }

    /// <summary>
    /// Makes <paramref name="page"/> the window's content. A page that <paramref name="scrolls"/>
    /// keeps its natural height and scrolls in a window shorter than it; one that does not fills
    /// the window, and <paramref name="grows"/> takes whatever height is left over.
    /// </summary>
    public static void Host(NSWindow window, NSStackView page, bool scrolls, NSView? grows = null)
    {
        if (!scrolls)
        {
            page.Distribution = NSStackViewDistribution.Fill;
            if (grows is not null)
            {
                grows.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Vertical);
                grows.SetContentCompressionResistancePriority(1, NSLayoutConstraintOrientation.Vertical);
            }

            var root = new NSView(window.ContentView?.Frame ?? new CGRect(0, 0, 600, 480));
            MacTheme.Fill(root, page);
            window.ContentView = root;
            return;
        }

        var document = new MacToolDocument { TranslatesAutoresizingMaskIntoConstraints = false };
        MacTheme.Fill(document, page);
        var scroll = new NSScrollView(window.ContentView?.Frame ?? new CGRect(0, 0, 600, 480))
        {
            HasVerticalScroller = true,
            AutohidesScrollers = true,
            DrawsBackground = false,
            BorderType = NSBorderType.NoBorder,
            DocumentView = document,
        };
        var clip = scroll.ContentView;
        document.TopAnchor.ConstraintEqualTo(clip.TopAnchor).Active = true;
        document.LeadingAnchor.ConstraintEqualTo(clip.LeadingAnchor).Active = true;
        document.TrailingAnchor.ConstraintEqualTo(clip.TrailingAnchor).Active = true;
        window.ContentView = scroll;
    }

    /// <summary>A card: the surface colour, rounded, edged with a hairline, holding <paramref name="views"/> at full width.</summary>
    public static RexPanel ToolCard(string? heading, params NSView[] views)
    {
        var all = new List<NSView>();
        if (heading is not null)
        {
            all.Add(MacTheme.Label(heading, 3, DesignRole.TextPrimary, semibold: true));
        }

        all.AddRange(views);
        var card = MacTheme.Panel(DesignRole.Surface, 2, hairline: true);
        var inner = MacTheme.Stack(true, 1, [.. all]);
        MacTheme.Fill(card, inner, MacTheme.Space(3));
        foreach (var view in all)
        {
            Stretch(inner, view, 0);
        }

        return card;
    }

    /// <summary>
    /// A row: <paramref name="label"/> and an optional <paramref name="description"/> at the left,
    /// <paramref name="control"/> at the trailing edge. A single control takes the label as its
    /// accessible name, unless it already carries one of its own.
    /// </summary>
    public static NSStackView ToolRow(string label, string? description, NSView control)
    {
        var words = new List<NSView> { MacTheme.Label(label) };
        if (description is not null)
        {
            var detail = MacTheme.WrappingLabel(description, 1, DesignRole.TextSecondary);
            detail.PreferredMaxLayoutWidth = 300;
            words.Add(detail);
        }

        var text = MacTheme.Stack(true, 0, [.. words]);
        text.Spacing = 2;
        text.SetHuggingPriority(1, NSLayoutConstraintOrientation.Horizontal);
        text.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
        if (control is not NSStackView && control is not RexButton && string.IsNullOrWhiteSpace(control.AccessibilityLabel))
        {
            control.AccessibilityLabel = label;
        }

        control.SetContentHuggingPriorityForOrientation(750, NSLayoutConstraintOrientation.Horizontal);
        var row = MacTheme.Stack(false, 3, text, control);
        row.HeightAnchor.ConstraintGreaterThanOrEqualTo((nfloat)RowHeight).Active = true;
        return row;
    }

    /// <summary>A checkbox as a row of its own, with an optional description under it.</summary>
    public static NSView ToolCheck(NSButton checkbox, string? description = null)
    {
        checkbox.Font = MacTheme.Font(2);
        if (string.IsNullOrWhiteSpace(((NSView)checkbox).AccessibilityLabel))
        {
            ((NSView)checkbox).AccessibilityLabel = checkbox.Title;
        }

        NSView[] views = description is null
            ? [checkbox]
            : [checkbox, Indented(MacTheme.WrappingLabel(description, 1, DesignRole.TextSecondary))];
        var column = MacTheme.Stack(true, 0, views);
        column.Spacing = 2;
        var row = MacTheme.Stack(false, 0, column);
        row.HeightAnchor.ConstraintGreaterThanOrEqualTo((nfloat)RowHeight).Active = true;
        return row;
    }

    /// <summary>Label and value, read-only: the label in secondary text, the value in primary text and selectable.</summary>
    public static NSStackView ToolValue(string label, string value, bool monospaced = false)
    {
        var name = MacTheme.Label(label, 2, DesignRole.TextSecondary);
        name.WidthAnchor.ConstraintEqualTo(140).Active = true;
        name.SetContentHuggingPriorityForOrientation(750, NSLayoutConstraintOrientation.Horizontal);
        var shown = MacTheme.WrappingLabel(value, monospaced ? 1 : 2, DesignRole.TextPrimary);
        shown.Selectable = true;
        shown.PreferredMaxLayoutWidth = 380;
        if (monospaced)
        {
            shown.Font = MonoFont();
        }

        ((NSView)shown).AccessibilityLabel = label + ": " + value;
        var row = MacTheme.Stack(false, 3, name, shown);
        row.Alignment = NSLayoutAttribute.FirstBaseline;
        return row;
    }

    /// <summary>Buttons side by side, a step apart.</summary>
    public static NSStackView ToolButtons(params NSView[] buttons) => MacTheme.Stack(false, 1, buttons);

    /// <summary>
    /// A scrolling view on a surface card, for a list or text that fills the rest of a window: the
    /// log, the transcript's lines.
    /// </summary>
    public static RexPanel ToolWell(NSScrollView scroll)
    {
        scroll.DrawsBackground = false;
        scroll.BorderType = NSBorderType.NoBorder;
        var well = MacTheme.Panel(DesignRole.Surface, 2, hairline: true);
        MacTheme.Fill(well, scroll, MacTheme.Space(0));
        return well;
    }

    /// <summary>A segmented control over <paramref name="pages"/>, showing one at a time.</summary>
    public static MacToolTabs ToolTabs(string label, string[] titles, NSView[] pages) => new(label, titles, pages);

    /// <summary>Monospaced text at caption size: log lines and codec details.</summary>
    public static NSFont MonoFont() => NSFont.MonospacedSystemFont(MacTheme.TypeSize(1), NSFontWeight.Regular)!;

    /// <summary>A view inset at its leading edge by the width of a checkbox, so its text lines up with the checkbox's title.</summary>
    private static NSView Indented(NSView view)
    {
        var spacer = new NSView { TranslatesAutoresizingMaskIntoConstraints = false };
        spacer.WidthAnchor.ConstraintEqualTo(18).Active = true;
        return MacTheme.Stack(false, 0, spacer, view);
    }

    /// <summary>Holds <paramref name="child"/> at the width of <paramref name="stack"/>, less <paramref name="inset"/>.</summary>
    internal static void Stretch(NSStackView stack, NSView child, nfloat inset) =>
        child.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -inset).Active = true;
}

/// <summary>
/// Read-only text whose colours are theme roles: it tells its owner when the appearance changes,
/// so text coloured line by line (the log) is coloured again for the new theme.
/// </summary>
internal sealed class MacToolText : NSTextView
{
    public MacToolText(CGRect frame)
        : base(frame)
    {
        Editable = false;
        Selectable = true;
        DrawsBackground = false;
        Font = MacToolViews.MonoFont();
        TextContainerInset = new CGSize(MacTheme.Space(1), MacTheme.Space(1));
    }

    /// <summary>Runs after the text's appearance changes, light to dark or back.</summary>
    public Action? AppearanceChanged { get; set; }

    public override void ViewDidChangeEffectiveAppearance()
    {
        base.ViewDidChangeEffectiveAppearance();
        AppearanceChanged?.Invoke();
    }
}

/// <summary>A page that grows downward from the top of its scroll view, as text is read.</summary>
internal sealed class MacToolDocument : NSView
{
    public override bool IsFlipped => true;
}

/// <summary>
/// Tabs as the Windows tool panels have them: a segmented control and, under it, the page of the
/// chosen segment; the others are hidden and take no room.
/// </summary>
internal sealed class MacToolTabs : NSStackView
{
    private readonly NSView[] _pages;
    private readonly NSSegmentedControl _segments;

    public MacToolTabs(string label, string[] titles, NSView[] pages)
    {
        _pages = pages;
        _segments = NSSegmentedControl.FromLabels(titles, NSSegmentSwitchTracking.SelectOne, () => Show((int)_segments!.SelectedSegment));
        ((NSView)_segments).AccessibilityLabel = label;
        Orientation = NSUserInterfaceLayoutOrientation.Vertical;
        Alignment = NSLayoutAttribute.Leading;
        Spacing = MacTheme.Space(3);
        TranslatesAutoresizingMaskIntoConstraints = false;
        AddArrangedSubview(_segments);
        Show(0);
    }

    /// <summary>The segment chosen, which is the page shown.</summary>
    public int Selected => (int)_segments.SelectedSegment;

    /// <summary>
    /// Shows the page at <paramref name="index"/> in place of the one shown. The page is put in and
    /// taken out rather than hidden, since a page taken out of the stack loses the constraint that
    /// holds it at the stack's width and needs it made again.
    /// </summary>
    public void Show(int index)
    {
        index = Math.Clamp(index, 0, _pages.Length - 1);
        _segments.SelectedSegment = index;
        foreach (var page in _pages)
        {
            if (page.Superview == this)
            {
                RemoveView(page);
            }
        }

        AddArrangedSubview(_pages[index]);
        MacToolViews.Stretch(this, _pages[index], 0);
    }
}
