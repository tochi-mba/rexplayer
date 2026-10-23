using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Rex.Media.AppCore.Library;
using Rex.Media.Containers.Tags;
using Rex.Media.Library;
using Windows.Storage.Pickers;

namespace Rex.Media.App;

/// <summary>
/// Edit details: the title, artist, album, the rest and the cover of one song or several, from the
/// library's menu. The library and Now Playing change at once; the files are written in the
/// background, and a file that is playing waits until it is not.
/// </summary>
public sealed partial class MainWindow
{
    private readonly PendingDetailWrites _detailWrites = new();
    private string? _detailWritesHeldFor;

    /// <summary>Adds "Edit details..." when the chosen lines hold songs.</summary>
    private void AddEditDetailsMenuItem(MenuFlyout menu, IReadOnlyList<LibraryEntry> entries)
    {
        var songs = entries.Where(entry => entry.Kind == LibraryKind.Music).ToList();
        if (songs.Count > 0)
        {
            menu.Items.Add(MenuItem("Edit details...", "LibraryMenuEditDetails", () => Forget(EditDetailsAsync(songs))));
        }
    }

    private async Task EditDetailsAsync(IReadOnlyList<LibraryEntry> songs)
    {
        var writing = _settings.WriteDetailsIntoFiles;
        var downloading = songs.Count(song => song.Downloading);
        // A file still arriving is never written into: its edit stays in rexplayer.
        var writable = writing ? songs.Where(song => !song.Downloading && TagFileWriter.CanWrite(song.Path)).ToList() : [];
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
            AutomationProperties.SetName(box, field.Label);
            AutomationProperties.SetHelpText(box, "Current local media details; unchanged mixed-value fields are preserved. Save keeps a local override and writes supported tags only when Preferences allows it.");
            boxes[field.Key] = box;
            panel.Children.Add(box);
        }

        PictureBlock? cover = null;
        var removeCover = new CheckBox { Content = "Remove the cover" };
        AutomationProperties.SetAutomationId(removeCover, "EditDetailsRemoveCover");
        AutomationProperties.SetHelpText(removeCover, "Explicitly remove the embedded cover when writing supported files. It does not remove the original unless you save.");
        if (writable.Count > 0)
        {
            var coverLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            var chosen = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
            AutomationProperties.SetAutomationId(chosen, "EditDetailsCoverStatus");
            var preview = new Image { Width = 112, Height = 112, Stretch = Stretch.UniformToFill, Visibility = Visibility.Collapsed };
            AutomationProperties.SetAutomationId(preview, "EditDetailsCoverPreview");
            AutomationProperties.SetName(preview, "New album cover preview");
            var choose = new Button { Content = "Change cover..." };
            AutomationProperties.SetAutomationId(choose, "EditDetailsCover");
            choose.Click += async (_, _) =>
            {
                var picker = Prepared(new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary, ViewMode = PickerViewMode.Thumbnail });
                foreach (var type in new[] { ".jpg", ".jpeg", ".png" })
                {
                    picker.FileTypeFilter.Add(type);
                }

                if (await picker.PickSingleFileAsync() is not { } file)
                {
                    return;
                }

                try
                {
                    if (new FileInfo(file.Path).Length > DetailWrite.MaxCoverBytes)
                    {
                        chosen.Text = "The chosen cover is too large (maximum 10 MB).";
                        return;
                    }

                    var bytes = await File.ReadAllBytesAsync(file.Path);
                    if (DetailWrite.CoverFrom(file.Path, bytes) is not { } picked)
                    {
                        chosen.Text = "Choose a valid JPEG or PNG cover.";
                        return;
                    }

                    // A tag is changed only after the native decoder verifies
                    // and previews the exact bytes that would be embedded.
                    var decoded = await DecodePictureAsync(bytes, 112, CancellationToken.None);
                    if (decoded is null)
                    {
                        chosen.Text = "The cover image could not be decoded.";
                        return;
                    }

                    cover = picked;
                    preview.Source = decoded;
                    preview.Visibility = Visibility.Visible;
                    chosen.Text = file.Name + " (preview; save to write)";
                    removeCover.IsChecked = false;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                    or System.Runtime.InteropServices.COMException or InvalidOperationException or NotSupportedException)
                {
                    chosen.Text = "That cover could not be opened: " + ex.Message;
                }
            };
            coverLine.Children.Add(choose);
            coverLine.Children.Add(chosen);
            panel.Children.Add(coverLine);
            panel.Children.Add(preview);
            panel.Children.Add(removeCover);
        }

        panel.Children.Add(new TextBlock
        {
            Text = DetailsForm.Note(songs.Count, writable.Count, downloading, writing),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
        });
        var title = songs.Count == 1 ? "Edit details" : $"Edit details of {songs.Count} songs";
        if (!await Ask(title, new ScrollViewer { Content = panel, MaxHeight = 560 }, "Save"))
        {
            return;
        }

        var change = DetailsForm.Change(fields, boxes.ToDictionary(pair => pair.Key, pair => pair.Value.Text, StringComparer.Ordinal));
        var changesCover = cover is not null || removeCover.IsChecked == true;
        if (change.IsEmpty && !changesCover)
        {
            return;
        }

        var paths = songs.Select(song => song.Path).ToList();
        if (!change.IsEmpty)
        {
            _library!.SetOverride(paths, change);
            _player.ShowEditedDetails(paths, change);
        }

        foreach (var song in writable)
        {
            _detailWrites.Add(new DetailWrite(song.Path, (song.Override ?? new DetailOverride()).Then(change), cover, removeCover.IsChecked == true));
        }

        Say(writable.Count == 0 ? "Saved the details in rexplayer" : "Saving the details into the files");
        FlushDetailWrites();
    }

    /// <summary>Writes the waiting edits on a worker thread, holding back the file that plays.</summary>
    private void FlushDetailWrites()
    {
        _detailWritesHeldFor = _player.Item?.Location;
        if (_detailWrites.Count == 0)
        {
            return;
        }

        var playing = _detailWritesHeldFor;
        Forget(Task.Run(() =>
        {
            var results = _detailWrites.Flush(path => string.Equals(path, playing, StringComparison.OrdinalIgnoreCase), (path, edit) => TagFileWriter.Write(path, edit));
            OnWindowThread(() => ShowDetailWrites(results));
        }));
    }

    /// <summary>Once a file holds its details rexplayer's own copy is dropped; a refusal keeps it and says why.</summary>
    private void ShowDetailWrites(IReadOnlyList<(DetailWrite Write, TagWriteResult Result)> results)
    {
        var written = results.Where(item => item.Result.Status == TagWriteStatus.Written).Select(item => item.Write.Path).ToList();
        if (written.Count > 0)
        {
            _library!.ClearOverride(written);
            _scanner?.Request();
        }

        if (results.FirstOrDefault(item => item.Result.Status == TagWriteStatus.Refused) is { Write: { } refused, Result: { } why })
        {
            Say($"Kept the details of {Path.GetFileName(refused.Path)} in rexplayer: {why.Message}");
        }
    }

    /// <summary>Called when what plays changes: an edit held back for the file that played can go in now.</summary>
    private void OnPlayingChangedForDetailWrites()
    {
        if (_detailWrites.Count > 0 && !string.Equals(_player.Item?.Location, _detailWritesHeldFor, StringComparison.OrdinalIgnoreCase))
        {
            FlushDetailWrites();
        }
    }
}
