using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Library;
using Rex.Media.Library;

namespace Rex.Media.App;

/// <summary>
/// Shows explicit local .notes.md/.notes.txt companions without executing HTML, resolving
/// image URLs or opening links. Disk reads and missing-drive errors stay off the UI thread.
/// </summary>
public sealed partial class MainWindow
{
    private async Task ShowPodcastNotesAsync(LibraryEntry episode)
    {
        string? notes;
        try
        {
            notes = await Task.Run(() =>
            {
                foreach (var path in PodcastNotes.Sidecars(episode.Path))
                {
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    // Bounded even if a file is replaced or grows between the length check and
                    // the read. ReadBlock never materializes the rest of a huge text file.
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite, 4096, FileOptions.SequentialScan);
                    using var reader = new StreamReader(stream);
                    var text = new char[PodcastNotes.MaxVisibleCharacters + 1];
                    var count = reader.ReadBlock(text, 0, text.Length);
                    return PodcastNotes.Display(new string(text, 0, count));
                }

                return null;
            });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException)
        {
            Say("The local episode notes could not be read: " + error.Message);
            return;
        }

        if (_closing)
        {
            return;
        }

        if (notes is null)
        {
            Say("No local notes are saved beside this episode (.notes.md or .notes.txt).");
            return;
        }

        var panel = new StackPanel { Spacing = 10, MaxWidth = 680 };
        var notice = new TextBlock
        {
            Text = "Local text only · URLs and markup are not opened or executed. Select text to copy.",
            TextWrapping = TextWrapping.Wrap,
        };
        var text = new TextBox
        {
            Text = notes,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = 420,
        };
        AutomationProperties.SetAutomationId(text, "PodcastNotesText");
        AutomationProperties.SetName(text, "Local episode notes, read-only and selectable");
        panel.Children.Add(notice);
        panel.Children.Add(text);
        var dialog = new ContentDialog
        {
            Title = "Episode notes · " + episode.Title,
            Content = panel,
            CloseButtonText = "Done",
            XamlRoot = Content.XamlRoot,
        };
        await ShowDialogAsync(dialog);
    }
}
