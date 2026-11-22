using System.Globalization;
using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Design;
using Rex.Media.Settings;

namespace Rex.Media.App.Mac;

/// <summary>
/// Editable Mac subtitle appearance, shared through PlayerSettings with other shells. Laid out as
/// a settings page in the Windows Subtitles section's words: a preview on the stage that shows the
/// style as it is chosen, then cards for the text, its outline and shadow, the box behind it and
/// where it sits. The colours chosen here are the subtitles' own, not the theme's.
/// </summary>
public sealed partial class MainWindow
{
    private AutomationReply SubtitleStyleSmokeAction(string? action)
    {
        ShowSubtitleStyle();
        if (action == "configure")
        {
            _styleFont!.StringValue = "Helvetica Neue";
            _styleColour!.StringValue = "F0E0D0";
            _styleSize!.DoubleValue = 150;
            _styleOpacity!.DoubleValue = 80;
            _styleMargin!.DoubleValue = 12;
            _styleBold!.State = NSCellStateValue.On;
            _styleInBars!.State = NSCellStateValue.On;
            _styleForceBottom!.State = NSCellStateValue.Off;
            _styleOutline!.SelectItem((int)OutlineChoice.Thick);
            _styleOutlineColour!.StringValue = "102030";
            _styleShadowColour!.StringValue = "203040";
            _styleShadowOpacity!.DoubleValue = 55;
            _styleShadowOffset!.DoubleValue = 4;
            _styleBoxColour!.StringValue = "304050";
            _styleBoxOpacity!.DoubleValue = 35;
            ApplySubtitleStyle();
        }
        else if (action is not null and not "status")
        {
            return AutomationReply.Failed("Unknown Mac subtitle-style action: " + action);
        }

        return new AutomationReply
        {
            Ok = true,
            Text = $"font={_settings.SubtitleFont};size={_settings.SubtitleSize};" +
                $"colour={_settings.SubtitleColor:X6};opacity={_settings.SubtitleOpacity};" +
                $"bold={_settings.SubtitleBold};outline={_settings.SubtitleOutline};" +
                $"outlineColour={_settings.SubtitleOutlineColor:X6};" +
                $"shadow={_settings.SubtitleShadowColor:X6}/{_settings.SubtitleShadowOpacity}/{_settings.SubtitleShadowOffset};" +
                $"box={_settings.SubtitleBoxColor:X6}/{_settings.SubtitleBoxOpacity};" +
                $"margin={_settings.SubtitleMargin};bars={_settings.SubtitlesInBars};" +
                $"forceBottom={_settings.SubtitlesAtBottom}",
        };
    }

    /// <summary>The height of the preview stage.</summary>
    private const double SubtitlePreviewHeight = 160;

    private NSWindow? _subtitleStyleWindow;
    private NSComboBox? _styleFont;
    private NSTextField? _styleColour;
    private NSSlider? _styleSize;
    private NSSlider? _styleOpacity;
    private NSSlider? _styleMargin;
    private NSButton? _styleBold;
    private NSButton? _styleInBars;
    private NSButton? _styleForceBottom;
    private NSPopUpButton? _styleOutline;
    private NSPopUpButton? _stylePolicy;
    private RexLabel? _styleNotice;
    private NSTextField? _styleOutlineColour;
    private NSTextField? _styleShadowColour;
    private NSTextField? _styleBoxColour;
    private NSSlider? _styleShadowOpacity;
    private NSSlider? _styleShadowOffset;
    private NSSlider? _styleBoxOpacity;
    private RexPanel? _stylePreviewBox;
    private NSTextField? _stylePreviewText;
    private NSLayoutConstraint? _stylePreviewGap;
    private readonly List<(NSSlider Slider, NSTextField Value, string Unit)> _styleValues = [];

    private void ShowSubtitleStyle()
    {
        if (_subtitleStyleWindow is null)
        {
            _subtitleStyleWindow = new NSWindow(new CGRect(0, 0, 640, 720),
                NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable,
                NSBackingStore.Buffered, false)
            {
                Title = "Subtitle appearance - rexplayer",
                MinSize = new CGSize(560, 480),
                AutorecalculatesKeyViewLoop = true,
            };
            _subtitleStyleWindow.ReleaseWhenClosed(false);
            MacTheme.Style(_subtitleStyleWindow);
            var page = new SettingsPage("Subtitle appearance",
                "These choices change live subtitles. Use a family installed on this Mac; " +
                "macOS chooses per-script font fallback when needed.");

            _styleFont = new NSComboBox { Completes = true, TranslatesAutoresizingMaskIntoConstraints = false };
            _styleFont.Add([.. NSFontManager.SharedFontManager.AvailableFontFamilies.Select(family => (NSObject)new NSString(family))]);
            _styleFont.WidthAnchor.ConstraintEqualTo(220).Active = true;
            _styleFont.Changed += (_, _) => PreviewSubtitleStyle();
            _styleFont.SelectionChanged += (_, _) => OnWindowThread(PreviewSubtitleStyle);
            _styleColour = StyleColourField();
            _styleSize = StyleSlider(100, 50, 400, "%");
            _styleOpacity = StyleSlider(100, 0, 100, "%");
            _styleMargin = StyleSlider(5, 0, 40, "%");
            _styleBold = StyleCheck();
            _styleInBars = StyleCheck();
            _styleForceBottom = StyleCheck();
            _styleOutline = MacSettings.Choices(["None", "Thin", "Normal", "Thick"]);
            _styleOutline.Activated += (_, _) => PreviewSubtitleStyle();
            _styleOutlineColour = StyleColourField();
            _styleShadowColour = StyleColourField();
            _styleBoxColour = StyleColourField();
            _styleShadowOpacity = StyleSlider(60, 0, 100, "%");
            _styleShadowOffset = StyleSlider(2, 0, 10, "pt");
            _styleBoxOpacity = StyleSlider(0, 0, 100, "%");
            _stylePolicy = MacSettings.Choices(["Use the file's colours and weights", "Always use mine"]);
            _styleNotice = MacTheme.WrappingLabel("", 1, DesignRole.TextSecondary);
            var save = MacTheme.TextButton("Apply", ApplySubtitleStyle, RexButtonLook.Accent, label: "Apply the subtitle appearance");
            var defaults = MacTheme.TextButton("Default style", () =>
            {
                _styleFont!.StringValue = "Helvetica Neue";
                _styleColour!.StringValue = "FFFFFF";
                _styleSize!.DoubleValue = 100;
                _styleOpacity!.DoubleValue = 100;
                _styleMargin!.DoubleValue = 5;
                _styleBold!.State = NSCellStateValue.Off;
                _styleInBars!.State = NSCellStateValue.Off;
                _styleForceBottom!.State = NSCellStateValue.Off;
                _styleOutline!.SelectItem((int)OutlineChoice.Normal);
                _styleOutlineColour!.StringValue = "000000";
                _styleShadowColour!.StringValue = "000000";
                _styleShadowOpacity!.DoubleValue = 60;
                _styleShadowOffset!.DoubleValue = 2;
                _styleBoxColour!.StringValue = "000000";
                _styleBoxOpacity!.DoubleValue = 0;
                _stylePolicy!.SelectItem((int)SubtitleStyleChoice.Respect);
                ApplySubtitleStyle();
            }, label: "Restore the default subtitle style");

            const string ColourHint = "Six hexadecimal digits, such as FFFFFF.";
            page.Add(
                SubtitlePreview(),
                MacSettings.Card("Text",
                    MacSettings.Row("Subtitle font", null, _styleFont),
                    MacSettings.Row("Subtitle size (%)", null, StyleSliderControl(_styleSize)),
                    MacSettings.Row("Subtitle colour", ColourHint, _styleColour),
                    MacSettings.Row("Subtitle opacity (%)", null, StyleSliderControl(_styleOpacity)),
                    MacSettings.Row("Bold subtitles", null, _styleBold)),
                MacSettings.Card("Outline and shadow",
                    MacSettings.Row("Outline", null, _styleOutline),
                    MacSettings.Row("Outline colour", ColourHint, _styleOutlineColour),
                    MacSettings.Row("Shadow colour", ColourHint, _styleShadowColour),
                    MacSettings.Row("Shadow opacity (%, 0 for none)", null, StyleSliderControl(_styleShadowOpacity)),
                    MacSettings.Row("Shadow offset", null, StyleSliderControl(_styleShadowOffset))),
                MacSettings.Card("Background box",
                    MacSettings.Row("Background box colour", ColourHint, _styleBoxColour),
                    MacSettings.Row("Background box opacity (%, 0 for none)", null, StyleSliderControl(_styleBoxOpacity))),
                MacSettings.Card("Position",
                    MacSettings.Row("Gap from the edge of the picture (% of its height)", null, StyleSliderControl(_styleMargin)),
                    MacSettings.Row("Put subtitles in the black bars below a wide picture", null, _styleInBars),
                    MacSettings.Row("Keep every subtitle at the bottom", null, _styleForceBottom)),
                MacSettings.Card(null,
                    MacSettings.Row("Styled subtitles", "Whether a subtitle file's own colours and weights win over the style above.", _stylePolicy)),
                _styleNotice);
            page.AddFitted(MacSettings.Buttons(save, defaults));
            var scroll = MacSettings.Scroller();
            _subtitleStyleWindow.ContentView = scroll;
            MacSettings.Show(scroll, page);
            _subtitleStyleWindow.Center();
        }

        _styleFont!.StringValue = _settings.SubtitleFont;
        _styleColour!.StringValue = _settings.SubtitleColor.ToString("X6", CultureInfo.InvariantCulture);
        _styleSize!.DoubleValue = _settings.SubtitleSize;
        _styleOpacity!.DoubleValue = _settings.SubtitleOpacity;
        _styleMargin!.DoubleValue = _settings.SubtitleMargin;
        _styleBold!.State = State(_settings.SubtitleBold);
        _styleInBars!.State = State(_settings.SubtitlesInBars);
        _styleForceBottom!.State = State(_settings.SubtitlesAtBottom);
        _styleOutline!.SelectItem((int)_settings.SubtitleOutline);
        _styleOutlineColour!.StringValue = _settings.SubtitleOutlineColor.ToString("X6", CultureInfo.InvariantCulture);
        _styleShadowColour!.StringValue = _settings.SubtitleShadowColor.ToString("X6", CultureInfo.InvariantCulture);
        _styleShadowOpacity!.DoubleValue = _settings.SubtitleShadowOpacity;
        _styleShadowOffset!.DoubleValue = _settings.SubtitleShadowOffset;
        _styleBoxColour!.StringValue = _settings.SubtitleBoxColor.ToString("X6", CultureInfo.InvariantCulture);
        _styleBoxOpacity!.DoubleValue = _settings.SubtitleBoxOpacity;
        _stylePolicy!.SelectItem((int)_settings.SubtitleStyles);
        PreviewSubtitleStyle();
        _subtitleStyleWindow.MakeKeyAndOrderFront(null);
    }

    /// <summary>A slider that redraws the preview as it moves; its value is said beside it.</summary>
    private NSSlider StyleSlider(double initial, double minimum, double maximum, string unit)
    {
        var slider = NSSlider.FromValue(initial, minimum, maximum, PreviewSubtitleStyle);
        slider.Continuous = true;
        _styleValues.Add((slider, MacSettings.ValueLabel(), unit));
        return slider;
    }

    private NSStackView StyleSliderControl(NSSlider slider) =>
        MacSettings.SliderWithValue(slider, _styleValues.First(entry => entry.Slider == slider).Value);

    private NSButton StyleCheck()
    {
        var check = MacSettings.Check(false);
        check.Activated += (_, _) => PreviewSubtitleStyle();
        return check;
    }

    private NSTextField StyleColourField()
    {
        var field = new NSTextField { PlaceholderString = "FFFFFF", TranslatesAutoresizingMaskIntoConstraints = false };
        field.WidthAnchor.ConstraintEqualTo(100).Active = true;
        field.Changed += (_, _) => PreviewSubtitleStyle();
        return field;
    }

    /// <summary>The preview: sample subtitle text on the stage, drawn in the style chosen.</summary>
    private RexPanel SubtitlePreview()
    {
        var stage = MacTheme.Panel(MacTheme.Stage, 2);
        stage.HeightAnchor.ConstraintEqualTo((nfloat)SubtitlePreviewHeight).Active = true;
        stage.AccessibilityLabel = "Subtitle preview";
        _stylePreviewBox = MacTheme.Panel(NSColor.Clear, 0);
        _stylePreviewText = new NSTextField
        {
            Editable = false,
            Selectable = false,
            Bordered = false,
            DrawsBackground = false,
            Alignment = NSTextAlignment.Center,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        _stylePreviewText.AccessibilityElement = false;
        _stylePreviewBox.AddSubview(_stylePreviewText);
        MacTheme.Fill(_stylePreviewBox, _stylePreviewText, (double)MacTheme.Space(0));
        stage.AddSubview(_stylePreviewBox);
        _stylePreviewBox.CenterXAnchor.ConstraintEqualTo(stage.CenterXAnchor).Active = true;
        _stylePreviewBox.WidthAnchor.ConstraintLessThanOrEqualTo(stage.WidthAnchor, 1, -MacTheme.Space(4)).Active = true;
        _stylePreviewGap = _stylePreviewBox.BottomAnchor.ConstraintEqualTo(stage.BottomAnchor, -MacTheme.Space(1));
        _stylePreviewGap.Active = true;
        return stage;
    }

    /// <summary>Draws the preview from the controls as they are now; a colour half typed keeps the last good one.</summary>
    private void PreviewSubtitleStyle()
    {
        foreach (var (slider, value, unit) in _styleValues)
        {
            value.StringValue = $"{Math.Round(slider.DoubleValue):0} {unit}";
        }

        if (_stylePreviewText is null || _stylePreviewBox is null || _styleSize is null)
        {
            return;
        }

        var size = (nfloat)Math.Clamp(20 * _styleSize.DoubleValue / 100, 8, 64);
        var family = string.IsNullOrWhiteSpace(_styleFont?.StringValue) ? "Helvetica Neue" : _styleFont!.StringValue.Trim();
        var bold = IsChecked(_styleBold);
        var font = NSFontManager.SharedFontManager.FontWithFamily(family, bold ? NSFontTraitMask.Bold : 0, bold ? 9 : 5, size)
            ?? NSFont.SystemFontOfSize(size, bold ? NSFontWeight.Bold : NSFontWeight.Regular)!;
        var text = SubtitleColour(_styleColour, 0xFFFFFF, _styleOpacity!.DoubleValue);
        var outline = (OutlineChoice)(int)_styleOutline!.IndexOfSelectedItem;
        var attributes = new NSStringAttributes
        {
            Font = font,
            ForegroundColor = text,
            ParagraphStyle = new NSMutableParagraphStyle { Alignment = NSTextAlignment.Center },
            Shadow = new NSShadow
            {
                ShadowColor = SubtitleColour(_styleShadowColour, 0, _styleShadowOpacity!.DoubleValue),
                ShadowOffset = new CGSize(_styleShadowOffset!.DoubleValue, -_styleShadowOffset.DoubleValue),
                ShadowBlurRadius = 2,
            },
        };
        if (outline != OutlineChoice.None)
        {
            // A negative width strokes the outline round the filled letters rather than instead of them.
            attributes.StrokeWidth = outline switch
            {
                OutlineChoice.Thin => -2f,
                OutlineChoice.Thick => -6f,
                _ => -4f,
            };
            attributes.StrokeColor = SubtitleColour(_styleOutlineColour, 0, 100);
        }

        _stylePreviewText.AttributedStringValue = new NSAttributedString("Subtitles look like this.", attributes);
        _stylePreviewBox.Fill = SubtitleColour(_styleBoxColour, 0, _styleBoxOpacity!.DoubleValue);
        if (_stylePreviewGap is not null)
        {
            _stylePreviewGap.Constant = -(nfloat)Math.Max(MacTheme.Space(1), SubtitlePreviewHeight * _styleMargin!.DoubleValue / 100);
        }
    }

    /// <summary>A subtitle colour from a hexadecimal field at an opacity in per cent; <paramref name="fallback"/> while the field does not hold one.</summary>
    private static NSColor SubtitleColour(NSTextField? field, int fallback, double opacity)
    {
        var rgb = TrySubtitleColour(field, out var typed) ? typed : fallback;
        return NSColor.FromSrgb((nfloat)(((rgb >> 16) & 0xFF) / 255.0), (nfloat)(((rgb >> 8) & 0xFF) / 255.0),
            (nfloat)((rgb & 0xFF) / 255.0), (nfloat)Math.Clamp(opacity / 100, 0, 1));
    }

    private void ApplySubtitleStyle()
    {
        if (!TrySubtitleColour(_styleColour, out var rgb) ||
            !TrySubtitleColour(_styleOutlineColour, out var outlineRgb) ||
            !TrySubtitleColour(_styleShadowColour, out var shadowRgb) ||
            !TrySubtitleColour(_styleBoxColour, out var boxRgb))
        {
            _styleNotice!.StringValue = "Enter six-digit hexadecimal colours, for example FFFFFF.";
            return;
        }

        var updated = (_settings with
        {
            SubtitleFont = string.IsNullOrWhiteSpace(_styleFont!.StringValue)
                ? "Helvetica Neue" : _styleFont.StringValue.Trim(),
            SubtitleColor = rgb,
            SubtitleSize = (int)Math.Round(_styleSize!.DoubleValue),
            SubtitleOpacity = (int)Math.Round(_styleOpacity!.DoubleValue),
            SubtitleMargin = (int)Math.Round(_styleMargin!.DoubleValue),
            SubtitleBold = IsChecked(_styleBold),
            SubtitlesInBars = IsChecked(_styleInBars),
            SubtitlesAtBottom = IsChecked(_styleForceBottom),
            SubtitleOutline = (OutlineChoice)(int)_styleOutline!.IndexOfSelectedItem,
            SubtitleOutlineColor = outlineRgb,
            SubtitleShadowColor = shadowRgb,
            SubtitleShadowOpacity = (int)Math.Round(_styleShadowOpacity!.DoubleValue),
            SubtitleShadowOffset = (int)Math.Round(_styleShadowOffset!.DoubleValue),
            SubtitleBoxColor = boxRgb,
            SubtitleBoxOpacity = (int)Math.Round(_styleBoxOpacity!.DoubleValue),
            SubtitleStyles = (SubtitleStyleChoice)(int)_stylePolicy!.IndexOfSelectedItem,
        }).Normalize();
        PreviewSubtitleStyle();
        try
        {
            SettingsStore.Save(_roots.SettingsPath, updated);
            _settings = updated;
            _player.Settings = updated;
            UpdateSubtitleOverlays();
            _styleNotice!.StringValue = "Appearance saved and applied.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _styleNotice!.StringValue = "Could not save subtitle style: " + ex.Message;
        }
    }

    private static bool TrySubtitleColour(NSTextField? field, out int rgb)
    {
        var value = field?.StringValue.Trim().TrimStart('#') ?? "";
        rgb = 0;
        return value.Length == 6 && int.TryParse(value, NumberStyles.HexNumber,
            CultureInfo.InvariantCulture, out rgb);
    }

    private void DisposeSubtitleStyle()
    {
        _subtitleStyleWindow?.Close();
        _subtitleStyleWindow?.Dispose();
        _subtitleStyleWindow = null;
        _styleValues.Clear();
    }
}
