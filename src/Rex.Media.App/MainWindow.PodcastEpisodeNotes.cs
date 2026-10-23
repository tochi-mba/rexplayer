using Rex.Media.AppCore.Library;
using Rex.Media.Library;

namespace Rex.Media.App;

/// <summary>
/// Only realized local podcast rows can request a notes preview. Eight worker reads run at
/// once; another 128 visible requests can wait. Navigation cancels old work, and later views
/// never inherit stale callbacks or build 1,000 sidecar readers at once.
/// </summary>
public sealed partial class MainWindow
{
    private readonly Dictionary<string, (string? Preview, DateTime CheckedUtc)> _episodeNotesPreviews =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingEpisodeNotes =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<(LibraryRow Row, LibraryEntry Episode, CancellationToken Token)> _notesRequests = new();
    private int _activeNotesReads;

    private LibraryRow AttachEpisodeNotes(LibraryRow row, LibraryEntry episode)
    {
        if (_episodeNotesPreviews.TryGetValue(episode.Path, out var preview))
        {
            row.SetNotesPreview(preview.Preview);
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

        if (_episodeNotesPreviews.TryGetValue(entry.Path, out var cached)
            && DateTime.UtcNow - cached.CheckedUtc < TimeSpan.FromMinutes(1))
        {
            row.SetNotesPreview(cached.Preview);
            return;
        }

        // Ignore repeats from recycled containers. The queue never includes offscreen rows
        // unless they were visible when first requested, and stays bounded during fast scroll.
        if (!_pendingEpisodeNotes.Add(entry.Path))
        {
            return;
        }

        if (_notesRequests.Count >= 128)
        {
            _pendingEpisodeNotes.Remove(entry.Path);
            return;
        }

        _notesRequests.Enqueue((row, entry, _libraryPictures.Token));
        PumpPodcastNotes();
    }

    private void PumpPodcastNotes()
    {
        while (_activeNotesReads < 8 && _notesRequests.TryDequeue(out var request))
        {
            if (request.Token.IsCancellationRequested)
            {
                _pendingEpisodeNotes.Remove(request.Episode.Path);
                continue;
            }

            _activeNotesReads++;
            Forget(Task.Run(() =>
            {
                try
                {
                    return request.Token.IsCancellationRequested ? null
                        : ReadLocalNotes(PodcastNotes.Sidecars(request.Episode.Path));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException
                    or NotSupportedException or ArgumentException)
                {
                    return null;
                }
            }).ContinueWith(task => OnWindowThread(() =>
            {
                _activeNotesReads--;
                _pendingEpisodeNotes.Remove(request.Episode.Path);
                if (!_closing && !request.Token.IsCancellationRequested && task.IsCompletedSuccessfully)
                {
                    var notes = task.Result?.Replace('\r', ' ').Replace('\n', ' ').Trim();
                    var preview = string.IsNullOrWhiteSpace(notes) ? null
                        : notes.Length > 90 ? notes[..90] + "…" : notes;
                    if (_episodeNotesPreviews.Count >= 256)
                    {
                        _episodeNotesPreviews.Clear();
                    }

                    _episodeNotesPreviews[request.Episode.Path] = (preview, DateTime.UtcNow);
                    request.Row.SetNotesPreview(preview);
                }

                if (!_closing)
                {
                    PumpPodcastNotes();
                }
            }), TaskScheduler.Default));
        }
    }
}
