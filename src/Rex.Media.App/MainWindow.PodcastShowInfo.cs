using Rex.Media.AppCore.Library;

namespace Rex.Media.App;

/// <summary>
/// Optional local podcast-show descriptions. A single-show folder can supply show.notes.md
/// or show.notes.txt; a mixed folder cannot quietly attribute one show's note to another.
/// Reads run on a worker and refresh only the still-open matching show page.
/// </summary>
public sealed partial class MainWindow
{
    private readonly Dictionary<string, string?> _podcastShowNotes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingPodcastShowNotes = new(StringComparer.OrdinalIgnoreCase);

    private string PodcastDescription(LibraryGroup show)
    {
        const string fallback = "Local episodes only · No show description is saved with these files.";
        var folders = show.Entries.Select(entry => Path.GetDirectoryName(entry.Path) ?? "")
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (folders.Length != 1 || folders[0].Length == 0)
        {
            return "Local episodes span multiple folders · No shared description is assumed.";
        }

        var folder = folders[0];
        if (Listening().Podcasts.Any(other => !string.Equals(other.Name, show.Name, StringComparison.OrdinalIgnoreCase)
            && other.Entries.Any(entry => string.Equals(Path.GetDirectoryName(entry.Path), folder, StringComparison.OrdinalIgnoreCase))))
        {
            return "Several shows share this folder · No common description is assumed.";
        }

        if (_podcastShowNotes.TryGetValue(folder, out var known))
        {
            return known ?? fallback;
        }

        if (_pendingPodcastShowNotes.Add(folder))
        {
            Forget(Task.Run(() =>
            {
                try
                {
                    return ReadLocalNotes([
                        Path.Combine(folder, "show.notes.md"),
                        Path.Combine(folder, "show.notes.txt")]);
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

                _podcastShowNotes[folder] = task.IsCompletedSuccessfully ? task.Result : null;
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
