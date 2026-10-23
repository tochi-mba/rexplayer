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
    /// <summary>Reads only explicit nearby local text, always off the UI thread.</summary>
    private static string? ReadLocalNotes(IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            // Bounded even for a growing sidecar and independent of any parser or browser.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite, 4096, FileOptions.SequentialScan);
            using var reader = new StreamReader(stream);
            var text = new char[PodcastNotes.MaxVisibleCharacters + 1];
            var count = reader.ReadBlock(text, 0, text.Length);
            return PodcastNotes.Display(new string(text, 0, count));
        }

        return null;
    }

    private async Task ShowPodcastNotesAsync(LibraryEntry episode)
    {
        string? notes;
        try
        {
            notes = await Task.Run(() => ReadLocalNotes(PodcastNotes.Sidecars(episode.Path)));
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

        await ShowPlainNotesDialogAsync("Episode notes · " + episode.Title, notes, "PodcastNotesText");
    }

    private async Task ShowPlainNotesDialogAsync(string title, string notes, string automationId)
    {
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
            Height = 420,
        };
        AutomationProperties.SetAutomationId(text, automationId);
        AutomationProperties.SetName(text, title + ", read-only and selectable");
        panel.Children.Add(notice);
        panel.Children.Add(text);
        var dialog = new ContentDialog
        {
            Title = title,
            Content = panel,
            CloseButtonText = "Done",
            XamlRoot = Content.XamlRoot,
        };
        await ShowDialogAsync(dialog);
    }
}
