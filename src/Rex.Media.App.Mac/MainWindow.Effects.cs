using System.Globalization;
using Rex.Media.Audio;
using Rex.Media.Settings;

namespace Rex.Media.App.Mac;

/// <summary>Live sound effects and channel policy, backed by the portable audio engine.</summary>
public sealed partial class MainWindow
{
    private static readonly string[] MacBandNames = ["31", "62", "125", "250", "500", "1k", "2k", "4k", "8k", "16k"];
    private NSWindow? _effectsWindow;
    private NSButton? _effectsEnabled;
    private NSPopUpButton? _effectsPreset;
    private NSPopUpButton? _effectsStereo;
    private NSPopUpButton? _effectsLoudness;
    private NSSlider? _effectsPreamp;
    private NSSlider? _effectsLoudnessPreamp;
    private readonly List<NSSlider> _effectsBands = [];
    private bool _effectsLoading;

    private void ShowEffects()
    {
        if (_effectsWindow is null)
        {
            _effectsWindow = new NSWindow(new CGRect(0, 0, 650, 620),
                NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable,
                NSBackingStore.Buffered, false)
            {
                Title = "Effects & filters - rexplayer",
                MinSize = new CGSize(520, 480),
            };
            _effectsWindow.ReleaseWhenClosed(false);

            var heading = NSTextField.CreateLabel("Effects & filters");
            heading.Font = NSFont.SystemFontOfSize(24, NSFontWeight.Semibold)!;
            var hint = NSTextField.CreateWrappingLabel(
                "Change your sound as it plays. The equalizer, channels and loudness policy " +
                "apply to subsequent tracks too.");
            hint.TextColor = NSColor.SecondaryLabel;
            _effectsEnabled = NSButton.CreateCheckbox("Enable 10-band equalizer", () => ApplyEffects());
            _effectsPreset = new NSPopUpButton(new CGRect(0, 0, 240, 28), false);
            _effectsPreset.AddItem("Custom");
            foreach (var preset in EqualizerSettings.Presets)
            {
                _effectsPreset.AddItem(preset.Name);
            }

            _effectsPreset.Activated += (_, _) => ApplyEffectPreset();
            _effectsPreamp = EffectSlider(0);
            _effectsLoudnessPreamp = EffectSlider(0);
            var rows = new List<NSView>
            {
                heading, hint, _effectsEnabled,
                EffectChoiceRow("Equalizer preset", _effectsPreset),
                EffectsHeading("10-band graphic equalizer"),
                EffectGainRow("Preamp", _effectsPreamp),
            };
            for (var i = 0; i < MacBandNames.Length; i++)
            {
                var band = EffectSlider(0);
                _effectsBands.Add(band);
                rows.Add(EffectGainRow(MacBandNames[i] + " Hz", band));
            }

            _effectsStereo = EffectChoices(["Stereo", "Mono", "Left only", "Right only", "Reversed"]);
            _effectsLoudness = EffectChoices(["Off", "Per track", "Per album"]);
            rows.Add(EffectsHeading("Channel mixing"));
            rows.Add(EffectChoiceRow("Stereo mode", _effectsStereo));
            rows.Add(EffectsHeading("ReplayGain / R128"));
            rows.Add(EffectChoiceRow("Loudness tags", _effectsLoudness));
            rows.Add(EffectGainRow("Loudness preamp", _effectsLoudnessPreamp));
            var reset = NSButton.CreateButton("Reset equalizer to flat", () =>
            {
                _effectsLoading = true;
                _effectsPreset!.SelectItem(0);
                _effectsPreamp!.DoubleValue = 0;
                foreach (var band in _effectsBands)
                {
                    band.DoubleValue = 0;
                }

                _effectsLoading = false;
                ApplyEffects();
            });
            rows.Add(reset);
            var content = NSStackView.FromViews([.. rows]);
            content.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            content.Alignment = NSLayoutAttribute.Leading;
            content.Spacing = 12;
            content.EdgeInsets = new NSEdgeInsets(16, 24, 16, 24);
            content.Frame = new CGRect(0, 0, 590, 1120);
            var scroll = new NSScrollView(new CGRect(0, 0, 650, 620))
            {
                HasVerticalScroller = true,
                AutohidesScrollers = true,
            };
            scroll.DocumentView = content;
            _effectsWindow.ContentView = scroll;
            _effectsWindow.Center();
        }

        LoadEffects();
        _effectsWindow.MakeKeyAndOrderFront(null);
    }

    private static NSTextField EffectsHeading(string title)
    {
        var label = NSTextField.CreateLabel(title);
        label.Font = NSFont.SystemFontOfSize(14, NSFontWeight.Semibold)!;
        return label;
    }

    private static NSPopUpButton EffectChoices(string[] titles)
    {
        var popup = new NSPopUpButton(new CGRect(0, 0, 260, 28), false);
        foreach (var title in titles)
        {
            popup.AddItem(title);
        }

        return popup;
    }

    private NSSlider EffectSlider(double initial)
    {
        var slider = NSSlider.FromValue(initial, -EqualizerSettings.Limit, EqualizerSettings.Limit, ApplyEffects);
        slider.Continuous = true;
        slider.WidthAnchor.ConstraintGreaterThanOrEqualTo(300).Active = true;
        return slider;
    }

    private static NSStackView EffectChoiceRow(string name, NSPopUpButton choices)
    {
        var title = NSTextField.CreateLabel(name);
        title.WidthAnchor.ConstraintEqualTo(160).Active = true;
        var row = NSStackView.FromViews([title, choices]);
        row.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        row.Spacing = 12;
        return row;
    }

    private static NSStackView EffectGainRow(string name, NSSlider slider)
    {
        var title = NSTextField.CreateLabel(name);
        title.WidthAnchor.ConstraintEqualTo(160).Active = true;
        var value = NSTextField.CreateLabel("");
        value.Font = NSFont.MonospacedDigitSystemFontOfSize(12, NSFontWeight.Regular)!;
        value.WidthAnchor.ConstraintEqualTo(74).Active = true;
        void Show() => value.StringValue =
            slider.DoubleValue.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " dB";
        slider.Activated += (_, _) => Show();
        Show();
        var row = NSStackView.FromViews([title, slider, value]);
        row.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        row.Spacing = 12;
        return row;
    }

    private void LoadEffects()
    {
        _effectsLoading = true;
        _effectsEnabled!.State = State(_settings.EqualizerEnabled);
        var index = _settings.EqualizerPreset is { } name
            ? EqualizerSettings.Presets.ToList().FindIndex(p => p.Name == name) + 1 : 0;
        _effectsPreset!.SelectItem(Math.Max(0, index));
        _effectsPreamp!.DoubleValue = _settings.EqualizerPreamp;
        for (var i = 0; i < _effectsBands.Count; i++)
        {
            _effectsBands[i].DoubleValue = _settings.EqualizerGains[i];
        }

        _effectsStereo!.SelectItem((int)_settings.Stereo);
        _effectsLoudness!.SelectItem((int)_settings.Loudness);
        _effectsLoudnessPreamp!.DoubleValue = _settings.LoudnessPreamp;
        _effectsLoading = false;
    }

    private void ApplyEffectPreset()
    {
        if (_effectsLoading)
        {
            return;
        }

        var index = (int)_effectsPreset!.IndexOfSelectedItem;
        if (index > 0 && index <= EqualizerSettings.Presets.Count)
        {
            _effectsLoading = true;
            var preset = EqualizerSettings.Presets[index - 1];
            _effectsEnabled!.State = NSCellStateValue.On;
            _effectsPreamp!.DoubleValue = 0;
            for (var i = 0; i < _effectsBands.Count; i++)
            {
                _effectsBands[i].DoubleValue = preset.Gains[i];
            }

            _effectsLoading = false;
        }

        ApplyEffects();
    }

    private void ApplyEffects()
    {
        if (_effectsLoading || _effectsWindow is null)
        {
            return;
        }

        var index = (int)_effectsPreset!.IndexOfSelectedItem;
        var chosen = index > 0 && index <= EqualizerSettings.Presets.Count &&
            _effectsBands.Select(b => b.DoubleValue).SequenceEqual(EqualizerSettings.Presets[index - 1].Gains);
        var settings = (_settings with
        {
            EqualizerEnabled = _effectsEnabled!.State == NSCellStateValue.On,
            EqualizerPreamp = _effectsPreamp!.DoubleValue,
            EqualizerGains = [.. _effectsBands.Select(b => b.DoubleValue)],
            EqualizerPreset = chosen ? EqualizerSettings.Presets[index - 1].Name : null,
            Stereo = (StereoChoice)(int)_effectsStereo!.IndexOfSelectedItem,
            Loudness = (LoudnessChoice)(int)_effectsLoudness!.IndexOfSelectedItem,
            LoudnessPreamp = _effectsLoudnessPreamp!.DoubleValue,
        }).Normalize();
        _settings = settings;
        _player.Settings = settings;
        try
        {
            SettingsStore.Save(_roots.SettingsPath, settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning(LogSource, "Sound settings could not be saved: " + ex.Message);
            Say("Sound settings could not be saved.");
        }
    }

    private void DisposeEffects()
    {
        _effectsWindow?.Close();
        _effectsWindow?.Dispose();
        _effectsWindow = null;
    }
}
