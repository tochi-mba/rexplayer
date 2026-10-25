using Rex.Media.AppCore.Player;
using Rex.Media.AppCore.Visuals;
using Rex.Media.Settings;

namespace Rex.Media.App.Mac;

/// <summary>Opt-in Mac settings for the shared non-camera visualisation scenes.</summary>
public sealed partial class MainWindow
{
    private NSWindow? _musicVisualizerWindow;
    private NSPopUpButton? _musicVisualizerChoice;
    private NSSlider? _musicVisualizerSensitivity;
    private NSButton? _musicVisualizerMeter;
    private readonly List<VisualizerChoice> _macVisualizerChoices = [];

    private void ShowMusicVisualizers()
    {
        if (_musicVisualizerWindow is null)
        {
            _musicVisualizerWindow = new NSWindow(new CGRect(0, 0, 560, 320),
                NSWindowStyle.Titled | NSWindowStyle.Closable, NSBackingStore.Buffered, false)
            {
                Title = "Audio visualisations - rexplayer",
            };
            _musicVisualizerWindow.ReleaseWhenClosed(false);
            var heading = NSTextField.CreateLabel("Visualise your music");
            heading.Font = NSFont.SystemFontOfSize(22, NSFontWeight.Semibold)!;
            var explanation = NSTextField.CreateWrappingLabel(
                "Choose a classic visualisation or one of the portable sound-reactive scenes. " +
                "Effects that need the camera are not offered by this Mac panel.");
            explanation.TextColor = NSColor.SecondaryLabel;
            _musicVisualizerChoice = new NSPopUpButton(new CGRect(0, 0, 460, 28), false);
            foreach (var choice in Enum.GetValues<VisualizerChoice>())
            {
                if (MacVisualizerNeedsCamera(choice)) continue;
                _macVisualizerChoices.Add(choice);
                _musicVisualizerChoice.AddItem(Visualizers.Name(choice));
            }

            _musicVisualizerChoice.Activated += (_, _) =>
            {
                SelectMusicVisualizer();
                LoadMusicVisualizerOptions();
            };
            _musicVisualizerSensitivity = NSSlider.FromValue(1, 0.25, 4, ApplyMusicVisualizerOptions);
            _musicVisualizerSensitivity.WidthAnchor.ConstraintEqualTo(260).Active = true;
            _musicVisualizerMeter = NSButton.CreateCheckbox(
                "Show a live beat meter with rendered scenes", ApplyMusicVisualizerOptions);
            var reset = NSButton.CreateButton("Reset this effect", () =>
            {
                var choice = _settings.Visualizer;
                _settings = _settings with
                {
                    VisualOptions = VisualizerOptions.Reset(_settings.VisualOptions, choice),
                };
                SaveMusicSettings();
                LoadMusicVisualizerOptions();
            });
            var stack = NSStackView.FromViews([
                heading, explanation,
                EffectChoiceRow("Visualisation", _musicVisualizerChoice),
                StyleSliderRow("Sensitivity", _musicVisualizerSensitivity),
                _musicVisualizerMeter, reset,
            ]);
            stack.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            stack.Alignment = NSLayoutAttribute.Leading;
            stack.Spacing = 14;
            stack.EdgeInsets = new NSEdgeInsets(20, 24, 20, 24);
            _musicVisualizerWindow.ContentView = stack;
            _musicVisualizerWindow.Center();
        }

        var index = _macVisualizerChoices.IndexOf(_settings.Visualizer);
        _musicVisualizerChoice!.SelectItem(index < 0 ? 0 : index);
        LoadMusicVisualizerOptions();
        _musicVisualizerWindow.MakeKeyAndOrderFront(null);
    }

    private void SelectMusicVisualizer()
    {
        var index = (int)(_musicVisualizerChoice?.IndexOfSelectedItem ?? -1);
        if (index < 0 || index >= _macVisualizerChoices.Count) return;
        _settings = _settings with { Visualizer = _macVisualizerChoices[index] };
        _musicScene = null;
        SaveMusicSettings();
        RefreshMusicPosition();
        Say("Audio visualisation: " + Visualizers.Name(_settings.Visualizer));
    }

    private void LoadMusicVisualizerOptions()
    {
        if (_musicVisualizerSensitivity is null || _musicVisualizerMeter is null) return;
        _musicVisualizerSensitivity.DoubleValue = VisualizerOptions.Number(
            _settings.VisualOptions, _settings.Visualizer, VisualizerOptions.Sensitivity);
        _musicVisualizerMeter.State = State(VisualizerOptions.Toggle(
            _settings.VisualOptions, _settings.Visualizer, VisualizerOptions.Meter));
        _musicVisualizerMeter.Enabled = VisualScene.Draws(_settings.Visualizer);
    }

    private void ApplyMusicVisualizerOptions()
    {
        if (_musicVisualizerSensitivity is null || _musicVisualizerMeter is null) return;
        var choice = _settings.Visualizer;
        var options = VisualizerOptions.With(_settings.VisualOptions, choice,
            VisualizerOptions.Sensitivity, _musicVisualizerSensitivity.DoubleValue);
        options = VisualizerOptions.With(options, choice, VisualizerOptions.Meter,
            IsChecked(_musicVisualizerMeter) ? 1 : 0);
        _settings = _settings with { VisualOptions = options };
        SaveMusicSettings();
    }

    private void DisposeMusicVisualizers()
    {
        _musicVisualizerWindow?.Close();
        _musicVisualizerWindow?.Dispose();
        _musicVisualizerWindow = null;
    }
}
