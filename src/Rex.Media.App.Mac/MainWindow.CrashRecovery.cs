using Rex.Media.AppCore.Player;

namespace Rex.Media.App.Mac;

/// <summary>Offer local crash recovery without automatically opening potentially private content.</summary>
public sealed partial class MainWindow
{
    private ResumePoint? _crashRecovery;
    private NSStackView? _crashBanner;
    private NSTextField? _crashNotice;
    private NSTimer? _crashTimer;

    private void AddCrashRecoveryBanner(NSStackView chrome)
    {
        _crashNotice = NSTextField.CreateWrappingLabel("rexplayer did not close cleanly.");
        _crashNotice.TextColor = NSColor.SecondaryLabel;
        var resume = NSButton.CreateButton("Resume last file", () => AnswerCrashRecovery(true));
        var dismiss = NSButton.CreateButton("Dismiss", () => AnswerCrashRecovery(false));
        _crashBanner = NSStackView.FromViews([_crashNotice, resume, dismiss]);
        _crashBanner.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        _crashBanner.Spacing = 8;
        _crashBanner.Hidden = true;
        chrome.InsertArrangedSubview(_crashBanner, 0);
    }

    private void BeginCrashRecovery()
    {
        try
        {
            _crashRecovery = ResumeMarker.Read(_roots.ResumePath);
            if (_crashRecovery is { } last && _crashNotice is not null && _crashBanner is not null)
            {
                _crashNotice.StringValue = "The last session ended unexpectedly. Resume " +
                    last.Title + " at " + TimeSpan.FromSeconds(Math.Max(0, last.PositionSeconds)).ToString(@"h\:mm\:ss") + "?";
                _crashBanner.Hidden = false;
            }

            // Keep the old marker intact until the user decides whether to recover. A new
            // marker starts once that offer has been handled, or immediately on normal startup.
            _crashTimer = NSTimer.CreateRepeatingScheduledTimer(
                TimeSpan.FromSeconds(5), _ => WriteCrashCheckpoint());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            _log.Warning(LogSource, "Crash recovery could not be initialized: " + ex.Message);
        }
    }

    private void AnswerCrashRecovery(bool resume)
    {
        var saved = _crashRecovery;
        _crashRecovery = null;
        if (_crashBanner is not null)
        {
            _crashBanner.Hidden = true;
        }

        try
        {
            ResumeMarker.Clear(_roots.ResumePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning(LogSource, "Crash marker could not be cleared: " + ex.Message);
        }

        if (resume && saved is { Location.Length: > 0 })
        {
            var localFile = !saved.Location.Contains("://", StringComparison.Ordinal);
            if (!localFile || File.Exists(saved.Location))
            {
                _player.Resume(saved.Location, TimeSpan.FromSeconds(Math.Max(0, saved.PositionSeconds)));
            }
            else
            {
                Say("The file from the interrupted session is no longer available.");
            }
        }
    }

    private void WriteCrashCheckpoint()
    {
        if (_crashRecovery is not null)
        {
            return;
        }

        try
        {
            if (_player.Item is { } item && _player.State is not
                (Rex.Media.Engine.SessionState.Idle or Rex.Media.Engine.SessionState.Ended or
                 Rex.Media.Engine.SessionState.Faulted or Rex.Media.Engine.SessionState.Opening))
            {
                ResumeMarker.Write(_roots.ResumePath,
                    new ResumePoint(item.Location, _player.Title, Math.Max(0, _player.Position.TotalSeconds)));
                _player.RememberPosition();
            }
            else
            {
                ResumeMarker.Clear(_roots.ResumePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning(LogSource, "Could not save the crash checkpoint: " + ex.Message);
        }
    }

    private void EndCrashRecovery()
    {
        _crashTimer?.Invalidate();
        _crashTimer = null;
        try
        {
            ResumeMarker.Clear(_roots.ResumePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning(LogSource, "Could not remove the crash checkpoint: " + ex.Message);
        }
    }
}
