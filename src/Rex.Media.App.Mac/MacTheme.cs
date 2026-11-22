using Rex.Media.AppCore.Design;

namespace Rex.Media.App.Mac;

/// <summary>How a <see cref="RexButton"/> is drawn. The names follow what the Windows shell uses in the same places.</summary>
internal enum RexButtonLook
{
    /// <summary>A square icon button on a surface: Previous, Stop, Full screen.</summary>
    Icon,

    /// <summary>An icon button that stays lit in the accent colour while <see cref="RexButton.On"/>: Shuffle, Library.</summary>
    IconToggle,

    /// <summary>The round accent icon button: Play.</summary>
    AccentRound,

    /// <summary>A text button filled with the accent colour: the primary action.</summary>
    Accent,

    /// <summary>A text button raised off its surface: every ordinary action.</summary>
    Standard,

    /// <summary>A text button on the stage, outlined so it shows on black.</summary>
    Overlay,
}

/// <summary>
/// The REX look in AppKit: <see cref="DesignTokens"/> and <see cref="DesignRoles"/> as colours, and
/// the builders every Mac window draws its panels, buttons and labels with, so the Mac shell looks
/// as the Windows shell does. Panels, labels and buttons made here take a <see cref="DesignRole"/>
/// and resolve it again whenever their appearance changes, so a window follows the theme while open.
/// </summary>
internal static class MacTheme
{
    /// <summary>The height and width of a transport button, as on Windows.</summary>
    public const double ControlSize = 44;

    /// <summary>The height of an ordinary text button.</summary>
    public const double ButtonHeight = 32;

    private static readonly List<IDisposable> WindowWatches = [];

    /// <summary>A token colour, 0xAARRGGBB, as an AppKit colour.</summary>
    public static NSColor Color(uint argb) => NSColor.FromSrgb(
        (nfloat)((byte)(argb >> 16) / 255.0), (nfloat)((byte)(argb >> 8) / 255.0), (nfloat)((byte)argb / 255.0), (nfloat)((byte)(argb >> 24) / 255.0));

    /// <summary>
    /// The colour of <paramref name="role"/> in the app's appearance as it is now. A colour made
    /// here does not follow a later change of theme: prefer the builders below, which take the
    /// role itself; use this inside drawing code, which runs again anyway.
    /// </summary>
    public static NSColor Role(DesignRole role) => Role(role, NSApplication.SharedApplication.EffectiveAppearance);

    /// <summary>The colour of <paramref name="role"/> in <paramref name="appearance"/>.</summary>
    public static NSColor Role(DesignRole role, NSAppearance? appearance) => Color(DesignRoles.Color(role, IsDark(appearance)));

    /// <summary>Whether <paramref name="appearance"/> is one of the dark ones.</summary>
    public static bool IsDark(NSAppearance? appearance) =>
        appearance?.FindBestMatch([NSAppearance.NameAqua.ToString(), NSAppearance.NameDarkAqua.ToString()]) == NSAppearance.NameDarkAqua.ToString();

    /// <summary>The primary action's colour: the system accent, as Windows uses its own.</summary>
    public static NSColor Accent => NSColor.ControlAccent;

    /// <summary>Text and icons drawn on <see cref="Accent"/>.</summary>
    public static NSColor OnAccent => NSColor.White;

    /// <summary>The REX signal: the playing entry and the seek bar's progress.</summary>
    public static NSColor Signal => Color(DesignTokens.Signal);

    /// <summary>Text and icons drawn on <see cref="Signal"/>.</summary>
    public static NSColor OnSignal => Color(DesignTokens.OnSignal);

    /// <summary>The tool accent, for on-picture tools.</summary>
    public static NSColor Tool => Color(DesignTokens.Tool);

    /// <summary>Attention and destructive actions.</summary>
    public static NSColor Attention => Color(DesignTokens.Attention);

    /// <summary>The stage behind the picture, black in every theme.</summary>
    public static NSColor Stage => Color(DesignRoles.Stage);

    /// <summary>A panel on the stage that nothing behind it may show through.</summary>
    public static NSColor StageSurface => Color(DesignRoles.StageSurface);

    /// <summary>A panel laid over the picture.</summary>
    public static NSColor Overlay => Color(DesignRoles.Overlay);

    /// <summary>A panel over the picture that holds controls.</summary>
    public static NSColor StrongOverlay => Color(DesignRoles.StrongOverlay);

    /// <summary>Text on the stage: primary, the same in every theme.</summary>
    public static NSColor OverlayText => Color(DesignRoles.OverlayText);

    /// <summary>Text on the stage: secondary.</summary>
    public static NSColor OverlaySecondaryText => Color(DesignRoles.OverlaySecondaryText);

    /// <summary>Text on the stage: tertiary.</summary>
    public static NSColor OverlayTertiaryText => Color(DesignRoles.OverlayTertiaryText);

    /// <summary>A faint fill over the picture.</summary>
    public static NSColor OverlayFill => Color(DesignTokens.OverlayFill);

    /// <summary>A line over the picture.</summary>
    public static NSColor OverlayHairline => Color(DesignTokens.OverlayHairline);

    /// <summary>A step of the spacing scale: 0 to 4 are 4, 8, 12, 16 and 24 points.</summary>
    public static nfloat Space(int step) => (nfloat)DesignTokens.Spacing[step];

    /// <summary>A corner radius: 0 chip, 1 small, 2 card, 3 page.</summary>
    public static nfloat Radius(int step) => (nfloat)DesignTokens.Radii[step];

    /// <summary>A type size: 0 micro, 1 caption, 2 body, 3 section, 4 title, 5 display.</summary>
    public static nfloat TypeSize(int step) => (nfloat)DesignTokens.TypeSizes[step];

    /// <summary>The system font at a step of the type scale.</summary>
    public static NSFont Font(int step, bool semibold = false) =>
        NSFont.SystemFontOfSize(TypeSize(step), semibold ? NSFontWeight.Semibold : NSFontWeight.Regular)!;

    /// <summary>Figures that keep their width as they change: times and counts.</summary>
    public static NSFont DigitFont(int step) => NSFont.MonospacedDigitSystemFontOfSize(TypeSize(step), NSFontWeight.Regular)!;

    /// <summary>Gives <paramref name="window"/> the canvas colour, title bar included, in whichever theme it is shown.</summary>
    public static void Style(NSWindow window)
    {
        window.TitlebarAppearsTransparent = true;
        window.BackgroundColor = Role(DesignRole.Canvas, window.EffectiveAppearance);
        WindowWatches.Add(window.AddObserver("effectiveAppearance", NSKeyValueObservingOptions.New, _ =>
            window.BackgroundColor = Role(DesignRole.Canvas, window.EffectiveAppearance)));
    }

    /// <summary>A panel filled with the colour of <paramref name="fill"/>, with rounded corners and, if asked, a hairline edge.</summary>
    public static RexPanel Panel(DesignRole fill, int radiusStep = -1, bool hairline = false) => new()
    {
        FillRole = fill,
        CornerRadius = radiusStep < 0 ? 0 : Radius(radiusStep),
        EdgeRole = hairline ? DesignRole.Hairline : null,
        TranslatesAutoresizingMaskIntoConstraints = false,
    };

    /// <summary>A panel filled with a colour that is the same in every theme: the stage, an overlay.</summary>
    public static RexPanel Panel(NSColor fill, int radiusStep = -1, NSColor? edge = null) => new()
    {
        Fill = fill,
        CornerRadius = radiusStep < 0 ? 0 : Radius(radiusStep),
        Edge = edge,
        TranslatesAutoresizingMaskIntoConstraints = false,
    };

    /// <summary>A one-point line in the hairline colour, across or down.</summary>
    public static RexPanel Separator(bool vertical = false)
    {
        var line = Panel(DesignRole.Hairline);
        if (vertical)
        {
            line.WidthAnchor.ConstraintEqualTo(1).Active = true;
        }
        else
        {
            line.HeightAnchor.ConstraintEqualTo(1).Active = true;
        }

        return line;
    }

    /// <summary>A line of text at a step of the type scale, in the colour of <paramref name="role"/>, whatever the theme becomes.</summary>
    public static RexLabel Label(string text, int typeStep = 2, DesignRole role = DesignRole.TextPrimary, bool semibold = false) =>
        new(text, wraps: false) { Font = Font(typeStep, semibold), Role = role };

    /// <summary>A line of text in a colour that is the same in every theme: text on the stage.</summary>
    public static RexLabel Label(string text, int typeStep, NSColor color, bool semibold = false) =>
        new(text, wraps: false) { Font = Font(typeStep, semibold), FixedColor = color };

    /// <summary>Text that wraps, at a step of the type scale, in the colour of <paramref name="role"/>.</summary>
    public static RexLabel WrappingLabel(string text, int typeStep = 2, DesignRole role = DesignRole.TextPrimary, bool semibold = false) =>
        new(text, wraps: true) { Font = Font(typeStep, semibold), Role = role };

    /// <summary>Text that wraps, in a colour that is the same in every theme.</summary>
    public static RexLabel WrappingLabel(string text, int typeStep, NSColor color, bool semibold = false) =>
        new(text, wraps: true) { Font = Font(typeStep, semibold), FixedColor = color };

    /// <summary>The small spaced capitals above a heading ("WELCOME", "LIBRARY"), in secondary text or <paramref name="color"/>.</summary>
    public static RexLabel Kicker(string text, NSColor? color = null)
    {
        var label = new RexLabel(text.ToUpperInvariant(), wraps: false)
        {
            Font = NSFont.SystemFontOfSize(TypeSize(0), NSFontWeight.Semibold)!,
            Tracking = TypeSize(0) * DesignTokens.KickerTracking / 1000,
        };
        if (color is null)
        {
            label.Role = DesignRole.TextSecondary;
        }
        else
        {
            label.FixedColor = color;
        }

        return label;
    }

    /// <summary>
    /// An icon button: an SF Symbol named <paramref name="symbol"/>, <paramref name="label"/> as its
    /// accessible name and tooltip, <paramref name="action"/> on click.
    /// </summary>
    public static RexButton IconButton(string symbol, string label, Action action, RexButtonLook look = RexButtonLook.Icon) =>
        new(look, symbol, null, label, action);

    /// <summary>A text button; <paramref name="symbol"/> adds a leading icon, <paramref name="label"/> an accessible name other than the title.</summary>
    public static RexButton TextButton(string title, Action action, RexButtonLook look = RexButtonLook.Standard, string? symbol = null, string? label = null) =>
        new(look, symbol, title, label ?? title, action);

    /// <summary>A stack of views, across or down, spaced by a step of the spacing scale.</summary>
    public static NSStackView Stack(bool vertical, int spaceStep, params NSView[] views)
    {
        var stack = NSStackView.FromViews(views);
        stack.Orientation = vertical ? NSUserInterfaceLayoutOrientation.Vertical : NSUserInterfaceLayoutOrientation.Horizontal;
        stack.Spacing = Space(spaceStep);
        stack.Alignment = vertical ? NSLayoutAttribute.Leading : NSLayoutAttribute.CenterY;
        stack.TranslatesAutoresizingMaskIntoConstraints = false;
        return stack;
    }

    /// <summary>A table drawn on its panel: no background of its own, no grid, no striped rows.</summary>
    public static void Style(NSTableView table, NSScrollView? scroll = null)
    {
        table.BackgroundColor = NSColor.Clear;
        table.GridStyleMask = NSTableViewGridStyle.None;
        table.UsesAlternatingRowBackgroundColors = false;
        if (scroll is not null)
        {
            scroll.DrawsBackground = false;
            scroll.BorderType = NSBorderType.NoBorder;
        }
    }

    /// <summary>Pins <paramref name="child"/> to every edge of <paramref name="host"/>, inset by <paramref name="inset"/>, adding it if it is not there.</summary>
    public static void Fill(NSView host, NSView child, double inset = 0)
    {
        child.TranslatesAutoresizingMaskIntoConstraints = false;
        if (child.Superview != host)
        {
            host.AddSubview(child);
        }

        child.TopAnchor.ConstraintEqualTo(host.TopAnchor, (nfloat)inset).Active = true;
        child.BottomAnchor.ConstraintEqualTo(host.BottomAnchor, (nfloat)(-inset)).Active = true;
        child.LeadingAnchor.ConstraintEqualTo(host.LeadingAnchor, (nfloat)inset).Active = true;
        child.TrailingAnchor.ConstraintEqualTo(host.TrailingAnchor, (nfloat)(-inset)).Active = true;
    }
}

/// <summary>
/// A view filled with a theme colour. The fill is resolved each time the appearance changes, which
/// a layer's own colour would not be: a CGColor is fixed when it is made.
/// </summary>
internal sealed class RexPanel : NSView
{
    private NSColor? _fill;
    private NSColor? _edge;
    private DesignRole? _fillRole;
    private DesignRole? _edgeRole;
    private nfloat _cornerRadius;

    public RexPanel()
    {
        WantsLayer = true;
    }

    /// <summary>A fill that is the same in every theme; none leaves the panel clear.</summary>
    public NSColor? Fill
    {
        get => _fill;
        set
        {
            _fill = value;
            NeedsDisplay = true;
        }
    }

    /// <summary>A one-point edge that is the same in every theme; none draws no edge.</summary>
    public NSColor? Edge
    {
        get => _edge;
        set
        {
            _edge = value;
            NeedsDisplay = true;
        }
    }

    /// <summary>The fill as a role, resolved for the panel's own appearance; it wins over <see cref="Fill"/>.</summary>
    public DesignRole? FillRole
    {
        get => _fillRole;
        set
        {
            _fillRole = value;
            NeedsDisplay = true;
        }
    }

    /// <summary>The edge as a role, resolved for the panel's own appearance; it wins over <see cref="Edge"/>.</summary>
    public DesignRole? EdgeRole
    {
        get => _edgeRole;
        set
        {
            _edgeRole = value;
            NeedsDisplay = true;
        }
    }

    public nfloat CornerRadius
    {
        get => _cornerRadius;
        set
        {
            _cornerRadius = value;
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

        var fill = _fillRole is { } fillRole ? MacTheme.Role(fillRole, EffectiveAppearance) : _fill;
        var edge = _edgeRole is { } edgeRole ? MacTheme.Role(edgeRole, EffectiveAppearance) : _edge;
        layer.BackgroundColor = (fill ?? NSColor.Clear).CGColor;
        layer.BorderColor = (edge ?? NSColor.Clear).CGColor;
        layer.BorderWidth = edge is null ? 0 : 1;
        layer.CornerRadius = _cornerRadius;
        layer.MasksToBounds = _cornerRadius > 0;
    }
}

/// <summary>
/// A label whose colour is a theme role: it is resolved again whenever the label's appearance
/// changes, so the text stays readable when the theme flips while the window is open.
/// </summary>
internal sealed class RexLabel : NSTextField
{
    private DesignRole? _role;
    private NSColor? _fixedColor;
    private nfloat _tracking;

    public RexLabel(string text, bool wraps)
    {
        Editable = false;
        Selectable = false;
        Bordered = false;
        DrawsBackground = false;
        TranslatesAutoresizingMaskIntoConstraints = false;
        if (wraps)
        {
            LineBreakMode = NSLineBreakMode.ByWordWrapping;
            UsesSingleLineMode = false;
            MaximumNumberOfLines = 0;
            SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
        }
        else
        {
            LineBreakMode = NSLineBreakMode.TruncatingTail;
            UsesSingleLineMode = true;
        }

        StringValue = text;
    }

    /// <summary>The role the text is drawn in; setting it clears <see cref="FixedColor"/>.</summary>
    public DesignRole? Role
    {
        get => _role;
        set
        {
            _role = value;
            _fixedColor = null;
            Recolour();
        }
    }

    /// <summary>A colour that is the same in every theme; setting it clears <see cref="Role"/>.</summary>
    public NSColor? FixedColor
    {
        get => _fixedColor;
        set
        {
            _fixedColor = value;
            _role = null;
            Recolour();
        }
    }

    /// <summary>Extra space between letters, in points: a kicker's spacing.</summary>
    public nfloat Tracking
    {
        get => _tracking;
        set
        {
            _tracking = value;
            Text = StringValue;
        }
    }

    /// <summary>The text shown, keeping the label's letter spacing, which plain StringValue would drop.</summary>
    public string Text
    {
        get => StringValue;
        set
        {
            if (_tracking == 0)
            {
                StringValue = value;
                return;
            }

            AttributedStringValue = new NSAttributedString(value, new NSStringAttributes
            {
                Font = Font,
                ForegroundColor = TextColor,
                KerningAdjustment = (float)_tracking,
            });
        }
    }

    public override void ViewDidChangeEffectiveAppearance()
    {
        base.ViewDidChangeEffectiveAppearance();
        Recolour();
    }

    private void Recolour()
    {
        if (_role is { } role)
        {
            TextColor = MacTheme.Role(role, EffectiveAppearance);
        }
        else if (_fixedColor is not null)
        {
            TextColor = _fixedColor;
        }

        if (_tracking != 0)
        {
            Text = StringValue;
        }
    }
}

/// <summary>
/// The Mac shell's button: borderless, filled and tinted from the theme, with an SF Symbol, a
/// title or both. It answers clicks, Space and Return as any button does, and keeps the accessible
/// name it was made with.
/// </summary>
internal sealed class RexButton : NSButton
{
    private readonly RexButtonLook _look;
    private readonly Action _action;
    private string? _symbol;
    private string? _title;
    private bool _on;
    private bool _hovered;
    private NSTrackingArea? _tracking;

    public RexButton(RexButtonLook look, string? symbol, string? title, string label, Action action)
    {
        _look = look;
        _action = action;
        _symbol = symbol;
        _title = title;
        Bordered = false;
        WantsLayer = true;
        SetButtonType(NSButtonType.MomentaryChange);
        FocusRingType = NSFocusRingType.Exterior;
        TranslatesAutoresizingMaskIntoConstraints = false;
        ToolTip = label;
        ((NSView)this).AccessibilityLabel = label;
        Activated += (_, _) => _action();
        if (IsIcon)
        {
            WidthAnchor.ConstraintEqualTo((nfloat)MacTheme.ControlSize).Active = true;
            HeightAnchor.ConstraintEqualTo((nfloat)MacTheme.ControlSize).Active = true;
        }
        else
        {
            HeightAnchor.ConstraintGreaterThanOrEqualTo((nfloat)MacTheme.ButtonHeight).Active = true;
        }

        Render();
    }

    /// <summary>Whether a toggle is lit: shuffle on, the library showing.</summary>
    public bool On
    {
        get => _on;
        set
        {
            if (_on != value)
            {
                _on = value;
                Render();
            }
        }
    }

    /// <summary>The SF Symbol shown; changing it redraws (Play becomes Pause).</summary>
    public string? Symbol
    {
        get => _symbol;
        set
        {
            if (_symbol != value)
            {
                _symbol = value;
                Render();
            }
        }
    }

    /// <summary>The title shown; changing it redraws.</summary>
    public string? Text
    {
        get => _title;
        set
        {
            if (_title != value)
            {
                _title = value;
                Render();
                InvalidateIntrinsicContentSize();
            }
        }
    }

    /// <summary>The accessible name and tooltip.</summary>
    public string Label
    {
        get => ToolTip ?? "";
        set
        {
            ToolTip = value;
            ((NSView)this).AccessibilityLabel = value;
        }
    }

    private bool IsIcon => _look is RexButtonLook.Icon or RexButtonLook.IconToggle or RexButtonLook.AccentRound;

    private bool Filled => _look is RexButtonLook.Accent or RexButtonLook.AccentRound || (_look == RexButtonLook.IconToggle && _on);

    public override bool WantsUpdateLayer => true;

    public override CGSize IntrinsicContentSize
    {
        get
        {
            if (IsIcon)
            {
                return new CGSize(MacTheme.ControlSize, MacTheme.ControlSize);
            }

            var size = base.IntrinsicContentSize;
            return new CGSize(Math.Max((double)size.Width + 28, 72), Math.Max((double)size.Height + 12, MacTheme.ButtonHeight));
        }
    }

    public override void UpdateLayer()
    {
        if (Layer is not { } layer)
        {
            return;
        }

        var fill = _look switch
        {
            RexButtonLook.Accent or RexButtonLook.AccentRound => MacTheme.Accent,
            RexButtonLook.IconToggle when _on => MacTheme.Accent,
            RexButtonLook.Overlay => MacTheme.OverlayFill,
            RexButtonLook.Standard => MacTheme.Role(DesignRole.Raised, EffectiveAppearance),
            _ => _hovered ? MacTheme.Role(DesignRole.Raised, EffectiveAppearance) : NSColor.Clear,
        };
        var edge = _look == RexButtonLook.Overlay
            ? (_hovered ? MacTheme.OverlaySecondaryText : MacTheme.OverlayTertiaryText)
            : NSColor.Clear;

        // The accent is a system colour that depends on the appearance it is read in.
        EffectiveAppearance.PerformAsCurrentDrawingAppearance(() =>
        {
            layer.BackgroundColor = fill.CGColor;
            layer.BorderColor = edge.CGColor;
        });
        layer.BorderWidth = _look == RexButtonLook.Overlay ? 1 : 0;
        layer.CornerRadius = _look == RexButtonLook.AccentRound ? (nfloat)(MacTheme.ControlSize / 2) : MacTheme.Radius(0);
        layer.Opacity = !Enabled ? 0.4f : _hovered && Filled ? 0.9f : 1f;
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

    public override void ViewDidChangeEffectiveAppearance()
    {
        base.ViewDidChangeEffectiveAppearance();
        Render();
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

    private void Render()
    {
        var ink = _look switch
        {
            RexButtonLook.Accent or RexButtonLook.AccentRound => MacTheme.OnAccent,
            RexButtonLook.IconToggle when _on => MacTheme.OnAccent,
            RexButtonLook.Overlay => MacTheme.OverlayText,
            _ => MacTheme.Role(DesignRole.TextPrimary, EffectiveAppearance),
        };
        ContentTintColor = ink;
        if (_symbol is not null && NSImage.GetSystemSymbol(_symbol, null) is { } image)
        {
            Image = image.GetImage(NSImageSymbolConfiguration.Create(IsIcon ? 16 : 13, NSFontWeight.Medium, NSImageSymbolScale.Medium)) ?? image;
            ImagePosition = _title is null ? NSCellImagePosition.ImageOnly : NSCellImagePosition.ImageLeading;
        }
        else
        {
            Image = null;
            ImagePosition = NSCellImagePosition.NoImage;
        }

        AttributedTitle = new NSAttributedString(_title ?? "", new NSStringAttributes
        {
            Font = MacTheme.Font(2, _look == RexButtonLook.Accent),
            ForegroundColor = ink,
            ParagraphStyle = new NSMutableParagraphStyle { Alignment = NSTextAlignment.Center },
        });
        NeedsDisplay = true;
    }
}
