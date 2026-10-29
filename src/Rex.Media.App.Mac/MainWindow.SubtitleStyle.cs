using System.Globalization;
using Rex.Media.AppCore.Automation;
using Rex.Media.Settings;

namespace Rex.Media.App.Mac;

/// <summary>Editable Mac subtitle appearance, shared through PlayerSettings with other shells.</summary>
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

    private NSWindow? _subtitleStyleWindow;
    private NSTextField? _styleFont;
    private NSTextField? _styleColour;
    private NSSlider? _styleSize;
    private NSSlider? _styleOpacity;
    private NSSlider? _styleMargin;
    private NSButton? _styleBold;
    private NSButton? _styleInBars;
    private NSButton? _styleForceBottom;
    private NSPopUpButton? _styleOutline;
    private NSPopUpButton? _stylePolicy;
    private NSTextField? _styleNotice;
    private NSTextField? _styleOutlineColour;
    private NSTextField? _styleShadowColour;
    private NSTextField? _styleBoxColour;
    private NSSlider? _styleShadowOpacity;
    private NSSlider? _styleShadowOffset;
    private NSSlider? _styleBoxOpacity;

    private void ShowSubtitleStyle()
    {
        if (_subtitleStyleWindow is null)
        {
            _subtitleStyleWindow = new NSWindow(new CGRect(0, 0, 560, 490),
                NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable,
                NSBackingStore.Buffered, false)
            {
                Title = "Subtitle appearance - rexplayer",
                MinSize = new CGSize(530, 420),
            };
            _subtitleStyleWindow.ReleaseWhenClosed(false);
            var heading = NSTextField.CreateLabel("Subtitle appearance");
            heading.Font = NSFont.SystemFontOfSize(22, NSFontWeight.Semibold)!;
            var intro = NSTextField.CreateWrappingLabel(
                "These choices change live subtitles. Use a family installed on this Mac; " +
                "macOS chooses per-script font fallback when needed.");
            intro.TextColor = NSColor.SecondaryLabel;

            _styleFont = new NSTextField(new CGRect(0, 0, 265, 24));
            _styleColour = new NSTextField(new CGRect(0, 0, 125, 24));
            _styleSize = NSSlider.FromValue(100, 50, 400, () => { });
            _styleOpacity = NSSlider.FromValue(100, 0, 100, () => { });
            _styleMargin = NSSlider.FromValue(5, 0, 40, () => { });
            _styleBold = NSButton.CreateCheckbox("Bold subtitle text", () => { });
            _styleInBars = NSButton.CreateCheckbox("Place subtitles in letterbox bars when possible", () => { });
            _styleForceBottom = NSButton.CreateCheckbox("Force subtitles to the bottom", () => { });
            _styleOutline = EffectChoices(["No outline", "Thin", "Normal", "Thick"]);
            _styleOutlineColour = new NSTextField(new CGRect(0, 0, 125, 24));
            _styleShadowColour = new NSTextField(new CGRect(0, 0, 125, 24));
            _styleBoxColour = new NSTextField(new CGRect(0, 0, 125, 24));
            _styleShadowOpacity = NSSlider.FromValue(60, 0, 100, () => { });
            _styleShadowOffset = NSSlider.FromValue(2, 0, 10, () => { });
            _styleBoxOpacity = NSSlider.FromValue(0, 0, 100, () => { });
            _stylePolicy = EffectChoices(["Respect embedded subtitle styles", "Use my chosen style"]);
            _styleNotice = NSTextField.CreateWrappingLabel("");
            _styleNotice.TextColor = NSColor.SecondaryLabel;
            var save = NSButton.CreateButton("Apply", ApplySubtitleStyle);
            var defaults = NSButton.CreateButton("Default style", () =>
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
            });
            var actions = NSStackView.FromViews([save, defaults]);
            actions.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            actions.Spacing = 10;
            var stack = NSStackView.FromViews([
                heading, intro,
                TimingRow("Font", _styleFont),
                TimingRow("Text colour (#RRGGBB)", _styleColour),
                StyleSliderRow("Size, 50-400%", _styleSize),
                StyleSliderRow("Opacity, 0-100%", _styleOpacity),
                StyleSliderRow("Bottom margin %", _styleMargin),
                _styleBold, _styleInBars, _styleForceBottom,
                EffectChoiceRow("Text outline", _styleOutline),
                TimingRow("Outline colour (#RRGGBB)", _styleOutlineColour),
                TimingRow("Shadow colour (#RRGGBB)", _styleShadowColour),
                StyleSliderRow("Shadow opacity %", _styleShadowOpacity),
                StyleSliderRow("Shadow offset", _styleShadowOffset),
                TimingRow("Background colour (#RRGGBB)", _styleBoxColour),
                StyleSliderRow("Background opacity %", _styleBoxOpacity),
                EffectChoiceRow("Embedded styles", _stylePolicy),
                actions, _styleNotice,
            ]);
            stack.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            stack.Alignment = NSLayoutAttribute.Leading;
            stack.Spacing = 10;
            stack.EdgeInsets = new NSEdgeInsets(20, 24, 20, 24);
            stack.Frame = new CGRect(0, 0, 540, 880);
            var scroll = new NSScrollView(new CGRect(0, 0, 560, 490))
            {
                HasVerticalScroller = true,
                DocumentView = stack,
            };
            _subtitleStyleWindow.ContentView = scroll;
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
        _subtitleStyleWindow.MakeKeyAndOrderFront(null);
    }

    private static NSStackView StyleSliderRow(string title, NSSlider slider)
    {
        var label = NSTextField.CreateLabel(title);
        ((NSView)slider).AccessibilityLabel = title;
        label.WidthAnchor.ConstraintEqualTo(170).Active = true;
        slider.WidthAnchor.ConstraintGreaterThanOrEqualTo(225).Active = true;
        var row = NSStackView.FromViews([label, slider]);
        row.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        row.Spacing = 8;
        return row;
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
    }
}
