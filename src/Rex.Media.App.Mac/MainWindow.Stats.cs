using Rex.Media.AppCore.Player;
using Rex.Media.AppCore.Automation;
using Rex.Media.Engine;
using Rex.Media.Primitives;

namespace Rex.Media.App.Mac;

/// <summary>Native stats overlay reading the same engine counters as the shared shell.</summary>
public sealed partial class MainWindow
{
    private RexLabel? _statsOverlay;
    private RexPanel? _statsPanel;
    private NSTimer? _statsTimer;
    private SessionStats? _previousStats;
    private DateTimeOffset _previousStatsAt;
    private bool _statsVisible;
    private bool _statsBusy;

    /// <summary>Lays the statistics card over the picture's top left corner, drawn as Windows draws its overlays.</summary>
    private void AddStatsOverlay(NSView container)
    {
        _statsOverlay = MacTheme.WrappingLabel("", 1, MacTheme.OverlayText);
        _statsOverlay.Font = NSFont.MonospacedSystemFont(MacTheme.TypeSize(1), NSFontWeight.Regular)!;
        _statsOverlay.Alignment = NSTextAlignment.Left;
        _statsOverlay.PreferredMaxLayoutWidth = 460 - 2 * MacTheme.Space(2);
        ((NSView)_statsOverlay).AccessibilityLabel = "Playback statistics";
        _statsPanel = MacTheme.Panel(MacTheme.Overlay, 1, MacTheme.OverlayHairline);
        _statsPanel.Hidden = true;
        MacTheme.Fill(_statsPanel, _statsOverlay, (double)MacTheme.Space(2));
        container.AddSubview(_statsPanel);
        _statsPanel.TopAnchor.ConstraintEqualTo(_picture!.TopAnchor, MacTheme.Space(2)).Active = true;
        _statsPanel.LeadingAnchor.ConstraintEqualTo(_picture.LeadingAnchor, MacTheme.Space(3)).Active = true;
        _statsPanel.WidthAnchor.ConstraintLessThanOrEqualTo(460).Active = true;
    }

    private void ToggleStatsOverlay()
    {
        _statsVisible = !_statsVisible;
        _statsPanel!.Hidden = !_statsVisible;
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
