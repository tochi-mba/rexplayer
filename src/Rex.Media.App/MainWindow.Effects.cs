using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Rex.Media.Audio;
using Rex.Media.Settings;

namespace Rex.Media.App;

public sealed partial class MainWindow
{
    private static readonly string[] BandNames = ["31", "62", "125", "250", "500", "1k", "2k", "4k", "8k", "16k"];

    /// <summary>
    /// The effects panel (UI-07): the equaliser with its presets, the stereo mode and loudness
    /// evening. Every change is heard at once; closing the panel keeps them.
    /// </summary>
    private async Task ShowEffectsAsync()
    {
        var enabled = new ToggleSwitch { Header = "Equaliser", IsOn = _settings.EqualizerEnabled };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(enabled, "EqualizerSwitch");
        var presets = new ComboBox
        {
            Header = "Preset",
            ItemsSource = EqualizerSettings.Presets.Select(preset => preset.Name).Prepend("Custom").ToList(),
            SelectedIndex = _settings.EqualizerPreset is { } name && EqualizerSettings.Presets.ToList().FindIndex(p => p.Name == name) is >= 0 and var index ? index + 1 : 0,
            MinWidth = 220,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(presets, "EqualizerPresets");
        var preamp = Band("Preamp", _settings.EqualizerPreamp);
        var bands = Enumerable.Range(0, 10).Select(i => Band(BandNames[i] + " Hz", _settings.EqualizerGains[i])).ToArray();
        var stereo = Choice("Stereo", ["Stereo", "Mono", "Left only", "Right only", "Left and right swapped"], (int)_settings.Stereo);
        var loudness = Choice("Even out loudness with the media's tags", ["Off", "Each track alike", "Each album alike"], (int)_settings.Loudness);

        var updating = false;
        void Apply(bool fromPreset)
        {
            if (updating)
            {
                return;
            }

            updating = true;
            if (fromPreset && presets.SelectedIndex > 0)
            {
                var gains = EqualizerSettings.Presets[presets.SelectedIndex - 1].Gains;
                for (var i = 0; i < 10; i++)
                {
                    ((Slider)bands[i].Children[1]).Value = gains[i];
                }

                ((Slider)preamp.Children[1]).Value = 0;
                enabled.IsOn = true;
            }
            else if (!fromPreset)
            {
                presets.SelectedIndex = 0;
            }

            updating = false;
            _settings = _settings with
            {
                EqualizerEnabled = enabled.IsOn,
                EqualizerPreamp = ((Slider)preamp.Children[1]).Value,
                EqualizerGains = [.. bands.Select(band => ((Slider)band.Children[1]).Value)],
                EqualizerPreset = presets.SelectedIndex > 0 ? EqualizerSettings.Presets[presets.SelectedIndex - 1].Name : null,
                Stereo = (StereoChoice)stereo.SelectedIndex,
                Loudness = (LoudnessChoice)loudness.SelectedIndex,
            };
            _player.Settings = _settings;
        }

        enabled.Toggled += (_, _) => Apply(fromPreset: false);
        presets.SelectionChanged += (_, _) => Apply(fromPreset: true);
        stereo.SelectionChanged += (_, _) => Apply(fromPreset: false);
        loudness.SelectionChanged += (_, _) => Apply(fromPreset: false);
        foreach (var slider in bands.Append(preamp).Select(band => (Slider)band.Children[1]))
        {
            slider.ValueChanged += (_, _) => Apply(fromPreset: false);
        }

        var sliders = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        sliders.Children.Add(preamp);
        foreach (var band in bands)
        {
            sliders.Children.Add(band);
        }

        var content = new StackPanel { Spacing = 12 };
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        top.Children.Add(enabled);
        top.Children.Add(presets);
        content.Children.Add(top);
        content.Children.Add(sliders);
        content.Children.Add(stereo);
        content.Children.Add(loudness);

        await Ask("Effects", new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto }, null);
        SaveSettings();
    }

    /// <summary>A vertical slider from -20 to +20 dB with its label above it.</summary>
    private static StackPanel Band(string label, double value)
    {
        var slider = new Slider
        {
            Orientation = Orientation.Vertical,
            Minimum = -EqualizerSettings.Limit,
            Maximum = EqualizerSettings.Limit,
            StepFrequency = 0.5,
            Value = value,
            Height = 180,
            TickPlacement = TickPlacement.Outside,
            TickFrequency = 10,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider, label + " gain");
        var panel = new StackPanel { Spacing = 4, Width = 44 };
        panel.Children.Add(new TextBlock { Text = label, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12 });
        panel.Children.Add(slider);
        var shown = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12 };
        void Show() => shown.Text = slider.Value.ToString("+0.#;-0.#;0", CultureInfo.InvariantCulture);
        slider.ValueChanged += (_, _) => Show();
        Show();
        panel.Children.Add(shown);
        return panel;
    }
}
