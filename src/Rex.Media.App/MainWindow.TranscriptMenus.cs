using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Captions;

namespace Rex.Media.App;

/// <summary>One accessible place to choose authored, embedded, original and corrected subtitle tracks.</summary>
public sealed partial class MainWindow
{
    private MenuFlyoutSubItem? _subtitleTracksMenu;

    private void BuildTranscriptMenus()
    {
        var subtitles = Menu.Items.First(item => item.Title == "Subtitles");
        _subtitleTracksMenu = new MenuFlyoutSubItem { Text = "Choose subtitle track" };
        AutomationProperties.SetAutomationId(_subtitleTracksMenu, "SubtitleTrackChoices");
        subtitles.Items.Insert(0, _subtitleTracksMenu);

        var edit = new MenuFlyoutItem { Text = "Edit transcript..." };
        AutomationProperties.SetAutomationId(edit, "EditTranscript");
        edit.Click += (_, _) => _ = ShowTranscriptEditorAsync();
        subtitles.Items.Insert(1, edit);
        subtitles.Items.Insert(2, new MenuFlyoutSeparator());
        RefreshTranscriptMenus();
    }

    private void RefreshTranscriptMenus()
    {
        if (_subtitleTracksMenu is not { } choices)
        {
            return;
        }

        choices.Items.Clear();
        var off = new RadioMenuFlyoutItem
        {
            Text = "Off",
            GroupName = "SubtitleTrackChoice",
            IsChecked = _player.Subtitles is null,
        };
        AutomationProperties.SetAutomationId(off, "SubtitleTrackOff");
        off.Click += (_, _) => { _player.ShowSubtitles(null); SelectTranscriptCoverage(null); };
        choices.Items.Add(off);

        var tracks = _player.SubtitleTracks.ToArray();
        for (var i = 0; i < tracks.Length; i++)
        {
            var track = tracks[i];
            var variant = TranscriptVariant.FromTrackName(track.Name);
            var text = variant is { } named ? named.DisplayName : track.Name;
            var item = new RadioMenuFlyoutItem
            {
                Text = text,
                GroupName = "SubtitleTrackChoice",
                IsChecked = track == _player.Subtitles,
            };
            AutomationProperties.SetAutomationId(item, "SubtitleTrackChoice-" + i);
            AutomationProperties.SetName(item, variant is { } identified ? "Transcript " + identified.DisplayName : text);
            item.Click += (_, _) => { _player.ShowSubtitles(track); SelectTranscriptCoverage(track); };
            choices.Items.Add(item);
        }
    }
}
