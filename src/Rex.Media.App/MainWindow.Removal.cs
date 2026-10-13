using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Player;
using Rex.Media.Primitives;

namespace Rex.Media.App;

/// <summary>
/// The removal section of the Subject Lock panel: its mode (Remove or Blend), its explanation, its two
/// sliders' names and live readouts, and a live line saying where the fill on screen came from. The
/// words come from <see cref="RemovalPanel"/>; the mode is saved in settings.
/// </summary>
public sealed partial class MainWindow
{
    private bool _removalPollBusy;

    private void WireRemovalTools()
    {
        SubjectRemovalMode.Header = RemovalPanel.ModeLabel;
        SubjectRemovalModeRemove.Content = RemovalPanel.ModeName(RemovalMode.Remove);
        SubjectRemovalModeBlend.Content = RemovalPanel.ModeName(RemovalMode.Blend);
        ToolTipService.SetToolTip(SubjectRemovalMode, RemovalPanel.ModeHelp);
        AutomationProperties.SetHelpText(SubjectRemovalMode, RemovalPanel.ModeHelp);
        SubjectRemovalNote.Text = RemovalPanel.Note;
        SubjectToleranceLabel.Text = RemovalPanel.CoverageLabel;
        SubjectFeatherLabel.Text = RemovalPanel.SoftnessLabel;
        ToolTipService.SetToolTip(SubjectTolerance, RemovalPanel.CoverageHelp);
        ToolTipService.SetToolTip(SubjectFeather, RemovalPanel.SoftnessHelp);
        AutomationProperties.SetHelpText(SubjectTolerance, RemovalPanel.CoverageHelp);
        AutomationProperties.SetHelpText(SubjectFeather, RemovalPanel.SoftnessHelp);
        ShowRemovalMode();
        ShowRemovalReadouts();
        SubjectFillText.Text = RemovalPanel.FillText(tracking: false, erase: false, 0, 0);
        if (_subjectTimer is { } timer)
        {
            timer.Tick += (_, _) => PollRemovalFill();
        }
    }

    /// <summary>Shows the saved mode and its explanation, and hands it to the presenter.</summary>
    private void ShowRemovalMode()
    {
        var mode = _settings.SubjectRemoval;
        SubjectRemovalMode.SelectedIndex = mode == RemovalMode.Blend ? 1 : 0;
        SubjectRemovalDescription.Text = RemovalPanel.Explain(mode);
        OnPresenterThread(p => p.SetSubjectRemovalMode(mode));
    }

    private void OnSubjectRemovalModeChanged(object sender, SelectionChangedEventArgs e)
    {
        var mode = SubjectRemovalMode.SelectedIndex == 1 ? RemovalMode.Blend : RemovalMode.Remove;
        if (mode == _settings.SubjectRemoval || _closed)
        {
            return;
        }

        _settings = _settings with { SubjectRemoval = mode };
        SaveSettings();
        SubjectRemovalDescription.Text = RemovalPanel.Explain(mode);
        var redraw = _subjectErase && !_player.IsPlaying;
        OnPresenterThread(p =>
        {
            p.SetSubjectRemovalMode(mode);
            if (redraw)
            {
                p.Redraw();
            }
        });
    }

    private void ShowRemovalReadouts()
    {
        SubjectFeatherText.Text = RemovalPanel.SoftnessText((int)SubjectFeather.Value);
        SubjectToleranceText.Text = RemovalPanel.CoverageText((int)SubjectTolerance.Value);
    }

    /// <summary>Asks the presenter where the last preview's fill came from and shows it.</summary>
    private void PollRemovalFill()
    {
        if (!_subjectOpen || _removalPollBusy || _presenter is null || _closed)
        {
            return;
        }

        _removalPollBusy = true;
        OnPresenterThread(p =>
        {
            var tracking = p.SubjectStatus().Tracking;
            var fill = p.SubjectFill();
            OnWindowThread(() =>
            {
                _removalPollBusy = false;
                if (_subjectOpen && !_closed)
                {
                    SubjectFillText.Text = RemovalPanel.FillText(tracking, fill.Erase, fill.ObservedPixels, fill.EstimatedPixels, fill.Source, fill.Mode);
                }
            });
        });
    }
}
