using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
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
    /// <summary>Whether what is open is a picture, which has nothing to hear and so nothing to transcribe or wait for.</summary>
    private bool ItemIsPicture => _player.Item is { } item && Rex.Media.AppCore.Player.MediaFiles.IsPicture(item.Location);

    /// <summary>Paint contiguous caption coverage on a separate, labelled track beneath playback.</summary>
    private void ShowSubtitleBuffer()
    {
        ShowTranscriptModelChoice();
        var view = Rex.Media.AppCore.Transcription.SubtitleBuffer.View(DisplayedCoverage, DisplayedDuration, _player.Position, DisplayedHearing);
        var key = string.Join(";", view.Spans);
        // Keep the quality picker available even before this model has any saved coverage; a picture has nothing to hear.
        TranscriptRail.Visibility = Rex.Media.AppCore.Transcription.SubtitleBuffer.RailShown(_player.Item is not null, ItemIsPicture, view.State) ? Visibility.Visible : Visibility.Collapsed;
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
                    Fill = RexTheme.Brush("RexToolBrush"),
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

        var view = Rex.Media.AppCore.Transcription.SubtitleBuffer.View(DisplayedCoverage, DisplayedDuration, _player.Position, DisplayedHearing);
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
        var coverage = DisplayedCoverage;
        var duration = DisplayedDuration;
        var state = Rex.Media.AppCore.Transcription.SubtitleBuffer.StateAt(coverage, duration, _player.Position, DisplayedHearing);
        if (!force && state == _shownBufferState && state is BufferState.None or BufferState.Complete)
        {
            return;
        }

        _shownBufferState = state;
        var view = Rex.Media.AppCore.Transcription.SubtitleBuffer.View(coverage, duration, _player.Position, DisplayedHearing);
        var ahead = coverage is null ? TimeSpan.Zero : Rex.Media.AppCore.Transcription.SubtitleBuffer.Ahead(coverage, _player.Position);
        // While a new quality's coverage loads during a wait, nothing is known yet to count.
        var progress = _holdingForSubtitles && coverage is not null
            ? $"{Math.Round(ahead.TotalMinutes):0}/{Math.Ceiling(Rex.Media.AppCore.Transcription.SubtitleBuffer.ReleaseTarget(_worker?.Speed.SafeFactor, _player.Speed, _player.Position, duration).TotalMinutes):0} min"
            : null;
        var label = _holdingForSubtitles && progress is null ? "Preparing captions"
            : _holdingForSubtitles
            ? $"Preparing captions · {progress}"
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
        // The overlay's heading already says captions are being prepared: its words say how far.
        CaptionBufferOverlayText.Text = _holdingForSubtitles
            ? (progress is null ? "" : progress + " ready. ") + "Playback will start automatically when enough captions are ready."
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
        var show = DisplayedCoverage is not null && DisplayedHearing;
        SeekPreviewSubtitles.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            SeekPreviewSubtitles.Text = Rex.Media.AppCore.Transcription.SubtitleBuffer.HintAt(DisplayedCoverage!, at);
        }
    }

    /// <summary>Playback held while it has caught up with the subtitles, when the user allows it.</summary>
    private void HoldForSubtitles()
    {
        // A cached, complete transcript can arrive before the media reader reports its duration.
        // At that point its coverage is empty and the rail is hidden; restore the full span as
        // soon as the duration becomes known, without waiting for a new transcription event.
        if (_bufferDuration is { } previous && previous <= TimeSpan.Zero && _player.Duration > TimeSpan.Zero)
        {
            _bufferDuration = _player.Duration;
            if (_heard is { IsEmpty: false } && !_bufferHearing)
            {
                _bufferDone = Rex.Media.AppCore.Transcription.SubtitleBuffer.Coverage(true, null, false, _player.Duration);
            }

            ShowSubtitleBuffer();
        }

        var done = _bufferDone ?? TimeRanges.Empty;
        // A missing or failed speech model cannot make more coverage. Never strand the
        // player behind an unavailable worker; the user may still choose to play manually.
        var hearing = _bufferHearing && _modelInstalled == true && _worker?.Status.Error is null;
        var hasUsableSubtitles = Rex.Media.AppCore.Transcription.SubtitleBuffer.HasUsableSubtitles(_player.SubtitleTracks);
        var waitForTranscript = _playingWanted && Rex.Media.AppCore.Transcription.SubtitleBuffer.ShouldWaitForTranscript(
            hasUsableSubtitles, _settings.KineticCaptions.Words);
        var allowed = _coverageVariant is null && !ItemIsPicture && _settings.Transcription.HoldForSubtitles && waitForTranscript;
        var desired = Rex.Media.AppCore.Transcription.SubtitleBuffer.Hold(allowed, _holdingForSubtitles,
            hearing, done, _player.Position, _bufferDuration ?? _player.Duration, _worker?.Speed.SafeFactor, _player.Speed);
        var action = _subtitleHold.Update(allowed, _player.State, desired);
        ShowSubtitleBufferState(force: true);
        switch (action)
        {
            case HoldAction.Hold:
                _player.Pause();
                // The overlay explains the wait; a banner over it would only say it again.
                App.Log.Info(LogSource, "Waiting for enough captions to play through without interruptions.");
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
