using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Player;
using Rex.Media.Settings;

namespace Rex.Media.App;

/// <summary>
/// The visualisations' settings (AU-18): choose one, and its own settings appear (its colours and
/// sensitivity, and what only it has), each change showing at once behind the dialog; Save keeps
/// them, Cancel puts everything back as it was.
/// </summary>
public sealed partial class MainWindow
{
    private async Task ShowVisualizerSettingsAsync()
    {
        var before = _settings;
        var options = new Dictionary<string, string>(_settings.VisualOptions, StringComparer.Ordinal);
        var shown = _settings.Visualizer == VisualizerChoice.Off ? VisualizerChoice.Spectrum : _settings.Visualizer;
        var choices = Enum.GetValues<VisualizerChoice>().Where(choice => choice != VisualizerChoice.Off).ToList();

        var which = new ComboBox { Header = "Visualisation", ItemsSource = choices.Select(Visualizers.Name).ToList(), SelectedIndex = choices.IndexOf(shown), MinWidth = 260 };
        AutomationProperties.SetAutomationId(which, "VisualizerChoice");
        AutomationProperties.SetName(which, "Visualisation");
        var panel = new StackPanel { Spacing = 10 };
        var note = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 440, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] };
        var reset = new Button { Content = "Back to how this one came" };
        AutomationProperties.SetAutomationId(reset, "VisualizerReset");

        // Shown behind the dialog as it changes.
        void Preview()
        {
            _settings = _settings with { Visualizer = shown, VisualOptions = new Dictionary<string, string>(options, StringComparer.Ordinal) };
            if (UsesCamera(shown) && !_settings.CameraAllowed)
            {
                // The camera waits until it has been agreed to, after Save.
                _settings = _settings with { Visualizer = before.Visualizer };
            }

            ApplyVisualizer();
            BuildVisualizer();
        }

        void Set(string key, double value)
        {
            foreach (var pair in VisualizerOptions.With(options, shown, key, value))
            {
                options[pair.Key] = pair.Value;
            }

            Preview();
        }

        void Show()
        {
            panel.Children.Clear();
            note.Text = shown switch
            {
                VisualizerChoice.Strobe => "Flashing light can trigger seizures in people with photosensitive epilepsy. Three flashes a second is the most guidelines allow.",
                VisualizerChoice.Silhouette => "Uses your camera to draw you. Nothing is recorded, kept or sent anywhere; the camera is on only while this shows.",
                VisualizerChoice.BeatEdit => "Edits your camera live to the music, or the song's cover when there is no camera. Nothing is recorded, kept or sent anywhere. Flashes never come more than three times a second.",
                VisualizerChoice.Vinyl => "The groove is the music itself, and the label is the song's cover when it has one.",
                _ => "",
            };
            note.Visibility = note.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var option in VisualizerOptions.For(shown))
            {
                panel.Children.Add(Control(option));
            }
        }

        UIElement Control(VisualOption option)
        {
            var id = "Visual-" + option.Key;
            switch (option.Kind)
            {
                case VisualOptionKind.Toggle:
                    var toggle = new ToggleSwitch { Header = option.Label, IsOn = VisualizerOptions.Toggle(options, shown, option.Key) };
                    AutomationProperties.SetAutomationId(toggle, id);
                    toggle.Toggled += (_, _) => Set(option.Key, toggle.IsOn ? 1 : 0);
                    return toggle;
                case VisualOptionKind.Choice:
                    var choice = Choice(option.Label, [.. option.Choices!], VisualizerOptions.Choice(options, shown, option.Key));
                    AutomationProperties.SetAutomationId(choice, id);
                    choice.SelectionChanged += (_, _) => Set(option.Key, choice.SelectedIndex);
                    return choice;
                case VisualOptionKind.Color:
                    var color = ColorChoice(option.Label, VisualizerOptions.Rgb(options, shown, option.Key));
                    AutomationProperties.SetAutomationId(color.Box, id);
                    color.Box.SelectionChanged += (_, _) => Set(option.Key, color.Value());
                    return color.Box;
                default:
                    var whole = option.Maximum - option.Minimum >= 10;
                    var slider = new Slider
                    {
                        Header = option.Label,
                        Minimum = option.Minimum,
                        Maximum = option.Maximum,
                        StepFrequency = whole ? 1 : (option.Maximum - option.Minimum) / 100,
                        Value = VisualizerOptions.Number(options, shown, option.Key),
                        MinWidth = 320,
                    };
                    AutomationProperties.SetAutomationId(slider, id);
                    AutomationProperties.SetName(slider, option.Label);
                    slider.ValueChanged += (_, e) => Set(option.Key, whole ? Math.Round(e.NewValue) : e.NewValue);
                    return slider;
            }
        }

        which.SelectionChanged += (_, _) =>
        {
            shown = choices[Math.Max(0, which.SelectedIndex)];
            Show();
            Preview();
        };
        reset.Click += (_, _) =>
        {
            var kept = VisualizerOptions.Reset(options, shown);
            options.Clear();
            foreach (var pair in kept)
            {
                options[pair.Key] = pair.Value;
            }

            Show();
            Preview();
        };

        Show();
        var content = new StackPanel { Spacing = 12, MinWidth = 440 };
        content.Children.Add(which);
        content.Children.Add(note);
        content.Children.Add(new ScrollViewer { Content = panel, MaxHeight = 420, Padding = new Thickness(0, 0, 16, 0) });
        content.Children.Add(reset);
        var keep = await Ask("Visualisation settings", content, "Save");
        if (!keep)
        {
            _settings = before;
            _player.Settings = _settings;
            ApplyVisualizer();
            BuildVisualizer();
            return;
        }

        _settings = (before with { VisualOptions = options }).Normalize();
        _player.Settings = _settings;
        SaveSettings();
        await ChooseVisualizerAsync(shown);
    }
}
