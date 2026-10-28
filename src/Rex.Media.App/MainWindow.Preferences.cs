using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.Settings;

namespace Rex.Media.App;

public sealed partial class MainWindow
{
    /// <summary>
    /// Preferences (UI-10): the settings people change most, in groups. Nothing applies until
    /// Save, and what is saved is normalized, so no value can leave the player in a state it cannot use.
    /// </summary>
    private async Task ShowPreferencesAsync()
    {
        var s = _settings;
        Func<Task>? requestedEditor = null;

        // What to show once Preferences has closed: WinUI shows one dialog at a time.

        // Not a live region: it changes with every step of a download, and the updater announces
        // its changes of state itself.
        var updateStatus = new TextBlock { Text = UpdatePanelNow().Line, TextWrapping = TextWrapping.Wrap };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(updateStatus, "PreferencesUpdateStatus");

        // Only the section shown is built; the others are built when first chosen (or searched).
        var sections = new Rex.Media.AppCore.LazySections<StackPanel, PlayerSettings> { Built = WrapLabels };
        AddPreferenceSections(sections, updateStatus, edit => requestedEditor = edit);
        var names = sections.Names;

        // The status line follows a check or a download while Preferences is open.
        _updateStatusText = updateStatus;
        bool saved;
        try
        {
            // The frame: the sections, the search, the page shown and the dialog, fitted to the window.
            var (dialog, stopFitting) = PreferencesDialog(sections, names);
            try
            {
                do
                {
                    requestedEditor = null;
                    saved = await ShowDialogAsync(dialog) == ContentDialogResult.Primary;
                    if (requestedEditor is { } edit && !_closed)
                    {
                        await edit();
                    }
                }
                while (requestedEditor is not null && !_closed);
            }
            finally
            {
                stopFitting();
            }
        }
        finally
        {
            _updateStatusText = null;
            ShowUpdates();
        }

        if (!saved)
        {
            return;
        }

        // Only the sections that were shown can hold edits; the rest keep their settings as they are.
        var next = sections.Apply(_settings).Normalize();
        var artworkChanged = s.GenerateAudioArtwork != next.GenerateAudioArtwork
            || s.AudioArtworkStyle != next.AudioArtworkStyle
            || s.AudioArtworkColor != next.AudioArtworkColor
            || s.AudioArtworkDetail != next.AudioArtworkDetail
            || s.AudioArtworkContrast != next.AudioArtworkContrast
            || s.AudioArtworkUsesIdentity != next.AudioArtworkUsesIdentity;
        _settings = next;
        _player.Settings = _settings;
        if ((s.VeryShortJumpSeconds, s.ShortJumpSeconds, s.MediumJumpSeconds, s.LongJumpSeconds) != (next.VeryShortJumpSeconds, next.ShortJumpSeconds, next.MediumJumpSeconds, next.LongJumpSeconds))
        {
            // The menus name each jump by its length.
            BuildMenus();
        }

        _player.SetVolume(_player.Volume);
        _controlsTimer.Interval = TimeSpan.FromSeconds(_settings.ControlsHideSeconds);
        ApplyTheme();
        ApplyAlwaysOnTop();
        ApplyVideoLook(_settings.VideoLook);
        ApplyBudget(force: PerformanceChosen());
        ApplyVideoEffect(_settings.VideoEffect, _settings.VideoEffectStrength);
        LayOutSubtitles();
        SetView(_view);
        ShowState();
        ShowTaskbarSettingsChanged();
        SaveSettings();
        TranscriptionSaved(s);
        MetadataSettingsSaved(s);
        Say("Preferences saved");

        // A changed mode or schedule may start, stop or allow a download or a prompt.
        UpdateTick();

        // A settings-specific cache key regenerates only visible covers, in the existing bounded
        // background queue. The rest are made lazily as the user scrolls to them.
        if (artworkChanged)
        {
            CancelLibraryPictures();
            _shownLibrary = "";
            _shownHero = "";
            _playlistRows.Clear();
            ShowPlaylist();
            ShowLibrary();
        }

        // Either default may need the camera. Ask once while saving preferences, even when the
        // audiobook default is not the kind playing at this moment; declining restores only the
        // camera-using defaults and leaves every other preference intact.
        if ((UsesCamera(_settings.Visualizer) || UsesCamera(_settings.AudiobookVisualizer)) && !_settings.CameraAllowed)
        {
            if (!await AllowCameraAsync())
            {
                _settings = _settings with
                {
                    Visualizer = UsesCamera(_settings.Visualizer) ? s.Visualizer : _settings.Visualizer,
                    AudiobookVisualizer = UsesCamera(_settings.AudiobookVisualizer) ? s.AudiobookVisualizer : _settings.AudiobookVisualizer,
                };
                _player.Settings = _settings;
                SaveSettings();
                ApplyVisualizer();
            }
        }
    }

    /// <summary>
    /// The first time rexplayer opens, a welcome with the one privacy choice it has (UI-14); after an
    /// update, what changed (UI-15). Each is shown once.
    /// </summary>
    private async Task GreetAsync()
    {
        if (!_settings.FirstRunDone)
        {
            // Network access is an explicit choice; merely completing the welcome does not opt in.
            var updates = Check("Look for new versions every day and download them in the background", false);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(updates, "WelcomeUpdates");
            var welcome = new StackPanel { Spacing = 12, Width = DialogWidth(Rex.Media.AppCore.Shell.DialogLayout.OrdinaryWidth) };
            welcome.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "Drop media on the window, or press Ctrl+O to open a file. Every command and its shortcut is in the menus, and F1 opens the help.",
            });
            var privacy = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "rexplayer needs no account and plays local files offline. Optional metadata lookups and update checks contact online services only when enabled.",
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(privacy, "WelcomePrivacy");
            welcome.Children.Add(privacy);
            welcome.Children.Add(updates);
            WrapLabels(welcome);
            // Start is the one way on, so it is the dialog's own action, not a way out of it.
            await Ask("Welcome to rexplayer", welcome, "Start", close: "");
            _settings = _settings with { FirstRunDone = true, UpdateChecks = updates.IsChecked == true ? UpdateCadence.Daily : UpdateCadence.Off, LastSeenVersion = Version };
            SaveSettings();
            return;
        }

        // After an update, a small note offers what changed rather than a dialog in the way.
        if (_settings.LastSeenVersion != Version)
        {
            _settings = _settings with { LastSeenVersion = Version, LastUpdatedVersion = Version, LastUpdatedAt = DateTimeOffset.UtcNow };
            SaveSettings();
            _showUpdatedNote = true;
        }
    }

    /// <summary>What changed in this version (UI-15), from the changelog the app carries.</summary>
    private async Task ShowWhatsNewAsync()
    {
        using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("CHANGELOG.md");
        if (stream is not null && Rex.Media.AppCore.Changelog.Section(new StreamReader(stream).ReadToEnd(), Version) is { } changes)
        {
            var text = new TextBlock { Text = changes, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(text, "WhatsNewText");
            await Ask($"What's new in rexplayer {Version}", new ScrollViewer { Content = text, MaxHeight = 480 }, null);
        }
        else
        {
            Say($"There are no notes for rexplayer {Version}.");
        }
    }

    /// <summary>A group's heading: with the space above it, groups stand 24 apart while their lines stand 8 apart.</summary>
    private static TextBlock Heading(string text) =>
        new() { Text = text, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], Margin = new Thickness(0, 16, 0, 0) };

    /// <summary>A note that explains the setting above it, in the one small, quieter style every such line has.</summary>
    private static TextBlock Note(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = (double)Application.Current.Resources["RexCaptionFontSize"],
        Foreground = RexTheme.Brush("TextFillColorSecondaryBrush"),
    };

    /// <summary>A jump length, described by the keys that make that jump so the two can be matched.</summary>
    private NumberBox Jump(string label, int seconds, string back, string forward)
    {
        var box = Number(label, seconds, 1, 3600);
        var keys = string.Join(" and ", new[] { _keymap.Label(back), _keymap.Label(forward) }.Where(key => key.Length > 0));
        Describe(box, keys.Length > 0 ? "Keys: " + keys : "No keys; set them in Keyboard and mouse");
        return box;
    }

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

    /// <summary>
    /// The visualisations, grouped under headings that cannot be chosen, so the long list is easy to
    /// scan; each choice carries its <see cref="VisualizerChoice"/>, which is what is saved.
    /// </summary>
    private static ComboBox VisualizerChoices(VisualizerChoice selected, string header)
    {
        var box = new ComboBox { Header = header, MinWidth = 220, MaxDropDownHeight = 480 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, header);
        box.Items.Add(new ComboBoxItem { Content = Rex.Media.AppCore.Player.VisualizerGroups.Nothing, Tag = VisualizerChoice.Off });
        foreach (var (heading, choices) in Rex.Media.AppCore.Player.VisualizerGroups.All)
        {
            var title = new ComboBoxItem { Content = heading, IsEnabled = false, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, IsTabStop = false };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(title, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            box.Items.Add(title);
            foreach (var (choice, label) in choices)
            {
                box.Items.Add(new ComboBoxItem { Content = label, Tag = choice, Padding = new Thickness(24, 5, 11, 7) });
            }
        }

        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag is VisualizerChoice choice && choice == selected) ?? box.Items[0];
        return box;
    }

    private static CheckBox Check(string label, bool value) => new() { Content = label, IsChecked = value };

    private static TextBox Words(string label, string value)
    {
        var box = new TextBox { Header = label, Text = value, MinWidth = 220 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, label);
        return box;
    }

    /// <summary>
    /// A line under a control that says what a less obvious choice does, read out with it too. It
    /// is a <see cref="Note"/>, so it wraps within the control's width instead of widening it.
    /// </summary>
    private static void Describe(Control control, string help)
    {
        switch (control)
        {
            case NumberBox number:
                number.Description = Note(help);
                break;
            case ComboBox choice:
                choice.Description = Note(help);
                break;
            case TextBox words:
                words.Description = Note(help);
                break;
        }

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(control, help);
    }

    private static readonly string[] SubtitleFonts = ["Segoe UI", "Arial", "Calibri", "Verdana", "Tahoma", "Georgia", "Times New Roman", "Cascadia Mono"];

    private static readonly (string Name, int Rgb)[] Colors =
    [
        ("White", 0xFFFFFF), ("Yellow", 0xFFFF00), ("Light grey", 0xC0C0C0), ("Grey", 0x808080), ("Black", 0x000000),
        ("Red", 0xFF0000), ("Scarlet", 0xFF3B30), ("Green", 0x00FF00), ("Cyan", 0x00FFFF), ("Blue", 0x0000FF), ("Magenta", 0xFF00FF),
    ];

    /// <summary>A choice of colours; one set by hand in the settings file stays on offer as it is.</summary>
    private static (ComboBox Box, Func<int> Value) ColorChoice(string label, int rgb)
    {
        var options = Colors.ToList();
        if (!options.Any(option => option.Rgb == rgb))
        {
            options.Insert(0, ($"Custom (#{rgb:X6})", rgb));
        }

        var box = Choice(label, [.. options.Select(option => option.Name)], options.FindIndex(option => option.Rgb == rgb));
        return (box, () => options[Math.Max(0, box.SelectedIndex)].Rgb);
    }
}
