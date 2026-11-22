using System.Globalization;
using Rex.Media.Audio;
using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Design;
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
    private readonly List<Action> _effectsReadouts = [];
    private bool _effectsLoading;

    private void ShowEffects()
    {
        if (_effectsWindow is null)
        {
            _effectsWindow = MacToolViews.ToolWindow("Effects & filters - rexplayer", 700, 680, 600, 480);
            _effectsEnabled = NSButton.CreateCheckbox("Equaliser", () => ApplyEffects());
            ((NSView)_effectsEnabled).AccessibilityLabel = "Enable 10-band equaliser";
            _effectsPreset = new NSPopUpButton(new CGRect(0, 0, 220, 28), false);
            _effectsPreset.AddItem("Custom");
            foreach (var preset in EqualizerSettings.Presets)
            {
                _effectsPreset.AddItem(preset.Name);
            }
            foreach (var name in MacUserPresetNames) _effectsPreset.AddItem(name);

            _effectsPreset.Activated += (_, _) => ApplyEffectPreset();
            ((NSView)_effectsPreset).AccessibilityLabel = "Equaliser preset";
            _effectsPreamp = EffectSlider(0, vertical: true);
            _effectsDoublePass = NSButton.CreateCheckbox("Double-pass equalizer", ApplyEffects);
            _effectsPresetName = new NSTextField(new CGRect(0, 0, 220, 24))
            {
                PlaceholderString = "My preset",
            };
            _effectsPresetName.WidthAnchor.ConstraintEqualTo(180).Active = true;
            ((NSView)_effectsPresetName).AccessibilityLabel = "Equalizer preset name";
            var savePreset = MacTheme.TextButton("Save user preset", SaveMacEqualizerPreset);
            _effectsLoudnessPreamp = EffectSlider(0);
            _effectsLoudnessUntagged = EffectSlider(0);
            _effectsLoudnessPeak = NSButton.CreateCheckbox("Prevent clipping at tagged peak", ApplyEffects);
            var reset = MacTheme.TextButton("Reset", () =>
            {
                _effectsLoading = true;
                _effectsPreset!.SelectItem(0);
                _effectsPreamp!.DoubleValue = 0;
                foreach (var band in _effectsBands)
                {
                    band.DoubleValue = 0;
                }

                _effectsLoading = false;
                ShowEffectReadouts();
                ApplyEffects();
            }, label: "Reset equalizer to flat");
            var strip = new List<NSView> { EffectBand("Preamp", _effectsPreamp) };
            for (var i = 0; i < MacBandNames.Length; i++)
            {
                var band = EffectSlider(0, vertical: true);
                _effectsBands.Add(band);
                strip.Add(EffectBand(MacBandNames[i] + " Hz", band));
            }

            var bands = MacTheme.Stack(false, 1, [.. strip]);
            bands.Alignment = NSLayoutAttribute.Top;
            _effectsStereo = EffectChoices(["Stereo", "Mono", "Left only", "Right only", "Left and right swapped",
                "Headphone cross-feed", "Matrix-surround decode"]);
            _effectsLoudness = EffectChoices(["Off", "Each track alike", "Each album alike"]);
            var page = MacToolViews.ToolPage("Effects & filters",
                "Change your sound as it plays. The equalizer, channels and loudness policy " +
                "apply to subsequent tracks too.",
                MacToolViews.ToolCard("10-band graphic equalizer",
                    MacToolViews.ToolRow("Preset", null, MacToolViews.ToolButtons(_effectsEnabled, _effectsPreset, reset)),
                    bands,
                    MacToolViews.ToolCheck(_effectsDoublePass),
                    MacToolViews.ToolRow("User preset", null, MacToolViews.ToolButtons(_effectsPresetName, savePreset))),
                MacToolViews.ToolCard("Channel mixing",
                    MacToolViews.ToolRow("Stereo mode", null, _effectsStereo)),
                MacToolViews.ToolCard("ReplayGain / R128",
                    MacToolViews.ToolRow("Even out loudness with the media's tags", null, _effectsLoudness),
                    EffectGainRow("Loudness preamp", _effectsLoudnessPreamp),
                    EffectGainRow("Untagged fallback gain", _effectsLoudnessUntagged),
                    MacToolViews.ToolCheck(_effectsLoudnessPeak)));
            MacToolViews.Host(_effectsWindow, page, scrolls: true);
            _effectsWindow.Center();
        }

        LoadEffects();
        _effectsWindow.MakeKeyAndOrderFront(null);
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

    private NSSlider EffectSlider(double initial, bool vertical = false)
    {
        var slider = NSSlider.FromValue(initial, -EqualizerSettings.Limit, EqualizerSettings.Limit, ApplyEffects);
        slider.Continuous = true;
        if (vertical)
        {
            slider.IsVertical = true;
            slider.TickMarksCount = 5;
            slider.HeightAnchor.ConstraintEqualTo(180).Active = true;
        }
        else
        {
            slider.WidthAnchor.ConstraintGreaterThanOrEqualTo(220).Active = true;
        }

        return slider;
    }

    /// <summary>A gain in decibels as a row: the slider and, beside it, its value in figures that keep their width.</summary>
    private NSStackView EffectGainRow(string name, NSSlider slider)
    {
        var value = MacTheme.Label("", 1, DesignRole.TextSecondary);
        value.Font = MacTheme.DigitFont(1);
        value.Alignment = NSTextAlignment.Right;
        value.WidthAnchor.ConstraintEqualTo(64).Active = true;
        void Show() => value.StringValue =
            slider.DoubleValue.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " dB";
        slider.Activated += (_, _) => Show();
        _effectsReadouts.Add(Show);
        Show();
        ((NSView)slider).AccessibilityLabel = name;
        return MacToolViews.ToolRow(name, null, MacToolViews.ToolButtons(slider, value));
    }

    /// <summary>
    /// One band of the equaliser as Windows draws it: its frequency above a vertical slider and the
    /// gain under it, which follows the slider whether a person or a preset moves it.
    /// </summary>
    private NSStackView EffectBand(string name, NSSlider slider)
    {
        var title = MacTheme.Label(name.Replace(" Hz", "", StringComparison.Ordinal), 1, DesignRole.TextSecondary);
        var value = MacTheme.Label("", 1, DesignRole.TextPrimary);
        value.Font = MacTheme.DigitFont(1);
        void Show() => value.StringValue = slider.DoubleValue.ToString("+0.#;-0.#;0", CultureInfo.InvariantCulture);
        slider.Activated += (_, _) => Show();
        _effectsReadouts.Add(Show);
        Show();
        ((NSView)slider).AccessibilityLabel = name + " gain";
        var band = MacTheme.Stack(true, 0, title, slider, value);
        band.Alignment = NSLayoutAttribute.CenterX;
        band.WidthAnchor.ConstraintEqualTo(44).Active = true;
        return band;
    }

    /// <summary>Shows each slider's value again after code, not a person, moved it: a preset, Reset, the saved settings.</summary>
    private void ShowEffectReadouts()
    {
        foreach (var show in _effectsReadouts)
        {
            show();
        }
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
        ShowEffectReadouts();
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
            ShowEffectReadouts();
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
            ShowEffectReadouts();
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

    private void ToggleMacStereoMode(StereoChoice mode)
    {
        var chosen = _settings.Stereo == mode ? StereoChoice.Stereo : mode;
        _settings = (_settings with { Stereo = chosen }).Normalize();
        _player.Settings = _settings;
        SettingsStore.Save(_roots.SettingsPath, _settings);
        if (_effectsWindow is not null) LoadEffects();
        Say(chosen switch
        {
            StereoChoice.Headphones => "Headphone cross-feed on",
            StereoChoice.MatrixSurround => "Matrix-surround decode on",
            _ => "Stereo output",
        });
    }

    private AutomationReply MacStereoSmokeAction(string? action)
    {
        switch (action)
        {
            case "headphones": ToggleMacStereoMode(StereoChoice.Headphones); break;
            case "matrix": ToggleMacStereoMode(StereoChoice.MatrixSurround); break;
            case "stereo":
                _settings = _settings with { Stereo = StereoChoice.Stereo };
                _player.Settings = _settings;
                break;
            case "status": break;
            default: return new AutomationReply { Ok = false, Error = "Unknown stereo action." };
        }

        return new AutomationReply { Ok = true, Text = $"mode={_player.Sound.StereoMode}" };
    }

    private void DisposeEffects()
    {
        _effectsWindow?.Close();
        _effectsWindow?.Dispose();
        _effectsWindow = null;
    }
}
