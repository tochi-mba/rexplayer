using CoreAnimation;
using Rex.Media.AppCore.Design;

namespace Rex.Media.App.Mac;

/// <summary>
/// The library's own views, drawn as the Windows library draws them: text buttons for titles and
/// links, the navigation column's items, artwork tiles with their placeholder glyph, the hover and
/// selection fill of a result line, and the page panel that hides the library on Escape.
/// </summary>
internal static class MacLibraryViews
{
    /// <summary>A link: a text button drawn in the accent colour that opens another page.</summary>
    public static RexTextButton Link(string title, string label, Action open, int typeStep = 2) =>
        new(title, label, open, null, MacTheme.Font(typeStep));

    /// <summary>A section heading inside a page: "Top songs", "More by …", a disc's name.</summary>
    public static RexLabel Heading(string text)
    {
        var heading = MacTheme.Label(text, 3, DesignRole.TextPrimary, semibold: true);
        heading.AccessibilityLabel = text;
        return heading;
    }

    /// <summary>A symbol image at a point size, or null where the system has no such symbol.</summary>
    public static NSImage? Symbol(string name, double size)
    {
        if (NSImage.GetSystemSymbol(name, null) is not { } image)
        {
            return null;
        }

        return image.GetImage(NSImageSymbolConfiguration.Create((nfloat)size, NSFontWeight.Regular, NSImageSymbolScale.Medium)) ?? image;
    }
}

/// <summary>
/// A borderless button whose title is its whole look: a result's title in primary text, or a link
/// in the accent colour. Return and Enter press it as well as Space, so a focused result plays.
/// </summary>
internal sealed class RexTextButton : NSButton
{
    private readonly Action _action;
    private readonly NSFont _font;
    private DesignRole? _role;
    private NSColor? _fixedColor;
    private string _title;

    /// <param name="title">The words shown.</param>
    /// <param name="label">The accessible name and tooltip.</param>
    /// <param name="action">What a press does.</param>
    /// <param name="role">The text's role; null draws it in the accent colour, as a link.</param>
    /// <param name="font">The type it is set in.</param>
    public RexTextButton(string title, string label, Action action, DesignRole? role, NSFont font)
    {
        _action = action;
        _font = font;
        _role = role;
        _title = title;
        Bordered = false;
        SetButtonType(NSButtonType.MomentaryChange);
        FocusRingType = NSFocusRingType.Exterior;
        TranslatesAutoresizingMaskIntoConstraints = false;
        Alignment = NSTextAlignment.Left;
        LineBreakMode = NSLineBreakMode.TruncatingTail;
        ToolTip = label;
        ((NSView)this).AccessibilityLabel = label;
        SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
        Activated += (_, _) => _action();
        Render();
    }

    /// <summary>A colour that is the same in every theme (the playing line's signal); null returns to the role.</summary>
    public NSColor? FixedColor
    {
        get => _fixedColor;
        set
        {
            _fixedColor = value;
            Render();
        }
    }

    /// <summary>The words shown.</summary>
    public string Text
    {
        get => _title;
        set
        {
            _title = value;
            Render();
            InvalidateIntrinsicContentSize();
        }
    }

    public override void ViewDidChangeEffectiveAppearance()
    {
        base.ViewDidChangeEffectiveAppearance();
        Render();
    }

    public override void KeyDown(NSEvent theEvent)
    {
        if (theEvent.Characters is "\r" or "\u0003")
        {
            PerformClick(this);
            return;
        }

        base.KeyDown(theEvent);
    }

    private void Render()
    {
        var ink = _fixedColor ?? (_role is { } role ? MacTheme.Role(role, EffectiveAppearance) : MacTheme.Accent);
        AttributedTitle = new NSAttributedString(_title, new NSStringAttributes
        {
            Font = _font,
            ForegroundColor = ink,
            ParagraphStyle = new NSMutableParagraphStyle { Alignment = NSTextAlignment.Left, LineBreakMode = NSLineBreakMode.TruncatingTail },
        });
        NeedsDisplay = true;
    }
}

/// <summary>
/// A view in the library's navigation column: its symbol and title, 44 points high, on the raised
/// fill with a small accent bar while it is the view shown. Compact, it shows the symbol alone,
/// centred, and keeps its title as its accessible name and tooltip.
/// </summary>
internal sealed class LibraryNavItem : NSButton
{
    private readonly Action _action;
    private readonly NSImageView _icon;
    private readonly RexLabel _name;
    private readonly RexPanel _pill;
    private readonly NSLayoutConstraint _iconLeading;
    private readonly NSLayoutConstraint _iconCentred;
    private bool _selected;
    private bool _hovered;
    private bool _compact;
    private NSTrackingArea? _tracking;

    public LibraryNavItem(string symbol, string title, string label, Action action, bool selectable = true)
    {
        _action = action;
        Selectable = selectable;
        Title = "";
        Bordered = false;
        WantsLayer = true;
        SetButtonType(NSButtonType.MomentaryChange);
        FocusRingType = NSFocusRingType.Exterior;
        TranslatesAutoresizingMaskIntoConstraints = false;
        ToolTip = label;
        ((NSView)this).AccessibilityLabel = label;
        Activated += (_, _) => _action();

        _icon = new NSImageView { Image = MacLibraryViews.Symbol(symbol, 16), TranslatesAutoresizingMaskIntoConstraints = false };
        _name = MacTheme.Label(title);
        _pill = MacTheme.Panel(MacTheme.Accent, 0);
        _pill.CornerRadius = (nfloat)1.5;
        _pill.Hidden = true;
        AddSubview(_icon);
        AddSubview(_name);
        AddSubview(_pill);
        HeightAnchor.ConstraintEqualTo((nfloat)MacTheme.ControlSize).Active = true;
        _icon.CenterYAnchor.ConstraintEqualTo(CenterYAnchor).Active = true;
        _icon.WidthAnchor.ConstraintEqualTo(20).Active = true;
        _iconLeading = _icon.LeadingAnchor.ConstraintEqualTo(LeadingAnchor, 14);
        _iconCentred = _icon.CenterXAnchor.ConstraintEqualTo(CenterXAnchor);
        _iconLeading.Active = true;
        _name.LeadingAnchor.ConstraintEqualTo(_icon.TrailingAnchor, MacTheme.Space(2)).Active = true;
        _name.TrailingAnchor.ConstraintLessThanOrEqualTo(TrailingAnchor, -MacTheme.Space(1)).Active = true;
        _name.CenterYAnchor.ConstraintEqualTo(CenterYAnchor).Active = true;
        _pill.LeadingAnchor.ConstraintEqualTo(LeadingAnchor).Active = true;
        _pill.CenterYAnchor.ConstraintEqualTo(CenterYAnchor).Active = true;
        _pill.WidthAnchor.ConstraintEqualTo(3).Active = true;
        _pill.HeightAnchor.ConstraintEqualTo(16).Active = true;
        Recolour();
    }

    /// <summary>Whether the item marks the view shown; Add folder never does.</summary>
    public bool Selectable { get; }

    /// <summary>Whether this is the view shown.</summary>
    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value && Selectable;
            _pill.Hidden = !_selected;
            ((NSView)this).AccessibilityValue = _selected ? new NSString("selected") : null;
            NeedsDisplay = true;
        }
    }

    /// <summary>Whether only the symbol shows, in the narrow icon rail.</summary>
    public bool Compact
    {
        get => _compact;
        set
        {
            _compact = value;
            _name.Hidden = value;
            _iconLeading.Active = !value;
            _iconCentred.Active = value;
        }
    }

    public override bool WantsUpdateLayer => true;

    public override NSView? HitTest(CGPoint aPoint) => base.HitTest(aPoint) is null ? null : this;

    public override void UpdateLayer()
    {
        if (Layer is not { } layer)
        {
            return;
        }

        var fill = _selected || _hovered ? MacTheme.Role(DesignRole.Raised, EffectiveAppearance) : NSColor.Clear;
        layer.BackgroundColor = fill.CGColor;
        layer.CornerRadius = MacTheme.Radius(0);
    }

    public override void UpdateTrackingAreas()
    {
        base.UpdateTrackingAreas();
        if (_tracking is not null)
        {
            RemoveTrackingArea(_tracking);
            _tracking.Dispose();
        }

        _tracking = new NSTrackingArea(Bounds, NSTrackingAreaOptions.MouseEnteredAndExited | NSTrackingAreaOptions.ActiveInKeyWindow, this, null);
        AddTrackingArea(_tracking);
    }

    public override void MouseEntered(NSEvent theEvent)
    {
        _hovered = true;
        NeedsDisplay = true;
    }

    public override void MouseExited(NSEvent theEvent)
    {
        _hovered = false;
        NeedsDisplay = true;
    }

    public override void ViewDidChangeEffectiveAppearance()
    {
        base.ViewDidChangeEffectiveAppearance();
        Recolour();
    }

    public override void KeyDown(NSEvent theEvent)
    {
        if (theEvent.Characters is "\r" or "\u0003")
        {
            PerformClick(this);
            return;
        }

        base.KeyDown(theEvent);
    }

    private void Recolour()
    {
        _icon.ContentTintColor = MacTheme.Role(DesignRole.TextPrimary, EffectiveAppearance);
        NeedsDisplay = true;
    }
}

/// <summary>
/// An artwork tile: a raised square with rounded corners and the kind's glyph in it, the picture
/// filling it (cropped, never stretched) once there is one.
/// </summary>
internal sealed class LibraryArtView : NSView
{
    private readonly NSImageView _glyph;
    private NSImage? _picture;

    public LibraryArtView(double size, nfloat cornerRadius, string glyph)
    {
        WantsLayer = true;
        TranslatesAutoresizingMaskIntoConstraints = false;
        CornerRadius = cornerRadius;
        _glyph = new NSImageView { TranslatesAutoresizingMaskIntoConstraints = false };
        AddSubview(_glyph);
        _glyph.CenterXAnchor.ConstraintEqualTo(CenterXAnchor).Active = true;
        _glyph.CenterYAnchor.ConstraintEqualTo(CenterYAnchor).Active = true;
        WidthAnchor.ConstraintEqualTo((nfloat)size).Active = true;
        HeightAnchor.ConstraintEqualTo((nfloat)size).Active = true;
        GlyphSize = Math.Max(14, size * 0.38);
        Glyph = glyph;
    }

    /// <summary>The corner radius; half the size makes the round tile of an artist.</summary>
    public nfloat CornerRadius { get; set; }

    private double GlyphSize { get; }

    /// <summary>The placeholder's SF Symbol, shown while there is no picture.</summary>
    public string Glyph
    {
        set
        {
            _glyph.Image = MacLibraryViews.Symbol(value, GlyphSize);
            Recolour();
        }
    }

    /// <summary>The picture, cropped to fill the tile; null shows the glyph.</summary>
    public NSImage? Picture
    {
        get => _picture;
        set
        {
            _picture = value;
            _glyph.Hidden = value is not null;
            NeedsDisplay = true;
        }
    }

    public override bool WantsUpdateLayer => true;

    public override void UpdateLayer()
    {
        if (Layer is not { } layer)
        {
            return;
        }

        layer.BackgroundColor = MacTheme.Role(DesignRole.Raised, EffectiveAppearance).CGColor;
        layer.CornerRadius = CornerRadius;
        layer.MasksToBounds = true;
        layer.ContentsGravity = CALayer.GravityResizeAspectFill;
        if (_picture is null)
        {
            layer.Contents = null;
        }
        else
        {
            layer.SetContents(_picture);
        }
    }

    public override void ViewDidChangeEffectiveAppearance()
    {
        base.ViewDidChangeEffectiveAppearance();
        Recolour();
    }

    private void Recolour()
    {
        _glyph.ContentTintColor = MacTheme.Role(DesignRole.TextSecondary, EffectiveAppearance);
        NeedsDisplay = true;
    }
}

/// <summary>
/// A line of results: it takes the raised fill while pointed at or chosen, chooses itself on a
/// click and plays on a double click, so the list reads and behaves as the Windows list does.
/// </summary>
internal sealed class LibraryResultRow : NSView
{
    private readonly Action? _open;
    private readonly Action<LibraryResultRow>? _choose;
    private bool _hovered;
    private bool _chosen;
    private NSTrackingArea? _tracking;

    /// <param name="open">What a double click does; null for a line that only shows.</param>
    /// <param name="choose">Told when a click chooses the line, so the list can let the others go.</param>
    public LibraryResultRow(Action? open, Action<LibraryResultRow>? choose)
    {
        _open = open;
        _choose = choose;
        WantsLayer = true;
        TranslatesAutoresizingMaskIntoConstraints = false;
    }

    /// <summary>Whether the line is the chosen one.</summary>
    public bool Chosen
    {
        get => _chosen;
        set
        {
            _chosen = value;
            NeedsDisplay = true;
        }
    }

    public override bool WantsUpdateLayer => true;

    public override void UpdateLayer()
    {
        if (Layer is not { } layer)
        {
            return;
        }

        var fill = _open is not null && (_hovered || _chosen) ? MacTheme.Role(DesignRole.Raised, EffectiveAppearance) : NSColor.Clear;
        layer.BackgroundColor = fill.CGColor;
        layer.CornerRadius = MacTheme.Radius(0);
    }

    public override void UpdateTrackingAreas()
    {
        base.UpdateTrackingAreas();
        if (_tracking is not null)
        {
            RemoveTrackingArea(_tracking);
            _tracking.Dispose();
        }

        _tracking = new NSTrackingArea(Bounds, NSTrackingAreaOptions.MouseEnteredAndExited | NSTrackingAreaOptions.ActiveInKeyWindow, this, null);
        AddTrackingArea(_tracking);
    }

    public override void MouseEntered(NSEvent theEvent)
    {
        _hovered = true;
        NeedsDisplay = true;
    }

    public override void MouseExited(NSEvent theEvent)
    {
        _hovered = false;
        NeedsDisplay = true;
    }

    public override void MouseDown(NSEvent theEvent)
    {
        if (_open is null)
        {
            base.MouseDown(theEvent);
            return;
        }

        _choose?.Invoke(this);
        if (theEvent.ClickCount == 2)
        {
            _open();
        }
    }
}

/// <summary>
/// The library pane's root: the canvas behind the navigation and the page, which hides the
/// library when Escape reaches it and says when its width changes so the navigation can narrow.
/// </summary>
internal sealed class LibraryPaneView : NSView
{
    private readonly Action _dismiss;
    private readonly Action<double> _resized;

    public LibraryPaneView(Action dismiss, Action<double> resized)
    {
        _dismiss = dismiss;
        _resized = resized;
        WantsLayer = true;
        TranslatesAutoresizingMaskIntoConstraints = false;
    }

    public override bool WantsUpdateLayer => true;

    public override void UpdateLayer()
    {
        if (Layer is { } layer)
        {
            layer.BackgroundColor = MacTheme.Role(DesignRole.Canvas, EffectiveAppearance).CGColor;
        }
    }

    public override void ViewDidChangeEffectiveAppearance()
    {
        base.ViewDidChangeEffectiveAppearance();
        NeedsDisplay = true;
    }

    public override void SetFrameSize(CGSize newSize)
    {
        var changed = newSize.Width != Frame.Width;
        base.SetFrameSize(newSize);
        if (changed)
        {
            _resized((double)newSize.Width);
        }
    }

    /// <summary>Escape, as AppKit sends it up the responder chain from a focused control.</summary>
    [Export("cancelOperation:")]
    public void CancelOperation(NSObject? sender)
    {
        // Whichever control sent it, Escape means the same: back to the player.
        _ = sender;
        _dismiss();
    }

    public override void KeyDown(NSEvent theEvent)
    {
        if (theEvent.KeyCode == 53)
        {
            _dismiss();
            return;
        }

        base.KeyDown(theEvent);
    }
}
