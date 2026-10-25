using Rex.Media.Primitives;
using Rex.Media.Settings;

namespace Rex.Media.App.Mac;

/// <summary>Local cover art, source-timed lyrics and a lightweight live audio meter for music playback.</summary>
public sealed partial class MainWindow
{
    private NSStackView? _musicPresentation;
    private NSImageView? _musicArtwork;
    private NSTextField? _musicHeading;
    private NSTextField? _musicLyrics;
    private NSTextField? _musicSpectrum;
    private NSTimer? _musicTimer;
    private NSImage? _musicImage;
    private byte[]? _musicImageSource;
    private int _musicLyricLine = -2;
    private bool _musicShowLyrics = true;
    private readonly float[] _musicLeft = new float[1024];
    private readonly float[] _musicRight = new float[1024];

    private void AddMusicPresentation(NSView content)
    {
        _musicArtwork = new NSImageView(new CGRect(0, 0, 160, 160))
        {
            ImageScaling = NSImageScale.ProportionallyUpOrDown,
            Hidden = true,
        };
        _musicArtwork.WidthAnchor.ConstraintEqualTo(160).Active = true;
        _musicArtwork.HeightAnchor.ConstraintEqualTo(160).Active = true;
        _musicHeading = NSTextField.CreateWrappingLabel("");
        _musicHeading.Font = NSFont.SystemFontOfSize(22, NSFontWeight.Semibold)!;
        _musicHeading.TextColor = NSColor.White;
        _musicHeading.Alignment = NSTextAlignment.Center;
        _musicSpectrum = NSTextField.CreateLabel("");
        _musicSpectrum.Font = NSFont.UserFixedPitchFontOfSize(18)!;
        _musicSpectrum.Alignment = NSTextAlignment.Center;
        _musicSpectrum.TextColor = NSColor.White;
        _musicLyrics = NSTextField.CreateWrappingLabel("");
        _musicLyrics.Font = NSFont.SystemFontOfSize(15, NSFontWeight.Medium)!;
        _musicLyrics.Alignment = NSTextAlignment.Center;
        _musicLyrics.TextColor = NSColor.White;
        _musicLyrics.WidthAnchor.ConstraintLessThanOrEqualTo(550).Active = true;
        _musicPresentation = NSStackView.FromViews([
            _musicArtwork, _musicHeading, _musicSpectrum, _musicLyrics,
        ]);
        _musicPresentation.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
        _musicPresentation.Alignment = NSLayoutAttribute.CenterX;
        _musicPresentation.Spacing = 12;
        _musicPresentation.TranslatesAutoresizingMaskIntoConstraints = false;
        _musicPresentation.Hidden = true;
        content.AddSubview(_musicPresentation);
        _musicPresentation.CenterXAnchor.ConstraintEqualTo(_picture!.CenterXAnchor).Active = true;
        _musicPresentation.CenterYAnchor.ConstraintEqualTo(_picture.CenterYAnchor).Active = true;
        _musicPresentation.WidthAnchor.ConstraintLessThanOrEqualTo(_picture.WidthAnchor, 1, -48).Active = true;
        ((NSView)_musicPresentation).AccessibilityLabel = "Now playing music, cover art, audio meter and lyrics";
        _musicShowLyrics = _settings.ShowLyrics;
        _musicTimer = NSTimer.CreateRepeatingScheduledTimer(TimeSpan.FromMilliseconds(120), _ => RefreshMusicPosition());
    }

    private bool IsMusicPlaying() =>
        _player.Item is not null && _player.Info?.FirstTrack(MediaKind.Video) is null
        && _player.Info?.FirstTrack(MediaKind.Audio) is not null;

    private void RefreshMusicPresentation()
    {
        if (_musicPresentation is null || _musicHeading is null || _musicArtwork is null)
        {
            return;
        }

        var visible = IsMusicPlaying();
        _musicPresentation.Hidden = !visible;
        if (!visible)
        {
            _musicLyricLine = -2;
            return;
        }

        var text = _player.Title + (string.IsNullOrWhiteSpace(_player.Artist) ? "" : "\n" + _player.Artist);
        if (_musicHeading.StringValue != text)
        {
            _musicHeading.StringValue = text;
        }

        // The controller owns the bytes. Only decode again when a different media item supplies art.
        if (!ReferenceEquals(_musicImageSource, _player.CoverArt))
        {
            _musicArtwork.Image = null;
            _musicImage?.Dispose();
            _musicImage = null;
            _musicImageSource = _player.CoverArt;
            if (_musicImageSource is { Length: > 0 } art)
            {
                try
                {
                    using var data = NSData.FromArray(art);
                    _musicImage = new NSImage(data);
                    _musicArtwork.Image = _musicImage;
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    _log.Warning(LogSource, "Cover artwork could not be displayed: " + ex.Message);
                }
            }

            _musicArtwork.Hidden = _musicImage is null;
        }

        _musicLyricLine = -2;
        RefreshMusicPosition();
    }

    private void RefreshMusicPosition()
    {
        if (_musicPresentation?.Hidden != false || _musicLyrics is null || _musicSpectrum is null)
        {
            return;
        }

        var lyrics = _musicShowLyrics ? _player.Lyrics : null;
        var line = lyrics?.IndexAt(_player.Position) ?? -1;
        if (line != _musicLyricLine)
        {
            _musicLyricLine = line;
            var text = lyrics is null ? "" : line < 0
                ? (lyrics.IsTimed ? "" : string.Join("\n", lyrics.Lines.Take(3).Select(item => item.Text)))
                : string.Join("\n", lyrics.Lines.Skip(Math.Max(0, line - 1)).Take(3)
                    .Select((entry, at) => at == Math.Min(1, line) ? "♪ " + entry.Text : entry.Text));
            _musicLyrics.StringValue = text;
            _musicLyrics.Hidden = text.Length == 0;
        }

        if (_settings.Visualizer == VisualizerChoice.Off || !_player.ReadSound(_musicLeft, _musicRight))
        {
            _musicSpectrum.StringValue = "";
            return;
        }

        // Display the actual current sample window, never a fake animation with invented peaks.
        const string blocks = "▁▂▃▄▅▆▇█";
        Span<char> visual = stackalloc char[48];
        var frame = _musicLeft.Length / visual.Length;
        for (var bar = 0; bar < visual.Length; bar++)
        {
            double peak = 0;
            for (var sample = bar * frame; sample < (bar + 1) * frame; sample++)
            {
                var level = _settings.Visualizer == VisualizerChoice.Meters
                    ? Math.Abs(bar < 24 ? _musicLeft[sample] : _musicRight[sample])
                    : Math.Max(Math.Abs(_musicLeft[sample]), Math.Abs(_musicRight[sample]));
                peak = Math.Max(peak, level);
            }

            visual[bar] = blocks[(int)Math.Clamp(peak * blocks.Length, 0, blocks.Length - 1)];
        }

        _musicSpectrum.StringValue = new string(visual);
    }

    private void ToggleMusicLyrics()
    {
        _musicShowLyrics = !_musicShowLyrics;
        _settings = _settings with { ShowLyrics = _musicShowLyrics };
        SaveMusicSettings();
        _musicLyricLine = -2;
        RefreshMusicPosition();
    }

    private void CycleMusicVisualizer()
    {
        // Only the visualisations actually rendered by this native surface are offered here.
        _settings = _settings with
        {
            Visualizer = _settings.Visualizer switch
            {
                VisualizerChoice.Off => VisualizerChoice.Meters,
                VisualizerChoice.Meters => VisualizerChoice.Spectrum,
                _ => VisualizerChoice.Off,
            },
        };
        SaveMusicSettings();
        Say("Audio visualisation: " + _settings.Visualizer);
    }

    private void SaveMusicSettings()
    {
        _player.Settings = _settings;
        try
        {
            SettingsStore.Save(_roots.SettingsPath, _settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning(LogSource, "Music preferences could not be saved: " + ex.Message);
        }
    }

    private void DisposeMusicPresentation()
    {
        _musicTimer?.Invalidate();
        _musicTimer = null;
        _musicArtwork!.Image = null;
        _musicImage?.Dispose();
        _musicImage = null;
    }
}
