using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;
using Rex.Media.AppCore.Transcription;
using Rex.Media.Subtitles;

namespace Rex.Media.App;

/// <summary>
/// Reads locally cached speech as keyboard-accessible paragraphs, rather than treating a
/// podcast or voice note like a line-by-line song. Only one page of buttons is materialized.
/// Manual scrolling and finding speech suspend follow mode until explicitly resumed.
/// </summary>
public sealed partial class MainWindow
{
    private async Task ShowTranscriptReaderAsync(string? selectedPath = null)
    {
        var media = selectedPath ?? _player.Item?.Location;
        if (string.IsNullOrWhiteSpace(media) || !File.Exists(media))
        {
            Say("Select an available local file before reading its transcript.");
            return;
        }

        var model = SpeechModels.For(_settings.KineticCaptions.TranscriptModel).Id;
        var duration = selectedPath is null ? _player.Duration
            : _library?.Entry(media)?.Duration ?? TimeSpan.Zero;
        var loaded = await Task.Run(() =>
        {
            var file = new FileInfo(media);
            var ledger = _ledger.Find(TranscriptSignature.Of(file.Length, file.LastWriteTimeUtc));
            var failure = ledger is { State: TranscriptState.Failed } failed
                && string.Equals(failed.Model, model, StringComparison.OrdinalIgnoreCase)
                ? failed.Reason ?? "The transcription job failed." : null;
            if (failure is { Length: > 200 })
            {
                failure = failure[..200] + "…";
            }

            var original = _transcripts.Load(media, model);
            var partial = original is null ? _transcripts.LoadPartial(media, model) : null;
            original ??= partial?.ToTranscript();
            if (original is null || original.IsEmpty)
            {
                return (Content: (Transcript?)null, Complete: false, Edited: false, Failure: failure);
            }

            var edited = _transcripts.LoadEdited(media, model)?.MergeGenerated(
                original, partial?.Done ?? (duration > TimeSpan.Zero
                    ? TimeRanges.Of([new TimeRange(TimeSpan.Zero, duration)]) : TimeRanges.Empty),
                partial?.Duration ?? duration, partial is null);
            return (Content: edited?.Content ?? original, Complete: partial is null,
                Edited: edited is not null, Failure: failure);
        });
        if (_closing || (selectedPath is null && _player.Item?.Location != media))
        {
            return;
        }

        if (loaded.Content is not { IsEmpty: false } transcript)
        {
            Say(loaded.Failure is { } error
                ? "Transcription failed: " + error + " Retry it from the library."
                : "No transcript is saved for this file yet. Transcribe it first.");
            return;
        }

        var cues = await Task.Run(transcript.ToCues);
        if (_closing || (selectedPath is null && _player.Item?.Location != media) || cues.Count == 0)
        {
            return;
        }

        var panel = new StackPanel { Spacing = 10, MaxWidth = 680 };
        var coverageText = loaded.Complete
            ? "Saved transcript · Local speech · Select a paragraph to jump to its time."
            : "Partial transcript · Some speech has not been transcribed yet.";
        if (loaded.Edited)
        {
            coverageText += " · Edited words shown; the generated original is preserved.";
        }

        if (loaded.Failure is { } failureReason)
        {
            coverageText += " · Transcription error: " + failureReason;
        }

        var coverage = new TextBlock
        {
            Text = coverageText,
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetAutomationId(coverage, "TranscriptReaderCoverage");
        panel.Children.Add(coverage);

        var navigation = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
        var previous = new Button { Content = "Previous paragraphs" };
        var next = new Button { Content = "Next paragraphs" };
        var follow = new Button { Content = "Follow playback", IsEnabled = false };
        var pageLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(previous, "TranscriptReaderPrevious");
        AutomationProperties.SetAutomationId(next, "TranscriptReaderNext");
        AutomationProperties.SetAutomationId(follow, "TranscriptReaderFollow");
        AutomationProperties.SetAutomationId(pageLabel, "TranscriptReaderPage");
        navigation.Children.Add(previous);
        navigation.Children.Add(pageLabel);
        navigation.Children.Add(next);
        navigation.Children.Add(follow);
        panel.Children.Add(navigation);

        var finding = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var query = new TextBox { Header = "Find speech", Width = 310, PlaceholderText = "Word or phrase" };
        var find = new Button { Content = "Find next", VerticalAlignment = VerticalAlignment.Bottom };
        var result = new TextBlock { Text = "Read or search the saved speech.", TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(query, "TranscriptReaderSearch");
        AutomationProperties.SetAutomationId(find, "TranscriptReaderFind");
        AutomationProperties.SetAutomationId(result, "TranscriptReaderResult");
        finding.Children.Add(query);
        finding.Children.Add(find);
        panel.Children.Add(finding);
        panel.Children.Add(result);

        var paragraphs = new StackPanel { Spacing = 8 };
        var scroll = new ScrollViewer { Content = paragraphs, MaxHeight = 440 };
        panel.Children.Add(scroll);
        const int pageSize = 35;
        var page = 0;
        var match = -1;
        var following = true;
        var autoScrolling = false;
        var active = -1;
        var buttons = new Dictionary<int, Button>();

        int CueAt(TimeSpan at)
        {
            var low = 0;
            var high = cues.Count;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (cues[middle].End <= at) low = middle + 1;
                else high = middle;
            }

            return Math.Min(low, cues.Count - 1);
        }

        void ShowPage()
        {
            buttons.Clear();
            paragraphs.Children.Clear();
            var first = page * pageSize;
            var end = Math.Min(cues.Count, first + pageSize);
            previous.IsEnabled = page > 0;
            next.IsEnabled = end < cues.Count;
            pageLabel.Text = $"Paragraphs {first + 1}–{end} of {cues.Count}";
            for (var index = first; index < end; index++)
            {
                var cueIndex = index;
                var cue = cues[index];
                var time = TimeText.Format(cue.Start, duration);
                var button = new Button
                {
                    Content = new TextBlock { Text = $"{time}   {cue.Text}", TextWrapping = TextWrapping.Wrap },
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(10, 8, 10, 8),
                    FontWeight = index == active ? FontWeights.SemiBold : FontWeights.Normal,
                };
                AutomationProperties.SetAutomationId(button, "TranscriptReaderCue-" + index);
                AutomationProperties.SetName(button, $"Jump to {time}. {cue.Text}");
                button.Click += (_, _) =>
                {
                    var at = cue.Start < TimeSpan.Zero ? TimeSpan.Zero : cue.Start;
                    if (_player.CanSeek && LibraryViews.SameLocation(_player.Item?.Location, media))
                    {
                        _player.Seek(at);
                    }
                    else if (selectedPath is not null && _library?.Entry(media) is { } selected
                        && !selected.Downloading && selected.Problem is null && File.Exists(selected.Path))
                    {
                        // A library transcript can be read without autoplay; seeking is explicit.
                        // The only file in the queue is the one whose transcript is being read.
                        _player.PlayFromLibrary([selected], 0, at);
                    }
                };
                buttons[index] = button;
                paragraphs.Children.Add(button);
            }
        }

        void ShowPosition()
        {
            if (!LibraryViews.SameLocation(_player.Item?.Location, media))
            {
                return;
            }

            var at = _player.Position;
            var index = CueAt(at);
            if (at < cues[index].Start || at >= cues[index].End)
            {
                index = -1;
            }

            if (index == active)
            {
                return;
            }

            if (buttons.TryGetValue(active, out var old))
            {
                old.FontWeight = FontWeights.Normal;
            }

            active = index;
            if (following && index >= 0 && page != index / pageSize)
            {
                page = index / pageSize;
                ShowPage();
            }

            if (buttons.TryGetValue(index, out var current))
            {
                current.FontWeight = FontWeights.SemiBold;
                if (following)
                {
                    autoScrolling = true;
                    current.StartBringIntoView(new BringIntoViewOptions
                    {
                        VerticalAlignmentRatio = 0.35,
                        AnimationDesired = false,
                    });
                    autoScrolling = false;
                }
            }
        }

        scroll.ViewChanged += (_, e) =>
        {
            if (!autoScrolling && e.IsIntermediate)
            {
                following = false;
                follow.IsEnabled = true;
            }
        };
        previous.Click += (_, _) => { following = false; follow.IsEnabled = true; page--; ShowPage(); };
        next.Click += (_, _) => { following = false; follow.IsEnabled = true; page++; ShowPage(); };
        follow.Click += (_, _) =>
        {
            following = true;
            follow.IsEnabled = false;
            active = -1;
            ShowPosition();
        };
        query.TextChanged += (_, _) => match = -1;
        find.Click += (_, _) =>
        {
            match = TranscriptSearch.FindNext(transcript.Words, query.Text, match);
            if (match < 0)
            {
                result.Text = "No matching speech in the saved transcript.";
                return;
            }

            var at = transcript.Words[match].Start;
            var cueIndex = CueAt(at);
            following = false;
            follow.IsEnabled = true;
            page = cueIndex / pageSize;
            ShowPage();
            result.Text = $"Match at {TimeText.Format(at, duration)}. Select the paragraph to jump.";
            if (buttons.TryGetValue(cueIndex, out var button))
            {
                button.Focus(FocusState.Programmatic);
            }
        };

        EventHandler? positionChanged = (_, _) => OnWindowThread(ShowPosition);
        _player.PositionChanged += positionChanged;
        try
        {
            ShowPage();
            ShowPosition();
            var dialog = new ContentDialog
            {
                Title = "Read " + new TranscriptVariant(model, false).Quality + " transcript",
                Content = panel,
                CloseButtonText = "Done",
                XamlRoot = Content.XamlRoot,
            };
            await ShowDialogAsync(dialog);
        }
        finally
        {
            _player.PositionChanged -= positionChanged;
        }
    }
}
