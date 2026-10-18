using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Transcription;
using Rex.Media.Settings;
using Windows.Storage.Pickers;

namespace Rex.Media.App;

/// <summary>
/// The Transcription group of Preferences (SUB-20): whether the library is heard in the background
/// and which folders (the library's own list, each ticked, and folders left out), what may run
/// when, the model and the language, and the transcripts' storage with ways to clear it. Off by
/// default. The storage is measured off the window's thread while the dialog is open.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The group's controls, and how the settings change on Save.</summary>
    private (UIElement[] Controls, Func<PlayerSettings, PlayerSettings> Apply) TranscriptionPreferences()
    {
        var t = _settings.Transcription;
        var k = _settings.KineticCaptions;
        var library = Check("Transcribe the library in the background, at low priority", t.Library);
        AutomationProperties.SetAutomationId(library, "PreferencesTranscribeLibrary");
        var scope = Choice("What to transcribe", ["Every video in the library", "Only the folders ticked below"], (int)t.Scope);
        AutomationProperties.SetAutomationId(scope, "PreferencesTranscriptionScope");

        // The library's own folders, each ticked or not; the ticks count when only those are transcribed.
        var folders = new StackPanel { Spacing = 2 };
        var ticks = new List<(CheckBox Box, string Folder)>();
        var i = 0;
        foreach (var folder in _library?.Folders ?? [])
        {
            var tick = new CheckBox { Content = folder, IsChecked = t.Folders.Contains(folder, StringComparer.OrdinalIgnoreCase) };
            AutomationProperties.SetAutomationId(tick, "PreferencesTranscriptionFolder-" + i++.ToString(CultureInfo.InvariantCulture));
            ticks.Add((tick, folder));
            folders.Children.Add(tick);
        }

        if (ticks.Count == 0)
        {
            folders.Children.Add(new TextBlock { Text = "The library watches no folders yet; add them in the library.", TextWrapping = TextWrapping.Wrap });
        }

        void Follow() => folders.Visibility = scope.SelectedIndex == (int)TranscriptionScope.ChosenFolders ? Visibility.Visible : Visibility.Collapsed;
        scope.SelectionChanged += (_, _) => Follow();
        Follow();

        // Folders left out: listed with a way to put each back, and a picker to leave one out.
        var excluded = t.Excluded.ToList();
        var leftOut = new StackPanel { Spacing = 2 };
        void ShowLeftOut()
        {
            leftOut.Children.Clear();
            foreach (var folder in excluded)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                row.Children.Add(new TextBlock { Text = folder, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 360 });
                var back = new HyperlinkButton { Content = "Put back" };
                AutomationProperties.SetName(back, "Put back " + folder);
                back.Click += (_, _) =>
                {
                    excluded.Remove(folder);
                    ShowLeftOut();
                };
                row.Children.Add(back);
                leftOut.Children.Add(row);
            }
        }

        ShowLeftOut();
        var leaveOut = new Button { Content = "Leave a folder out..." };
        AutomationProperties.SetAutomationId(leaveOut, "PreferencesTranscriptionLeaveOut");
        leaveOut.Click += async (_, _) =>
        {
            var picker = Prepared(new FolderPicker { SuggestedStartLocation = PickerLocationId.VideosLibrary });
            picker.FileTypeFilter.Add("*");
            if (await picker.PickSingleFolderAsync() is { } chosen && !excluded.Contains(chosen.Path, StringComparer.OrdinalIgnoreCase))
            {
                excluded.Add(chosen.Path);
                ShowLeftOut();
            }
        };

        var music = Check("Transcribe songs too (music is mostly sung, so it is left out unless you ask)", t.IncludeMusic);
        var whatPlays = Check("Transcribe whatever I play, from where I am, ahead of the playhead", t.WhatPlays);
        var whilePlaying = Check("Keep transcribing the library while something plays (it uses one processor thread)", t.WhilePlaying);
        var battery = Check("Keep transcribing the library on battery power", t.OnBattery);
        // This is the same opt-in hold used by progressive captions, not a second buffering mode.
        // Explain the potentially long initial wait and the escape hatch before the user opts in.
        var hold = Check("Wait for generated subtitles before playing", t.HoldForSubtitles);
        var holdHint = new TextBlock
        {
            Text = "If the selected caption source needs a transcript, wait for enough words to avoid gaps. To generate subtitles for media not being transcribed from your library, also turn on \"Transcribe whatever I play\" above. Existing usable subtitles play without waiting unless kinetic captions are set to transcript-only. Choose Play now to skip the wait.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(32, 0, 0, 8),
            Opacity = 0.75,
        };
        AutomationProperties.SetHelpText(hold, holdHint.Text);
        var holdGroup = new StackPanel { Spacing = 2 };
        holdGroup.Children.Add(hold);
        holdGroup.Children.Add(holdHint);
        AutomationProperties.SetAutomationId(music, "PreferencesTranscribeMusic");
        AutomationProperties.SetAutomationId(whatPlays, "PreferencesTranscribeWhatPlays");
        AutomationProperties.SetAutomationId(whilePlaying, "PreferencesTranscribeWhilePlaying");
        AutomationProperties.SetAutomationId(battery, "PreferencesTranscribeOnBattery");
        AutomationProperties.SetAutomationId(hold, "PreferencesHoldForSubtitles");
        var writeSrt = Check("Keep original and edited SRT files beside transcribed media", t.WriteSrtBesideMedia);
        AutomationProperties.SetAutomationId(writeSrt, "PreferencesWriteTranscriptSrt");
        AutomationProperties.SetHelpText(writeSrt, "Files include the transcription quality and version in their names, such as Movie.transcript.fast.original.srt. Authored subtitle files are never replaced.");
        var model = Choice("Transcription quality", [.. SpeechModels.QualityChoices], (int)k.TranscriptModel);
        AutomationProperties.SetAutomationId(model, "PreferencesTranscriptionModel");
        var language = Words("Spoken language (two letters, such as \"en\"; the models offered hear English)", k.TranscriptLanguage);
        AutomationProperties.SetAutomationId(language, "PreferencesTranscriptionLanguage");

        var modelLine = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(modelLine, "PreferencesTranscriptionModelStatus");
        var download = new Button { Content = "Download now" };
        AutomationProperties.SetAutomationId(download, "PreferencesTranscriptionDownload");
        SpeechModel SelectedModel() => SpeechModels.For((TranscriptModelChoice)Math.Max(0, model.SelectedIndex));
        void ShowModel()
        {
            var selected = SelectedModel();
            var installed = _speechModels.IsInstalled(selected);
            modelLine.Text = installed
                ? $"The {selected.Name} model is downloaded."
                : $"The {selected.Name} model ({selected.Size}) is not downloaded yet. It downloads once, when transcription is first used, and never on a metered connection unless you choose Download now.";
            download.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
        }

        model.SelectionChanged += (_, _) => ShowModel();
        ShowModel();
        download.Click += async (_, _) =>
        {
            var selected = SelectedModel();
            download.IsEnabled = false;
            await EnsureSpeechModelAsync(askedNow: true, requestedModel: selected);
            ShowModel();
            download.IsEnabled = true;
        };

        var storage = new TextBlock { Text = "Measuring the transcripts kept...", TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(storage, "PreferencesTranscriptStorage");
        void Measure() => _ = Task.Run(_transcripts.Usage).ContinueWith(task => OnWindowThread(() => storage.Text = task.IsCompletedSuccessfully
            ? string.Create(CultureInfo.InvariantCulture, $"Transcripts kept: {task.Result.Files} files, {task.Result.Bytes / 1_000_000.0:0.0} MB, in {_transcripts.Root}.")
            : "The transcripts kept could not be measured."), TaskScheduler.Default);
        Measure();
        var forgetGone = new Button { Content = "Forget transcripts of files not in the library" };
        AutomationProperties.SetAutomationId(forgetGone, "PreferencesForgetGoneTranscripts");
        forgetGone.Click += (_, _) =>
        {
            var kept = (_library?.Entries ?? []).Select(TranscriptionOrder.Sign).ToHashSet();
            _ = Task.Run(() =>
            {
                foreach (var record in _ledger.Remove(record => !kept.Contains(record.Signature)))
                {
                    _transcripts.Forget(record.Signature, record.Model);
                }
            }).ContinueWith(_ => OnWindowThread(Measure), TaskScheduler.Default);
        };
        var forgetAll = new Button { Content = "Forget every transcript" };
        AutomationProperties.SetAutomationId(forgetAll, "PreferencesForgetAllTranscripts");
        forgetAll.Click += (_, _) => _ = Task.Run(() =>
        {
            _ledger.Remove(_ => true);
            _transcripts.Clear();
        }).ContinueWith(_ => OnWindowThread(() =>
        {
            _worker?.Unsettle([.. (_library?.Entries ?? []).Select(entry => entry.Path)]);
            _queueDirty = true;
            Measure();
            RefreshTranscriptBadges();
        }), TaskScheduler.Default);
        var clearing = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        clearing.Children.Add(forgetGone);
        clearing.Children.Add(forgetAll);

        var libraryOptions = new StackPanel { Spacing = 8 };
        foreach (var control in new UIElement[] { scope, folders, leftOut, leaveOut, music, whilePlaying, battery })
        {
            libraryOptions.Children.Add(control);
        }
        var backgroundDetails = new Expander
        {
            Header = "Library transcription folders and scheduling",
            Content = libraryOptions,
            IsExpanded = t.Library,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetAutomationId(backgroundDetails, "PreferencesTranscriptionDetails");
        AutomationProperties.SetName(backgroundDetails, "Library transcription folders and scheduling");
        library.Checked += (_, _) => backgroundDetails.IsExpanded = true;

        UIElement[] controls =
        [
            new TextBlock { Text = "Subtitles made from the speech, on this computer: what plays is heard from where it plays, its subtitles appearing as they are heard, and the library ahead of time.", TextWrapping = TextWrapping.Wrap },
            library, backgroundDetails, whatPlays, holdGroup, writeSrt, model, language, modelLine, download, storage, clearing,
        ];
        return (controls, settings => settings with
        {
            Transcription = (settings.Transcription with
            {
                Library = library.IsChecked == true,
                Scope = (TranscriptionScope)Math.Max(0, scope.SelectedIndex),
                Folders = [.. ticks.Where(tick => tick.Box.IsChecked == true).Select(tick => tick.Folder)],
                Excluded = excluded,
                IncludeMusic = music.IsChecked == true,
                WhatPlays = whatPlays.IsChecked == true,
                WhilePlaying = whilePlaying.IsChecked == true,
                OnBattery = battery.IsChecked == true,
                HoldForSubtitles = hold.IsChecked == true,
                WriteSrtBesideMedia = writeSrt.IsChecked == true,
            }).Normalize(),
            KineticCaptions = (settings.KineticCaptions with
            {
                TranscriptModel = (TranscriptModelChoice)Math.Max(0, model.SelectedIndex),
                TranscriptLanguage = language.Text,
            }).Normalize(),
        });
    }

    /// <summary>After Save: the queue and the model looked at again; the model fetched if transcription was turned on.</summary>
    private void TranscriptionSaved(PlayerSettings before)
    {
        _queueDirty = true;
        _plannedTranscription = null;
        _modelInstalled = null;
        var turnedOn = (_settings.Transcription.Library && !before.Transcription.Library) || (_settings.Transcription.WhatPlays && !before.Transcription.WhatPlays);
        if (_settings.Transcription.Library || _settings.Transcription.WhatPlays)
        {
            StartTranscription();
        }

        if (turnedOn)
        {
            _ = EnsureSpeechModelAsync(askedNow: false);
        }

        if (_settings.Transcription.WriteSrtBesideMedia && !before.Transcription.WriteSrtBesideMedia)
        {
            _ = Task.Run(ExportCachedTranscriptSrts);
        }

        TranscriptionTick();
        RefreshTranscriptBadges();
    }
}
