using Rex.Media.AppCore.Commands;

namespace Rex.Media.App.Mac;

/// <summary>Dock actions and battery/sleep-friendly transport integration.</summary>
public sealed partial class MainWindow
{
    private bool _pausedBySleep;

    public IReadOnlyList<string> DockRecentMedia => _player.Recent;

    public void ExecuteDockCommand(string commandId) => Run(commandId);

    public void OpenDockRecent(string location) => _player.Open([location]);

    public void OnMacWillSleep()
    {
        _pausedBySleep = _player.IsPlaying;
        _player.RememberPosition();
        if (_pausedBySleep)
        {
            _player.Execute(CommandCatalog.PlayPause);
        }

        UpdateDockTile();
    }

    public void OnMacDidWake()
    {
        if (_pausedBySleep)
        {
            // Do not resume automatically: the original output device may have vanished.
            _pausedBySleep = false;
            Say("Mac woke up. Press Play when your audio device is ready.");
        }

        UpdateDockTile();
    }

    private void UpdateDockTile()
    {
        if (_player.Item is null)
        {
            NSApplication.SharedApplication.DockTile.BadgeLabel = "";
            return;
        }

        if (_player.IsPlaying && _player.Duration > TimeSpan.Zero)
        {
            var percent = (int)Math.Clamp(Math.Round(_player.Position / _player.Duration * 100), 0, 100);
            NSApplication.SharedApplication.DockTile.BadgeLabel = percent + "%";
        }
        else
        {
            NSApplication.SharedApplication.DockTile.BadgeLabel = _player.IsPlaying ? "▶" : "Ⅱ";
        }
    }
}
