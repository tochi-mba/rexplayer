using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Rex.Media.AppCore;
using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;
using Rex.Media.AppCore.Transcription;
using Rex.Media.Codecs.MediaFoundation;
using Rex.Media.Library;

namespace Rex.Media.App;

/// <summary>
/// Cancellable, sampled local recording waveform. Windows decoding runs off the UI thread with
/// small, spaced reads. A four-item cache is invalidated by file size or modification time.
/// Buttons are seekable, keyboard reachable; the horizontal scroll surface pans when zoomed.
/// </summary>
public sealed partial class MainWindow
{
    private sealed record WaveformSnapshot(long Size, DateTime Modified, TimeSpan Duration, float[] Peaks);

    private readonly Dictionary<string, WaveformSnapshot> _recordWaveformCache =
        new(StringComparer.OrdinalIgnoreCase);

    private async Task ShowRecordingWaveformAsync(LibraryEntry recording)
    {
        if (recording.Downloading || recording.Problem is not null || !File.Exists(recording.Path))
        {
            Say("This local recording is not available for waveform review.");
            return;
        }

        using var cancel = new CancellationTokenSource();
        var token = cancel.Token;
        var panel = new StackPanel { Spacing = 12, MaxWidth = 680 };
        var status = new TextBlock
        {
            Text = "Reading a sampled preview from the local file...",
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetAutomationId(status, "RecordingWaveformStatus");
        panel.Children.Add(status);
        var labels = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var start = new TextBlock { Text = "0:00" };
        var middle = new TextBlock();
        var end = new TextBlock();
        labels.Children.Add(start);
        labels.Children.Add(middle);
        labels.Children.Add(end);
        panel.Children.Add(labels);
        var bars = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var scroll = new ScrollViewer
        {
            Content = bars,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Enabled,
            MaxHeight = 180,
        };
        panel.Children.Add(scroll);
        var zoomLabel = new TextBlock { Text = "Zoom (scroll horizontally to pan)" };
        var zoom = new Slider { Minimum = 1, Maximum = 4, StepFrequency = 1, Value = 1, IsEnabled = false };
        AutomationProperties.SetAutomationId(zoom, "RecordingWaveformZoom");
        panel.Children.Add(zoomLabel);
        panel.Children.Add(zoom);
        var help = new TextBlock
        {
            Text = "Select a bar to seek if this recording is playing. This is a sampled preview, not an exhaustive loudness measurement.",
            TextWrapping = TextWrapping.Wrap,
        };
        panel.Children.Add(help);

        var snapshot = _recordWaveformCache.TryGetValue(recording.Path, out var cached)
            && cached.Size == recording.Size && cached.Modified == recording.Modified ? cached : null;
        var barButtons = new List<Button>();
        var current = -1;

        void RefreshPlayhead()
        {
            if (snapshot is null || !LibraryViews.SameLocation(_player.Item?.Location, recording.Path))
            {
                return;
            }

            var index = RecordingWaveform.Current(snapshot.Duration, _player.Position, snapshot.Peaks.Length);
            if (index == current)
            {
                return;
            }

            if (current >= 0 && current < barButtons.Count)
            {
                barButtons[current].BorderThickness = new Thickness(0);
            }

            current = index;
            if (index >= 0 && index < barButtons.Count)
            {
                barButtons[index].BorderThickness = new Thickness(2);
            }
        }

        void Draw()
        {
            if (snapshot is null)
            {
                return;
            }

            bars.Children.Clear();
            barButtons.Clear();
            current = -1;
            end.Text = "End " + TimeText.Format(snapshot.Duration, snapshot.Duration);
            middle.Text = "Middle " + TimeText.Format(snapshot.Duration / 2, snapshot.Duration);
            var accent = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            for (var index = 0; index < snapshot.Peaks.Length; index++)
            {
                var bin = index;
                var at = RecordingWaveform.At(snapshot.Duration, index, snapshot.Peaks.Length);
                var bar = new Border
                {
                    Width = 5,
                    Height = 5 + 110 * snapshot.Peaks[index],
                    Background = accent,
                    VerticalAlignment = VerticalAlignment.Bottom,
                };
                var button = new Button
                {
                    Content = bar,
                    Width = 10 * zoom.Value,
                    Height = 144,
                    Padding = new Thickness(1),
                    BorderThickness = new Thickness(0),
                    VerticalContentAlignment = VerticalAlignment.Bottom,
                };
                AutomationProperties.SetAutomationId(button, "RecordingWaveformBin-" + index);
                AutomationProperties.SetName(button,
                    $"Jump to {TimeText.Format(at, snapshot.Duration)}, sampled peak {snapshot.Peaks[index]:P0}");
                button.Click += (_, _) =>
                {
                    if (_player.CanSeek && LibraryViews.SameLocation(_player.Item?.Location, recording.Path))
                    {
                        _player.Seek(at);
                    }
                    else
                    {
                        Say("Play this recording first to seek within its waveform.");
                    }
                };
                bars.Children.Add(button);
                barButtons.Add(button);
            }

            RefreshPlayhead();
        }

        zoom.ValueChanged += (_, _) => Draw();
        EventHandler? moved = (_, _) => OnWindowThread(RefreshPlayhead);
        var initialMedia = _player.Item?.Location;
        EventHandler? mediaChanged = (_, _) =>
        {
            if (!string.Equals(_player.Item?.Location, initialMedia, StringComparison.OrdinalIgnoreCase))
            {
                cancel.Cancel();
            }
        };
        _player.PositionChanged += moved;
        _player.Changed += mediaChanged;
        try
        {
            var dialog = new ContentDialog
            {
                Title = "Recording waveform · " + recording.Title,
                Content = panel,
                CloseButtonText = "Done",
                XamlRoot = Content.XamlRoot,
            };

            if (snapshot is not null)
            {
                zoom.IsEnabled = true;
                status.Text = "Sampled waveform · Cached for this local file.";
                Draw();
            }
            else
            {
                Forget(Task.Run(() => DecodeRecordingWaveform(recording, token), token)
                    .ContinueWith(task => OnWindowThread(() =>
                    {
                        if (token.IsCancellationRequested || _closing)
                        {
                            return;
                        }

                        if (!task.IsCompletedSuccessfully || task.Result is not { } data)
                        {
                            status.Text = "The waveform could not be read. The recording remains playable.";
                            return;
                        }

                        snapshot = data;
                        if (_recordWaveformCache.Count >= 4)
                        {
                            _recordWaveformCache.Remove(_recordWaveformCache.Keys.First());
                        }

                        _recordWaveformCache[recording.Path] = data;
                        zoom.IsEnabled = true;
                        status.Text = "Sampled waveform · Select a bar to review the recording.";
                        Draw();
                    }), TaskScheduler.Default));
            }

            await ShowDialogAsync(dialog);
        }
        finally
        {
            cancel.Cancel();
            _player.PositionChanged -= moved;
            _player.Changed -= mediaChanged;
        }
    }

    private static WaveformSnapshot? DecodeRecordingWaveform(LibraryEntry entry, CancellationToken token)
    {
        using var reader = new SpeechAudioReader(entry.Path,
            MediaRegistries.Decoders(new MfDecoderFactory(), new WicDecoderFactory()), token);
        if (!reader.HasSound)
        {
            return null;
        }

        var duration = reader.Duration ?? entry.Duration ?? TimeSpan.Zero;
        if (duration <= TimeSpan.Zero)
        {
            return null;
        }

        const int buckets = 96;
        var peaks = new float[buckets];
        for (var index = 0; index < buckets; index++)
        {
            token.ThrowIfCancellationRequested();
            var from = RecordingWaveform.At(duration, index, buckets);
            var to = from + TimeSpan.FromMilliseconds(350);
            if (to > duration)
            {
                to = duration;
            }

            if (to > from)
            {
                peaks[index] = RecordingWaveform.Peak(reader.Read(new TimeRange(from, to), token));
            }
        }

        return new WaveformSnapshot(entry.Size, entry.Modified, duration, peaks);
    }
}
