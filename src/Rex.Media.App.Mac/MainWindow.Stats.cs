using Rex.Media.AppCore.Player;
using Rex.Media.AppCore.Automation;
using Rex.Media.Engine;
using Rex.Media.Primitives;

namespace Rex.Media.App.Mac;

/// <summary>Native stats overlay reading the same engine counters as the shared shell.</summary>
public sealed partial class MainWindow
{
    private NSTextField? _statsOverlay;
    private NSTimer? _statsTimer;
    private SessionStats? _previousStats;
    private DateTimeOffset _previousStatsAt;
    private bool _statsVisible;
    private bool _statsBusy;

    private void AddStatsOverlay(NSView container)
    {
        _statsOverlay = NSTextField.CreateWrappingLabel("");
        _statsOverlay.Font = NSFont.UserFixedPitchFontOfSize(12)!;
        _statsOverlay.TextColor = NSColor.White;
        _statsOverlay.Alignment = NSTextAlignment.Left;
        _statsOverlay.Hidden = true;
        _statsOverlay.TranslatesAutoresizingMaskIntoConstraints = false;
        ((NSView)_statsOverlay).AccessibilityLabel = "Playback statistics";
        container.AddSubview(_statsOverlay);
        _statsOverlay.TopAnchor.ConstraintEqualTo(_picture!.TopAnchor, 12).Active = true;
        _statsOverlay.LeadingAnchor.ConstraintEqualTo(_picture.LeadingAnchor, 16).Active = true;
        _statsOverlay.WidthAnchor.ConstraintLessThanOrEqualTo(460).Active = true;
    }

    private void ToggleStatsOverlay()
    {
        _statsVisible = !_statsVisible;
        _statsOverlay!.Hidden = !_statsVisible;
        _previousStats = null;
        _statsTimer?.Invalidate();
        _statsTimer = null;
        if (_statsVisible)
        {
            _statsTimer = NSTimer.CreateRepeatingScheduledTimer(TimeSpan.FromSeconds(1), _ => RequestStats());
            RequestStats();
        }

        ShowChecks();
    }

    private void RequestStats()
    {
        if (!_statsVisible || _statsBusy)
        {
            return;
        }

        _statsBusy = true;
        _ = FetchStatsAsync();
    }

    private async Task FetchStatsAsync()
    {
        try
        {
            var stats = await _player.StatsAsync();
            OnWindowThread(() =>
            {
                if (!_statsVisible || _statsOverlay is null || stats is null)
                {
                    return;
                }

                var now = DateTimeOffset.UtcNow;
                var interval = _previousStats is null ? TimeSpan.Zero : now - _previousStatsAt;
                _statsOverlay.StringValue = StatsText.Describe(stats, _player.Info,
                    _player.AudioTrack, _previousStats, interval, "macOS, in software")
                    + Environment.NewLine + MacDecoderPolicyDescription();
                _previousStats = stats;
                _previousStatsAt = now;
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            _log.Warning(LogSource, "Could not read playback counters: " + ex.Message);
        }
        finally
        {
            OnWindowThread(() => _statsBusy = false);
        }
    }

    private string MacDecoderPolicyDescription()
    {
        var codec = _player.Info?.FirstTrack(MediaKind.Video)?.Codec;
        var allowed = _settings.HardwareVideoDecoding && (codec switch
        {
            CodecId.H264 => _settings.HardwareH264Decoding,
            CodecId.Hevc => _settings.HardwareHevcDecoding,
            _ => true,
        });
        return allowed ? "macOS, VideoToolbox (hardware permitted)"
            : "macOS, VideoToolbox (hardware disabled by preference)";
    }

    private AutomationReply MacStatsSmokeAction(string? action)
    {
        if (action == "show" && !_statsVisible) ToggleStatsOverlay();
        else if (action != "status") return AutomationReply.Failed("Unknown stats action.");
        return new AutomationReply
        {
            Ok = true,
            Text = _statsOverlay?.StringValue,
            Title = $"visible={_statsVisible}",
        };
    }

    private void DisposeStats()
    {
        _statsTimer?.Invalidate();
        _statsTimer = null;
        _statsVisible = false;
    }
}
