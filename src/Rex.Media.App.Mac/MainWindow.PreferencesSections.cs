using Rex.Media.AppCore;
using Rex.Media.Settings;

namespace Rex.Media.App.Mac;

/// <summary>
/// Preferences' sections, each built the first time it is shown: opening the window builds one
/// page, not all of them. Each says how it changes the settings on Save; a section never shown has
/// no edits, so it changes nothing. The set mirrors the Windows dialog where this shell has the
/// feature, in Windows' words; the sections this Mac lacks arrive with their features. Each page is
/// a <see cref="SettingsPage"/> of cards whose rows give the setting at the left and its control
/// at the right.
/// </summary>
public sealed partial class MainWindow
{
    private void AddMacPreferenceSections(LazySections<NSView, PlayerSettings> sections)
    {
        sections.Add("Playback", () =>
        {
            _preferencesResumeMode = MacSettings.Choices(["Ask before resuming", "Always resume", "Start from beginning"], (int)_settings.ResumeMode);
            _preferencesMaxVolumeValue = MacSettings.ValueLabel();
            _preferencesMaxVolume = Slider(100, 200, UpdatePreferenceLabels);
            _preferencesMaxVolume.DoubleValue = _settings.MaxVolumePercent;
            UpdatePreferenceLabels();
            var page = new SettingsPage("Playback");
            page.Add(MacSettings.Card(null,
                MacSettings.Row("Saved position", "What happens when you open something you stopped part way through.", _preferencesResumeMode),
                MacSettings.Row("Loudest volume (%)",
                    "How far the volume goes. Above 100 % rexplayer makes quiet media louder than it was recorded, which can distort.",
                    MacSettings.SliderWithValue(_preferencesMaxVolume, _preferencesMaxVolumeValue))));
            return (page, n => n with
            {
                ResumeMode = (ResumeBehavior)(int)_preferencesResumeMode!.IndexOfSelectedItem,
                MaxVolumePercent = (int)Math.Round(_preferencesMaxVolume!.DoubleValue),
            });
        });

        sections.Add("Video and pictures", () =>
        {
            _preferencesPictureSecondsValue = MacSettings.ValueLabel();
            _preferencesPictureSeconds = Slider(1, 60, UpdatePreferenceLabels);
            _preferencesPictureSeconds.DoubleValue = _settings.PictureSeconds;
            _preferencesHardwareDecoding = MacSettings.Check(_settings.HardwareVideoDecoding);
            _preferencesHardwareDecoding.ToolTip =
                "Turn off to request software decoding for H.264 and HEVC on this Mac.";
            _preferencesH264Hardware = MacSettings.Check(_settings.HardwareH264Decoding);
            _preferencesHevcHardware = MacSettings.Check(_settings.HardwareHevcDecoding);
            UpdatePreferenceLabels();
            var page = new SettingsPage("Video and pictures");
            page.Add(
                MacSettings.Card(null,
                    MacSettings.Row("Show each picture for (seconds)", null,
                        MacSettings.SliderWithValue(_preferencesPictureSeconds, _preferencesPictureSecondsValue))),
                MacSettings.Card("Hardware decoding",
                    MacSettings.Row("Allow VideoToolbox hardware decoding",
                        "Applies to the next opened video. Turn off to request software decoding for H.264 and HEVC on this Mac.",
                        _preferencesHardwareDecoding),
                    MacSettings.Row("Prefer hardware H.264 decoding", null, _preferencesH264Hardware),
                    MacSettings.Row("Prefer hardware HEVC decoding", null, _preferencesHevcHardware)));
            return (page, n => n with
            {
                PictureSeconds = (int)Math.Round(_preferencesPictureSeconds!.DoubleValue),
                HardwareVideoDecoding = IsChecked(_preferencesHardwareDecoding),
                HardwareH264Decoding = IsChecked(_preferencesH264Hardware),
                HardwareHevcDecoding = IsChecked(_preferencesHevcHardware),
            });
        });

        sections.Add("Snapshots", () =>
        {
            _preferencesSnapshotPattern = new NSTextField
            {
                PlaceholderString = "{title} {time} (or {n})",
                StringValue = _settings.SnapshotNamePattern,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            _preferencesSnapshotPattern.WidthAnchor.ConstraintEqualTo(240).Active = true;
            _preferencesSnapshotSequential = MacSettings.Check(_settings.SnapshotSequential);
            var page = new SettingsPage("Snapshots");
            page.Add(MacSettings.Card(null,
                MacSettings.Row("Snapshot filename pattern", "Filename tokens: {title}, {time}, {n}", _preferencesSnapshotPattern),
                MacSettings.Row("Number snapshots if the name is already used", null, _preferencesSnapshotSequential)));
            return (page, n => n with
            {
                SnapshotNamePattern = _preferencesSnapshotPattern!.StringValue,
                SnapshotSequential = IsChecked(_preferencesSnapshotSequential),
            });
        });

        sections.Add("Window", () =>
        {
            _preferencesTheme = MacSettings.Choices(["macOS's choice", "Light", "Dark"], (int)_settings.Theme);
            _preferencesOsd = MacSettings.Check(_settings.OnScreenMessages);
            _preferencesOsdPosition = MacSettings.Choices(["Top", "Centre", "Bottom"], (int)_settings.OnScreenMessagePosition);
            _preferencesOsdSecondsValue = MacSettings.ValueLabel();
            _preferencesOsdSeconds = Slider(0, 30, UpdatePreferenceLabels);
            _preferencesOsdSeconds.DoubleValue = _settings.OnScreenMessageSeconds;
            _preferencesTitleSecondsValue = MacSettings.ValueLabel();
            _preferencesTitleSeconds = Slider(0, 30, UpdatePreferenceLabels);
            _preferencesTitleSeconds.DoubleValue = _settings.TitleSeconds;
            UpdatePreferenceLabels();
            var page = new SettingsPage("Window");
            page.Add(
                MacSettings.Card(null, MacSettings.Row("Theme", null, _preferencesTheme)),
                MacSettings.Card(null,
                    MacSettings.Row("Show messages over the picture", null, _preferencesOsd),
                    MacSettings.Row("Message position", null, _preferencesOsdPosition),
                    MacSettings.Row("Message duration", null,
                        MacSettings.SliderWithValue(_preferencesOsdSeconds, _preferencesOsdSecondsValue)),
                    MacSettings.Row("Show the title when an item starts for (seconds, 0 for never)", null,
                        MacSettings.SliderWithValue(_preferencesTitleSeconds, _preferencesTitleSecondsValue))));
            return (page, n => n with
            {
                Theme = (ThemeChoice)(int)_preferencesTheme!.IndexOfSelectedItem,
                OnScreenMessages = IsChecked(_preferencesOsd),
                OnScreenMessagePosition = (OnScreenMessagePosition)(int)_preferencesOsdPosition!.IndexOfSelectedItem,
                OnScreenMessageSeconds = _preferencesOsdSeconds!.DoubleValue,
                TitleSeconds = _preferencesTitleSeconds!.DoubleValue,
            });
        });

        sections.Add("History and resume", () =>
        {
            _preferencesHistory = MacSettings.Check(_settings.KeepHistory);
            _preferencesRestoreQueue = MacSettings.Check(_settings.RestoreQueue);
            var clearHistory = MacTheme.TextButton("Clear playback history", () =>
                ConfirmInWindow("Clear playback history and saved positions? This cannot be undone.", () =>
                {
                    _player.ClearHistory();
                    Say("Playback history cleared");
                }));
            var page = new SettingsPage("History and resume");
            page.Add(
                MacSettings.Card(null,
                    MacSettings.Row("Remember where things were left and what played recently", null, _preferencesHistory),
                    MacSettings.Row("Put the playlist back when rexplayer starts", null, _preferencesRestoreQueue)),
                MacSettings.Card(null,
                    MacSettings.Row("Playback history", "Forget what played and where each item was left. This cannot be undone.", clearHistory)));
            return (page, n => n with
            {
                KeepHistory = IsChecked(_preferencesHistory),
                RestoreQueue = IsChecked(_preferencesRestoreQueue),
            });
        });

        sections.Add("Opening files", () =>
        {
            _preferencesEnqueue = MacSettings.Check(_settings.EnqueueFromSecondLaunch);
            _preferencesSingleInstance = NSButton.CreateCheckbox("", () => _preferencesEnqueue.Enabled = IsChecked(_preferencesSingleInstance));
            _preferencesSingleInstance.State = State(_settings.SingleInstance);
            _preferencesEnqueue.Enabled = _settings.SingleInstance;
            var page = new SettingsPage("Opening files");
            page.Add(MacSettings.Card(null,
                MacSettings.Row("Use one window: open files in the player already running", null, _preferencesSingleInstance),
                MacSettings.Row("Files opened that way join the playlist instead of playing",
                    "Applies when files open in the existing player window.", _preferencesEnqueue)));
            return (page, n => n with
            {
                SingleInstance = IsChecked(_preferencesSingleInstance),
                EnqueueFromSecondLaunch = IsChecked(_preferencesEnqueue),
            });
        });

        sections.Add("Diagnostics", () =>
        {
            _preferencesCrashDumps = MacSettings.Check(_settings.LocalCrashDumps);
            var page = new SettingsPage("Diagnostics");
            page.Add(MacSettings.Card(null,
                MacSettings.Row("Keep local diagnostic dumps after a crash",
                    "Saved only on this Mac in rexplayer's data folder; never uploaded.", _preferencesCrashDumps)));
            return (page, n => n with { LocalCrashDumps = IsChecked(_preferencesCrashDumps) });
        });
    }
}
