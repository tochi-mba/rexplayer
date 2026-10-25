using Rex.Media.Settings;

namespace Rex.Media.App.Mac;

public sealed partial class MainWindow
{
    private NSWindow? _preferencesWindow;
    private NSButton? _preferencesHistory;
    private NSButton? _preferencesRestoreQueue;
    private NSButton? _preferencesEnqueue;
    private NSSlider? _preferencesMaxVolume;
    private NSSlider? _preferencesPictureSeconds;
    private NSTextField? _preferencesMaxVolumeValue;
    private NSTextField? _preferencesPictureSecondsValue;

    /// <summary>Shows the native Mac surface for the shared settings used by this shell.</summary>
    private void ShowPreferences()
    {
        if (_preferencesWindow is null)
        {
            BuildPreferences();
        }

        LoadPreferences();
        _preferencesWindow!.MakeKeyAndOrderFront(null);
    }

    private void BuildPreferences()
    {
        _preferencesWindow = new NSWindow(new CGRect(0, 0, 560, 440),
            NSWindowStyle.Titled | NSWindowStyle.Closable, NSBackingStore.Buffered, false)
        {
            Title = "Preferences - rexplayer",
        };
        _preferencesWindow.ReleaseWhenClosed(false);

        var heading = NSTextField.CreateLabel("Preferences");
        heading.Font = NSFont.SystemFontOfSize(24, NSFontWeight.Semibold)!;
        var explanation = NSTextField.CreateWrappingLabel(
            "These choices are shared with the player and saved for the next launch.");
        explanation.TextColor = NSColor.SecondaryLabel;

        _preferencesHistory = CheckBox("Remember recently played media and saved positions");
        _preferencesRestoreQueue = CheckBox("Restore the queue when rexplayer starts");
        _preferencesEnqueue = CheckBox("Files handed over by another launch join the queue");

        _preferencesMaxVolumeValue = NSTextField.CreateLabel("");
        _preferencesMaxVolumeValue.Font = NSFont.MonospacedDigitSystemFontOfSize(12, NSFontWeight.Regular)!;
        _preferencesMaxVolume = Slider(100, 200, UpdatePreferenceLabels);
        var volume = PreferenceRow("Loudest volume", _preferencesMaxVolume, _preferencesMaxVolumeValue);

        _preferencesPictureSecondsValue = NSTextField.CreateLabel("");
        _preferencesPictureSecondsValue.Font = NSFont.MonospacedDigitSystemFontOfSize(12, NSFontWeight.Regular)!;
        _preferencesPictureSeconds = Slider(1, 60, UpdatePreferenceLabels);
        var pictures = PreferenceRow("Show each picture for", _preferencesPictureSeconds, _preferencesPictureSecondsValue);

        var cancel = NSButton.CreateButton("Cancel", () => _preferencesWindow.OrderOut(null));
        var save = NSButton.CreateButton("Save", SavePreferences);
        save.KeyEquivalent = "\r";
        var actions = NSStackView.FromViews([cancel, save]);
        actions.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        actions.Spacing = 10;

        var stack = NSStackView.FromViews([
            heading,
            explanation,
            PreferencesHeading("Memory"),
            _preferencesHistory,
            _preferencesRestoreQueue,
            PreferencesHeading("Playback"),
            volume,
            pictures,
            PreferencesHeading("Opening media"),
            _preferencesEnqueue,
            actions,
        ]);
        stack.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
        stack.Alignment = NSLayoutAttribute.Leading;
        stack.Spacing = 14;
        stack.EdgeInsets = new NSEdgeInsets(24, 24, 24, 24);
        explanation.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
        volume.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
        pictures.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
        _preferencesWindow.ContentView = stack;
        _preferencesWindow.Center();
    }

    private static NSButton CheckBox(string title) => NSButton.CreateCheckbox(title, () => { });

    private static NSSlider Slider(double minimum, double maximum, Action changed)
    {
        var slider = NSSlider.FromValue(minimum, minimum, maximum, changed);
        slider.Continuous = true;
        slider.WidthAnchor.ConstraintGreaterThanOrEqualTo(250).Active = true;
        return slider;
    }

    private static NSTextField PreferencesHeading(string title)
    {
        var label = NSTextField.CreateLabel(title);
        label.Font = NSFont.SystemFontOfSize(13, NSFontWeight.Semibold)!;
        return label;
    }

    private static NSStackView PreferenceRow(string title, NSSlider slider, NSTextField value)
    {
        var label = NSTextField.CreateLabel(title);
        label.WidthAnchor.ConstraintEqualTo(150).Active = true;
        value.WidthAnchor.ConstraintEqualTo(74).Active = true;
        var row = NSStackView.FromViews([label, slider, value]);
        row.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        row.Spacing = 10;
        return row;
    }

    private void LoadPreferences()
    {
        _preferencesHistory!.State = State(_settings.KeepHistory);
        _preferencesRestoreQueue!.State = State(_settings.RestoreQueue);
        _preferencesEnqueue!.State = State(_settings.EnqueueFromSecondLaunch);
        _preferencesMaxVolume!.DoubleValue = _settings.MaxVolumePercent;
        _preferencesPictureSeconds!.DoubleValue = _settings.PictureSeconds;
        UpdatePreferenceLabels();
    }

    private void UpdatePreferenceLabels()
    {
        _preferencesMaxVolumeValue!.StringValue = $"{Math.Round(_preferencesMaxVolume!.DoubleValue):0} %";
        _preferencesPictureSecondsValue!.StringValue = $"{Math.Round(_preferencesPictureSeconds!.DoubleValue):0} sec";
    }

    private void SavePreferences()
    {
        _settings = (_settings with
        {
            KeepHistory = IsChecked(_preferencesHistory),
            RestoreQueue = IsChecked(_preferencesRestoreQueue),
            EnqueueFromSecondLaunch = IsChecked(_preferencesEnqueue),
            MaxVolumePercent = (int)Math.Round(_preferencesMaxVolume!.DoubleValue),
            PictureSeconds = (int)Math.Round(_preferencesPictureSeconds!.DoubleValue),
        }).Normalize();
        _player.Settings = _settings;
        _volume.MaxValue = _settings.MaxVolumePercent / 100.0;
        _player.SetVolume(Math.Min(_player.Volume, _volume.MaxValue));
        try
        {
            SettingsStore.Save(_roots.SettingsPath, _settings);
            Say("Preferences saved");
            _preferencesWindow!.OrderOut(null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning(LogSource, "Preferences could not be saved: " + ex.Message);
            Say("Preferences could not be saved.");
        }
    }

    private static NSCellStateValue State(bool value) => value ? NSCellStateValue.On : NSCellStateValue.Off;

    private static bool IsChecked(NSButton? button) => button?.State == NSCellStateValue.On;

    private void DisposePreferences()
    {
        _preferencesWindow?.Close();
        _preferencesWindow?.Dispose();
        _preferencesWindow = null;
    }
}
