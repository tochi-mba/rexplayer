using Rex.Media.AppCore.Library;
using Rex.Media.Library;

namespace Rex.Media.App;

/// <summary>
/// Lazily checks explicit episode-notes sidecars only for visible local podcast rows. No disk
/// checks occur while building a 1,000-episode show, and only eight reads can be pending.
/// </summary>
public sealed partial class MainWindow
{
    private readonly Dictionary<string, string?> _episodeNotesPreviews =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingEpisodeNotes =
        new(StringComparer.OrdinalIgnoreCase);

    private LibraryRow AttachEpisodeNotes(LibraryRow row, LibraryEntry episode)
    {
        if (_episodeNotesPreviews.TryGetValue(episode.Path, out var preview))
        {
            row.SetNotesPreview(preview);
        }

        return row;
    }

    private void RequestEpisodeNotes(LibraryRow? row)
    {
        if (_librarySource != LibrarySource.Podcasts || _libraryGroup is null
            || row?.Item is not LibraryEntry entry || ShelfOf(entry) != AudioShelf.Podcasts)
        {
            return;
        }

        if (_episodeNotesPreviews.TryGetValue(entry.Path, out var cached))
        {
            row.SetNotesPreview(cached);
            return;
        }

        if (_pendingEpisodeNotes.Count >= 8 || !_pendingEpisodeNotes.Add(entry.Path))
        {
            return;
        }

        var token = _libraryPictures.Token;
        Forget(Task.Run(() =>
        {
            try
            {
                return token.IsCancellationRequested ? null
                    : ReadLocalNotes(PodcastNotes.Sidecars(entry.Path));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException
                or NotSupportedException or ArgumentException)
            {
                return null;
            }
        }).ContinueWith(task => OnWindowThread(() =>
        {
            _pendingEpisodeNotes.Remove(entry.Path);
            if (_closing || token.IsCancellationRequested)
            {
                return;
            }

            var notes = task.IsCompletedSuccessfully ? task.Result : null;
            var oneLine = notes?.Replace('\r', ' ').Replace('\n', ' ').Trim();
            var preview = string.IsNullOrWhiteSpace(oneLine) ? null
                : oneLine.Length > 90 ? oneLine[..90] + "…" : oneLine;
            if (_episodeNotesPreviews.Count >= 256)
            {
                _episodeNotesPreviews.Clear();
            }

            _episodeNotesPreviews[entry.Path] = preview;
            row.SetNotesPreview(preview);
        }), TaskScheduler.Default));
    }
}
