using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Design;

namespace Rex.Media.App.Mac;

/// <summary>
/// The parts every Mac settings window is built from, laid out as the Windows settings pages are:
/// a page with its title at display size, cards on the surface colour, and rows that give the
/// setting's name and what it does at the left with its control at the right. The controls stay
/// stock AppKit, which follow light and dark by themselves; the text and fills around them are
/// theme roles, resolved again whenever the theme changes.
/// </summary>
internal static class MacSettings
{
    /// <summary>The least height of a row, so a row of a checkbox lines up with a row of a pop-up.</summary>
    public const double RowMinHeight = 48;

    /// <summary>A card: the surface colour with a hairline edge, an optional heading, and rows parted by hairlines.</summary>
    public static RexPanel Card(string? heading, params NSView[] rows)
    {
        var card = MacTheme.Panel(DesignRole.Surface, 2, hairline: true);
        var stack = MacTheme.Stack(true, 0);
        stack.Alignment = NSLayoutAttribute.Leading;
        if (heading is not null)
        {
            var title = MacTheme.Label(heading, 3, semibold: true);
            ((NSView)title).AccessibilityRole = NSAccessibilityRoles.StaticTextRole;
            stack.AddArrangedSubview(title);
            stack.SetCustomSpacing(MacTheme.Space(1), title);
            title.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor).Active = true;
        }

        for (var i = 0; i < rows.Length; i++)
        {
            if (i > 0)
            {
                var line = MacTheme.Separator();
                stack.AddArrangedSubview(line);
                line.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor).Active = true;
            }

            stack.AddArrangedSubview(rows[i]);
            rows[i].WidthAnchor.ConstraintEqualTo(stack.WidthAnchor).Active = true;
        }

        card.AddSubview(stack);
        var inset = MacTheme.Space(3);
        stack.TopAnchor.ConstraintEqualTo(card.TopAnchor, heading is null ? MacTheme.Space(0) : inset).Active = true;
        stack.BottomAnchor.ConstraintEqualTo(card.BottomAnchor, -MacTheme.Space(0)).Active = true;
        stack.LeadingAnchor.ConstraintEqualTo(card.LeadingAnchor, inset).Active = true;
        stack.TrailingAnchor.ConstraintEqualTo(card.TrailingAnchor, -inset).Active = true;
        return card;
    }

    /// <summary>
    /// A row: <paramref name="label"/> in primary text with <paramref name="description"/> under it
    /// in secondary caption text, wrapping, and <paramref name="control"/> at the trailing edge. The
    /// label is the control's accessible name and the description its help; clicking the label of
    /// a checkbox or a switch flips it, as clicking a checkbox's own title would.
    /// </summary>
    public static NSView Row(string label, string? description, NSView? control)
    {
        var row = new NSView { TranslatesAutoresizingMaskIntoConstraints = false };
        var title = MacTheme.WrappingLabel(label, 2);
        var texts = MacTheme.Stack(true, 0, title);
        texts.Spacing = 2;
        title.WidthAnchor.ConstraintEqualTo(texts.WidthAnchor).Active = true;
        if (description is not null)
        {
            var note = MacTheme.WrappingLabel(description, 1, DesignRole.TextSecondary);
            texts.AddArrangedSubview(note);
            note.WidthAnchor.ConstraintEqualTo(texts.WidthAnchor).Active = true;
        }

        texts.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
        row.AddSubview(texts);
        var gap = MacTheme.Space(1);
        texts.LeadingAnchor.ConstraintEqualTo(row.LeadingAnchor).Active = true;
        texts.CenterYAnchor.ConstraintEqualTo(row.CenterYAnchor).Active = true;
        texts.TopAnchor.ConstraintGreaterThanOrEqualTo(row.TopAnchor, gap).Active = true;
        texts.BottomAnchor.ConstraintLessThanOrEqualTo(row.BottomAnchor, -gap).Active = true;
        row.HeightAnchor.ConstraintGreaterThanOrEqualTo((nfloat)RowMinHeight).Active = true;
        if (control is null)
        {
            texts.TrailingAnchor.ConstraintEqualTo(row.TrailingAnchor).Active = true;
            return row;
        }

        control.TranslatesAutoresizingMaskIntoConstraints = false;
        control.SetContentCompressionResistancePriority(750, NSLayoutConstraintOrientation.Horizontal);
        control.SetContentHuggingPriorityForOrientation(750, NSLayoutConstraintOrientation.Horizontal);
        row.AddSubview(control);
        control.TrailingAnchor.ConstraintEqualTo(row.TrailingAnchor).Active = true;
        control.CenterYAnchor.ConstraintEqualTo(row.CenterYAnchor).Active = true;
        control.TopAnchor.ConstraintGreaterThanOrEqualTo(row.TopAnchor, gap).Active = true;
        control.BottomAnchor.ConstraintLessThanOrEqualTo(row.BottomAnchor, -gap).Active = true;
        texts.TrailingAnchor.ConstraintEqualTo(control.LeadingAnchor, -MacTheme.Space(3)).Active = true;
        // A slider with its value beside it is named on the slider; a REX button keeps its own name.
        var named = control is NSStackView group
            ? group.ArrangedSubviews.FirstOrDefault(view => view is NSControl and not RexLabel)
            : control;
        if (named is not null and not RexButton)
        {
            named.AccessibilityLabel = label;
            if (description is not null)
            {
                named.AccessibilityHelp = description;
            }
        }

        if (control is NSButton or NSSwitch)
        {
            texts.AddGestureRecognizer(new NSClickGestureRecognizer(() => Flip(control)));
        }

        return row;
    }

    /// <summary>A checkbox with no title of its own: its row's label names it.</summary>
    public static NSButton Check(bool on)
    {
        var check = NSButton.CreateCheckbox("", () => { });
        check.State = on ? NSCellStateValue.On : NSCellStateValue.Off;
        return check;
    }

    /// <summary>A switch, where Windows uses a toggle switch.</summary>
    public static NSSwitch Switch(bool on) => new() { State = on ? 1 : 0 };

    /// <summary>Whether <paramref name="control"/> is switched on.</summary>
    public static bool IsOn(NSSwitch? control) => control?.State == 1;

    /// <summary>A pop-up of <paramref name="titles"/> with <paramref name="selected"/> chosen.</summary>
    public static NSPopUpButton Choices(string[] titles, int selected = 0)
    {
        var popup = new NSPopUpButton(new CGRect(0, 0, 220, 28), false);
        foreach (var title in titles)
        {
            popup.AddItem(title);
        }

        popup.SelectItem(Math.Clamp(selected, 0, Math.Max(0, titles.Length - 1)));
        popup.WidthAnchor.ConstraintGreaterThanOrEqualTo(180).Active = true;
        return popup;
    }

    /// <summary>A slider with its value said beside it, as one control for a row.</summary>
    public static NSStackView SliderWithValue(NSSlider slider, NSTextField value, double width = 200)
    {
        slider.WidthAnchor.ConstraintEqualTo((nfloat)width).Active = true;
        value.WidthAnchor.ConstraintEqualTo(64).Active = true;
        value.Alignment = NSTextAlignment.Right;
        return MacTheme.Stack(false, 1, slider, value);
    }

    /// <summary>The value beside a slider, in figures that keep their width.</summary>
    public static RexLabel ValueLabel()
    {
        var label = MacTheme.Label("", 1, DesignRole.TextSecondary);
        label.Font = MacTheme.DigitFont(1);
        return label;
    }

    /// <summary>A row of buttons, leading first.</summary>
    public static NSStackView Buttons(params NSView[] buttons) => MacTheme.Stack(false, 1, buttons);

    /// <summary>
    /// A scroll view for a settings page that never scrolls sideways; <see cref="Show"/> puts a
    /// page in it.
    /// </summary>
    public static NSScrollView Scroller()
    {
        var scroll = new NSScrollView
        {
            HasVerticalScroller = true,
            HasHorizontalScroller = false,
            AutohidesScrollers = true,
            DrawsBackground = false,
            BorderType = NSBorderType.NoBorder,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        scroll.ContentView.DrawsBackground = false;
        return scroll;
    }

    /// <summary>Shows <paramref name="page"/> in <paramref name="scroll"/>, as wide as the scroll view, from its top.</summary>
    public static void Show(NSScrollView scroll, NSView page)
    {
        page.TranslatesAutoresizingMaskIntoConstraints = false;
        scroll.DocumentView = page;
        var clip = scroll.ContentView;
        page.TopAnchor.ConstraintEqualTo(clip.TopAnchor).Active = true;
        page.LeadingAnchor.ConstraintEqualTo(clip.LeadingAnchor).Active = true;
        page.TrailingAnchor.ConstraintEqualTo(clip.TrailingAnchor).Active = true;
        scroll.LayoutSubtreeIfNeeded();
        clip.ScrollToPoint(CGPoint.Empty);
        scroll.ReflectScrolledClipView(clip);
    }

    /// <summary>
    /// One shortcut as key caps, a key each, drawn on the raised colour. The line the caps sit on
    /// says the keys to a screen reader, so the caps themselves are only drawn.
    /// </summary>
    public static NSStackView KeyCaps(KeyChord chord)
    {
        var caps = MacTheme.Stack(false, 0);
        foreach (var key in KeyNames.Keycaps(chord, ShortcutStyle.Mac))
        {
            caps.AddArrangedSubview(KeyCap(key));
        }

        caps.AccessibilityElement = false;
        return caps;
    }

    /// <summary>A single key cap.</summary>
    public static RexPanel KeyCap(string key)
    {
        var cap = MacTheme.Panel(DesignRole.Raised, 0, hairline: true);
        var text = MacTheme.Label(key, 1);
        text.Font = MacTheme.DigitFont(1);
        text.Alignment = NSTextAlignment.Center;
        text.AccessibilityElement = false;
        cap.AddSubview(text);
        text.LeadingAnchor.ConstraintEqualTo(cap.LeadingAnchor, 6).Active = true;
        text.TrailingAnchor.ConstraintEqualTo(cap.TrailingAnchor, -6).Active = true;
        text.TopAnchor.ConstraintEqualTo(cap.TopAnchor, 2).Active = true;
        text.BottomAnchor.ConstraintEqualTo(cap.BottomAnchor, -2).Active = true;
        cap.WidthAnchor.ConstraintGreaterThanOrEqualTo(24).Active = true;
        cap.AccessibilityElement = false;
        return cap;
    }

    private static void Flip(NSView control)
    {
        switch (control)
        {
            case NSButton { Enabled: true } button:
                button.PerformClick(button);
                break;
            case NSSwitch { Enabled: true } toggle:
                toggle.State = toggle.State == 1 ? 0 : 1;
                break;
        }
    }
}

/// <summary>
/// A settings page: its title at display size, an optional description in secondary text, then
/// its cards a group apart. It is drawn top down, so a scroll view holding it starts at its title.
/// </summary>
internal sealed class SettingsPage : NSView
{
    private readonly NSStackView _stack;

    public SettingsPage(string title, string? description = null)
    {
        TranslatesAutoresizingMaskIntoConstraints = false;
        Title = MacTheme.Label(title, 5, semibold: true);
        _stack = MacTheme.Stack(true, 3, Title);
        _stack.SetCustomSpacing(MacTheme.Space(1), Title);
        if (description is not null)
        {
            DescriptionLabel = MacTheme.WrappingLabel(description, 2, DesignRole.TextSecondary);
            Add(DescriptionLabel);
        }

        MacTheme.Fill(this, _stack, (double)MacTheme.Space(4));
    }

    /// <summary>The page's title.</summary>
    public RexLabel Title { get; }

    /// <summary>The words under the title, when the page has them.</summary>
    public RexLabel? DescriptionLabel { get; }

    public override bool IsFlipped => true;

    /// <summary>Adds views as wide as the page: cards, notes.</summary>
    public void Add(params NSView[] views)
    {
        foreach (var view in views)
        {
            _stack.AddArrangedSubview(view);
            view.WidthAnchor.ConstraintEqualTo(_stack.WidthAnchor).Active = true;
        }
    }

    /// <summary>Adds a view at its own width, at the leading edge: a row of buttons.</summary>
    public void AddFitted(NSView view) => _stack.AddArrangedSubview(view);

    /// <summary>Puts small spaced capitals above the title, such as the version a page is about.</summary>
    public RexLabel AddKicker(string text)
    {
        var kicker = MacTheme.Kicker(text);
        _stack.InsertArrangedSubview(kicker, 0);
        _stack.SetCustomSpacing(MacTheme.Space(0), kicker);
        return kicker;
    }
}

/// <summary>
/// Read-only text that can be selected and scrolled, such as release notes, in primary text at
/// body size; its colour is the theme's, resolved again when the theme changes.
/// </summary>
internal sealed class RexTextView : NSTextView
{
    public RexTextView()
        : base(new CGRect(0, 0, 400, 200))
    {
        Editable = false;
        Selectable = true;
        DrawsBackground = false;
        Font = MacTheme.Font(2);
        TextContainerInset = new CGSize(0, MacTheme.Space(1));
        VerticallyResizable = true;
        HorizontallyResizable = false;
        AutoresizingMask = NSViewResizingMask.WidthSizable;
        MaxSize = new CGSize(double.MaxValue, double.MaxValue);
        if (TextContainer is { } container)
        {
            container.WidthTracksTextView = true;
        }

        Recolour();
    }

    /// <summary>The text shown, in the theme's colour.</summary>
    public string Text
    {
        get => Value;
        set
        {
            Value = value;
            Recolour();
        }
    }

    public override void ViewDidChangeEffectiveAppearance()
    {
        base.ViewDidChangeEffectiveAppearance();
        Recolour();
    }

    /// <summary>A scroll view that holds this text at the height given, with no border or fill of its own.</summary>
    public NSScrollView InScroller(double height)
    {
        var scroll = new NSScrollView
        {
            HasVerticalScroller = true,
            AutohidesScrollers = true,
            DrawsBackground = false,
            BorderType = NSBorderType.NoBorder,
            DocumentView = this,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        scroll.HeightAnchor.ConstraintEqualTo((nfloat)height).Active = true;
        return scroll;
    }

    private void Recolour() => TextColor = MacTheme.Role(DesignRole.TextPrimary, EffectiveAppearance);
}
