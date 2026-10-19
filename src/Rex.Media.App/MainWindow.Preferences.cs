using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Commands;
using Rex.Media.Primitives;
using Rex.Media.Settings;
using Rex.Media.Subtitles;

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
        var veryShort = Jump("Very short jump (seconds)", s.VeryShortJumpSeconds, CommandCatalog.JumpBackVeryShort, CommandCatalog.JumpForwardVeryShort);
        var shortJump = Jump("Short jump (seconds)", s.ShortJumpSeconds, CommandCatalog.JumpBackShort, CommandCatalog.JumpForwardShort);
        var medium = Jump("Medium jump (seconds)", s.MediumJumpSeconds, CommandCatalog.JumpBackMedium, CommandCatalog.JumpForwardMedium);
        var longJump = Jump("Long jump (seconds)", s.LongJumpSeconds, CommandCatalog.JumpBackLong, CommandCatalog.JumpForwardLong);
        var step = Number("Volume step (%)", s.VolumeStepPercent, 1, 25);
        var maxVolume = Number("Loudest volume (%)", s.MaxVolumePercent, 100, 200);
        var seekPreview = Check("Show the video frame under the pointer on the timeline", s.SeekPreview);
        var pictures = Number("Show each picture for (seconds)", s.PictureSeconds, 1, 3600);
        var navigator = Check("Show the whole picture in a corner while zoomed in", s.ShowNavigator);
        var pictureLook = Choice("Default video picture look", VideoLooks.Names.ToArray(), (int)s.VideoLook);
        var pictureEffect = Choice("Picture effects (optional)", [.. Enum.GetValues<VideoEffect>().Select(VideoEffects.PickerLabel)], (int)s.VideoEffect);
        var lookSettings = new Button { Content = "Customize the current picture look..." };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(lookSettings, "PreferencesLookSettings");
        lookSettings.Click += (_, _) =>
        {
            _dialog?.Hide();
            DispatcherQueue.TryEnqueue(() => _ = ShowVideoStyleSettingsAsync(effect: false));
        };
        var effectSettings = new Button { Content = "Customize the current picture effect..." };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(effectSettings, "PreferencesEffectSettings");
        effectSettings.Click += (_, _) =>
        {
            _dialog?.Hide();
            DispatcherQueue.TryEnqueue(() => _ = ShowVideoStyleSettingsAsync(effect: true));
        };
        var visualizer = VisualizerChoices(s.Visualizer);
        var generatedArt = Check("Generate artwork from music when no cover exists", s.GenerateAudioArtwork);
        var artworkStyle = Choice("Generated artwork style", ["Prism", "Orbit", "Wave", "Minimal"], (int)s.AudioArtworkStyle);
        var artworkColor = Number("Artwork colour (%)", s.AudioArtworkColor, 0, 200);
        var artworkDetail = Number("Artwork detail (%)", s.AudioArtworkDetail, 0, 200);
        var artworkContrast = Number("Artwork contrast (%)", s.AudioArtworkContrast, 0, 200);
        var artworkIdentity = Check("Let the file name make each generated cover more distinct", s.AudioArtworkUsesIdentity);
        var theme = Choice("Theme", ["Windows' choice", "Light", "Dark"], (int)s.Theme);
        var onTop = Choice("Always on top", ["Never", "Always", "While playing"], (int)s.AlwaysOnTop);
        var hide = Number("Hide the full-screen controls after (seconds)", s.ControlsHideSeconds, 0.5, 10);
        var messages = Check("Show messages over the picture", s.OnScreenMessages);
        var shellMedia = Check("Use Windows taskbar buttons, media controls and the jump list", s.ShellMediaControls);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(shellMedia, "PreferencesShellMediaControls");
        var title = Number("Show the title when an item starts for (seconds, 0 for never)", s.TitleSeconds, 0, 30);
        var single = Check("Use one window: open files in the player already running", s.SingleInstance);
        var enqueue = Check("Files opened that way join the playlist instead of playing", s.EnqueueFromSecondLaunch);
        enqueue.IsEnabled = single.IsChecked == true;
        single.Checked += (_, _) => enqueue.IsEnabled = true;
        single.Unchecked += (_, _) => enqueue.IsEnabled = false;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(enqueue, "Applies when files open in the existing player window.");
        var updates = Choice("Look for new versions", ["Never", "Daily", "Weekly"], (int)s.UpdateChecks);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(updates, "PreferencesUpdateChecks");
        var updateMode = Choice("When a new version is found", ["Install it by itself when rexplayer is closed or not in use", "Download it, then ask me when to install", "Only tell me about it"], (int)s.UpdateMode);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(updateMode, "PreferencesUpdateMode");
        // Not a live region: it changes with every step of a download, and the updater announces
        // its changes of state itself.
        var updateStatus = new TextBlock { Text = UpdatePanelNow().Line, TextWrapping = TextWrapping.Wrap };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(updateStatus, "PreferencesUpdateStatus");
        var checkNow = new Button { Content = "Check for updates now" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(checkNow, "PreferencesCheckUpdates");
        checkNow.Click += (_, _) => _ = CheckForUpdatesAsync(asked: true);
        var installNow = new Button { Content = "Restart and install now", Visibility = Pending is { Stage: Rex.Media.Settings.UpdateStage.Ready } ? Visibility.Visible : Visibility.Collapsed };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(installNow, "PreferencesInstallUpdate");
        installNow.Click += (_, _) =>
        {
            _dialog?.Hide();
            DispatcherQueue.TryEnqueue(() => _ = InstallAsync(unattended: false));
        };
        var updateButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        updateButtons.Children.Add(checkNow);
        updateButtons.Children.Add(installNow);
        var lastUpdated = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        if (Rex.Media.AppCore.Updates.UpdateFlow.LastUpdated(s.LastUpdatedVersion, s.LastUpdatedAt, DateTimeOffset.Now) is { } updated)
        {
            var updatedText = new TextBlock { Text = updated, VerticalAlignment = VerticalAlignment.Center };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(updatedText, "PreferencesLastUpdated");
            var whatsNew = new HyperlinkButton { Content = "What's new" };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(whatsNew, "PreferencesWhatsNew");
            whatsNew.Click += (_, _) =>
            {
                _dialog?.Hide();
                DispatcherQueue.TryEnqueue(() => _ = ShowWhatsNewAsync());
            };
            lastUpdated.Children.Add(updatedText);
            lastUpdated.Children.Add(whatsNew);
        }

        var mediaOpenClick = Choice("Open media on", ["Single click", "Double click"], (int)s.MediaOpenClickMode);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(mediaOpenClick, "PreferencesMediaOpenClick");
        var history = Check("Remember where things were left and what played recently", s.KeepHistory);
        var libraryOpening = Choice("When opening the library", ["Show the playing item", "Return to where I left the library"], s.OpenLibraryAtPlayingItem ? 0 : 1);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(libraryOpening, "PreferencesLibraryOpening");
        var libraryFullScreen = Check("Leave full screen when the library opens", s.LeaveFullScreenWhenLibraryOpens);
        var libraryPlayback = Choice("When media starts from the library", ["Show the player", "Keep browsing while audio plays"], s.ShowPlayerWhenLibraryMediaStarts ? 0 : 1);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(libraryFullScreen, "PreferencesLibraryFullScreen");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(libraryPlayback, "PreferencesLibraryPlayback");
        var pauseLibrary = Check("Pause playback when the library opens", s.PauseWhenLibraryOpens);
        var keepLibraryAudio = Check("Keep audio-only items playing in the library", s.KeepAudioPlayingInLibrary);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(pauseLibrary, "PreferencesPauseLibrary");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(keepLibraryAudio, "PreferencesKeepLibraryAudio");
        keepLibraryAudio.IsEnabled = pauseLibrary.IsChecked == true;
        pauseLibrary.Checked += (_, _) => keepLibraryAudio.IsEnabled = true;
        pauseLibrary.Unchecked += (_, _) => keepLibraryAudio.IsEnabled = false;
        var autoIntro = Check("Skip intros automatically", s.SkipIntrosAutomatically);
        var autoCredits = Check("Skip credits automatically (plays the next episode when one follows)", s.SkipCreditsAutomatically);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(autoIntro, "PreferencesAutoSkipIntro");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(autoCredits, "PreferencesAutoSkipCredits");
        var queue = Check("Put the playlist back when rexplayer starts", s.RestoreQueue);
        var sleep = Choice("When the sleep timer's time comes", ["Pause", "Stop"], (int)s.SleepAction);
        var audioLanguages = Words("Play sound in (languages, best first, such as \"ja, original\")", s.AudioLanguages);
        var subtitleLanguages = Words("Show subtitles in (languages, best first, such as \"en, fr\")", s.SubtitleLanguages);
        var font = new ComboBox { Header = "Subtitle font", IsEditable = true, ItemsSource = SubtitleFonts, Text = s.SubtitleFont, MinWidth = 220 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(font, "Subtitle font");
        var size = Number("Subtitle size (%)", s.SubtitleSize, 50, 400);
        var color = ColorChoice("Subtitle colour", s.SubtitleColor);
        var opacity = Number("Subtitle opacity (%)", s.SubtitleOpacity, 0, 100);
        var bold = Check("Bold subtitles", s.SubtitleBold);
        var outline = Choice("Outline", ["None", "Thin", "Normal", "Thick"], (int)s.SubtitleOutline);
        var outlineColor = ColorChoice("Outline colour", s.SubtitleOutlineColor);
        var shadow = Number("Shadow opacity (%, 0 for none)", s.SubtitleShadowOpacity, 0, 100);
        var box = Number("Background box opacity (%, 0 for none)", s.SubtitleBoxOpacity, 0, 100);
        var boxColor = ColorChoice("Background box colour", s.SubtitleBoxColor);
        var margin = Number("Gap from the edge of the picture (% of its height)", s.SubtitleMargin, 0, 40);
        var inBars = Check("Put subtitles in the black bars below a wide picture", s.SubtitlesInBars);
        var atBottom = Check("Keep every subtitle at the bottom", s.SubtitlesAtBottom);
        var styles = Choice("Styled subtitles", ["Use the file's colours and weights", "Always use mine"], (int)s.SubtitleStyles);
        var codePages = SubtitleText.Fallbacks;
        var codePage = Choice("Text of older subtitle files", [.. codePages.Select(entry => entry.Name)], Math.Max(0, codePages.ToList().FindIndex(entry => entry.CodePage == s.SubtitleCodePage)));

        var performance = Choice("Fit rexplayer to this computer", ["Automatic", "Best quality", "Balanced", "Fastest"], (int)s.Performance);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(performance, "PreferencesPerformance");
        var performanceNow = new TextBlock { TextWrapping = TextWrapping.Wrap };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(performanceNow, "PreferencesPerformanceNow");
        var performanceChanges = new TextBlock { TextWrapping = TextWrapping.Wrap };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(performanceChanges, "PreferencesPerformanceChanges");
        var performanceTranscriptionNote = new TextBlock
        {
            Text = "Transcription quality stays as you choose it on the caption rail or under Transcription, in every mode.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(performanceTranscriptionNote, "PreferencesPerformanceTranscriptionNote");
        void ShowPerformance()
        {
            var choice = (PerformanceChoice)Math.Max(0, performance.SelectedIndex);
            performanceNow.Text = Rex.Media.AppCore.Performance.DeviceProfile.Describe(choice, _facts);
            var changes = Rex.Media.AppCore.Performance.DeviceProfile.Changes(Rex.Media.AppCore.Performance.DeviceProfile.For(Rex.Media.AppCore.Performance.DeviceProfile.Choose(choice, _facts), _facts));
            performanceChanges.Text = changes.Count == 0 ? "Nothing is held back: every picture, effect and visualisation at full quality." : string.Join("\n", changes);
        }

        performance.SelectionChanged += (_, _) => ShowPerformance();
        ShowPerformance();

        var content = new StackPanel();
        var sections = new List<(string Name, StackPanel Page)>();
        void AddSection(string name)
        {
            content = new StackPanel { Spacing = 8, MinWidth = 280, MaxWidth = 460 };
            var heading = Heading(name);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHeadingLevel(heading, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
            sections.Add((name, content));
            content.Children.Add(heading);
        }
        AddSection("Playback");
        foreach (var control in new UIElement[] { veryShort, shortJump, medium, longJump, step, maxVolume, seekPreview })
        {
            content.Children.Add(control);
        }

        AddSection("Intros and credits");
        content.Children.Add(new TextBlock { Text = "While an episode's intro or credits play, a button offers to skip them. Turn these on to skip without a click; a 5 second countdown lets you cancel. To choose per show, use Always skip for this show on the button's menu.", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(autoIntro);
        content.Children.Add(autoCredits);

        AddSection("Video and pictures");
        foreach (var control in new UIElement[] { pictures, navigator, pictureLook, pictureEffect, lookSettings, effectSettings })
        {
            content.Children.Add(control);
        }

        content.Children.Add(new TextBlock
        {
            Text = "Customize opens a separate editor for the saved look or effect and closes Preferences without saving. Save changes here first.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
        });

        AddSection("Music and artwork");
        content.Children.Add(visualizer);
        content.Children.Add(generatedArt);
        var artworkOptions = new StackPanel { Spacing = 8 };
        foreach (var control in new UIElement[] { artworkStyle, artworkColor, artworkDetail, artworkContrast, artworkIdentity })
        {
            artworkOptions.Children.Add(control);
        }
        var artworkDetails = new Expander
        {
            Header = "Generated artwork appearance",
            Content = artworkOptions,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(artworkDetails, "PreferencesArtworkDetails");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(artworkDetails, "Generated artwork appearance");
        content.Children.Add(artworkDetails);
        content.Children.Add(new TextBlock
        {
            Text = "These choices apply when rexplayer generates a cover. Existing album covers are kept.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
        });

        AddSection("Performance");
        content.Children.Add(new TextBlock { Text = "On a slower computer rexplayer can do a little less so playback stays smooth. Automatic decides from the computer and Windows' energy saver; choose a level to decide yourself.", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(performance);
        content.Children.Add(performanceNow);
        content.Children.Add(performanceChanges);
        content.Children.Add(performanceTranscriptionNote);

        AddSection("Languages");
        content.Children.Add(audioLanguages);
        content.Children.Add(subtitleLanguages);

        AddSection("Subtitles");
        foreach (var control in new UIElement[] { font, size, color.Box, opacity, bold, outline, outlineColor.Box, shadow, box, boxColor.Box, margin, inBars, atBottom, styles, codePage })
        {
            content.Children.Add(control);
        }

        var transcription = TranscriptionPreferences();
        AddSection("Transcription");
        foreach (var control in transcription.Controls)
        {
            content.Children.Add(control);
        }

        AddSection("Window");
        foreach (var control in new UIElement[] { theme, onTop, hide, messages, title, shellMedia })
        {
            content.Children.Add(control);
        }

        AddSection("Library");
        content.Children.Add(libraryOpening);
        content.Children.Add(mediaOpenClick);
        content.Children.Add(libraryFullScreen);
        content.Children.Add(libraryPlayback);
        content.Children.Add(pauseLibrary);
        content.Children.Add(keepLibraryAudio);

        content.Children.Add(new TextBlock
        {
            Text = "Return to where I left the library keeps your browsing view while the player is open. Ctrl or Shift clicks still select media without opening it.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
        });

        AddSection("History and resume");
        foreach (var control in new UIElement[] { history, queue, sleep })
        {
            content.Children.Add(control);
        }

        AddSection("Keyboard and mouse");
        var keys = new Button { Content = "Change shortcuts and what the mouse does..." };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(keys, "PreferencesKeyboard");
        keys.Click += (_, _) =>
        {
            // Keep this dialog and its pending values while the separate editor is open.
            requestedEditor = ShowKeyboardAndMouseAsync;
            _dialog?.Hide();
        };
        content.Children.Add(keys);
        content.Children.Add(new TextBlock
        {
            Text = "Your pending Preferences changes stay here while you edit shortcuts. Save or cancel the shortcut editor to return.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
        });

        AddSection("Opening files");
        foreach (var control in new UIElement[] { single, enqueue })
        {
            content.Children.Add(control);
        }

        AddSection("Updates");
        foreach (var control in new UIElement[] { updates, updateMode, updateStatus, lastUpdated, updateButtons })
        {
            content.Children.Add(control);
        }

        // Long check-box labels wrap inside the available page width instead of widening it.
        void WrapLabels(UIElement element)
        {
            if (element is CheckBox { Content: string label } check)
            {
                check.Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(check, label);
            }
            else if (element is Panel group)
            {
                foreach (var child in group.Children)
                {
                    WrapLabels(child);
                }
            }
            else if (element is Expander { Content: UIElement body })
            {
                WrapLabels(body);
            }
        }
        foreach (var page in sections)
        {
            WrapLabels(page.Page);
        }

        // The status line follows a check or a download while Preferences is open.
        _updateStatusText = updateStatus;
        bool saved;
        try
        {
            var scroll = new ScrollViewer
            {
                Content = sections[0].Page,
                MaxHeight = 560,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(scroll, "PreferencesScroll");
            var section = Choice("Settings section", [.. sections.Select(item => item.Name)], 0);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(section, "PreferencesSection");
            section.HorizontalAlignment = HorizontalAlignment.Stretch;
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
                scroll.Content = sections[shownSection].Page;
                scroll.UpdateLayout();
                // A view already at its saved place is left alone: a redundant change lands after
                // layout and would undo a scroll the user starts straight after switching.
                if (Math.Abs(scroll.VerticalOffset - offsets[shownSection]) > 0.5)
                {
                    scroll.ChangeView(null, offsets[shownSection], null, disableAnimation: true);
                }
            };
            var navigation = new StackPanel { Spacing = 4 };
            navigation.Children.Add(section);
            var saveHint = new TextBlock
            {
                Text = "Setting changes across all sections apply on Save. Cancel keeps your current settings. Downloads, update checks and transcript cleanup run when selected.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(saveHint, "PreferencesSaveHint");
            navigation.Children.Add(saveHint);
            var panel = new Grid { RowSpacing = 12 };
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            panel.Children.Add(navigation);
            Grid.SetRow(scroll, 1);
            panel.Children.Add(scroll);
            var dialog = new ContentDialog
            {
                Title = "Preferences",
                Content = panel,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot,
            };
            var root = Content.XamlRoot;
            void FitPreferences(XamlRoot sender, XamlRootChangedEventArgs args)
            {
                panel.Width = Math.Clamp(root.Size.Width - 96, 280, 460);
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

        var next = transcription.Apply((_settings with
        {
            VeryShortJumpSeconds = (int)veryShort.Value,
            ShortJumpSeconds = (int)shortJump.Value,
            MediumJumpSeconds = (int)medium.Value,
            LongJumpSeconds = (int)longJump.Value,
            VolumeStepPercent = (int)step.Value,
            MaxVolumePercent = (int)maxVolume.Value,
            SeekPreview = seekPreview.IsChecked == true,
            PictureSeconds = (int)pictures.Value,
            ShowNavigator = navigator.IsChecked == true,
            VideoLook = (VideoLook)pictureLook.SelectedIndex,
            VideoEffect = (VideoEffect)pictureEffect.SelectedIndex,
            Visualizer = visualizer.SelectedItem is ComboBoxItem { Tag: VisualizerChoice shown } ? shown : s.Visualizer,
            GenerateAudioArtwork = generatedArt.IsChecked == true,
            AudioArtworkStyle = (ArtworkStyle)artworkStyle.SelectedIndex,
            AudioArtworkColor = (int)artworkColor.Value,
            AudioArtworkDetail = (int)artworkDetail.Value,
            AudioArtworkContrast = (int)artworkContrast.Value,
            AudioArtworkUsesIdentity = artworkIdentity.IsChecked == true,
            Theme = (ThemeChoice)theme.SelectedIndex,
            AlwaysOnTop = (AlwaysOnTop)onTop.SelectedIndex,
            ControlsHideSeconds = hide.Value,
            OnScreenMessages = messages.IsChecked == true,
            ShellMediaControls = shellMedia.IsChecked == true,
            TitleSeconds = title.Value,
            SingleInstance = single.IsChecked == true,
            EnqueueFromSecondLaunch = enqueue.IsChecked == true,
            UpdateChecks = (UpdateCadence)updates.SelectedIndex,
            UpdateMode = (UpdateMode)updateMode.SelectedIndex,
            MediaOpenClickMode = (MediaOpenClick)Math.Max(0, mediaOpenClick.SelectedIndex),
            KeepHistory = history.IsChecked == true,
            OpenLibraryAtPlayingItem = libraryOpening.SelectedIndex == 0,
            LeaveFullScreenWhenLibraryOpens = libraryFullScreen.IsChecked == true,
            ShowPlayerWhenLibraryMediaStarts = libraryPlayback.SelectedIndex == 0,
            PauseWhenLibraryOpens = pauseLibrary.IsChecked == true,
            KeepAudioPlayingInLibrary = keepLibraryAudio.IsChecked == true,
            SkipIntrosAutomatically = autoIntro.IsChecked == true,
            SkipCreditsAutomatically = autoCredits.IsChecked == true,
            RestoreQueue = queue.IsChecked == true,
            SleepAction = (SleepChoice)sleep.SelectedIndex,
            AudioLanguages = audioLanguages.Text,
            SubtitleLanguages = subtitleLanguages.Text,
            SubtitleFont = font.Text,
            SubtitleSize = (int)size.Value,
            SubtitleColor = color.Value(),
            SubtitleOpacity = (int)opacity.Value,
            SubtitleBold = bold.IsChecked == true,
            SubtitleOutline = (OutlineChoice)outline.SelectedIndex,
            SubtitleOutlineColor = outlineColor.Value(),
            SubtitleShadowOpacity = (int)shadow.Value,
            SubtitleBoxOpacity = (int)box.Value,
            SubtitleBoxColor = boxColor.Value(),
            SubtitleMargin = (int)margin.Value,
            SubtitlesInBars = inBars.IsChecked == true,
            SubtitlesAtBottom = atBottom.IsChecked == true,
            SubtitleStyles = (SubtitleStyleChoice)styles.SelectedIndex,
            SubtitleCodePage = codePages[Math.Max(0, codePage.SelectedIndex)].CodePage,
            Performance = (PerformanceChoice)Math.Max(0, performance.SelectedIndex),
        }).Normalize()).Normalize();
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
        ApplyBudget();
        ApplyVideoEffect(_settings.VideoEffect, _settings.VideoEffectStrength);
        LayOutSubtitles();
        SetView(_view);
        ShowState();
        ShowTaskbarSettingsChanged();
        SaveSettings();
        TranscriptionSaved(s);
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

        // The camera is used only once it has been agreed to.
        if (UsesCamera(_settings.Visualizer) && !_settings.CameraAllowed)
        {
            var chosen = _settings.Visualizer;
            _settings = _settings with { Visualizer = s.Visualizer };
            await ChooseVisualizerAsync(chosen);
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
            var updates = Check("Look for new versions every day and download them in the background", true);
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
