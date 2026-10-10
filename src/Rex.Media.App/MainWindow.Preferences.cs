using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        var veryShort = Number("Very short jump (seconds)", s.VeryShortJumpSeconds, 1, 3600);
        var shortJump = Number("Short jump (seconds)", s.ShortJumpSeconds, 1, 3600);
        var medium = Number("Medium jump (seconds)", s.MediumJumpSeconds, 1, 3600);
        var longJump = Number("Long jump (seconds)", s.LongJumpSeconds, 1, 3600);
        var step = Number("Volume step (%)", s.VolumeStepPercent, 1, 25);
        var maxVolume = Number("Loudest volume (%)", s.MaxVolumePercent, 100, 200);
        var seekPreview = Check("Show the video frame under the pointer on the timeline", s.SeekPreview);
        var pictures = Number("Show each picture for (seconds)", s.PictureSeconds, 1, 3600);
        var navigator = Check("Show the whole picture in a corner while zoomed in", s.ShowNavigator);
        var pictureLook = Choice("Default video picture look", VideoLooks.Names, (int)s.VideoLook);
        var visualizer = Choice("While music plays, show", ["Nothing", "A spectrum", "An oscilloscope", "Level meters", "A spectrogram", "A vinyl record", "A halo", "A mirrored wave", "An aurora", "Embers", "Ripples", "A colour strobe", "My silhouette, from the camera", "A live beat edit"], (int)s.Visualizer);
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
        var title = Number("Show the title when an item starts for (seconds, 0 for never)", s.TitleSeconds, 0, 30);
        var single = Check("Use one window: open files in the player already running", s.SingleInstance);
        var enqueue = Check("Files opened that way join the playlist instead of playing", s.EnqueueFromSecondLaunch);
        var updates = Choice("Look for new versions", ["Never", "Daily", "Weekly"], (int)s.UpdateChecks);
        var resume = Choice("Opening something left part-way through", ["Offer to go back there", "Always go back there", "Start from the top"], (int)s.ResumePlayback);
        var history = Check("Remember where things were left and what played recently", s.KeepHistory);
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

        var content = new StackPanel { Spacing = 8, MinWidth = 460 };
        content.Children.Add(Heading("Playback"));
        foreach (var control in new UIElement[] { veryShort, shortJump, medium, longJump, step, maxVolume, seekPreview })
        {
            content.Children.Add(control);
        }

        content.Children.Add(Heading("Video and pictures"));
        foreach (var control in new UIElement[] { pictures, navigator, pictureLook, visualizer, generatedArt, artworkStyle, artworkColor, artworkDetail, artworkContrast, artworkIdentity })
        {
            content.Children.Add(control);
        }

        content.Children.Add(Heading("Languages"));
        content.Children.Add(audioLanguages);
        content.Children.Add(subtitleLanguages);

        content.Children.Add(Heading("Subtitles"));
        foreach (var control in new UIElement[] { font, size, color.Box, opacity, bold, outline, outlineColor.Box, shadow, box, boxColor.Box, margin, inBars, atBottom, styles, codePage })
        {
            content.Children.Add(control);
        }

        content.Children.Add(Heading("Window"));
        foreach (var control in new UIElement[] { theme, onTop, hide, messages, title })
        {
            content.Children.Add(control);
        }

        content.Children.Add(Heading("Memory"));
        foreach (var control in new UIElement[] { resume, history, queue, sleep })
        {
            content.Children.Add(control);
        }

        content.Children.Add(Heading("Keyboard and mouse"));
        var keys = new Button { Content = "Change shortcuts and what the mouse does..." };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(keys, "PreferencesKeyboard");
        keys.Click += (_, _) =>
        {
            // One dialog at a time: this one closes, unsaved, and the editor opens.
            _dialog?.Hide();
            DispatcherQueue.TryEnqueue(() => _ = ShowKeyboardAndMouseAsync());
        };
        content.Children.Add(keys);

        content.Children.Add(Heading("rexplayer"));
        foreach (var control in new UIElement[] { single, enqueue, updates })
        {
            content.Children.Add(control);
        }

        if (!await Ask("Preferences", new ScrollViewer { Content = content, MaxHeight = 560 }, "Save"))
        {
            return;
        }

        var next = (s with
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
            Visualizer = (VisualizerChoice)visualizer.SelectedIndex,
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
            TitleSeconds = title.Value,
            SingleInstance = single.IsChecked == true,
            EnqueueFromSecondLaunch = enqueue.IsChecked == true,
            UpdateChecks = (UpdateCadence)updates.SelectedIndex,
            ResumePlayback = (ResumeChoice)resume.SelectedIndex,
            KeepHistory = history.IsChecked == true,
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
        }).Normalize();
        var artworkChanged = s.GenerateAudioArtwork != next.GenerateAudioArtwork
            || s.AudioArtworkStyle != next.AudioArtworkStyle
            || s.AudioArtworkColor != next.AudioArtworkColor
            || s.AudioArtworkDetail != next.AudioArtworkDetail
            || s.AudioArtworkContrast != next.AudioArtworkContrast
            || s.AudioArtworkUsesIdentity != next.AudioArtworkUsesIdentity;
        _settings = next;
        _player.Settings = _settings;
        _player.SetVolume(_player.Volume);
        _controlsTimer.Interval = TimeSpan.FromSeconds(_settings.ControlsHideSeconds);
        ApplyTheme();
        ApplyAlwaysOnTop();
        ApplyVideoLook(_settings.VideoLook);
        LayOutSubtitles();
        SetView(_view);
        ShowState();
        SaveSettings();
        Say("Preferences saved");

        // A settings-specific cache key regenerates only visible covers, in the existing bounded
        // background queue. The rest are made lazily as the user scrolls to them.
        if (artworkChanged && LibraryPane.Visibility == Visibility.Visible)
        {
            CancelLibraryPictures();
            _shownLibrary = "";
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
