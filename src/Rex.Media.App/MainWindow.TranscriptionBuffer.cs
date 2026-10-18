using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Transcription;
using Rex.Media.Engine;
using Rex.Media.Settings;

namespace Rex.Media.App;

/// <summary>The selected transcription quality, coverage rail and playback wait.</summary>
public sealed partial class MainWindow
{
    /// <summary>Paint contiguous caption coverage on a separate, labelled track beneath playback.</summary>
    private void ShowSubtitleBuffer()
    {
        ShowTranscriptModelChoice();
        var view = Rex.Media.AppCore.Transcription.SubtitleBuffer.View(_bufferDone, _bufferDuration ?? _player.Duration, _player.Position, _bufferHearing);
        var key = string.Join(";", view.Spans);
        // Keep the quality picker available even before this model has any saved coverage.
        TranscriptRail.Visibility = _player.Item is not null || view.State != BufferState.None ? Visibility.Visible : Visibility.Collapsed;
        if (key != _shownBuffer)
        {
            _shownBuffer = key;
            while (_bufferMarks.Count < view.Spans.Count)
            {
                var mark = new Rectangle
                {
                    Height = 6,
                    RadiusX = 3,
                    RadiusY = 3,
                    Fill = new SolidColorBrush(Microsoft.UI.Colors.MediumTurquoise),
                    IsHitTestVisible = false,
                };
                SubtitleBuffer.Children.Add(mark);
                _bufferMarks.Add(mark);
            }

            for (var i = 0; i < _bufferMarks.Count; i++)
            {
                _bufferMarks[i].Visibility = i < view.Spans.Count ? Visibility.Visible : Visibility.Collapsed;
            }

            PositionSubtitleBuffer();
        }

        ShowSubtitleBufferState(force: true);
    }

    private void ShowTranscriptModelChoice()
    {
        var choice = ActiveTranscriptModelChoice;
        var (name, description) = choice switch
        {
            TranscriptModelChoice.Tiny => ("Fast", SpeechModels.TinyEnglish.Speed),
            TranscriptModelChoice.Small => ("Best", SpeechModels.SmallEnglish.Speed),
            _ => ("Default", SpeechModels.BaseEnglish.Speed),
        };
        TranscriptModelButton.Content = name;
        AutomationProperties.SetName(TranscriptModelButton, "Transcription quality: " + name);
        TranscriptModelFast.IsChecked = choice == TranscriptModelChoice.Tiny;
        TranscriptModelDefault.IsChecked = choice == TranscriptModelChoice.Base;
        TranscriptModelBest.IsChecked = choice == TranscriptModelChoice.Small;
        ToolTipService.SetToolTip(TranscriptModelButton, $"{name} transcription · {description}");
    }

    private void OnTranscriptModelClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string value } || !int.TryParse(value, out var selected))
        {
            return;
        }

        var choice = (TranscriptModelChoice)Math.Clamp(selected, 0, 2);
        SetKineticCaptions(_settings.KineticCaptions with { TranscriptModel = choice });
        Say($"Transcription quality: {TranscriptModelButton.Content}");
    }

    private void PositionSubtitleBuffer()
    {
        if (TranscriptRail.Visibility != Visibility.Visible)
        {
            return;
        }

        var view = Rex.Media.AppCore.Transcription.SubtitleBuffer.View(_bufferDone, _bufferDuration ?? _player.Duration, _player.Position, _bufferHearing);
        var width = SubtitleBuffer.ActualWidth;
        if (width <= 0)
        {
            return;
        }

        for (var i = 0; i < view.Spans.Count && i < _bufferMarks.Count; i++)
        {
            var (from, to) = view.Spans[i];
            _bufferMarks[i].Width = Math.Max(2, (to - from) * width);
            Canvas.SetLeft(_bufferMarks[i], from * width);
            Canvas.SetTop(_bufferMarks[i], 0);
        }
    }

    /// <summary>A short live readout; no floating message over the video or playback rail.</summary>
    private void ShowSubtitleBufferState(bool force = false)
    {
        var duration = _bufferDuration ?? _player.Duration;
        var state = Rex.Media.AppCore.Transcription.SubtitleBuffer.StateAt(_bufferDone, duration, _player.Position, _bufferHearing);
        if (!force && state == _shownBufferState && state is BufferState.None or BufferState.Complete)
        {
            return;
        }

        _shownBufferState = state;
        var view = Rex.Media.AppCore.Transcription.SubtitleBuffer.View(_bufferDone, duration, _player.Position, _bufferHearing);
        var ahead = _bufferDone is null ? TimeSpan.Zero : Rex.Media.AppCore.Transcription.SubtitleBuffer.Ahead(_bufferDone, _player.Position);
        var label = _holdingForSubtitles && _bufferDone is not null
            ? $"Preparing captions · {Math.Round(ahead.TotalMinutes):0}/{Math.Ceiling(Rex.Media.AppCore.Transcription.SubtitleBuffer.ReleaseTarget(_worker?.Speed.SafeFactor, _player.Speed, _player.Position, duration).TotalMinutes):0} min"
            : state switch
            {
                BufferState.Ahead => $"{Math.Max(1, (int)Math.Ceiling(ahead.TotalSeconds))}s ahead",
                BufferState.CatchingUp when ahead > TimeSpan.Zero => $"{Math.Max(1, (int)Math.Ceiling(ahead.TotalSeconds))}s remaining",
                BufferState.CatchingUp => "Catching up",
                BufferState.Stalled => "Not ready here",
                BufferState.Complete => "All ready",
                _ => "",
            };
        if (SubtitleBufferState.Text != label)
        {
            SubtitleBufferState.Text = label;
        }

        CaptionBufferOverlay.Visibility = _holdingForSubtitles ? Visibility.Visible : Visibility.Collapsed;
        CaptionBufferOverlayText.Text = _holdingForSubtitles
            ? label + ". Playback will start automatically when enough captions are ready."
            : "";

        if (AutomationProperties.GetName(TranscriptRail) != view.ToolTip)
        {
            AutomationProperties.SetName(TranscriptRail, view.ToolTip);
            ToolTipService.SetToolTip(TranscriptRail, view.ToolTip.Length > 0 ? view.ToolTip : null);
        }
    }

    /// <summary>What the timeline preview says of the subtitles at the pointer, while any are being heard.</summary>
    private void ShowSeekPreviewSubtitles(TimeSpan at)
    {
        var show = _bufferDone is not null && _bufferHearing;
        SeekPreviewSubtitles.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            SeekPreviewSubtitles.Text = Rex.Media.AppCore.Transcription.SubtitleBuffer.HintAt(_bufferDone!, at);
        }
    }

    /// <summary>Playback held while it has caught up with the subtitles, when the user allows it.</summary>
    private void HoldForSubtitles()
    {
        var done = _bufferDone ?? TimeRanges.Empty;
        // A missing or failed speech model cannot make more coverage. Never strand the
        // player behind an unavailable worker; the user may still choose to play manually.
        var hearing = _bufferHearing && _modelInstalled == true && _worker?.Status.Error is null;
        var hasUsableSubtitles = Rex.Media.AppCore.Transcription.SubtitleBuffer.HasUsableSubtitles(_player.SubtitleTracks);
        var waitForTranscript = _playingWanted && Rex.Media.AppCore.Transcription.SubtitleBuffer.ShouldWaitForTranscript(
            hasUsableSubtitles, _settings.KineticCaptions.Words);
        var allowed = _settings.Transcription.HoldForSubtitles && waitForTranscript;
        var desired = Rex.Media.AppCore.Transcription.SubtitleBuffer.Hold(allowed, _holdingForSubtitles,
            hearing, done, _player.Position, _bufferDuration ?? _player.Duration, _worker?.Speed.SafeFactor, _player.Speed);
        var action = _subtitleHold.Update(allowed, _player.State, desired);
        ShowSubtitleBufferState(force: true);
        switch (action)
        {
            case HoldAction.Hold:
                _player.Pause();
                Say("Preparing enough subtitles to play through without interruptions. This may take a while on slower computers.");
                ShowSubtitleBufferState(force: true);
                break;
            case HoldAction.Release:
                ShowSubtitleBufferState(force: true);
                if (_player.State == SessionState.Paused)
                {
                    _player.Execute(CommandCatalog.PlayPause);
                }

                break;
        }
    }

    /// <summary>Lets the viewer explicitly trade uninterrupted captions for immediate playback.</summary>
    private void OnPlayWithoutCaptionBuffer(object sender, RoutedEventArgs e)
    {
        _subtitleHold.PlayNow();
        ShowSubtitleBufferState(force: true);
        if (!_player.IsPlaying)
        {
            _player.Execute(CommandCatalog.PlayPause);
        }
    }

}
