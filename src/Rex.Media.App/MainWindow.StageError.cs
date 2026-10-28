using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Rex.Media.AppCore.Player;
using Rex.Media.Engine;

namespace Rex.Media.App;

/// <summary>
/// The stage of a file that could not be played: a sign and an eyebrow in the attention colour
/// rather than the accent, the reason in one sentence (the title block's hint), the rest of the
/// reason behind Details, and a way on. The stage is the notice, so the passing message that
/// announced the failure is not left repeating it over the stage.
/// </summary>
public sealed partial class MainWindow
{
    private bool _stageShowsError;

    private void ShowStageError()
    {
        var failed = _player.Item is not null && _player.State == SessionState.Faulted;
        if (failed)
        {
            var detail = DamageNotices.Detail(_player.Failure);
            ErrorDetailText.Text = detail ?? "";
            ErrorDetails.Visibility = detail is null ? Visibility.Collapsed : Visibility.Visible;
            if (OsdBox.Visibility == Visibility.Visible && ReferenceEquals(_osdNoticeItem, _player.Item)
                && Osd.Text.EndsWith(" could not be played: " + DamageNotices.Short(_player.Failure), StringComparison.Ordinal))
            {
                _osdTimer.Stop();
                OsdBox.Visibility = Visibility.Collapsed;
                Osd.Text = "";
            }
        }

        if (failed == _stageShowsError)
        {
            return;
        }

        _stageShowsError = failed;
        IdleErrorIcon.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        ErrorActions.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        IdleKicker.Foreground = failed ? RexTheme.Brush("RexAttentionBrush")
            : RexTheme.Brush("AccentFillColorDefaultBrush", ElementTheme.Dark);
        if (!failed)
        {
            ErrorDetails.IsExpanded = false;
            return;
        }

        // The message that would have said why is gone; a screen reader still hears it.
        FrameworkElementAutomationPeer.FromElement(Stage)?.RaiseNotificationEvent(
            AutomationNotificationKind.ActionCompleted, AutomationNotificationProcessing.ImportantMostRecent,
            DamageNotices.Failed(null, _player.Failure ?? ""), "PlaybackError");
    }
}
