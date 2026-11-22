using Rex.Media.AppCore.Design;
using Rex.Media.AppCore.Player;
using Rex.Media.Settings;

namespace Rex.Media.App.Mac;

/// <summary>
/// The visualisations' settings (AU-18), in the order the Windows dialog has them: choose one, and
/// its own settings appear (its colours and sensitivity, the beat meter where it has one, and what
/// only it has), each change showing at once on the stage and kept as it is made. A setting that
/// counts only with another (its own colour, with One colour) shows only then. The camera scenes are
/// allowed here, and "Restore defaults" at the foot puts back the one visualisation shown.
/// </summary>
public sealed partial class MainWindow
{
    private NSWindow? _musicVisualizerWindow;
    private NSPopUpButton? _musicVisualizerChoice;
    private NSSlider? _musicVisualizerSensitivity;
    private NSButton? _musicVisualizerMeter;
    private NSButton? _musicVisualizerCamera;
    private RexLabel? _musicVisualizerNote;
    private NSStackView? _musicVisualizerOptions;
    private readonly List<VisualizerChoice> _macVisualizerChoices = [];
    private readonly List<(NSView Row, string Key)> _musicVisualizerRows = [];

    private static readonly (string Name, int Rgb)[] MusicColors =
    [
        ("White", 0xFFFFFF), ("Yellow", 0xFFFF00), ("Light grey", 0xC0C0C0), ("Grey", 0x808080), ("Black", 0x000000),
        ("Red", 0xFF0000), ("Scarlet", 0xFF3B30), ("Green", 0x00FF00), ("Cyan", 0x00FFFF), ("Blue", 0x0000FF), ("Magenta", 0xFF00FF),
    ];

    private void ShowMusicVisualizers()
    {
        if (_musicVisualizerWindow is null)
        {
            BuildMusicVisualizerWindow();
        }

        var index = _macVisualizerChoices.IndexOf(_settings.Visualizer);
        _musicVisualizerChoice!.SelectItem(index < 0 ? 0 : index);
        LoadMusicVisualizerOptions();
        _musicVisualizerWindow!.MakeKeyAndOrderFront(null);
    }

    private void BuildMusicVisualizerWindow()
    {
        var window = new NSWindow(new CGRect(0, 0, 520, 600),
            NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable, NSBackingStore.Buffered, false)
        {
            Title = "Audio visualisations - rexplayer",
            MinSize = new CGSize(420, 360),
        };
        window.ReleaseWhenClosed(false);
        MacTheme.Style(window);

        var heading = MacTheme.Label("Visualisation settings", 4, DesignRole.TextPrimary, semibold: true);
        _musicVisualizerChoice = new NSPopUpButton(new CGRect(0, 0, 460, 28), false)
        {
            TranslatesAutoresizingMaskIntoConstraints = false,
            ToolTip = "Choose a sound-reactive scene",
        };
        ((NSView)_musicVisualizerChoice).AccessibilityLabel = "Visualisation";
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
        var chooser = MacTheme.Stack(true, 1, MacTheme.Label("Visualisation", 2, DesignRole.TextPrimary), _musicVisualizerChoice);
        _musicVisualizerNote = MacTheme.WrappingLabel("", 2, DesignRole.TextSecondary);

        // The two settings the window always has; the rest are made for the visualisation shown.
        _musicVisualizerSensitivity = NSSlider.FromValue(1, 0.25, 4, () => SetMusicVisualizerOption(VisualizerOptions.Sensitivity, _musicVisualizerSensitivity!.DoubleValue));
        _musicVisualizerSensitivity.TranslatesAutoresizingMaskIntoConstraints = false;
        ((NSView)_musicVisualizerSensitivity).AccessibilityLabel = "Visualisation sensitivity";
        _musicVisualizerMeter = NSButton.CreateCheckbox("Show the beat meter (what the music is heard as)",
            () => SetMusicVisualizerOption(VisualizerOptions.Meter, IsChecked(_musicVisualizerMeter!) ? 1 : 0));
        ((NSView)_musicVisualizerMeter).AccessibilityLabel = "Show the beat meter";

        _musicVisualizerOptions = MacTheme.Stack(true, 3);
        var document = new MusicSettingsDocument { TranslatesAutoresizingMaskIntoConstraints = false };
        document.AddSubview(_musicVisualizerOptions);
        var scroll = new NSScrollView
        {
            HasVerticalScroller = true,
            DrawsBackground = false,
            BorderType = NSBorderType.NoBorder,
            TranslatesAutoresizingMaskIntoConstraints = false,
            DocumentView = document,
        };
        scroll.AccessibilityLabel = "Settings of this visualisation";
        document.TopAnchor.ConstraintEqualTo(scroll.ContentView.TopAnchor).Active = true;
        document.LeadingAnchor.ConstraintEqualTo(scroll.ContentView.LeadingAnchor).Active = true;
        document.WidthAnchor.ConstraintEqualTo(scroll.ContentView.WidthAnchor).Active = true;
        _musicVisualizerOptions.TopAnchor.ConstraintEqualTo(document.TopAnchor).Active = true;
        _musicVisualizerOptions.LeadingAnchor.ConstraintEqualTo(document.LeadingAnchor).Active = true;
        _musicVisualizerOptions.TrailingAnchor.ConstraintEqualTo(document.TrailingAnchor, -MacTheme.Space(3)).Active = true;
        _musicVisualizerOptions.BottomAnchor.ConstraintEqualTo(document.BottomAnchor).Active = true;

        _musicVisualizerCamera = NSButton.CreateCheckbox("Allow camera-based scenes", () =>
        {
            _settings = _settings with { CameraAllowed = IsChecked(_musicVisualizerCamera!) };
            if (!_settings.CameraAllowed) StopMacVisualizerCamera();
            SaveMusicSettings();
            WakeMusicVisualizer();
        });
        ((NSView)_musicVisualizerCamera).AccessibilityLabel = "Allow camera-based scenes";
        _musicVisualizerCamera.ToolTip = "Camera scenes keep live pictures in memory only and stop the camera when another scene is chosen";
        var reset = MacTheme.TextButton("Restore defaults", () =>
        {
            var choice = _settings.Visualizer;
            _settings = _settings with { VisualOptions = VisualizerOptions.Reset(_settings.VisualOptions, choice) };
            SaveMusicSettings();
            LoadMusicVisualizerOptions();
            WakeMusicVisualizer();
        }, label: "Restore defaults for this visualisation");
        reset.ToolTip = "Only this visualisation's settings are reset. Other visualisations keep their saved values.";

        var root = MacTheme.Stack(true, 3, heading, chooser, _musicVisualizerNote, scroll, _musicVisualizerCamera, reset);
        root.EdgeInsets = new NSEdgeInsets(MacTheme.Space(4), MacTheme.Space(4), MacTheme.Space(4), MacTheme.Space(4));
        MacTheme.Fill(window.ContentView!, root);
        foreach (var wide in new NSView[] { chooser, _musicVisualizerNote, scroll })
        {
            wide.WidthAnchor.ConstraintEqualTo(root.WidthAnchor, 1, -2 * MacTheme.Space(4)).Active = true;
        }

        _musicVisualizerChoice.WidthAnchor.ConstraintEqualTo(chooser.WidthAnchor).Active = true;
        scroll.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Vertical);
        scroll.HeightAnchor.ConstraintGreaterThanOrEqualTo(120).Active = true;
        _musicVisualizerWindow = window;
        window.Center();
    }

    private void SelectMusicVisualizer()
    {
        var index = (int)(_musicVisualizerChoice?.IndexOfSelectedItem ?? -1);
        if (index < 0 || index >= _macVisualizerChoices.Count) return;
        _settings = _settings with { Visualizer = _macVisualizerChoices[index] };
        SaveMusicSettings();
        ApplyMusicVisualizer();
        Say("Audio visualisation: " + Visualizers.Name(_settings.Visualizer));
    }

    /// <summary>Shows the settings of the visualisation chosen now, each control reading what is saved.</summary>
    private void LoadMusicVisualizerOptions()
    {
        if (_musicVisualizerOptions is not { } panel || _musicVisualizerSensitivity is null || _musicVisualizerMeter is null
            || _musicVisualizerCamera is null || _musicVisualizerNote is null)
        {
            return;
        }

        var shown = _settings.Visualizer;
        _musicVisualizerNote.StringValue = shown switch
        {
            VisualizerChoice.Strobe => "Flashing light can trigger seizures in people with photosensitive epilepsy. Three flashes a second is the most guidelines allow.",
            VisualizerChoice.Silhouette => "Uses your camera to draw you. Nothing is recorded, kept or sent anywhere; the camera is on only while this shows.",
            VisualizerChoice.BeatEdit => "Edits your camera live to the music, or the song's cover when there is no camera. Nothing is recorded, kept or sent anywhere. Flashes never come more than three times a second.",
            VisualizerChoice.Vinyl => "The groove is the music itself, and the label is the song's cover when it has one.",
            VisualizerChoice.Off => "No visualisation shows while music plays.",
            _ => "",
        };
        _musicVisualizerNote.Hidden = _musicVisualizerNote.StringValue.Length == 0;

        foreach (var old in panel.ArrangedSubviews)
        {
            panel.RemoveArrangedSubview(old);
            old.RemoveFromSuperview();
        }

        _musicVisualizerRows.Clear();
        var options = _settings.VisualOptions;
        _musicVisualizerSensitivity.DoubleValue = VisualizerOptions.Number(options, shown, VisualizerOptions.Sensitivity);
        // Only rendered scenes have a beat meter; asking a classic one for it throws.
        var metered = VisualizerOptions.For(shown).Any(option => option.Key == VisualizerOptions.Meter);
        _musicVisualizerMeter.State = State(metered && VisualizerOptions.Toggle(options, shown, VisualizerOptions.Meter));
        _musicVisualizerMeter.Enabled = metered;
        _musicVisualizerCamera.State = State(_settings.CameraAllowed);
        if (shown == VisualizerChoice.Off)
        {
            return;
        }

        foreach (var option in VisualizerOptions.For(shown))
        {
            var row = MusicVisualizerRow(option, options, shown);
            _musicVisualizerRows.Add((row, option.Key));
            panel.AddArrangedSubview(row);
            row.WidthAnchor.ConstraintEqualTo(panel.WidthAnchor).Active = true;
        }

        ShowMusicVisualizerRowsThatApply();
    }

    /// <summary>One setting's control: a checkbox, a list of choices or colours, or a slider under its name.</summary>
    private NSView MusicVisualizerRow(VisualOption option, IReadOnlyDictionary<string, string> options, VisualizerChoice shown)
    {
        var name = Visualizers.Name(shown);
        switch (option.Kind)
        {
            case VisualOptionKind.Toggle:
                if (option.Key == VisualizerOptions.Meter)
                {
                    return _musicVisualizerMeter!;
                }

                NSButton? toggle = null;
                toggle = NSButton.CreateCheckbox(option.Label, () => SetMusicVisualizerOption(option.Key, IsChecked(toggle!) ? 1 : 0));
                toggle.State = State(VisualizerOptions.Toggle(options, shown, option.Key));
                ((NSView)toggle).AccessibilityLabel = option.Label;
                toggle.ToolTip = "Toggle " + option.Label + " for " + name;
                return toggle;
            case VisualOptionKind.Choice:
                // The accent the stage draws in is the system's own on a Mac.
                var names = option.Choices!.Select(text => text == "Windows' accent" ? "macOS accent" : text).ToArray();
                var choice = MusicVisualizerPopUp(option.Label, "Choose " + option.Label + " for " + name, names,
                    VisualizerOptions.Choice(options, shown, option.Key));
                choice.Activated += (_, _) => SetMusicVisualizerOption(option.Key, (int)choice.IndexOfSelectedItem);
                return MusicVisualizerLabelled(option.Label, choice);
            case VisualOptionKind.Color:
                var rgb = VisualizerOptions.Rgb(options, shown, option.Key);
                var colors = MusicColors.ToList();
                if (!colors.Any(color => color.Rgb == rgb))
                {
                    colors.Insert(0, ($"Custom (#{rgb:X6})", rgb));
                }

                var color = MusicVisualizerPopUp(option.Label, "Choose " + option.Label + " for " + name,
                    [.. colors.Select(entry => entry.Name)], colors.FindIndex(entry => entry.Rgb == rgb));
                color.Activated += (_, _) => SetMusicVisualizerOption(option.Key, colors[Math.Max(0, (int)color.IndexOfSelectedItem)].Rgb);
                return MusicVisualizerLabelled(option.Label, color);
            default:
                NSSlider slider;
                if (option.Key == VisualizerOptions.Sensitivity)
                {
                    slider = _musicVisualizerSensitivity!;
                }
                else
                {
                    var whole = option.Maximum - option.Minimum >= 10;
                    NSSlider? made = null;
                    made = NSSlider.FromValue(VisualizerOptions.Number(options, shown, option.Key), option.Minimum, option.Maximum,
                        () => SetMusicVisualizerOption(option.Key, whole ? Math.Round(made!.DoubleValue) : made!.DoubleValue));
                    made.TranslatesAutoresizingMaskIntoConstraints = false;
                    ((NSView)made).AccessibilityLabel = option.Label;
                    slider = made;
                }

                slider.ToolTip = $"{option.Label}, from {option.Minimum} to {option.Maximum}, for {name}";
                return MusicVisualizerLabelled(option.Label, slider);
        }
    }

    private static NSPopUpButton MusicVisualizerPopUp(string label, string help, string[] items, int selected)
    {
        var popup = new NSPopUpButton(new CGRect(0, 0, 320, 28), false) { TranslatesAutoresizingMaskIntoConstraints = false, ToolTip = help };
        ((NSView)popup).AccessibilityLabel = label;
        foreach (var item in items)
        {
            popup.AddItem(item);
        }

        popup.SelectItem(Math.Max(0, selected));
        return popup;
    }

    /// <summary>A control under its name, as wide as the window, as the Windows dialog heads its fields.</summary>
    private static NSStackView MusicVisualizerLabelled(string label, NSView control)
    {
        var row = MacTheme.Stack(true, 1, MacTheme.Label(label, 2, DesignRole.TextPrimary), control);
        control.WidthAnchor.ConstraintEqualTo(row.WidthAnchor).Active = true;
        return row;
    }

    /// <summary>Offers only the settings that count with the others as they are: its own colour only with One colour.</summary>
    private void ShowMusicVisualizerRowsThatApply()
    {
        var shown = _settings.Visualizer;
        foreach (var (row, key) in _musicVisualizerRows)
        {
            row.Hidden = !VisualizerOptions.Applies(_settings.VisualOptions, shown, key);
        }
    }

    /// <summary>Keeps one setting of the visualisation shown, and shows it on the stage at once.</summary>
    private void SetMusicVisualizerOption(string key, double value)
    {
        var choice = _settings.Visualizer;
        if (!VisualizerOptions.For(choice).Any(option => option.Key == key))
        {
            return;
        }

        _settings = _settings with { VisualOptions = VisualizerOptions.With(_settings.VisualOptions, choice, key, value) };
        SaveMusicSettings();
        if (key == VisualizerOptions.Colors)
        {
            ShowMusicVisualizerRowsThatApply();
        }

        WakeMusicVisualizer();
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

    /// <summary>A flipped document, so the settings start at the top of their scroll view.</summary>
    private sealed class MusicSettingsDocument : NSView
    {
        public override bool IsFlipped => true;
    }
}
