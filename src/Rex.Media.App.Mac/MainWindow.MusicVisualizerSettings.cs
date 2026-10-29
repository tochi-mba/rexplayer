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
    private NSButton? _musicVisualizerCamera;
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
                "Choose a classic or rendered sound-reactive scene. Camera scenes keep live " +
                "pictures in memory only and stop the camera when another scene is selected.");
            explanation.TextColor = NSColor.SecondaryLabel;
            _musicVisualizerChoice = new NSPopUpButton(new CGRect(0, 0, 460, 28), false);
            ((NSView)_musicVisualizerChoice).AccessibilityLabel = "Visualisation";
            _musicVisualizerChoice.ToolTip = "Choose a sound-reactive scene";
            foreach (var choice in Enum.GetValues<VisualizerChoice>())
            {
                _macVisualizerChoices.Add(choice);
                _musicVisualizerChoice.AddItem(Visualizers.Name(choice));
            }

            _musicVisualizerChoice.Activated += (_, _) =>
            {
                SelectMusicVisualizer();
                LoadMusicVisualizerOptions();
            };
            _musicVisualizerSensitivity = NSSlider.FromValue(1, 0.25, 4, ApplyMusicVisualizerOptions);
            ((NSView)_musicVisualizerSensitivity).AccessibilityLabel = "Visualisation sensitivity";
            _musicVisualizerSensitivity.WidthAnchor.ConstraintEqualTo(260).Active = true;
            _musicVisualizerMeter = NSButton.CreateCheckbox(
                "Show a live beat meter with rendered scenes", ApplyMusicVisualizerOptions);
            _musicVisualizerCamera = NSButton.CreateCheckbox(
                "Allow camera-based scenes", ApplyMusicVisualizerOptions);
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
                _musicVisualizerMeter, _musicVisualizerCamera, reset,
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
        if (_musicVisualizerSensitivity is null || _musicVisualizerMeter is null || _musicVisualizerCamera is null) return;
        _musicVisualizerSensitivity.DoubleValue = VisualizerOptions.Number(
            _settings.VisualOptions, _settings.Visualizer, VisualizerOptions.Sensitivity);
        // Only rendered scenes have a beat meter; asking a classic scene for it throws.
        var drawn = VisualScene.Draws(_settings.Visualizer);
        _musicVisualizerMeter.State = State(drawn && VisualizerOptions.Toggle(
            _settings.VisualOptions, _settings.Visualizer, VisualizerOptions.Meter));
        _musicVisualizerMeter.Enabled = drawn;
        _musicVisualizerCamera.State = State(_settings.CameraAllowed);
    }

    private void ApplyMusicVisualizerOptions()
    {
        if (_musicVisualizerSensitivity is null || _musicVisualizerMeter is null || _musicVisualizerCamera is null) return;
        var choice = _settings.Visualizer;
        var options = VisualizerOptions.With(_settings.VisualOptions, choice,
            VisualizerOptions.Sensitivity, _musicVisualizerSensitivity.DoubleValue);
        if (VisualScene.Draws(choice))
        {
            options = VisualizerOptions.With(options, choice, VisualizerOptions.Meter,
                IsChecked(_musicVisualizerMeter) ? 1 : 0);
        }
        _settings = _settings with
        {
            VisualOptions = options,
            CameraAllowed = IsChecked(_musicVisualizerCamera),
        };
        if (!_settings.CameraAllowed) StopMacVisualizerCamera();
        SaveMusicSettings();
    }

    private Rex.Media.AppCore.Automation.AutomationReply MacVisualizerSmokeAction()
    {
        ShowMusicVisualizers();
        var all = Enum.GetValues<VisualizerChoice>();
        var camera = all.Where(MacVisualizerNeedsCamera).ToArray();
        var complete = _macVisualizerChoices.Distinct().Count() == all.Length
            && camera.Length > 0 && camera.All(_macVisualizerChoices.Contains)
            && _musicVisualizerCamera is { Enabled: true }
            && _musicVisualizerSensitivity is { Enabled: true }
            && _musicVisualizerMeter is not null;
        return complete
            ? new Rex.Media.AppCore.Automation.AutomationReply
            {
                Ok = true,
                Text = $"scenes={all.Length};camera={camera.Length};sensitivity=True;meter=True;options=True",
            }
            : Rex.Media.AppCore.Automation.AutomationReply.Failed(
                "The Mac visualisation chooser does not expose every scene and control.");
    }

    private void DisposeMusicVisualizers()
    {
        _musicVisualizerWindow?.Close();
        _musicVisualizerWindow?.Dispose();
        _musicVisualizerWindow = null;
    }
}
