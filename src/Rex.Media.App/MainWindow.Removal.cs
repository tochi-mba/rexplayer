using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Player;

namespace Rex.Media.App;

/// <summary>
/// The removal section of the Subject Lock panel: its explanation, its two sliders' names and live
/// readouts, and a live line saying where the fill on screen came from. The words come from
/// <see cref="RemovalPanel"/>.
/// </summary>
public sealed partial class MainWindow
{
    private bool _removalPollBusy;

    private void WireRemovalTools()
    {
        SubjectRemovalDescription.Text = RemovalPanel.Description;
        SubjectRemovalNote.Text = RemovalPanel.Note;
        SubjectToleranceLabel.Text = RemovalPanel.CoverageLabel;
        SubjectFeatherLabel.Text = RemovalPanel.SoftnessLabel;
        ToolTipService.SetToolTip(SubjectTolerance, RemovalPanel.CoverageHelp);
        ToolTipService.SetToolTip(SubjectFeather, RemovalPanel.SoftnessHelp);
        AutomationProperties.SetHelpText(SubjectTolerance, RemovalPanel.CoverageHelp);
        AutomationProperties.SetHelpText(SubjectFeather, RemovalPanel.SoftnessHelp);
        ShowRemovalReadouts();
        SubjectFillText.Text = RemovalPanel.FillText(tracking: false, erase: false, 0, 0);
        if (_subjectTimer is { } timer)
        {
            timer.Tick += (_, _) => PollRemovalFill();
        }
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
                    SubjectFillText.Text = RemovalPanel.FillText(tracking, fill.Erase, fill.ObservedPixels, fill.EstimatedPixels);
                }
            });
        });
    }
}
