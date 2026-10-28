using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Player;
using Rex.Media.Settings;

namespace Rex.Media.App;

/// <summary>
/// The visualisations' settings (AU-18): choose one, and its own settings appear (its colours and
/// sensitivity, and what only it has), each change showing at once behind the dialog; Save keeps
/// them, Cancel puts everything back as it was. A setting that counts only with another (its own
/// colour, with One colour) shows only then; every field is as wide as the dialog; and the dialog
/// fits the window, its settings scrolling under a soft foot when they do not all fit.
/// </summary>
public sealed partial class MainWindow
{
    private async Task ShowVisualizerSettingsAsync()
    {
        var before = _settings;
        var options = new Dictionary<string, string>(_settings.VisualOptions, StringComparer.Ordinal);
        var activeBefore = ActiveVisualizer;
        var shown = activeBefore == VisualizerChoice.Off ? VisualizerChoice.Spectrum : activeBefore;
        var choices = Enum.GetValues<VisualizerChoice>().Where(choice => choice != VisualizerChoice.Off).ToList();

        var which = new ComboBox { Header = "Visualisation", ItemsSource = choices.Select(Visualizers.Name).ToList(), SelectedIndex = choices.IndexOf(shown), HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(which, "VisualizerChoice");
        AutomationProperties.SetName(which, "Visualisation");
        var panel = new StackPanel { Spacing = 12 };
        var note = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = RexTheme.Brush("TextFillColorSecondaryBrush") };

        // At the foot of the dialog, beside its own buttons: it puts back the one visualisation shown.
        var reset = new Button { Content = "Restore defaults" };
        AutomationProperties.SetAutomationId(reset, "VisualizerReset");
        AutomationProperties.SetName(reset, "Restore defaults for this visualisation");
        AutomationProperties.SetHelpText(reset, "Only this visualisation's settings are reset. Other visualisations keep their saved values.");

        // Each setting's control, shown only while the setting counts with the others as they are.
        var controls = new List<(UIElement Control, string Key)>();
        void ShowWhatApplies()
        {
            foreach (var (control, key) in controls)
            {
                control.Visibility = VisualizerOptions.Applies(options, shown, key) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        // Shown behind the dialog as it changes.
        void Preview()
        {
            SetActiveVisualizer(shown);
            _settings = _settings with { VisualOptions = new Dictionary<string, string>(options, StringComparer.Ordinal) };
            if (UsesCamera(shown) && !_settings.CameraAllowed)
            {
                // The camera waits until it has been agreed to, after Save.
                SetActiveVisualizer(activeBefore);
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

            // Only the colours decide whether another setting counts.
            if (key == VisualizerOptions.Colors)
            {
                ShowWhatApplies();
            }

            Preview();
        }

        void Show()
        {
            panel.Children.Clear();
            controls.Clear();
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
                var control = Control(option);
                controls.Add((control, option.Key));
                panel.Children.Add(control);
            }

            ShowWhatApplies();
        }

        UIElement Control(VisualOption option)
        {
            var id = "Visual-" + option.Key;
            switch (option.Kind)
            {
                case VisualOptionKind.Toggle:
                    var toggle = new ToggleSwitch { Header = option.Label, IsOn = VisualizerOptions.Toggle(options, shown, option.Key) };
                    AutomationProperties.SetAutomationId(toggle, id);
                    AutomationProperties.SetName(toggle, option.Label);
                    AutomationProperties.SetHelpText(toggle, "Toggle " + option.Label + " for " + Visualizers.Name(shown));
                    toggle.Toggled += (_, _) => Set(option.Key, toggle.IsOn ? 1 : 0);
                    return toggle;
                case VisualOptionKind.Choice:
                    var choice = Choice(option.Label, [.. option.Choices!], VisualizerOptions.Choice(options, shown, option.Key));
                    choice.HorizontalAlignment = HorizontalAlignment.Stretch;
                    AutomationProperties.SetAutomationId(choice, id);
                    AutomationProperties.SetName(choice, option.Label);
                    AutomationProperties.SetHelpText(choice, "Choose " + option.Label + " for " + Visualizers.Name(shown));
                    choice.SelectionChanged += (_, _) => Set(option.Key, choice.SelectedIndex);
                    return choice;
                case VisualOptionKind.Color:
                    var color = ColorChoice(option.Label, VisualizerOptions.Rgb(options, shown, option.Key));
                    color.Box.HorizontalAlignment = HorizontalAlignment.Stretch;
                    AutomationProperties.SetAutomationId(color.Box, id);
                    AutomationProperties.SetName(color.Box, option.Label);
                    AutomationProperties.SetHelpText(color.Box, "Choose " + option.Label + " for " + Visualizers.Name(shown));
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
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                    };
                    AutomationProperties.SetAutomationId(slider, id);
                    AutomationProperties.SetName(slider, option.Label);
                    AutomationProperties.SetHelpText(slider, $"{option.Label}, from {option.Minimum} to {option.Maximum}, for {Visualizers.Name(shown)}");
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

        // The choice and its note on top, its settings scrolling below them when the window is too
        // short for all of them, and Restore defaults at the foot, always in view.
        var heading = new StackPanel { Spacing = 12 };
        heading.Children.Add(which);
        heading.Children.Add(note);
        var settings = WithScrollCue(new ScrollViewer
        {
            Content = panel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 16, 0),
        });
        var content = new Grid { RowSpacing = 16, Width = DialogWidth(Rex.Media.AppCore.Shell.DialogLayout.OrdinaryWidth), MaxHeight = DialogHeight(600) };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.Children.Add(heading);
        Grid.SetRow(settings, 1);
        content.Children.Add(settings);
        Grid.SetRow(reset, 2);
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
