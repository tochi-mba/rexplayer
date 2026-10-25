using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Player;
using Rex.Media.Primitives;
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
    private NSLayoutConstraint? _subtitleTopConstraint;
    private NSLayoutConstraint? _subtitleMiddleConstraint;
    private NSLayoutConstraint? _secondaryTopConstraint;
    private NSLayoutConstraint? _secondaryMiddleConstraint;
    private NSLayoutConstraint? _secondaryBottomConstraint;
    private SubtitlePlacement _primaryPlacement = SubtitlePlacement.Bottom;
    private SubtitlePlacement _secondaryPlacement = SubtitlePlacement.Top;

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
        _subtitleTopConstraint = _primarySubtitleOverlay.TopAnchor.ConstraintEqualTo(_picture.TopAnchor, 28);
        _subtitleMiddleConstraint = _primarySubtitleOverlay.CenterYAnchor.ConstraintEqualTo(_picture.CenterYAnchor);
        _subtitleBottomConstraint.Active = true;
        _secondaryTopConstraint = _secondarySubtitleOverlay.TopAnchor.ConstraintEqualTo(_picture.TopAnchor, 28);
        _secondaryMiddleConstraint = _secondarySubtitleOverlay.CenterYAnchor.ConstraintEqualTo(_picture.CenterYAnchor);
        _secondaryBottomConstraint = _secondarySubtitleOverlay.BottomAnchor.ConstraintEqualTo(_primarySubtitleOverlay.TopAnchor, -12);
        _secondaryTopConstraint.Active = true;
        _videoMessageOverlay.TopAnchor.ConstraintEqualTo(_picture.TopAnchor, 14).Active = true;
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

        var look = SubtitleLook.For(_settings, (double)(_picture?.Bounds.Height ?? 400));
        var fontSize = (nfloat)Math.Clamp(look.FontSize, 11, 96);
        var font = NSFont.FromFontName(look.Font == "Segoe UI" ? "Helvetica Neue" : look.Font, fontSize)
            ?? NSFont.SystemFontOfSize(fontSize, look.Bold ? NSFontWeight.Semibold : NSFontWeight.Regular)!;
        _primarySubtitleOverlay.Font = font;
        _secondarySubtitleOverlay.Font = font;
        var styleChanged = !ReferenceEquals(_macRenderedSubtitleSettings, _settings) ||
            _macRenderedSubtitleFontSize != fontSize;
        _macRenderedSubtitleSettings = _settings;
        _macRenderedSubtitleFontSize = fontSize;
        var primary = _player.SubtitlesAt(_player.Position);
        var secondary = _player.SecondarySubtitlesAt(_player.Position);
        PositionMacSubtitles(primary, secondary);
        var visible = _player.Item is not null;
        var regular = visible && (!MacKineticVisible || _settings.KineticCaptions.KeepSubtitles);
        RenderMacSubtitleLines(_primarySubtitleOverlay, regular ? primary : [],
            _player.Position, look, font, styleChanged);
        RenderMacSubtitleLines(_secondarySubtitleOverlay, regular ? secondary : [],
            _player.Position, look, font, styleChanged);
    }

    private void PositionMacSubtitles(IReadOnlyList<SubtitleCue> primary,
        IReadOnlyList<SubtitleCue> secondary)
    {
        if (_picture is null || _subtitleBottomConstraint is null ||
            _subtitleTopConstraint is null || _subtitleMiddleConstraint is null ||
            _secondaryTopConstraint is null || _secondaryMiddleConstraint is null ||
            _secondaryBottomConstraint is null) return;

        var width = (double)_picture.Bounds.Width;
        var height = (double)_picture.Bounds.Height;
        var video = _player.Info?.FirstTrack(MediaKind.Video)?.Video;
        // Derive the actual letterbox from source dimensions or the user's aspect override.
        // The picture may be resized while playing; compute offsets from its live bounds.
        var ratio = MacAspectRatios[_aspectIndex].Value;
        var across = ratio is { } chosen ? (long)Math.Round(chosen * 10000) : video?.Width ?? 0;
        var down = ratio is not null ? 10000 : video?.Height ?? 0;
        var picture = SubtitleLook.Picture(width, height, across, down);
        var margin = Math.Max(8, height * _settings.SubtitleMargin / 100.0);
        var bottomBar = picture.Y;
        var topBar = Math.Max(0, height - picture.Y - picture.Height);
        var bottomOffset = _settings.SubtitlesInBars && bottomBar > 16
            ? Math.Min(margin, bottomBar / 2) : picture.Y + margin;
        var topOffset = _settings.SubtitlesInBars && topBar > 16
            ? Math.Min(margin, topBar / 2) : topBar + margin;

        _subtitleBottomConstraint.Constant = -(nfloat)bottomOffset;
        _subtitleTopConstraint.Constant = (nfloat)topOffset;
        _secondaryTopConstraint.Constant = (nfloat)topOffset;
        _secondaryBottomConstraint.Constant = -(nfloat)Math.Max(8, margin / 3);

        var primaryPlacement = _settings.SubtitlesAtBottom ? SubtitlePlacement.Bottom
            : primary.FirstOrDefault()?.Placement ?? SubtitlePlacement.Bottom;
        // The second language normally stays at the top to keep two streams legible.
        var secondaryPlacement = _settings.SubtitlesAtBottom ? SubtitlePlacement.Bottom
            : secondary.FirstOrDefault()?.Placement is SubtitlePlacement.Middle or SubtitlePlacement.Top
                ? secondary[0].Placement : SubtitlePlacement.Top;
        if (primaryPlacement != _primaryPlacement)
        {
            _subtitleBottomConstraint.Active = false;
            _subtitleTopConstraint.Active = false;
            _subtitleMiddleConstraint.Active = false;
            _primaryPlacement = primaryPlacement;
            (primaryPlacement switch
            {
                SubtitlePlacement.Top => _subtitleTopConstraint,
                SubtitlePlacement.Middle => _subtitleMiddleConstraint,
                _ => _subtitleBottomConstraint,
            }).Active = true;
        }

        if (secondaryPlacement != _secondaryPlacement)
        {
            _secondaryTopConstraint.Active = false;
            _secondaryMiddleConstraint.Active = false;
            _secondaryBottomConstraint.Active = false;
            _secondaryPlacement = secondaryPlacement;
            (secondaryPlacement switch
            {
                SubtitlePlacement.Middle => _secondaryMiddleConstraint,
                SubtitlePlacement.Bottom => _secondaryBottomConstraint,
                _ => _secondaryTopConstraint,
            }).Active = true;
        }
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
