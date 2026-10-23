using Rex.Media.AppCore.Library;

namespace Rex.Media.App;

/// <summary>
/// Optional local podcast-show descriptions. A single-show folder can supply show.notes.md
/// or show.notes.txt; a mixed folder cannot quietly attribute one show's note to another.
/// Reads run on a worker and refresh only the still-open matching show page.
/// </summary>
public sealed partial class MainWindow
{
    private readonly Dictionary<string, (string? Notes, DateTime CheckedUtc)> _podcastShowNotes =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingPodcastShowNotes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Only a folder belonging to one show can supply its show-wide description.</summary>
    private string? LocalShowNotesFolder(LibraryGroup show)
    {
        var folders = show.Entries.Select(entry => Path.GetDirectoryName(entry.Path) ?? "")
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (folders.Length != 1 || folders[0].Length == 0)
        {
            return null;
        }

        var folder = folders[0];
        return Listening().Podcasts.Any(other =>
            !string.Equals(other.Name, show.Name, StringComparison.OrdinalIgnoreCase)
            && other.Entries.Any(entry => string.Equals(Path.GetDirectoryName(entry.Path), folder,
                StringComparison.OrdinalIgnoreCase))) ? null : folder;
    }

    private static IReadOnlyList<string> ShowNotesPaths(string folder) =>
        [Path.Combine(folder, "show.notes.md"), Path.Combine(folder, "show.notes.txt")];

    private async Task ShowPodcastShowNotesAsync(LibraryGroup show)
    {
        var source = _librarySource;
        var opened = _libraryGroup?.Name;
        if (LocalShowNotesFolder(show) is not { } folder)
        {
            Say("This show's episodes do not have an unambiguous local notes folder.");
            return;
        }

        string? notes;
        try
        {
            notes = await Task.Run(() => ReadLocalNotes(ShowNotesPaths(folder)));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException)
        {
            Say("The local show notes could not be read: " + error.Message);
            return;
        }

        if (_closing || _librarySource != source || _libraryGroup?.Name != opened)
        {
            return;
        }

        if (notes is null)
        {
            Say("No show.notes.md or show.notes.txt is saved with these local episodes.");
            return;
        }

        await ShowPlainNotesDialogAsync("Show notes · " + show.Name, notes, "PodcastShowNotesText");
    }

    private string PodcastDescription(LibraryGroup show)
    {
        const string fallback = "Local episodes only · No show description is saved with these files.";
        var folder = LocalShowNotesFolder(show);
        if (folder is null)
        {
            return "Local episodes only · A show-wide description cannot safely be attributed across these folders.";
        }

        if (_podcastShowNotes.TryGetValue(folder, out var known)
            && DateTime.UtcNow - known.CheckedUtc < TimeSpan.FromMinutes(1))
        {
            return known.Notes is null ? fallback : known.Notes.Length > 750
                ? known.Notes[..750] + "… (Open show notes to read the rest.)" : known.Notes;
        }

        if (_pendingPodcastShowNotes.Add(folder))
        {
            Forget(Task.Run(() =>
            {
                try
                {
                    return ReadLocalNotes(ShowNotesPaths(folder));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException
                    or NotSupportedException or ArgumentException)
                {
                    App.Log.Info(LogSource, "Local podcast show notes could not be read: " + error.Message);
                    return null;
                }
            }).ContinueWith(task => OnWindowThread(() =>
            {
                _pendingPodcastShowNotes.Remove(folder);
                if (_closing)
                {
                    return;
                }

                if (_podcastShowNotes.Count >= 64)
                {
                    _podcastShowNotes.Clear();
                }

                _podcastShowNotes[folder] =
                    (task.IsCompletedSuccessfully ? task.Result : null, DateTime.UtcNow);
                if (_librarySource == LibrarySource.Podcasts && _libraryGroup is { } opened
                    && string.Equals(opened.Name, show.Name, StringComparison.OrdinalIgnoreCase))
                {
                    ShowLibrary();
                }
            }), TaskScheduler.Default));
        }

        return "Loading a local show description if one exists...";
    }
}
