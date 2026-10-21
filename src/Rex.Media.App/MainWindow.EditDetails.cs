using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Library;
using Rex.Media.Library;

namespace Rex.Media.App;

/// <summary>Edit details: the title, artist, album and the rest of one song or several, from the library's menu.</summary>
public sealed partial class MainWindow
{
    /// <summary>Adds "Edit details..." when the chosen lines hold songs.</summary>
    private void AddEditDetailsMenuItem(MenuFlyout menu, IReadOnlyList<LibraryEntry> entries)
    {
        var songs = entries.Where(entry => entry.Kind == LibraryKind.Music).ToList();
        if (songs.Count > 0)
        {
            menu.Items.Add(MenuItem("Edit details...", "LibraryMenuEditDetails", () => _ = EditDetailsAsync(songs)));
        }
    }

    private async Task EditDetailsAsync(IReadOnlyList<LibraryEntry> songs)
    {
        var fields = DetailsForm.Fields(songs);
        var boxes = new Dictionary<string, TextBox>(StringComparer.Ordinal);
        var panel = new StackPanel { Spacing = 8, MinWidth = 360 };
        foreach (var field in fields)
        {
            var box = new TextBox
            {
                Header = field.Label,
                Text = field.Value,
                PlaceholderText = field.Several ? DetailsForm.SeveralValues : "",
            };
            AutomationProperties.SetAutomationId(box, "EditDetails_" + field.Key);
            boxes[field.Key] = box;
            panel.Children.Add(box);
        }

        panel.Children.Add(new TextBlock
        {
            Text = "Saved in rexplayer only; the files are not changed.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
        });
        var title = songs.Count == 1 ? "Edit details" : $"Edit details of {songs.Count} songs";
        if (!await Ask(title, new ScrollViewer { Content = panel, MaxHeight = 520 }, "Save"))
        {
            return;
        }

        var change = DetailsForm.Change(fields, boxes.ToDictionary(pair => pair.Key, pair => pair.Value.Text, StringComparer.Ordinal));
        if (change.IsEmpty)
        {
            return;
        }

        var paths = songs.Select(song => song.Path).ToList();
        _library!.SetOverride(paths, change);
        _player.ShowEditedDetails(paths, change);
        Say(songs.Count == 1 ? "Saved the song's details in rexplayer" : $"Saved the details of {songs.Count} songs in rexplayer");
    }
}
