using System.Globalization;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Transcription;
using Rex.Media.IO;

namespace Rex.Media.App;

/// <summary>Correct model-specific speech word by word without changing its original or timings.</summary>
public sealed partial class MainWindow
{
    private async Task ShowTranscriptEditorAsync()
    {
        if (_player.Item?.Location is not { } media || !File.Exists(media))
        {
            Say("Open a local media file before editing its transcript.");
            return;
        }

        var model = SpeechModels.For(_settings.KineticCaptions.TranscriptModel).Id;
        var duration = _player.Duration;
        var loaded = await Task.Run(() =>
        {
            var original = _transcripts.Load(media, model);
            var partial = original is null ? _transcripts.LoadPartial(media, model) : null;
            original ??= partial?.ToTranscript();
            TranscriptRevision? source = original is { IsEmpty: false }
                ? new TranscriptRevision(original,
                    partial?.Done ?? (duration > TimeSpan.Zero ? TimeRanges.Of([new TimeRange(TimeSpan.Zero, duration)]) : TimeRanges.Empty),
                    partial?.Duration ?? duration, partial is null)
                : null;
            return (source, edited: _transcripts.LoadEdited(media, model));
        });
        if (loaded.source is not { } source)
        {
            Say("There are no recognised words to edit yet. Transcribe this media first.");
            return;
        }

        // Recognition may have continued since the saved correction. Keep the edited words in
        // ranges that were already heard, but show newly recognised words after those ranges.
        var edited = loaded.edited?.MergeGenerated(source.Content, source.Done, source.Duration, source.Complete);
        var history = new TranscriptEditHistory(source, edited?.Content);
        var variant = new TranscriptVariant(model, true);
        var container = new StackPanel { Spacing = 10, MinWidth = 570 };
        var instructions = new TextBlock
        {
            Text = "Edit individual words. Their timings and the generated original stay unchanged. Changes are saved automatically.",
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetAutomationId(instructions, "TranscriptEditInstructions");
        container.Children.Add(instructions);

        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var undo = new Button { Content = "Undo" };
        var redo = new Button { Content = "Redo" };
        var reset = new Button { Content = "Reset to original" };
        var retry = new Button { Content = "Retry saving", Visibility = Visibility.Collapsed };
        AutomationProperties.SetAutomationId(retry, "TranscriptEditRetry");
        AutomationProperties.SetAutomationId(undo, "TranscriptEditUndo");
        AutomationProperties.SetAutomationId(redo, "TranscriptEditRedo");
        AutomationProperties.SetAutomationId(reset, "TranscriptEditReset");
        controls.Children.Add(undo);
        controls.Children.Add(redo);
        controls.Children.Add(reset);
        controls.Children.Add(retry);
        container.Children.Add(controls);
        var saveState = new TextBlock
        {
            Text = "The generated original is preserved. Edits are saved locally.",
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetAutomationId(saveState, "TranscriptEditSaveState");
        AutomationProperties.SetLiveSetting(saveState, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        container.Children.Add(saveState);

        var navigation = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var previous = new Button { Content = "Previous words" };
        var next = new Button { Content = "Next words" };
        var pageText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(previous, "TranscriptEditPrevious");
        AutomationProperties.SetAutomationId(next, "TranscriptEditNext");
        AutomationProperties.SetAutomationId(pageText, "TranscriptEditPage");
        navigation.Children.Add(previous);
        navigation.Children.Add(pageText);
        navigation.Children.Add(next);
        container.Children.Add(navigation);

        var findControls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var search = new TextBox { Header = "Find speech", PlaceholderText = "Word or phrase", Width = 280 };
        var find = new Button { Content = "Find next", VerticalAlignment = VerticalAlignment.Bottom };
        var jump = new Button { Content = "Jump to match", VerticalAlignment = VerticalAlignment.Bottom, IsEnabled = false };
        var matchStatus = new TextBlock { Text = "Search the words that have been transcribed.", TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(search, "TranscriptEditSearch");
        AutomationProperties.SetAutomationId(find, "TranscriptEditFindNext");
        AutomationProperties.SetAutomationId(jump, "TranscriptEditJump");
        AutomationProperties.SetAutomationId(matchStatus, "TranscriptEditMatch");
        findControls.Children.Add(search);
        findControls.Children.Add(find);
        findControls.Children.Add(jump);
        container.Children.Add(findControls);
        container.Children.Add(matchStatus);

        var words = new StackPanel { Spacing = 5 };
        container.Children.Add(new ScrollViewer { Content = words, MaxHeight = 420 });
        var page = 0;
        var matchIndex = -1;
        const int pageSize = 50;
        var changing = false;
        Task pendingWrite = Task.CompletedTask;
        var lastSaveFailed = false;
        var requestedSave = 0;
        var editedSidecar = Transcript.SidecarSrtPath(media, model, edited: true);

        void SaveInOrder()
        {
            var snapshot = history.Snapshot;
            var isOriginal = history.IsOriginal;
            var writeSidecar = _settings.Transcription.WriteSrtBesideMedia;
            var revision = ++requestedSave;
            lastSaveFailed = false;
            saveState.Text = "Saving transcript edits…";
            retry.Visibility = Visibility.Collapsed;
            pendingWrite = pendingWrite.ContinueWith(_ =>
            {
                // A user may correct several words in succession. Never write
                // every obsolete keystroke snapshot and block dialog closure;
                // only the most recent revision is durably saved.
                if (revision != Volatile.Read(ref requestedSave))
                {
                    return;
                }

                try
                {
                    if (isOriginal)
                    {
                        _transcripts.ForgetEdited(media, model);
                        if (File.Exists(editedSidecar))
                        {
                            File.Delete(editedSidecar);
                        }
                    }
                    else
                    {
                        _transcripts.SaveEdited(media, snapshot);
                        if (writeSidecar)
                        {
                            AtomicFile.Write(editedSidecar, Encoding.UTF8.GetBytes(snapshot.Content.ToSrt()));
                        }
                    }

                    if (revision == Volatile.Read(ref requestedSave))
                    {
                        lastSaveFailed = false;
                    }

                    OnWindowThread(() =>
                    {
                        if (revision == requestedSave && !_closing)
                        {
                            saveState.Text = isOriginal ? "Original transcript restored and saved." : "Transcript edits saved.";
                        }
                    });
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    if (revision == Volatile.Read(ref requestedSave))
                    {
                        lastSaveFailed = true;
                    }

                    OnWindowThread(() =>
                    {
                        if (revision == requestedSave && !_closing)
                        {
                            saveState.Text = "The transcript edit could not be saved: " + error.Message;
                            retry.Visibility = Visibility.Visible;
                            Say(saveState.Text);
                        }
                    });
                }
            }, TaskScheduler.Default);
        }

        retry.Click += (_, _) => SaveInOrder();

        void ShowEditedTrack()
        {
            if (_player.Item?.Location != media)
            {
                return;
            }

            if (history.IsOriginal)
            {
                if (_player.Subtitles?.Name == variant.TrackName)
                {
                    var original = _player.SubtitleTracks.FirstOrDefault(track => TranscriptVariant.FromTrackName(track.Name) is { } entry
                        && entry.Model == model && !entry.Edited);
                    _player.ShowSubtitles(original);
                    SelectTranscriptCoverage(original);
                }

                // Reset removes the edited choice from playback as well as from disk.
                foreach (var editedTrack in _player.SubtitleTracks.Where(track => track.Name == variant.TrackName).ToArray())
                {
                    _player.RemoveSubtitles(editedTrack);
                }

                return;
            }

            var shown = _player.Subtitles?.Name == variant.TrackName;
            _player.AddSubtitles(history.Current.ToTrack(variant.TrackName), show: shown);
        }

        void UpdateNavigation()
        {
            var total = history.Current.Words.Count;
            previous.IsEnabled = page > 0;
            next.IsEnabled = (page + 1) * pageSize < total;
            undo.IsEnabled = history.CanUndo;
            redo.IsEnabled = history.CanRedo;
            reset.IsEnabled = !history.IsOriginal;
            var from = page * pageSize;
            pageText.Text = string.Create(CultureInfo.InvariantCulture, $"Words {from + 1}–{Math.Min(from + pageSize, total)} of {total}");
        }

        void ShowPage()
        {
            changing = true;
            words.Children.Clear();
            var spoken = history.Current.Words;
            for (var i = page * pageSize; i < Math.Min((page + 1) * pageSize, spoken.Count); i++)
            {
                var index = i;
                var word = spoken[index];
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                var time = word.Start.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
                var label = new TextBlock { Text = time, VerticalAlignment = VerticalAlignment.Center, Width = 98 };
                var text = new TextBox { Text = word.Text, Width = 420, MaxLength = 200, Header = null };
                AutomationProperties.SetAutomationId(text, "TranscriptEditWord-" + index);
                AutomationProperties.SetName(text, $"Word {index + 1} at {time}");
                text.TextChanged += (_, _) =>
                {
                    if (!changing && history.ReplaceWord(index, text.Text))
                    {
                        SaveInOrder();
                        ShowEditedTrack();
                        UpdateNavigation();
                    }
                };
                row.Children.Add(label);
                row.Children.Add(text);
                words.Children.Add(row);
            }

            changing = false;
            UpdateNavigation();
        }

        var searchedText = search.Text;
        search.TextChanged += (_, _) =>
        {
            // ValuePattern.SetValue and a following Invoke can be delivered in the
            // same dispatcher turn.  Do not let that delayed TextChanged callback
            // erase the result which Find has just announced for this exact query.
            if (string.Equals(searchedText, search.Text, StringComparison.Ordinal))
            {
                return;
            }

            searchedText = search.Text;
            matchIndex = -1;
            jump.IsEnabled = false;
            matchStatus.Text = "Search the words that have been transcribed.";
        };
        find.Click += (_, _) =>
        {
            searchedText = search.Text;
            matchIndex = TranscriptSearch.FindNext(history.Current.Words, search.Text, matchIndex);
            if (matchIndex < 0)
            {
                jump.IsEnabled = false;
                matchStatus.Text = "No matching speech was found.";
                return;
            }

            page = matchIndex / pageSize;
            ShowPage();
            var at = history.Current.Words[matchIndex].Start;
            matchStatus.Text = $"Match at word {matchIndex + 1} of {history.Current.Words.Count}, {Rex.Media.AppCore.Player.TimeText.Format(at, _player.Duration)}.";
            jump.IsEnabled = _player.CanSeek && _player.Item?.Location == media;
        };
        jump.Click += (_, _) =>
        {
            if (matchIndex >= 0 && matchIndex < history.Current.Words.Count
                && _player.CanSeek && _player.Item?.Location == media)
            {
                var at = history.Current.Words[matchIndex].Start;
                _player.Seek(at < TimeSpan.Zero ? TimeSpan.Zero : at);
            }
        };

        previous.Click += (_, _) => { page--; ShowPage(); };
        next.Click += (_, _) => { page++; ShowPage(); };
        undo.Click += (_, _) =>
        {
            if (history.Undo())
            {
                SaveInOrder();
                ShowEditedTrack();
                ShowPage();
            }
        };
        redo.Click += (_, _) =>
        {
            if (history.Redo())
            {
                SaveInOrder();
                ShowEditedTrack();
                ShowPage();
            }
        };
        reset.Click += (_, _) =>
        {
            history.Reset();
            SaveInOrder();
            ShowEditedTrack();
            ShowPage();
        };
        ShowPage();

        var dialog = new ContentDialog
        {
            Title = "Edit " + new TranscriptVariant(model, false).Quality + " transcript",
            Content = container,
            CloseButtonText = "Done",
            XamlRoot = Content.XamlRoot,
        };
        // Never silently let the user dismiss a failed autosave. Wait for all
        // revisions scheduled before dismissal, cancel closing on disk error,
        // and leave the Retry button usable after permissions/space are fixed.
        dialog.CloseButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                while (true)
                {
                    var newest = pendingWrite;
                    await newest;
                    if (ReferenceEquals(newest, pendingWrite))
                    {
                        break;
                    }
                }

                if (lastSaveFailed)
                {
                    args.Cancel = true;
                    retry.Visibility = Visibility.Visible;
                    saveState.Text = "Your corrections are not saved. Fix the storage problem and choose Retry saving.";
                }
            }
            catch (Exception error)
            {
                args.Cancel = true;
                retry.Visibility = Visibility.Visible;
                saveState.Text = "Saving could not finish: " + error.Message;
            }
            finally
            {
                deferral.Complete();
            }
        };
        await ShowDialogAsync(dialog);
        await pendingWrite;
    }
}
