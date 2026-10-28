using Microsoft.UI.Xaml;
using Rex.Media.AppCore.Shell;

namespace Rex.Media.App;

public sealed partial class MainWindow
{
    /// <summary>
    /// A short message over the picture, which fades once there was time to read it. With on-screen
    /// messages off, only a problem is shown: otherwise it would reach only the log.
    /// </summary>
    private void Say(string text)
    {
        App.Log.Info(LogSource, text);
        if (!OnScreenMessage.Shows(text, _settings.OnScreenMessages))
        {
            return;
        }

        _osdNoticeItem = _player.Item;
        Osd.Text = text;
        OsdBox.Visibility = Visibility.Visible;
        _osdTimer.Stop();
        _osdTimer.Interval = _evidenceOsd ? TimeSpan.FromSeconds(30) : OnScreenMessage.Duration(text);
        _osdTimer.Start();
    }
}
