using System.Globalization;
using Rex.Media.Audio;
using Rex.Media.AppCore.Automation;
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
    private NSSlider? _effectsLoudnessUntagged;
    private NSButton? _effectsLoudnessPeak;
    private NSButton? _effectsDoublePass;
    private NSTextField? _effectsPresetName;
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
            foreach (var name in MacUserPresetNames) _effectsPreset.AddItem(name);

            _effectsPreset.Activated += (_, _) => ApplyEffectPreset();
            _effectsPreamp = EffectSlider(0);
            _effectsDoublePass = NSButton.CreateCheckbox("Double-pass equalizer", ApplyEffects);
            _effectsPresetName = new NSTextField(new CGRect(0, 0, 220, 24))
            {
                PlaceholderString = "My preset",
            };
            ((NSView)_effectsPresetName).AccessibilityLabel = "Equalizer preset name";
            var savePreset = NSButton.CreateButton("Save user preset", SaveMacEqualizerPreset);
            var presetEditor = NSStackView.FromViews([_effectsPresetName, savePreset]);
            presetEditor.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            presetEditor.Spacing = 8;
            _effectsLoudnessPreamp = EffectSlider(0);
            _effectsLoudnessUntagged = EffectSlider(0);
            _effectsLoudnessPeak = NSButton.CreateCheckbox("Prevent clipping at tagged peak", ApplyEffects);
            var rows = new List<NSView>
            {
                heading, hint, _effectsEnabled,
                EffectChoiceRow("Equalizer preset", _effectsPreset),
                EffectsHeading("10-band graphic equalizer"),
                EffectGainRow("Preamp", _effectsPreamp),
                _effectsDoublePass,
                presetEditor,
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
            rows.Add(EffectGainRow("Untagged fallback gain", _effectsLoudnessUntagged));
            rows.Add(_effectsLoudnessPeak);
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
        var selectedName = _settings.EqualizerPreset;
        _effectsPreset!.RemoveAllItems();
        _effectsPreset.AddItem("Custom");
        foreach (var preset in EqualizerSettings.Presets) _effectsPreset.AddItem(preset.Name);
        foreach (var name in MacUserPresetNames) _effectsPreset.AddItem(name);
        _effectsEnabled!.State = State(_settings.EqualizerEnabled);
        var presetNames = EqualizerSettings.Presets.Select(preset => preset.Name).Prepend("Custom")
            .Concat(MacUserPresetNames).ToArray();
        var index = selectedName is null ? 0 : Math.Max(0, Array.IndexOf(presetNames, selectedName));
        _effectsPreset!.SelectItem(Math.Max(0, index));
        _effectsPreamp!.DoubleValue = _settings.EqualizerPreamp;
        _effectsDoublePass!.State = State(_settings.EqualizerDoublePass);
        for (var i = 0; i < _effectsBands.Count; i++)
        {
            _effectsBands[i].DoubleValue = _settings.EqualizerGains[i];
        }

        _effectsStereo!.SelectItem((int)_settings.Stereo);
        _effectsLoudness!.SelectItem((int)_settings.Loudness);
        _effectsLoudnessPreamp!.DoubleValue = _settings.LoudnessPreamp;
        _effectsLoudnessUntagged!.DoubleValue = _settings.LoudnessUntaggedGain;
        _effectsLoudnessPeak!.State = State(_settings.LoudnessPreventClipping);
        _effectsLoading = false;
    }

    private void ApplyEffectPreset()
    {
        if (_effectsLoading)
        {
            return;
        }

        var index = (int)_effectsPreset!.IndexOfSelectedItem;
        IReadOnlyList<double>? gains = index > 0 && index <= EqualizerSettings.Presets.Count
            ? EqualizerSettings.Presets[index - 1].Gains
            : _effectsPreset.TitleOfSelectedItem is { } name && _settings.EqualizerUserPresets.TryGetValue(name, out var user)
                ? user : null;
        if (gains is not null)
        {
            _effectsLoading = true;
            _effectsEnabled!.State = NSCellStateValue.On;
            _effectsPreamp!.DoubleValue = 0;
            for (var i = 0; i < _effectsBands.Count; i++)
            {
                _effectsBands[i].DoubleValue = gains[i];
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
        var selected = _effectsPreset.TitleOfSelectedItem;
        var chosenGains = index > 0 && index <= EqualizerSettings.Presets.Count
            ? EqualizerSettings.Presets[index - 1].Gains
            : selected is not null && _settings.EqualizerUserPresets.TryGetValue(selected, out var user) ? user : null;
        var chosen = chosenGains is not null && _effectsBands.Select(b => b.DoubleValue).SequenceEqual(chosenGains);
        var settings = (_settings with
        {
            EqualizerEnabled = _effectsEnabled!.State == NSCellStateValue.On,
            EqualizerPreamp = _effectsPreamp!.DoubleValue,
            EqualizerGains = [.. _effectsBands.Select(b => b.DoubleValue)],
            EqualizerPreset = chosen ? selected : null,
            EqualizerDoublePass = IsChecked(_effectsDoublePass),
            Stereo = (StereoChoice)(int)_effectsStereo!.IndexOfSelectedItem,
            Loudness = (LoudnessChoice)(int)_effectsLoudness!.IndexOfSelectedItem,
            LoudnessPreamp = _effectsLoudnessPreamp!.DoubleValue,
            LoudnessUntaggedGain = _effectsLoudnessUntagged!.DoubleValue,
            LoudnessPreventClipping = IsChecked(_effectsLoudnessPeak),
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

    private string[] MacUserPresetNames => [.. _settings.EqualizerUserPresets.Keys.Order(StringComparer.Ordinal)];

    private void SaveMacEqualizerPreset()
    {
        var name = _effectsPresetName?.StringValue.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Say("Name the equalizer preset first.");
            return;
        }

        var existed = _settings.EqualizerUserPresets.ContainsKey(name);
        var presets = _settings.EqualizerUserPresets.ToDictionary(pair => pair.Key, pair => pair.Value,
            StringComparer.Ordinal);
        presets[name] = [.. _effectsBands.Select(band => band.DoubleValue)];
        _settings = _settings with { EqualizerUserPresets = presets, EqualizerPreset = name };
        if (!existed) _effectsPreset!.AddItem(name);
        _effectsPreset!.SelectItem(name);
        ApplyEffects();
        LoadEffects();
    }

    private AutomationReply MacLoudnessSmokeAction(string? action)
    {
        if (action == "configure")
        {
            ShowEffects();
            _effectsLoudness!.SelectItem((int)LoudnessChoice.Album);
            _effectsLoudnessPreamp!.DoubleValue = 3.5;
            _effectsLoudnessUntagged!.DoubleValue = -7.0;
            _effectsLoudnessPeak!.State = NSCellStateValue.On;
            ApplyEffects();
        }
        else if (action != "status")
        {
            return new AutomationReply { Ok = false, Error = "Unknown loudness action." };
        }

        var replay = _player.Sound.ReplayGain;
        return new AutomationReply
        {
            Ok = true,
            Text = $"mode={replay.Mode};preamp={replay.Preamp:0.0};fallback={replay.UntaggedGain:0.0};" +
                $"protect={replay.PreventClipping}",
        };
    }

    private AutomationReply MacEqualizerSmokeAction(string? action)
    {
        if (action == "configure")
        {
            ShowEffects();
            _effectsEnabled!.State = NSCellStateValue.On;
            _effectsDoublePass!.State = NSCellStateValue.On;
            _effectsPreamp!.DoubleValue = 2;
            for (var i = 0; i < _effectsBands.Count; i++) _effectsBands[i].DoubleValue = i - 5;
            _effectsPresetName!.StringValue = "Mac listening room";
            SaveMacEqualizerPreset();
        }
        else if (action != "status")
        {
            return new AutomationReply { Ok = false, Error = "Unknown equalizer action." };
        }

        var equalizer = _player.Sound.Equalizer;
        return new AutomationReply
        {
            Ok = true,
            Text = $"enabled={equalizer.Enabled};preamp={equalizer.Preamp:0.0};" +
                $"double={equalizer.DoublePass};bands={string.Join(',', equalizer.Gains)};" +
                $"users={_settings.EqualizerUserPresets.Count}",
        };
    }

    private void DisposeEffects()
    {
        _effectsWindow?.Close();
        _effectsWindow?.Dispose();
        _effectsWindow = null;
    }
}
