using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Rex.Media.App;

/// <summary>Picking and adding one or several folders to the watched library.</summary>
public sealed partial class MainWindow
{
    private async void OnLibraryAddFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            var chosen = new List<string>();
            while (true)
            {
                var picker = Prepared(new FolderPicker { SuggestedStartLocation = PickerLocationId.MusicLibrary });
                picker.FileTypeFilter.Add("*");
                if (await picker.PickSingleFolderAsync() is not { } folder)
                {
                    break;
                }

                if (!chosen.Contains(folder.Path, StringComparer.OrdinalIgnoreCase))
                {
                    chosen.Add(folder.Path);
                }

                var list = new StackPanel { Spacing = 4, MinWidth = 360 };
                list.Children.Add(new TextBlock
                {
                    Text = "Choose another folder, or add everything selected so far.",
                    TextWrapping = TextWrapping.Wrap,
                });
                foreach (var path in chosen)
                {
                    var line = new TextBlock
                    {
                        Text = path,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    };
                    ToolTipService.SetToolTip(line, path);
                    list.Children.Add(line);
                }

                var dialog = new ContentDialog
                {
                    Title = chosen.Count == 1 ? "Add this folder?" : $"Add {chosen.Count} folders?",
                    Content = list,
                    PrimaryButtonText = chosen.Count == 1 ? "Add folder" : $"Add {chosen.Count} folders",
                    SecondaryButtonText = "Choose another",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = Content.XamlRoot,
                };
                var answer = await ShowDialogAsync(dialog);
                if (answer == ContentDialogResult.Primary)
                {
                    AddLibraryFolders(chosen);
                    return;
                }

                if (answer != ContentDialogResult.Secondary)
                {
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            App.Log.Error(LogSource, "A folder could not be added: " + ex);
            Say("The folder could not be added: " + Rex.Media.AppCore.Player.DamageNotices.Short(ex.Message));
        }
    }

    private void AddLibraryFolder(string path)
    {
        AddLibraryFolders([path]);
    }

    private void AddLibraryFolders(IEnumerable<string> paths)
    {
        var added = new List<string>();
        var alreadyWatched = new List<string>();
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (_library!.AddFolder(path))
            {
                added.Add(path);
                App.Log.Info(LogSource, "The library now watches " + path);
            }
            else
            {
                alreadyWatched.Add(path);
            }
        }

        if (added.Count == 0)
        {
            Say(alreadyWatched.Count == 1
                ? $"The library already watches {alreadyWatched[0]}."
                : "The library already watches all of those folders.");
            return;
        }

        Say(added.Count == 1
            ? "The library now watches " + added[0]
            : $"The library now watches {added.Count} new folders" + (alreadyWatched.Count > 0 ? $" ({alreadyWatched.Count} already watched)." : "."));
        WatchLibraryFolders();
        _scanner!.Request();
    }

}
