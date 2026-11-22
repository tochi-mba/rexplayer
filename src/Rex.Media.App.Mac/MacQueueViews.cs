using CoreAnimation;
using Rex.Media.AppCore.Design;
using Rex.Media.AppCore.Player;

namespace Rex.Media.App.Mac;

/// <summary>
/// What one line of Up Next shows, decided once per refresh so the table's cells only draw it:
/// the entry, its words, how long it plays, and whether it is the current or a missing one.
/// </summary>
internal sealed record QueueLine(PlaylistItem Item, string Title, string Detail, string Duration, bool Current, bool Missing, string? State, string Symbol)
{
    /// <summary>What a screen reader says for the line, as Windows says it: state, title, detail, "Missing".</summary>
    public string AccessibleName => string.Join(". ", new[] { State, Title, Detail, Missing ? "Missing" : null }
        .Where(part => !string.IsNullOrWhiteSpace(part)));
}

/// <summary>
/// The Up Next pane's own view: Escape anywhere inside it hides the pane, as the responder chain
/// carries the cancel message up from whichever control has the keyboard.
/// </summary>
internal sealed class QueuePaneView : NSView
{
    private readonly Action _cancel;

    public QueuePaneView(Action cancel)
    {
        _cancel = cancel;
        TranslatesAutoresizingMaskIntoConstraints = false;
    }

    /// <summary>The responder message Esc sends.</summary>
    [Export("cancelOperation:")]
    public void CancelFromKeyboard(NSObject? _) => _cancel();
}

/// <summary>
/// The Up Next list: Return plays the chosen line, Delete removes it, Option+Up/Down moves the
/// chosen lines, Escape hides the pane, and a right click acts on the line under the pointer.
/// </summary>
internal sealed class QueueTableView : NSTableView
{
    private const ushort UpArrow = 126;
    private const ushort DownArrow = 125;
    private const ushort Return = 36;
    private const ushort Enter = 76;
    private const ushort Delete = 51;
    private const ushort ForwardDelete = 117;
    private const ushort Escape = 53;

    /// <summary>Moves the chosen lines one place: -1 earlier, 1 later.</summary>
    public Action<int>? Move { get; set; }

    /// <summary>Plays the chosen line.</summary>
    public Action? Play { get; set; }

    /// <summary>Removes the chosen lines.</summary>
    public Action? Remove { get; set; }

    /// <summary>Hides the pane.</summary>
    public Action? Cancel { get; set; }

    /// <summary>The menu for the chosen lines, once the line clicked has been chosen.</summary>
    public Func<NSMenu?>? EntryMenu { get; set; }

    public override void KeyDown(NSEvent theEvent)
    {
        var option = (theEvent.ModifierFlags & NSEventModifierMask.AlternateKeyMask) != 0;
        var chosen = SelectedRowCount > 0;
        switch (theEvent.KeyCode)
        {
            case UpArrow when option && chosen:
                Move?.Invoke(-1);
                return;
            case DownArrow when option && chosen:
                Move?.Invoke(1);
                return;
            case Return or Enter when chosen:
                Play?.Invoke();
                return;
            case Delete or ForwardDelete when chosen:
                Remove?.Invoke();
                return;
            case Escape:
                Cancel?.Invoke();
                return;
            default:
                base.KeyDown(theEvent);
                return;
        }
    }

    public override NSMenu? MenuForEvent(NSEvent theEvent)
    {
        var row = GetRow(ConvertPointFromView(theEvent.LocationInWindow, null));
        if (row < 0)
        {
            return null;
        }

        // As on Windows, a right click on a line outside the selection acts on that line alone.
        if (!IsRowSelected(row))
        {
            SelectRow(row, false);
        }

        return EntryMenu?.Invoke();
    }
}

/// <summary>A line of the list, its selection filled in the raised colour rather than the system's highlight.</summary>
internal sealed class QueueRowView : NSTableRowView
{
    public override NSBackgroundStyle InteriorBackgroundStyle => NSBackgroundStyle.Normal;

    public override void DrawSelection(CGRect dirtyRect)
    {
        MacTheme.Role(DesignRole.Raised, EffectiveAppearance).SetFill();
        NSBezierPath.FromRoundedRect(Bounds.Inset(2, 1), MacTheme.Radius(0), MacTheme.Radius(0)).Fill();
    }
}

/// <summary>
/// Three bars that rise and fall while the line plays and rest while it is paused, as the
/// Windows playing line shows; they stay still when the system asks for reduced motion.
/// </summary>
internal sealed class QueuePlayingBars : NSView
{
    private static readonly double[] Periods = [0.45, 0.62, 0.53];
    private static readonly double[] Rest = [0.5, 0.9, 0.65];
    private readonly CALayer[] _bars = [new(), new(), new()];
    private NSColor _colour = MacTheme.Signal;
    private bool _animating;

    public QueuePlayingBars()
    {
        WantsLayer = true;
        TranslatesAutoresizingMaskIntoConstraints = false;
        WidthAnchor.ConstraintEqualTo(12).Active = true;
        HeightAnchor.ConstraintEqualTo(12).Active = true;
        foreach (var bar in _bars)
        {
            bar.AnchorPoint = new CGPoint(0.5, 0);
            Layer!.AddSublayer(bar);
        }

        ((NSView)this).AccessibilityElement = false;
    }

    /// <summary>The bars' colour; the accent is a system colour, so it is read in the bars' own appearance.</summary>
    public NSColor Colour
    {
        get => _colour;
        set
        {
            _colour = value;
            Recolour();
        }
    }

    /// <summary>Whether the bars move: while playing, not while paused.</summary>
    public bool Animating
    {
        get => _animating;
        set
        {
            if (_animating == value)
            {
                return;
            }

            _animating = value;
            Animate();
        }
    }

    public override void Layout()
    {
        base.Layout();
        var width = (Bounds.Width - 4) / 3;
        for (var i = 0; i < _bars.Length; i++)
        {
            _bars[i].Bounds = new CGRect(0, 0, width, Bounds.Height);
            _bars[i].Position = new CGPoint(i * (width + 2) + width / 2, 0);
            _bars[i].CornerRadius = 1;
        }
    }

    public override void ViewDidChangeEffectiveAppearance()
    {
        base.ViewDidChangeEffectiveAppearance();
        Recolour();
    }

    public override void ViewDidMoveToWindow()
    {
        base.ViewDidMoveToWindow();
        Animate();
    }

    private void Recolour() => EffectiveAppearance.PerformAsCurrentDrawingAppearance(() =>
    {
        foreach (var bar in _bars)
        {
            bar.BackgroundColor = _colour.CGColor;
        }
    });

    private void Animate()
    {
        var still = !_animating || Window is null || NSWorkspace.SharedWorkspace.AccessibilityDisplayShouldReduceMotion;
        for (var i = 0; i < _bars.Length; i++)
        {
            _bars[i].RemoveAllAnimations();
            // At rest the bars stand at three heights, so a paused line still reads as the current one.
            _bars[i].Transform = CATransform3D.MakeScale(1, still ? (nfloat)Rest[i] : 1, 1);
            if (still)
            {
                continue;
            }

            var bounce = CABasicAnimation.FromKeyPath("transform.scale.y");
            bounce.From = NSNumber.FromDouble(0.25);
            bounce.To = NSNumber.FromDouble(1);
            bounce.Duration = Periods[i];
            bounce.AutoReverses = true;
            bounce.RepeatCount = float.PositiveInfinity;
            bounce.TimingFunction = CAMediaTimingFunction.FromName(CAMediaTimingFunction.EaseInEaseOut);
            _bars[i].AddAnimation(bounce, "playing");
        }
    }
}

/// <summary>
/// One line of Up Next, laid out as the Windows row: the drag handle, the picture's place, the
/// title with its duration (or the missing-file warning), and the playing state with the detail.
/// </summary>
internal sealed class QueueCellView : NSTableCellView
{
    /// <summary>The identifier the table reuses these cells by.</summary>
    public const string Reuse = "QueueLine";

    private readonly NSImageView _handle;
    private readonly NSImageView _placeholder;
    private readonly RexPanel _art;
    private readonly RexLabel _title;
    private readonly RexLabel _duration;
    private readonly NSStackView _missing;
    private readonly NSImageView _warning;
    private readonly QueuePlayingBars _bars;
    private readonly RexLabel _state;
    private readonly RexLabel _detail;
    private bool _current;

    public QueueCellView()
    {
        Identifier = Reuse;
        _handle = Symbol("line.3.horizontal", 13, "Drag to reorder");
        _handle.ToolTip = "Drag to reorder";
        _placeholder = Symbol("music.note", 16, null);
        _art = MacTheme.Panel(DesignRole.Raised, 0);
        _art.WidthAnchor.ConstraintEqualTo(40).Active = true;
        _art.HeightAnchor.ConstraintEqualTo(40).Active = true;
        _art.AddSubview(_placeholder);
        _placeholder.CenterXAnchor.ConstraintEqualTo(_art.CenterXAnchor).Active = true;
        _placeholder.CenterYAnchor.ConstraintEqualTo(_art.CenterYAnchor).Active = true;
        _title = MacTheme.Label("", 2);
        _title.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
        _title.SetContentHuggingPriorityForOrientation(200, NSLayoutConstraintOrientation.Horizontal);
        _duration = MacTheme.Label("", 1, DesignRole.TextSecondary);
        _duration.Font = MacTheme.DigitFont(1);
        _duration.SetContentCompressionResistancePriority(760, NSLayoutConstraintOrientation.Horizontal);
        _duration.SetContentHuggingPriorityForOrientation(760, NSLayoutConstraintOrientation.Horizontal);
        _warning = Symbol("exclamationmark.triangle.fill", 11, null);
        _warning.ContentTintColor = MacTheme.Attention;
        var missingWord = MacTheme.Label("Missing", 1, MacTheme.Attention);
        _missing = MacTheme.Stack(false, 0, _warning, missingWord);
        _missing.SetContentCompressionResistancePriority(760, NSLayoutConstraintOrientation.Horizontal);
        _bars = new QueuePlayingBars();
        _state = MacTheme.Label("", 1);
        _state.SetContentCompressionResistancePriority(760, NSLayoutConstraintOrientation.Horizontal);
        _detail = MacTheme.Label("", 1, DesignRole.TextSecondary);
        _detail.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
        _detail.SetContentHuggingPriorityForOrientation(200, NSLayoutConstraintOrientation.Horizontal);

        var top = MacTheme.Stack(false, 1, _title, _duration, _missing);
        var bottom = MacTheme.Stack(false, 0, _bars, _state, _detail);
        bottom.Spacing = 6;
        var words = MacTheme.Stack(true, 0, top, bottom);
        words.Spacing = 2;
        top.WidthAnchor.ConstraintEqualTo(words.WidthAnchor).Active = true;
        bottom.WidthAnchor.ConstraintEqualTo(words.WidthAnchor).Active = true;
        var line = MacTheme.Stack(false, 1, _handle, _art, words);
        _handle.WidthAnchor.ConstraintEqualTo(16).Active = true;
        AddSubview(line);
        line.LeadingAnchor.ConstraintEqualTo(LeadingAnchor, 2).Active = true;
        line.TrailingAnchor.ConstraintEqualTo(TrailingAnchor, -8).Active = true;
        line.CenterYAnchor.ConstraintEqualTo(CenterYAnchor).Active = true;
        Recolour();
    }

    /// <summary>Shows <paramref name="line"/> in this cell.</summary>
    public void Show(QueueLine line)
    {
        _current = line.Current;
        _title.StringValue = line.Title;
        _duration.StringValue = line.Duration;
        _duration.Hidden = line.Missing || line.Duration.Length == 0;
        _missing.Hidden = !line.Missing;
        _detail.StringValue = line.Detail;
        _detail.Hidden = string.IsNullOrWhiteSpace(line.Detail);
        _state.StringValue = line.State ?? "";
        _state.Hidden = line.State is null;
        _bars.Hidden = !line.Current || line.State is null;
        _bars.Animating = line.State == "Playing";
        _art.AlphaValue = line.Missing ? 0.5f : 1f;
        if (NSImage.GetSystemSymbol(line.Symbol, null) is { } glyph)
        {
            _placeholder.Image = glyph.GetImage(NSImageSymbolConfiguration.Create(16, NSFontWeight.Regular, NSImageSymbolScale.Medium)) ?? glyph;
        }

        ToolTip = line.Missing ? "Missing file: " + line.Item.Location : line.Item.Location;
        ((NSView)this).AccessibilityLabel = line.AccessibleName;
        Recolour();
    }

    public override void ViewDidChangeEffectiveAppearance()
    {
        base.ViewDidChangeEffectiveAppearance();
        Recolour();
    }

    /// <summary>
    /// The playing line's title and state are in the signal colour on dark, and in the accent on
    /// light, where the signal is too pale to read against white.
    /// </summary>
    private void Recolour()
    {
        var playing = MacTheme.IsDark(EffectiveAppearance) ? MacTheme.Signal : MacTheme.Accent;
        if (_current)
        {
            _title.FixedColor = playing;
        }
        else
        {
            _title.Role = DesignRole.TextPrimary;
        }

        _state.FixedColor = playing;
        _bars.Colour = playing;
        _handle.ContentTintColor = MacTheme.Role(DesignRole.TextTertiary, EffectiveAppearance);
        _placeholder.ContentTintColor = MacTheme.Role(DesignRole.TextTertiary, EffectiveAppearance);
    }

    private static NSImageView Symbol(string name, double size, string? label)
    {
        var view = new NSImageView
        {
            TranslatesAutoresizingMaskIntoConstraints = false,
            ImageScaling = NSImageScale.ProportionallyDown,
        };
        if (NSImage.GetSystemSymbol(name, label) is { } image)
        {
            view.Image = image.GetImage(NSImageSymbolConfiguration.Create((nfloat)size, NSFontWeight.Regular, NSImageSymbolScale.Medium)) ?? image;
        }

        ((NSView)view).AccessibilityElement = false;
        return view;
    }
}
