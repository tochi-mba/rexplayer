using Rex.Media.AppCore.Commands;
using Rex.Media.Engine;
using Rex.Media.Settings;
using Rex.Media.Subtitles;

namespace Rex.Media.App.Mac;

/// <summary>Native text/dual-subtitle and OSD overlay, composited above the Metal picture.</summary>
public sealed partial class MainWindow
{
    private NSTextField? _primarySubtitleOverlay;
    private NSTextField? _secondarySubtitleOverlay;
    private NSTextField? _videoMessageOverlay;
    private SessionState _lastTitleState = SessionState.Idle;
    private NSLayoutConstraint? _subtitleBottomConstraint;
    private NSLayoutConstraint? _secondaryTopConstraint;

    private void AddSubtitleOverlays(NSView container)
    {
        _primarySubtitleOverlay = SubtitleLabel("Primary subtitle");
        _secondarySubtitleOverlay = SubtitleLabel("Secondary subtitle");
        _videoMessageOverlay = SubtitleLabel("Playback message");
        _videoMessageOverlay.Font = NSFont.SystemFontOfSize(14, NSFontWeight.Medium)!;
        foreach (var label in new[] { _primarySubtitleOverlay, _secondarySubtitleOverlay, _videoMessageOverlay })
        {
            container.AddSubview(label);
            label.LeadingAnchor.ConstraintEqualTo(_picture!.LeadingAnchor, 28).Active = true;
            label.TrailingAnchor.ConstraintEqualTo(_picture.TrailingAnchor, -28).Active = true;
        }

        _subtitleBottomConstraint = _primarySubtitleOverlay.BottomAnchor.ConstraintEqualTo(_picture!.BottomAnchor, -28);
        _subtitleBottomConstraint.Active = true;
        _secondaryTopConstraint = _secondarySubtitleOverlay.TopAnchor.ConstraintEqualTo(_picture.TopAnchor, 28);
        _secondaryTopConstraint.Active = true;
        _videoMessageOverlay.TopAnchor.ConstraintEqualTo(_secondarySubtitleOverlay.BottomAnchor, 12).Active = true;
    }

    private static NSTextField SubtitleLabel(string accessibilityName)
    {
        var label = NSTextField.CreateWrappingLabel("");
        label.Alignment = NSTextAlignment.Center;
        label.Font = NSFont.SystemFontOfSize(24, NSFontWeight.Semibold)!;
        label.TextColor = NSColor.White;
        label.Hidden = true;
        label.TranslatesAutoresizingMaskIntoConstraints = false;
        ((NSView)label).AccessibilityLabel = accessibilityName;
        return label;
    }

    private void UpdateSubtitleOverlays()
    {
        if (_primarySubtitleOverlay is null || _secondarySubtitleOverlay is null)
        {
            return;
        }

        var fontSize = (nfloat)Math.Clamp(22.0 * _settings.SubtitleSize / 100.0, 11, 96);
        var font = NSFont.FromFontName(_settings.SubtitleFont == "Segoe UI" ? "Helvetica Neue" : _settings.SubtitleFont, fontSize)
            ?? NSFont.SystemFontOfSize(fontSize, _settings.SubtitleBold ? NSFontWeight.Semibold : NSFontWeight.Regular)!;
        _primarySubtitleOverlay.Font = font;
        _secondarySubtitleOverlay.Font = font;
        var colour = _settings.SubtitleColor;
        var rgba = NSColor.FromRgba(
            (colour >> 16) & 0xff, (colour >> 8) & 0xff, colour & 0xff,
            (int)Math.Round(_settings.SubtitleOpacity * 255.0 / 100));
        _primarySubtitleOverlay.TextColor = rgba;
        _secondarySubtitleOverlay.TextColor = rgba;
        if (_subtitleBottomConstraint is not null && _secondaryTopConstraint is not null)
        {
            var margin = Math.Max(8, (double)_picture!.Bounds.Height *
                _settings.SubtitleMargin / 100.0);
            _subtitleBottomConstraint.Constant = _settings.SubtitlesInBars
                ? -8 : -(nfloat)margin;
            _secondaryTopConstraint.Constant = (nfloat)margin;
        }
        var primary = _player.SubtitlesAt(_player.Position);
        var secondary = _player.SecondarySubtitlesAt(_player.Position);
        var visible = _player.Item is not null;
        var regular = visible && (!MacKineticVisible || _settings.KineticCaptions.KeepSubtitles);
        SetSubtitleLines(_primarySubtitleOverlay, regular ? primary : [], _player.Position);
        SetSubtitleLines(_secondarySubtitleOverlay, regular ? secondary : [], _player.Position);
    }

    private static void SetSubtitleLines(NSTextField label, IReadOnlyList<SubtitleCue> cues, TimeSpan position)
    {
        // SubtitleRun.At is relative to cue start. Only reveal source-timed runs
        // when they are spoken; untimed captions display in full, without inventing
        // a per-word clock.
        var text = string.Join("\n", cues.Select(cue =>
            string.Join("\n", cue.Lines.Select(line =>
                string.Concat(line.Runs.Where(run =>
                    run.At is null || run.At <= position - cue.Start).Select(run => run.Text)))))
            .Where(value => value.Length > 0));
        if (label.StringValue != text)
        {
            label.StringValue = text;
        }

        label.Hidden = text.Length == 0;
    }

    private void ShowTitleOnPlaybackStart()
    {
        var state = _player.State;
        var enteringOpen = state == SessionState.Opening && _lastTitleState != SessionState.Opening;
        _lastTitleState = state;
        if (!enteringOpen || _player.Item is null || _settings.TitleSeconds <= 0)
        {
            return;
        }

        SetVideoOSD(_player.Title);
        _clearMessage?.Invalidate();
        _clearMessage = NSTimer.CreateScheduledTimer(TimeSpan.FromSeconds(_settings.TitleSeconds),
            _ => SetVideoOSD(""));
    }

    private void SetVideoOSD(string message)
    {
        if (_videoMessageOverlay is not null)
        {
            _videoMessageOverlay.StringValue = message;
            _videoMessageOverlay.Hidden = message.Length == 0;
        }
    }

    private void ChangeSubtitleScale(int delta)
    {
        _settings = (_settings with
        {
            SubtitleSize = delta == 0 ? 100 : Math.Clamp(_settings.SubtitleSize + delta, 50, 400),
        }).Normalize();
        _player.Settings = _settings;
        UpdateSubtitleOverlays();
        try
        {
            SettingsStore.Save(_roots.SettingsPath, _settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning(LogSource, "Subtitle size could not be saved: " + ex.Message);
        }

        Say("Subtitle size " + _settings.SubtitleSize + "%");
    }
}
