using System.Globalization;
using Rex.Media.Settings;

namespace Rex.Media.App.Mac;

/// <summary>Editable Mac subtitle appearance, shared through PlayerSettings with other shells.</summary>
public sealed partial class MainWindow
{
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
                EffectChoiceRow("Embedded styles", _stylePolicy),
                actions, _styleNotice,
            ]);
            stack.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            stack.Alignment = NSLayoutAttribute.Leading;
            stack.Spacing = 10;
            stack.EdgeInsets = new NSEdgeInsets(20, 24, 20, 24);
            stack.Frame = new CGRect(0, 0, 540, 620);
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
        _stylePolicy!.SelectItem((int)_settings.SubtitleStyles);
        _subtitleStyleWindow.MakeKeyAndOrderFront(null);
    }

    private static NSStackView StyleSliderRow(string title, NSSlider slider)
    {
        var label = NSTextField.CreateLabel(title);
        label.WidthAnchor.ConstraintEqualTo(170).Active = true;
        slider.WidthAnchor.ConstraintGreaterThanOrEqualTo(225).Active = true;
        var row = NSStackView.FromViews([label, slider]);
        row.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        row.Spacing = 8;
        return row;
    }

    private void ApplySubtitleStyle()
    {
        var colour = _styleColour!.StringValue.Trim().TrimStart('#');
        if (colour.Length != 6 || !int.TryParse(colour, NumberStyles.HexNumber,
            CultureInfo.InvariantCulture, out var rgb))
        {
            _styleNotice!.StringValue = "Enter a six-digit hexadecimal colour, for example FFFFFF.";
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

    private void DisposeSubtitleStyle()
    {
        _subtitleStyleWindow?.Close();
        _subtitleStyleWindow?.Dispose();
        _subtitleStyleWindow = null;
    }
}
