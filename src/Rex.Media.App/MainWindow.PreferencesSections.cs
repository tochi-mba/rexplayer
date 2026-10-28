using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore;
using Rex.Media.AppCore.Commands;
using Rex.Media.Primitives;
using Rex.Media.Settings;
using Rex.Media.Subtitles;
using Peers = Microsoft.UI.Xaml.Automation.Peers;
using Props = Microsoft.UI.Xaml.Automation.AutomationProperties;

namespace Rex.Media.App;

/// <summary>
/// Preferences' sections, each built the first time it is shown: opening the dialog builds one
/// page, not all of them. Each says how it changes the settings on Save; a section never shown has
/// no edits, so it changes nothing.
/// </summary>
public sealed partial class MainWindow
{
    private void AddPreferenceSections(LazySections<StackPanel, PlayerSettings> sections, TextBlock updateStatus,
        Action<Func<Task>> requestEditor)
    {
        var s = _settings;
        sections.Add("Playback", () =>
        {
            var page = PreferencePage("Playback");
            var veryShort = Jump("Very short jump (seconds)", s.VeryShortJumpSeconds, CommandCatalog.JumpBackVeryShort, CommandCatalog.JumpForwardVeryShort);
            var shortJump = Jump("Short jump (seconds)", s.ShortJumpSeconds, CommandCatalog.JumpBackShort, CommandCatalog.JumpForwardShort);
            var medium = Jump("Medium jump (seconds)", s.MediumJumpSeconds, CommandCatalog.JumpBackMedium, CommandCatalog.JumpForwardMedium);
            var longJump = Jump("Long jump (seconds)", s.LongJumpSeconds, CommandCatalog.JumpBackLong, CommandCatalog.JumpForwardLong);
            var step = Number("Volume step (%)", s.VolumeStepPercent, 1, 25);
            var maxVolume = Number("Loudest volume (%)", s.MaxVolumePercent, 100, 200);
            Describe(maxVolume, "How far the volume goes. Above 100 % rexplayer makes quiet media louder than it was recorded, which can distort.");
            var seekPreview = Check("Show the video frame under the pointer on the timeline", s.SeekPreview);
            // The sleep timer is a playback control, so what it does at its time is chosen beside the others.
            var sleep = Choice("When the sleep timer's time comes", ["Pause", "Stop"], (int)s.SleepAction);
            Fill(page, veryShort, shortJump, medium, longJump, step, maxVolume, seekPreview, sleep);
            return (page, n => n with
            {
                VeryShortJumpSeconds = (int)veryShort.Value,
                ShortJumpSeconds = (int)shortJump.Value,
                MediumJumpSeconds = (int)medium.Value,
                LongJumpSeconds = (int)longJump.Value,
                VolumeStepPercent = (int)step.Value,
                MaxVolumePercent = (int)maxVolume.Value,
                SeekPreview = seekPreview.IsChecked == true,
                SleepAction = (SleepChoice)sleep.SelectedIndex,
            });
        });

        sections.Add("Intros and credits", () =>
        {
            var page = PreferencePage("Intros and credits");
            var autoIntro = Check("Skip intros automatically", s.SkipIntrosAutomatically);
            var autoCredits = Check("Skip credits automatically (plays the next episode when one follows)", s.SkipCreditsAutomatically);
            Props.SetAutomationId(autoIntro, "PreferencesAutoSkipIntro");
            Props.SetAutomationId(autoCredits, "PreferencesAutoSkipCredits");
            Fill(page, new TextBlock { Text = "While an episode's intro or credits play, a button offers to skip them. Turn these on to skip without a click; a 5 second countdown lets you cancel. To choose per show, use Always skip for this show on the button's menu.", TextWrapping = TextWrapping.Wrap },
                autoIntro, autoCredits);
            return (page, n => n with
            {
                SkipIntrosAutomatically = autoIntro.IsChecked == true,
                SkipCreditsAutomatically = autoCredits.IsChecked == true,
            });
        });

        sections.Add("Video and pictures", () =>
        {
            var page = PreferencePage("Video and pictures");
            var pictures = Number("Show each picture for (seconds)", s.PictureSeconds, 1, 3600);
            var navigator = Check("Show the whole picture in a corner while zoomed in", s.ShowNavigator);
            var pictureLook = Choice("Default video picture look", VideoLooks.Names.ToArray(), (int)s.VideoLook);
            var pictureEffect = Choice("Picture effects (optional)", [.. Enum.GetValues<VideoEffect>().Select(VideoEffects.PickerLabel)], (int)s.VideoEffect);
            var lookSettings = new Button { Content = "Customize the current picture look..." };
            Props.SetAutomationId(lookSettings, "PreferencesLookSettings");
            lookSettings.Click += (_, _) =>
            {
                // Preferences comes back with its pending changes once the editor closes.
                requestEditor(() => ShowVideoStyleSettingsAsync(effect: false));
                _dialog?.Hide();
            };
            var effectSettings = new Button { Content = "Customize the current picture effect..." };
            Props.SetAutomationId(effectSettings, "PreferencesEffectSettings");
            effectSettings.Click += (_, _) =>
            {
                requestEditor(() => ShowVideoStyleSettingsAsync(effect: true));
                _dialog?.Hide();
            };
            Fill(page, pictures, navigator, pictureLook, pictureEffect, lookSettings, effectSettings, new TextBlock
            {
                Text = "Customize opens a separate editor for the saved look or effect. Your pending Preferences changes stay here until you return.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75,
            });
            // Enhanced Subject Lock's optional models sit with the picture effects that Subject Lock belongs to.
            Fill(page, Heading(Rex.Media.Vision.ModelDownloadText.Heading), SubjectModelsSection());
            return (page, n => n with
            {
                PictureSeconds = (int)pictures.Value,
                ShowNavigator = navigator.IsChecked == true,
                VideoLook = (VideoLook)pictureLook.SelectedIndex,
                VideoEffect = (VideoEffect)pictureEffect.SelectedIndex,
            });
        });

        sections.Add("Music and artwork", () =>
        {
            var page = PreferencePage("Music and artwork");
            var visualizer = VisualizerChoices(s.Visualizer);
            var audiobookVisualizer = VisualizerChoices(s.AudiobookVisualizer);
            Describe(audiobookVisualizer, "Used only for items recognised or manually classified as audiobooks. Other audio keeps the default above.");
            var generatedArt = Check("Generate artwork from music when no cover exists", s.GenerateAudioArtwork);
            var artworkStyle = Choice("Generated artwork style", ["Prism", "Orbit", "Wave", "Minimal"], (int)s.AudioArtworkStyle);
            var artworkColor = Number("Artwork colour (%)", s.AudioArtworkColor, 0, 200);
            var artworkDetail = Number("Artwork detail (%)", s.AudioArtworkDetail, 0, 200);
            var artworkContrast = Number("Artwork contrast (%)", s.AudioArtworkContrast, 0, 200);
            var artworkIdentity = Check("Let the file name make each generated cover more distinct", s.AudioArtworkUsesIdentity);
            var artworkOptions = new StackPanel { Spacing = 8 };
            Fill(artworkOptions, artworkStyle, artworkColor, artworkDetail, artworkContrast, artworkIdentity);
            var artworkDetails = new Expander
            {
                Header = "Generated artwork appearance",
                Content = artworkOptions,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            Props.SetAutomationId(artworkDetails, "PreferencesArtworkDetails");
            Props.SetName(artworkDetails, "Generated artwork appearance");
            Fill(page, Heading("Default visualisation for songs and other audio"), visualizer,
                Heading("Default visualisation for audiobooks"), audiobookVisualizer, generatedArt, artworkDetails, new TextBlock
                {
                    Text = "These choices apply when rexplayer generates a cover. Existing album covers are kept.",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.75,
                });
            return (page, n => n with
            {
                Visualizer = visualizer.SelectedItem is ComboBoxItem { Tag: VisualizerChoice shown } ? shown : s.Visualizer,
                AudiobookVisualizer = audiobookVisualizer.SelectedItem is ComboBoxItem { Tag: VisualizerChoice audiobookShown } ? audiobookShown : s.AudiobookVisualizer,
                GenerateAudioArtwork = generatedArt.IsChecked == true,
                AudioArtworkStyle = (ArtworkStyle)artworkStyle.SelectedIndex,
                AudioArtworkColor = (int)artworkColor.Value,
                AudioArtworkDetail = (int)artworkDetail.Value,
                AudioArtworkContrast = (int)artworkContrast.Value,
                AudioArtworkUsesIdentity = artworkIdentity.IsChecked == true,
            });
        });

        sections.Add("Performance", () => PerformancePreferences(s));

        sections.Add("Languages", () =>
        {
            var page = PreferencePage("Languages");
            var audioLanguages = Words("Play sound in (languages, best first, such as \"ja, original\")", s.AudioLanguages);
            var subtitleLanguages = Words("Show subtitles in (languages, best first, such as \"en, fr\")", s.SubtitleLanguages);
            Fill(page, audioLanguages, subtitleLanguages);
            return (page, n => n with { AudioLanguages = audioLanguages.Text, SubtitleLanguages = subtitleLanguages.Text });
        });

        sections.Add("Subtitles", () => SubtitlePreferences(s));

        sections.Add("Transcription", () =>
        {
            var page = PreferencePage("Transcription");
            var transcription = TranscriptionPreferences();
            Fill(page, transcription.Controls);
            return (page, transcription.Apply);
        });

        sections.Add("Window", () =>
        {
            var page = PreferencePage("Window");
            var theme = Choice("Theme", ["Windows' choice", "Light", "Dark"], (int)s.Theme);
            var onTop = Choice("Always on top", ["Never", "Always", "While playing"], (int)s.AlwaysOnTop);
            var hide = Number("Hide the full-screen controls after (seconds)", s.ControlsHideSeconds, 0.5, 10);
            var messages = Check("Show messages over the picture", s.OnScreenMessages);
            var title = Number("Show the title when an item starts for (seconds, 0 for never)", s.TitleSeconds, 0, 30);
            var shellMedia = Check("Use Windows taskbar buttons, media controls and the jump list", s.ShellMediaControls);
            Props.SetAutomationId(shellMedia, "PreferencesShellMediaControls");
            Fill(page, theme, onTop, hide, messages, title, shellMedia);
            return (page, n => n with
            {
                Theme = (ThemeChoice)theme.SelectedIndex,
                AlwaysOnTop = (AlwaysOnTop)onTop.SelectedIndex,
                ControlsHideSeconds = hide.Value,
                OnScreenMessages = messages.IsChecked == true,
                TitleSeconds = title.Value,
                ShellMediaControls = shellMedia.IsChecked == true,
            });
        });

        sections.Add("Library", () => LibraryPreferences(s));

        sections.Add("Metadata", () =>
        {
            var page = PreferencePage("Metadata");
            var metadata = MetadataPreferences();
            Fill(page, metadata.Controls);
            return (page, metadata.Apply);
        });

        sections.Add("History and resume", () =>
        {
            var page = PreferencePage("History and resume");
            var history = Check("Remember where things were left and what played recently", s.KeepHistory);
            var queue = Check("Put the playlist back when rexplayer starts", s.RestoreQueue);
            Fill(page, history, queue);
            return (page, n => n with { KeepHistory = history.IsChecked == true, RestoreQueue = queue.IsChecked == true });
        });

        sections.Add("Keyboard and mouse", () =>
        {
            var page = PreferencePage("Keyboard and mouse");
            var keys = new Button { Content = "Change shortcuts and what the mouse does..." };
            Props.SetAutomationId(keys, "PreferencesKeyboard");
            keys.Click += (_, _) =>
            {
                // Keep this dialog and its pending values while the separate editor is open.
                // The editor names the jumps by the lengths set here, saved or not.
                var jumps = sections.Apply(_settings);
                requestEditor(() => ShowKeyboardAndMouseAsync(_settings with
                {
                    VeryShortJumpSeconds = jumps.VeryShortJumpSeconds,
                    ShortJumpSeconds = jumps.ShortJumpSeconds,
                    MediumJumpSeconds = jumps.MediumJumpSeconds,
                    LongJumpSeconds = jumps.LongJumpSeconds,
                }));
                _dialog?.Hide();
            };
            Fill(page, keys, new TextBlock
            {
                Text = "Your pending Preferences changes stay here while you edit shortcuts. Save or cancel the shortcut editor to return.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75,
            });
            return (page, n => n);
        });

        sections.Add("Opening files", () =>
        {
            var page = PreferencePage("Opening files");
            var single = Check("Use one window: open files in the player already running", s.SingleInstance);
            var enqueue = Check("Files opened that way join the playlist instead of playing", s.EnqueueFromSecondLaunch);
            enqueue.IsEnabled = single.IsChecked == true;
            single.Checked += (_, _) => enqueue.IsEnabled = true;
            single.Unchecked += (_, _) => enqueue.IsEnabled = false;
            Props.SetHelpText(enqueue, "Applies when files open in the existing player window.");
            Fill(page, single, enqueue);
            return (page, n => n with { SingleInstance = single.IsChecked == true, EnqueueFromSecondLaunch = enqueue.IsChecked == true });
        });

        sections.Add("Updates", () => UpdatePreferences(s, updateStatus, requestEditor, () =>
        {
            _settings = sections.Apply(_settings);
            SaveSettings();
        }));
    }

    private (StackPanel, Func<PlayerSettings, PlayerSettings>) PerformancePreferences(PlayerSettings s)
    {
        var page = PreferencePage("Performance");
        var performance = Choice("Fit rexplayer to this computer", ["Automatic", "Best quality", "Balanced", "Fastest"], (int)s.Performance);
        Props.SetAutomationId(performance, "PreferencesPerformance");
        var performanceNow = new TextBlock { TextWrapping = TextWrapping.Wrap };
        Props.SetAutomationId(performanceNow, "PreferencesPerformanceNow");
        var performanceChanges = new TextBlock { TextWrapping = TextWrapping.Wrap };
        Props.SetAutomationId(performanceChanges, "PreferencesPerformanceChanges");
        var performanceTranscriptionNote = new TextBlock
        {
            Text = "Transcription quality stays as you choose it on the caption rail or under Transcription, in every mode.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
        };
        Props.SetAutomationId(performanceTranscriptionNote, "PreferencesPerformanceTranscriptionNote");
        var energySaverAutomatic = Check("When Energy Saver is on, use the Automatic performance mode", s.AutomaticWhenEnergySaver);
        Props.SetAutomationId(energySaverAutomatic, "PreferencesEnergySaverAutomatic");
        var energySaverHint = new TextBlock
        {
            Text = "The mode chosen above returns when Energy Saver turns off.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(32, 0, 0, 8),
            Opacity = 0.75,
        };
        Props.SetHelpText(energySaverAutomatic, energySaverHint.Text);
        void ShowPerformance()
        {
            // What would be in effect once saved: a mode chosen while Energy Saver holds Automatic wins.
            var choice = (PerformanceChoice)Math.Max(0, performance.SelectedIndex);
            var energy = _energy.WithChoice(choice, energySaverAutomatic.IsChecked == true);
            performanceNow.Text = energy.Describe(_facts);
            var changes = Rex.Media.AppCore.Performance.DeviceProfile.Changes(Rex.Media.AppCore.Performance.DeviceProfile.For(Rex.Media.AppCore.Performance.DeviceProfile.Choose(energy.Effective, _facts), _facts));
            performanceChanges.Text = changes.Count == 0 ? "Nothing is held back: every picture, effect and visualisation at full quality." : string.Join("\n", changes);
        }

        performance.SelectionChanged += (_, _) => ShowPerformance();
        energySaverAutomatic.Checked += (_, _) => ShowPerformance();
        energySaverAutomatic.Unchecked += (_, _) => ShowPerformance();
        ShowPerformance();
        Fill(page, new TextBlock { Text = "On a slower computer rexplayer can do a little less so playback stays smooth. Automatic decides from the computer and Windows' energy saver; choose a level to decide yourself.", TextWrapping = TextWrapping.Wrap },
            performance, energySaverAutomatic, energySaverHint, performanceNow, performanceChanges, performanceTranscriptionNote);
        return (page, n => n with
        {
            Performance = (PerformanceChoice)Math.Max(0, performance.SelectedIndex),
            AutomaticWhenEnergySaver = energySaverAutomatic.IsChecked == true,
        });
    }

    private static (StackPanel, Func<PlayerSettings, PlayerSettings>) SubtitlePreferences(PlayerSettings s)
    {
        var page = PreferencePage("Subtitles");
        var font = new ComboBox { Header = "Subtitle font", IsEditable = true, ItemsSource = SubtitleFonts, Text = s.SubtitleFont, MinWidth = 220 };
        Props.SetName(font, "Subtitle font");
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
        Describe(codePage, "For subtitle files that do not say how their text is written. Choose another if their letters come out wrong.");
        Fill(page, font, size, color.Box, opacity, bold, outline, outlineColor.Box, shadow, box, boxColor.Box, margin, inBars, atBottom, styles, codePage);
        return (page, n => n with
        {
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
        });
    }

    private static (StackPanel, Func<PlayerSettings, PlayerSettings>) LibraryPreferences(PlayerSettings s)
    {
        var page = PreferencePage("Library");
        var mediaOpenClick = Choice("Open media on", ["Single click", "Double click"], (int)s.MediaOpenClickMode);
        Props.SetAutomationId(mediaOpenClick, "PreferencesMediaOpenClick");
        var libraryOpening = Choice("When opening the library", ["Show the playing item", "Return to where I left the library"], s.OpenLibraryAtPlayingItem ? 0 : 1);
        Props.SetAutomationId(libraryOpening, "PreferencesLibraryOpening");
        var detailWriting = Choice("When I edit song details", ["Write them into the files", "Keep them in rexplayer only"], s.WriteDetailsIntoFiles ? 0 : 1);
        Props.SetAutomationId(detailWriting, "PreferencesDetailWriting");
        var libraryFullScreen = Check("Leave full screen when the library opens", s.LeaveFullScreenWhenLibraryOpens);
        var libraryPlayback = Choice("When media starts from the library", ["Show the player", "Keep browsing while audio plays"], s.ShowPlayerWhenLibraryMediaStarts ? 0 : 1);
        Props.SetAutomationId(libraryFullScreen, "PreferencesLibraryFullScreen");
        Props.SetAutomationId(libraryPlayback, "PreferencesLibraryPlayback");
        var pauseLibrary = Check("Pause playback when the library opens", s.PauseWhenLibraryOpens);
        var keepLibraryAudio = Check("Keep audio-only items playing in the library", s.KeepAudioPlayingInLibrary);
        Props.SetAutomationId(pauseLibrary, "PreferencesPauseLibrary");
        Props.SetAutomationId(keepLibraryAudio, "PreferencesKeepLibraryAudio");
        keepLibraryAudio.IsEnabled = pauseLibrary.IsChecked == true;
        pauseLibrary.Checked += (_, _) => keepLibraryAudio.IsEnabled = true;
        pauseLibrary.Unchecked += (_, _) => keepLibraryAudio.IsEnabled = false;
        Fill(page, libraryOpening, detailWriting, mediaOpenClick, libraryFullScreen, libraryPlayback, pauseLibrary, keepLibraryAudio, new TextBlock
        {
            Text = "Return to where I left the library keeps your browsing view while the player is open. Ctrl or Shift clicks still select media without opening it.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
        });
        return (page, n => n with
        {
            MediaOpenClickMode = (MediaOpenClick)Math.Max(0, mediaOpenClick.SelectedIndex),
            OpenLibraryAtPlayingItem = libraryOpening.SelectedIndex == 0,
            WriteDetailsIntoFiles = detailWriting.SelectedIndex == 0,
            LeaveFullScreenWhenLibraryOpens = libraryFullScreen.IsChecked == true,
            ShowPlayerWhenLibraryMediaStarts = libraryPlayback.SelectedIndex == 0,
            PauseWhenLibraryOpens = pauseLibrary.IsChecked == true,
            KeepAudioPlayingInLibrary = keepLibraryAudio.IsChecked == true,
        });
    }

    private (StackPanel, Func<PlayerSettings, PlayerSettings>) UpdatePreferences(PlayerSettings s, TextBlock updateStatus,
        Action<Func<Task>> requestEditor, Action savePending)
    {
        var page = PreferencePage("Updates");
        var updates = Choice("Look for new versions", ["Never", "Daily", "Weekly"], (int)s.UpdateChecks);
        Props.SetAutomationId(updates, "PreferencesUpdateChecks");
        var updateMode = Choice("When a new version is found", ["Install it by itself when rexplayer is closed or not in use", "Download it, then ask me when to install", "Only tell me about it"], (int)s.UpdateMode);
        Props.SetAutomationId(updateMode, "PreferencesUpdateMode");
        var checkNow = new Button { Content = "Check for updates now" };
        Props.SetAutomationId(checkNow, "PreferencesCheckUpdates");
        checkNow.Click += (_, _) => Forget(CheckForUpdatesAsync(asked: true));
        var installNow = new Button { Content = "Restart and install now", Visibility = Pending is { Stage: UpdateStage.Ready } ? Visibility.Visible : Visibility.Collapsed };
        Props.SetAutomationId(installNow, "PreferencesInstallUpdate");
        installNow.Click += (_, _) =>
        {
            // The app restarts to install, so pending changes are saved rather than lost.
            savePending();
            _dialog?.Hide();
            OnWindowThread(() => Forget(InstallAsync(unattended: false)));
        };
        var updateButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        Fill(updateButtons, checkNow, installNow);
        var lastUpdated = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        if (Rex.Media.AppCore.Updates.UpdateFlow.LastUpdated(s.LastUpdatedVersion, s.LastUpdatedAt, DateTimeOffset.Now) is { } updated)
        {
            var updatedText = new TextBlock { Text = updated, VerticalAlignment = VerticalAlignment.Center };
            Props.SetAutomationId(updatedText, "PreferencesLastUpdated");
            var whatsNew = new HyperlinkButton { Content = "What's new" };
            Props.SetAutomationId(whatsNew, "PreferencesWhatsNew");
            whatsNew.Click += (_, _) =>
            {
                requestEditor(ShowWhatsNewAsync);
                _dialog?.Hide();
            };
            Fill(lastUpdated, updatedText, whatsNew);
        }

        Fill(page, updates, updateMode, updateStatus, lastUpdated, updateButtons);
        return (page, n => n with
        {
            UpdateChecks = (UpdateCadence)updates.SelectedIndex,
            UpdateMode = (UpdateMode)updateMode.SelectedIndex,
        });
    }

    private static StackPanel PreferencePage(string name)
    {
        var page = new StackPanel { Spacing = 8, MinWidth = 280, MaxWidth = 460 };
        var heading = Heading(name);
        Props.SetHeadingLevel(heading, Peers.AutomationHeadingLevel.Level2);
        page.Children.Add(heading);
        return page;
    }

    private static void Fill(Panel panel, params UIElement[] controls)
    {
        foreach (var control in controls)
        {
            panel.Children.Add(control);
        }
    }

    /// <summary>Long check-box labels wrap inside the available page width instead of widening it.</summary>
    private static void WrapLabels(UIElement element)
    {
        if (element is CheckBox { Content: string label } check)
        {
            check.Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap };
            Props.SetName(check, label);
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
}
