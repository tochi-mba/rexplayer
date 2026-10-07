using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.Settings;

namespace Rex.Media.App;

public sealed partial class MainWindow
{
    /// <summary>
    /// Preferences (UI-10): the settings people change most, in three groups. Nothing applies until
    /// Save, and what is saved is normalized, so no value can leave the player in a state it cannot use.
    /// </summary>
    private async Task ShowPreferencesAsync()
    {
        var s = _settings;
        var veryShort = Number("Very short jump (seconds)", s.VeryShortJumpSeconds, 1, 3600);
        var shortJump = Number("Short jump (seconds)", s.ShortJumpSeconds, 1, 3600);
        var medium = Number("Medium jump (seconds)", s.MediumJumpSeconds, 1, 3600);
        var longJump = Number("Long jump (seconds)", s.LongJumpSeconds, 1, 3600);
        var step = Number("Volume step (%)", s.VolumeStepPercent, 1, 25);
        var maxVolume = Number("Loudest volume (%)", s.MaxVolumePercent, 100, 200);
        var theme = Choice("Theme", ["Windows' choice", "Light", "Dark"], (int)s.Theme);
        var onTop = Choice("Always on top", ["Never", "Always", "While playing"], (int)s.AlwaysOnTop);
        var hide = Number("Hide the full-screen controls after (seconds)", s.ControlsHideSeconds, 0.5, 10);
        var messages = Check("Show messages over the picture", s.OnScreenMessages);
        var title = Number("Show the title when an item starts for (seconds, 0 for never)", s.TitleSeconds, 0, 30);
        var single = Check("Use one window: open files in the player already running", s.SingleInstance);
        var enqueue = Check("Files opened that way join the playlist instead of playing", s.EnqueueFromSecondLaunch);
        var updates = Choice("Look for new versions", ["Never", "Daily", "Weekly"], (int)s.UpdateChecks);

        var content = new StackPanel { Spacing = 8, MinWidth = 460 };
        content.Children.Add(Heading("Playback"));
        foreach (var control in new UIElement[] { veryShort, shortJump, medium, longJump, step, maxVolume })
        {
            content.Children.Add(control);
        }

        content.Children.Add(Heading("Window"));
        foreach (var control in new UIElement[] { theme, onTop, hide, messages, title })
        {
            content.Children.Add(control);
        }

        content.Children.Add(Heading("rexplayer"));
        foreach (var control in new UIElement[] { single, enqueue, updates })
        {
            content.Children.Add(control);
        }

        if (!await Ask("Preferences", new ScrollViewer { Content = content, MaxHeight = 560 }, "Save"))
        {
            return;
        }

        _settings = (s with
        {
            VeryShortJumpSeconds = (int)veryShort.Value,
            ShortJumpSeconds = (int)shortJump.Value,
            MediumJumpSeconds = (int)medium.Value,
            LongJumpSeconds = (int)longJump.Value,
            VolumeStepPercent = (int)step.Value,
            MaxVolumePercent = (int)maxVolume.Value,
            Theme = (ThemeChoice)theme.SelectedIndex,
            AlwaysOnTop = (AlwaysOnTop)onTop.SelectedIndex,
            ControlsHideSeconds = hide.Value,
            OnScreenMessages = messages.IsChecked == true,
            TitleSeconds = title.Value,
            SingleInstance = single.IsChecked == true,
            EnqueueFromSecondLaunch = enqueue.IsChecked == true,
            UpdateChecks = (UpdateCadence)updates.SelectedIndex,
        }).Normalize();
        _player.Settings = _settings;
        _player.SetVolume(_player.Volume);
        _controlsTimer.Interval = TimeSpan.FromSeconds(_settings.ControlsHideSeconds);
        ApplyTheme();
        ApplyAlwaysOnTop();
        ShowState();
        SaveSettings();
        Say("Preferences saved");
    }

    /// <summary>
    /// The first time rexplayer opens, a welcome with the one privacy choice it has (UI-14); after an
    /// update, what changed (UI-15). Each is shown once.
    /// </summary>
    private async Task GreetAsync()
    {
        if (!_settings.FirstRunDone)
        {
            var updates = Check("Look for new versions once a week", true);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(updates, "WelcomeUpdates");
            var welcome = new StackPanel { Spacing = 12, MaxWidth = 480 };
            welcome.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "Drop media on the window, or press Ctrl+O to open a file. Every command and its shortcut is in the menus, and F1 opens the help.",
            });
            welcome.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "rexplayer has no account and sends nothing about you. The only time it goes online by itself is to look for a new version, and only if you allow it.",
            });
            welcome.Children.Add(updates);
            await Ask("Welcome to rexplayer", welcome, null, "Start");
            _settings = _settings with { FirstRunDone = true, UpdateChecks = updates.IsChecked == true ? UpdateCadence.Weekly : UpdateCadence.Off, LastSeenVersion = Version };
            SaveSettings();
            return;
        }

        if (_settings.LastSeenVersion != Version)
        {
            _settings = _settings with { LastSeenVersion = Version };
            SaveSettings();
            using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("CHANGELOG.md");
            if (stream is not null && Rex.Media.AppCore.Changelog.Section(new StreamReader(stream).ReadToEnd(), Version) is { } changes)
            {
                var text = new TextBlock { Text = changes, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(text, "WhatsNewText");
                await Ask($"What's new in rexplayer {Version}", new ScrollViewer { Content = text, MaxHeight = 480 }, null);
            }
        }
    }

    private static TextBlock Heading(string text) =>
        new() { Text = text, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], Margin = new Thickness(0, 12, 0, 0) };

    private static NumberBox Number(string label, double value, double minimum, double maximum)
    {
        var box = new NumberBox { Header = label, Value = value, Minimum = minimum, Maximum = maximum, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, label);
        return box;
    }

    private static ComboBox Choice(string label, string[] options, int selected)
    {
        var box = new ComboBox { Header = label, ItemsSource = options, SelectedIndex = Math.Clamp(selected, 0, options.Length - 1), MinWidth = 220 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, label);
        return box;
    }

    private static CheckBox Check(string label, bool value) => new() { Content = label, IsChecked = value };
}
