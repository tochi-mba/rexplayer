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
            var scroll = new ScrollViewer
            {
                Content = sections.Page(0),
                MaxHeight = 560,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(scroll, "PreferencesScroll");
            var section = Choice("Settings section", [.. names], 0);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(section, "PreferencesSection");
            section.HorizontalAlignment = HorizontalAlignment.Stretch;
            // Keep every section directly reachable when the picker reopens near the end. Once
            // Metadata made this the eleventh item, the default-height popup virtualized Playback
            // above the viewport instead of exposing it to keyboard and UI Automation selection.
            section.MaxDropDownHeight = 560;
            var offsets = new double[sections.Count];
            var shownSection = 0;
            section.SelectionChanged += (_, _) =>
            {
                if (section.SelectedIndex < 0)
                {
                    return;
                }

                offsets[shownSection] = scroll.VerticalOffset;
                shownSection = section.SelectedIndex;
                scroll.Content = sections.Page(shownSection);
                scroll.UpdateLayout();
                // A view already at its saved place is left alone: a redundant change lands after
                // layout and would undo a scroll the user starts straight after switching.
                if (Math.Abs(scroll.VerticalOffset - offsets[shownSection]) > 0.5)
                {
                    scroll.ChangeView(null, offsets[shownSection], null, disableAnimation: true);
                }
            };
            // The compact section picker remains available on small windows and for automation.
            // Larger windows also offer a persistent, searchable category rail.
            var categorySearch = new TextBox
            {
                PlaceholderText = "Search settings and categories",
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(categorySearch, "PreferencesCategorySearch");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(categorySearch, "Search settings and categories");
            var categories = new ListView
            {
                SelectionMode = ListViewSelectionMode.Single,
                ItemsSource = names.ToList(),
                SelectedIndex = 0,
                MaxHeight = 480,
                MinWidth = 170,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(categories, "PreferencesCategoryList");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(categories, "Matching settings categories");
            categories.SelectionChanged += (_, _) =>
            {
                if (categories.SelectedItem is string name)
                {
                    var index = sections.IndexOf(name);
                    if (index >= 0 && section.SelectedIndex != index)
                    {
                        section.SelectedIndex = index;
                    }
                }
            };
            section.SelectionChanged += (_, _) =>
            {
                if (section.SelectedIndex >= 0 && categories.ItemsSource is IEnumerable<string> listed)
                {
                    var name = names[section.SelectedIndex];
                    if (listed.Contains(name) && !Equals(categories.SelectedItem, name))
                    {
                        categories.SelectedItem = name;
                    }
                }
            };
            // Match the labels inside each page as well as the category name. A search builds
            // the pages not yet shown, so no setting values or state need to be duplicated.
            // WrapLabels changes long CheckBox contents from strings into TextBlocks; search
            // must read their text, not the TextBlock type name. Include automation labels and
            // available choice items (including the contents of closed optional expanders).
            static bool Says(object? value, string query)
            {
                var label = value is TextBlock block ? block.Text : value?.ToString();
                return label is not null && System.Globalization.CultureInfo.CurrentCulture.CompareInfo.IndexOf(
                    label, query, System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) >= 0;
            }

            static bool HasSettingText(UIElement node, string query)
            {
                if (node is FrameworkElement element
                    && (Says(Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(element), query)
                        || Says(Microsoft.UI.Xaml.Automation.AutomationProperties.GetHelpText(element), query)))
                {
                    return true;
                }

                if (node is TextBlock text && Says(text.Text, query)
                    || node is CheckBox check && Says(check.Content, query)
                    || node is ToggleSwitch toggle && Says(toggle.Header, query)
                    || node is ComboBox choice && (Says(choice.Header, query)
                        || choice.Items.Any(item => Says(item is ComboBoxItem option ? option.Content : item, query)))
                    || node is NumberBox number && Says(number.Header, query)
                    || node is TextBox input && Says(input.Header, query)
                    || node is Button button && Says(button.Content, query))
                {
                    return true;
                }

                if (node is Expander expander
                    && (Says(expander.Header, query)
                        || expander.Content is UIElement expanded && HasSettingText(expanded, query)))
                {
                    return true;
                }

                if (node is Panel panel)
                {
                    return panel.Children.Any(child => HasSettingText(child, query));
                }

                if (node is Border { Child: UIElement child })
                {
                    return HasSettingText(child, query);
                }

                return node is ContentControl { Content: UIElement content }
                    && HasSettingText(content, query);
            }

            var searchResult = new TextBlock
            {
                Text = "No matching settings. Try a different word.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75,
                Visibility = Visibility.Collapsed,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(searchResult, "PreferencesSearchEmpty");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(searchResult, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
            categorySearch.TextChanged += (_, _) =>
            {
                var query = categorySearch.Text.Trim();
                var matches = query.Length == 0 ? [.. names] : sections.All()
                    .Where(item => Says(item.Name, query) || HasSettingText(item.Page, query))
                    .Select(item => item.Name).ToList();
                categories.ItemsSource = matches;
                // Updating the ListView's items can clear its selection without raising a
                // reliable SelectionChanged for a newly assigned SelectedItem. Navigate
                // through the authoritative section picker first, so recovering from an
                // empty result never leaves the old section visible.
                if (matches.Count > 0)
                {
                    // A direct category match outranks incidental matches in controls on
                    // other pages. For example, searching "subtitle" must land on
                    // Subtitles, not on an earlier page that happens to mention subtitles.
                    var directCategory = names.FirstOrDefault(name => Says(name, query));
                    var selectedName = directCategory ?? (matches.Contains(names[shownSection])
                        ? names[shownSection] : matches[0]);
                    var index = sections.IndexOf(selectedName);
                    if (section.SelectedIndex != index)
                    {
                        section.SelectedIndex = index;
                    }

                    categories.SelectedItem = selectedName;
                }
                else
                {
                    categories.SelectedItem = null;
                }

                searchResult.Visibility = matches.Count == 0 && categories.Visibility == Visibility.Visible
                    ? Visibility.Visible : Visibility.Collapsed;
            };
            var navigation = new StackPanel { Spacing = 8 };
            navigation.Children.Add(section);
            navigation.Children.Add(categorySearch);
            navigation.Children.Add(categories);
            navigation.Children.Add(searchResult);
            var saveHint = new TextBlock
            {
                Text = "Setting changes across all sections apply on Save. Cancel keeps your current settings. Downloads, update checks and transcript cleanup run when selected.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(saveHint, "PreferencesSaveHint");
            navigation.Children.Add(saveHint);
            var panel = new Grid { ColumnSpacing = 20, RowSpacing = 12 };
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            panel.Children.Add(navigation);
            Grid.SetColumn(scroll, 1);
            panel.Children.Add(scroll);
            var dialog = new ContentDialog
            {
                Title = "Preferences",
                Content = panel,
                MaxWidth = 960,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot,
            };
            var root = Content.XamlRoot;
            void FitPreferences(XamlRoot sender, XamlRootChangedEventArgs args)
            {
                var wide = root.Size.Width >= 860;
                panel.Width = Math.Clamp(root.Size.Width - 96, 280, 840);
                panel.ColumnDefinitions[0].Width = wide ? new GridLength(190) : new GridLength(1, GridUnitType.Star);
                panel.ColumnDefinitions[1].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
                categorySearch.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
                categories.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
                categories.MaxHeight = Math.Clamp(root.Size.Height - 360, 100, 480);
                // Search is hidden in compact mode, so do not leave a hidden filter
                // governing the category rail when the user widens the window again.
                if (!wide && categorySearch.Text.Length > 0)
                {
                    categorySearch.Text = "";
                }

                searchResult.Visibility = wide && categories.Items.Count == 0
                    ? Visibility.Visible : Visibility.Collapsed;
                navigation.Visibility = Visibility.Visible;
                // Small windows retain the original single-column section picker.
                if (!wide)
                {
                    Grid.SetColumn(scroll, 0);
                    Grid.SetRow(navigation, 0);
                    Grid.SetRow(scroll, 1);
                    panel.RowDefinitions.Clear();
                    panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                }
                else
                {
                    Grid.SetRow(scroll, 0);
                    Grid.SetColumn(scroll, 1);
                    panel.RowDefinitions.Clear();
                }
                scroll.MaxHeight = Math.Clamp(root.Size.Height - 300, 100, 560);
            }
            FitPreferences(root, null!);
            root.Changed += FitPreferences;
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
                root.Changed -= FitPreferences;
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
            var welcome = new StackPanel { Spacing = 12, MaxWidth = 480 };
            welcome.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "Drop media on the window, or press Ctrl+O to open a file. Every command and its shortcut is in the menus, and F1 opens the help.",
            });
            var privacy = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "rexplayer needs no account and plays local files offline. Optional metadata lookups and update checks contact online services only when enabled. This update choice starts turned off.",
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(privacy, "WelcomePrivacy");
            welcome.Children.Add(privacy);
            welcome.Children.Add(updates);
            await Ask("Welcome to rexplayer", welcome, null, "Start");
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

    private static TextBlock Heading(string text) =>
        new() { Text = text, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], Margin = new Thickness(0, 12, 0, 0) };

    /// <summary>A jump length, described by the keys that make that jump so the two can be matched.</summary>
    private NumberBox Jump(string label, int seconds, string back, string forward)
    {
        var box = Number(label, seconds, 1, 3600);
        var keys = string.Join(" and ", new[] { _keymap.Label(back), _keymap.Label(forward) }.Where(key => key.Length > 0));
        var help = keys.Length > 0 ? "Keys: " + keys : "No keys; set them in Keyboard and mouse";
        box.Description = help;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(box, help);
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
    private static ComboBox VisualizerChoices(VisualizerChoice selected)
    {
        const string Label = "While music plays, show";
        var box = new ComboBox { Header = Label, MinWidth = 220, MaxDropDownHeight = 480 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, Label);
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

    /// <summary>A line under a control that says what a less obvious choice does, read out with it too.</summary>
    private static void Describe(Control control, string help)
    {
        switch (control)
        {
            case NumberBox number:
                number.Description = help;
                break;
            case ComboBox choice:
                choice.Description = help;
                break;
        }

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(control, help);
    }

    private static readonly string[] SubtitleFonts = ["Segoe UI", "Arial", "Calibri", "Verdana", "Tahoma", "Georgia", "Times New Roman", "Cascadia Mono"];

    private static readonly (string Name, int Rgb)[] Colors =
    [
        ("White", 0xFFFFFF), ("Yellow", 0xFFFF00), ("Light grey", 0xC0C0C0), ("Grey", 0x808080), ("Black", 0x000000),
        ("Red", 0xFF0000), ("Green", 0x00FF00), ("Cyan", 0x00FFFF), ("Blue", 0x0000FF), ("Magenta", 0xFF00FF),
    ];

    /// <summary>A choice of colours; one set by hand in the settings file stays on offer as it is.</summary>
    private static (ComboBox Box, Func<int> Value) ColorChoice(string label, int rgb)
    {
        var options = Colors.ToList();
        if (!options.Any(option => option.Rgb == rgb))
        {
            options.Insert(0, ($"#{rgb:X6}", rgb));
        }

        var box = Choice(label, [.. options.Select(option => option.Name)], options.FindIndex(option => option.Rgb == rgb));
        return (box, () => options[Math.Max(0, box.SelectedIndex)].Rgb);
    }
}
