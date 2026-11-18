using Rex.Media.AppCore;
using Rex.Media.Settings;

namespace Rex.Media.App.Mac;

/// <summary>
/// Preferences' sections, each built the first time it is shown: opening the window builds one
/// page, not all of them. Each says how it changes the settings on Save; a section never shown has
/// no edits, so it changes nothing. The set mirrors the Windows dialog where this shell has the
/// feature; the sections this Mac lacks arrive with their features.
/// </summary>
public sealed partial class MainWindow
{
    private void AddMacPreferenceSections(LazySections<NSStackView, PlayerSettings> sections)
    {
        sections.Add("Playback", () =>
        {
            _preferencesResumeMode = EffectChoices(["Ask before resuming", "Always resume", "Start from beginning"]);
            _preferencesResumeMode.SelectItem((int)_settings.ResumeMode);
            _preferencesMaxVolumeValue = NSTextField.CreateLabel("");
            _preferencesMaxVolumeValue.Font = NSFont.MonospacedDigitSystemFontOfSize(12, NSFontWeight.Regular)!;
            _preferencesMaxVolume = Slider(100, 200, UpdatePreferenceLabels);
            _preferencesMaxVolume.DoubleValue = _settings.MaxVolumePercent;
            UpdatePreferenceLabels();
            var page = PreferencePage("Playback",
                EffectChoiceRow("Saved position", _preferencesResumeMode),
                PreferenceRow("Loudest volume", _preferencesMaxVolume, _preferencesMaxVolumeValue));
            return (page, n => n with
            {
                ResumeMode = (ResumeBehavior)(int)_preferencesResumeMode!.IndexOfSelectedItem,
                MaxVolumePercent = (int)Math.Round(_preferencesMaxVolume!.DoubleValue),
            });
        });

        sections.Add("Video and pictures", () =>
        {
            _preferencesPictureSecondsValue = NSTextField.CreateLabel("");
            _preferencesPictureSecondsValue.Font = NSFont.MonospacedDigitSystemFontOfSize(12, NSFontWeight.Regular)!;
            _preferencesPictureSeconds = Slider(1, 60, UpdatePreferenceLabels);
            _preferencesPictureSeconds.DoubleValue = _settings.PictureSeconds;
            _preferencesHardwareDecoding = CheckBox(
                "Allow VideoToolbox hardware decoding (applies to the next opened video)");
            _preferencesHardwareDecoding.ToolTip =
                "Turn off to request software decoding for H.264 and HEVC on this Mac.";
            _preferencesHardwareDecoding.State = State(_settings.HardwareVideoDecoding);
            _preferencesH264Hardware = CheckBox("Prefer hardware H.264 decoding");
            _preferencesH264Hardware.State = State(_settings.HardwareH264Decoding);
            _preferencesHevcHardware = CheckBox("Prefer hardware HEVC decoding");
            _preferencesHevcHardware.State = State(_settings.HardwareHevcDecoding);
            UpdatePreferenceLabels();
            var page = PreferencePage("Video and pictures",
                PreferenceRow("Show each picture for", _preferencesPictureSeconds, _preferencesPictureSecondsValue),
                _preferencesHardwareDecoding,
                _preferencesH264Hardware,
                _preferencesHevcHardware);
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
            _preferencesSnapshotPattern = new NSTextField(new CGRect(0, 0, 300, 24))
            {
                PlaceholderString = "{title} {time} (or {n})",
                StringValue = _settings.SnapshotNamePattern,
            };
            ((NSView)_preferencesSnapshotPattern).AccessibilityLabel = "Snapshot filename pattern";
            _preferencesSnapshotSequential = CheckBox("Number snapshots if the name is already used");
            _preferencesSnapshotSequential.State = State(_settings.SnapshotSequential);
            var page = PreferencePage("Snapshots",
                TimingRow("Filename tokens: {title}, {time}, {n}", _preferencesSnapshotPattern),
                _preferencesSnapshotSequential);
            return (page, n => n with
            {
                SnapshotNamePattern = _preferencesSnapshotPattern!.StringValue,
                SnapshotSequential = IsChecked(_preferencesSnapshotSequential),
            });
        });

        sections.Add("Window", () =>
        {
            _preferencesTheme = EffectChoices(["Follow system appearance", "Light", "Dark"]);
            _preferencesTheme.SelectItem((int)_settings.Theme);
            _preferencesOsd = CheckBox("Show playback messages over the picture");
            _preferencesOsd.State = State(_settings.OnScreenMessages);
            _preferencesOsdPosition = EffectChoices(["Top", "Centre", "Bottom"]);
            _preferencesOsdPosition.SelectItem((int)_settings.OnScreenMessagePosition);
            _preferencesOsdSecondsValue = NSTextField.CreateLabel("");
            _preferencesOsdSeconds = Slider(0, 30, UpdatePreferenceLabels);
            _preferencesOsdSeconds.DoubleValue = _settings.OnScreenMessageSeconds;
            _preferencesTitleSecondsValue = NSTextField.CreateLabel("");
            _preferencesTitleSeconds = Slider(0, 30, UpdatePreferenceLabels);
            _preferencesTitleSeconds.DoubleValue = _settings.TitleSeconds;
            UpdatePreferenceLabels();
            var page = PreferencePage("Window",
                EffectChoiceRow("Theme", _preferencesTheme),
                _preferencesOsd,
                EffectChoiceRow("Message position", _preferencesOsdPosition),
                PreferenceRow("Message duration", _preferencesOsdSeconds, _preferencesOsdSecondsValue),
                PreferenceRow("Starting title duration", _preferencesTitleSeconds, _preferencesTitleSecondsValue));
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
            _preferencesHistory = CheckBox("Remember recently played media and saved positions");
            _preferencesHistory.State = State(_settings.KeepHistory);
            _preferencesRestoreQueue = CheckBox("Restore the queue when rexplayer starts");
            _preferencesRestoreQueue.State = State(_settings.RestoreQueue);
            var clearHistory = NSButton.CreateButton("Clear playback history", () =>
                ConfirmInWindow("Clear playback history and saved positions? This cannot be undone.", () =>
                {
                    _player.ClearHistory();
                    Say("Playback history cleared");
                }));
            var page = PreferencePage("History and resume",
                _preferencesHistory,
                _preferencesRestoreQueue,
                clearHistory);
            return (page, n => n with
            {
                KeepHistory = IsChecked(_preferencesHistory),
                RestoreQueue = IsChecked(_preferencesRestoreQueue),
            });
        });

        sections.Add("Opening files", () =>
        {
            _preferencesSingleInstance = CheckBox("Use one rexplayer window");
            _preferencesSingleInstance.State = State(_settings.SingleInstance);
            _preferencesEnqueue = CheckBox("Files handed over by another launch join the queue");
            _preferencesEnqueue.State = State(_settings.EnqueueFromSecondLaunch);
            var page = PreferencePage("Opening files", _preferencesSingleInstance, _preferencesEnqueue);
            return (page, n => n with
            {
                SingleInstance = IsChecked(_preferencesSingleInstance),
                EnqueueFromSecondLaunch = IsChecked(_preferencesEnqueue),
            });
        });

        sections.Add("Diagnostics", () =>
        {
            _preferencesCrashDumps = CheckBox("Keep local diagnostic dumps after a crash");
            _preferencesCrashDumps.ToolTip = "Saved only on this Mac in rexplayer's data folder; never uploaded.";
            _preferencesCrashDumps.State = State(_settings.LocalCrashDumps);
            var page = PreferencePage("Diagnostics", _preferencesCrashDumps);
            return (page, n => n with { LocalCrashDumps = IsChecked(_preferencesCrashDumps) });
        });
    }

    /// <summary>A section's page: its name as the page's title, then its settings 14 apart.</summary>
    private static NSStackView PreferencePage(string name, params NSView[] rows)
    {
        var title = NSTextField.CreateLabel(name);
        title.Font = NSFont.SystemFontOfSize(17, NSFontWeight.Semibold)!;
        var page = NSStackView.FromViews([title, .. rows]);
        page.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
        page.Alignment = NSLayoutAttribute.Leading;
        page.Spacing = 14;
        page.EdgeInsets = new NSEdgeInsets(4, 4, 4, 16);
        return page;
    }
}
