using System.Collections.Concurrent;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Rex.Media.AppCore.Library;
using Rex.Media.Library;
using Rex.Media.Settings;

namespace Rex.Media.App;

/// <summary>
/// What keeps the library quick over a large collection. Views are worked out from the library's
/// immutable snapshot and kept beside it (<see cref="LibraryMemo"/>), so a view visited again over
/// an unchanged library is not worked out again, and a change is worked out ahead on a background
/// thread. Home keeps its shelf controls while what they show is the same, so returning to it
/// binds the shelves it already has instead of building and decoding them again. Search waits for
/// a pause in typing, filters a prepared index on a background thread, drops a search a newer one
/// replaced, and shows only the newest; clearing it is immediate.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan SearchPause = TimeSpan.FromMilliseconds(150);

    private DispatcherQueueTimer? _librarySearchTimer;
    private CancellationTokenSource? _librarySearchCancel;
    private FoundSearch? _librarySearchFound;
    // A search typed but not yet shown counts as pending work, so a script waiting for the view to settle waits for it too.
    private PendingPicture? _librarySearchSettling;
    private SearchExtras? _librarySearchExtras;
    private HomeShelfControls? _homeShelfControls;
    private (IReadOnlyList<LibraryEntry> Entries, ListeningShelves? Listening, IReadOnlyList<LibraryEntry> Music)? _withoutListening;
    // Whether a recently played file is still there, as last looked; forgotten when the library changes.
    private readonly ConcurrentDictionary<string, bool> _recentExists = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What a search found, and what it was found over.</summary>
    private sealed record FoundSearch(IReadOnlyList<LibraryEntry> Entries, string Text, SearchExtras Extras, IReadOnlyList<LibraryEntry> Found);

    /// <summary>Home's shelf controls and their rows, kept while the shelves show the same things.</summary>
    private sealed record HomeShelfControls(string Key, IReadOnlyList<LibraryRow> Rows, IReadOnlyList<UIElement> Panels);

    /// <summary>
    /// Each file's downloaded facts as one searchable line (genres, cast, creators, book credits),
    /// worked out once for a library snapshot and the lookups as they stood.
    /// </summary>
    private sealed class SearchExtras(IReadOnlyList<LibraryEntry> entries, ListeningShelves listening, (int, int, int) lookups, MetadataSettings settings, IReadOnlyDictionary<string, string> byPath)
    {
        public bool Describes(IReadOnlyList<LibraryEntry> now, ListeningShelves nowListening, (int, int, int) nowLookups, MetadataSettings nowSettings) =>
            ReferenceEquals(entries, now) && ReferenceEquals(listening, nowListening) && lookups == nowLookups && settings == nowSettings;

        public string? Of(LibraryEntry entry) => byPath.GetValueOrDefault(entry.Path);
    }

    /// <summary>Works out ahead, on a background thread, what the views and search will ask of the library as it now is.</summary>
    private void WarmLibraryViews()
    {
        _recentExists.Clear();
        if (_library is { } library)
        {
            var snapshot = library.Entries;
            Forget(Task.Run(() => LibraryMemo.Warm(snapshot)));
        }
    }

    /// <summary>The recently played files still on the disk, each looked for once per library change rather than on every visit.</summary>
    private IEnumerable<string> ExistingRecent() =>
        _player.Memory.Recent.Where(path => _recentExists.GetOrAdd(path, File.Exists));

    /// <summary>The entries that are not audiobooks, podcasts or recordings: the same list while the library and its classification stay the same.</summary>
    private IReadOnlyList<LibraryEntry> WithoutListening(IReadOnlyList<LibraryEntry> entries)
    {
        Listening();
        if (_audioShelfOf.Count == 0)
        {
            return entries;
        }

        if (_withoutListening is { } kept && ReferenceEquals(kept.Entries, entries) && ReferenceEquals(kept.Listening, _listening))
        {
            return kept.Music;
        }

        IReadOnlyList<LibraryEntry> music = Array.AsReadOnly(entries.Where(entry => !_audioShelfOf.ContainsKey(entry.Path)).ToArray());
        _withoutListening = (entries, _listening, music);
        return music;
    }

    /// <summary>Puts Home's shelves on screen, reusing the controls already made when they show the same rows.</summary>
    private void PlaceHomeShelves(string shown, IReadOnlyList<(string Title, LibrarySource See, IReadOnlyList<LibraryRow> Rows, bool Wide)> shelves)
    {
        var key = shown + "\n" + _settings.MediaOpenClickMode;
        if (_homeShelfControls?.Key != key)
        {
            _homeShelfControls = new HomeShelfControls(key, [.. shelves.SelectMany(shelf => shelf.Rows)],
            [
                .. shelves.Where(shelf => shelf.Rows.Count > 0).Select(shelf => Shelf(shelf.Title, shelf.See, shelf.Rows, shelf.Wide,
                    seeAll: shelf.Title != "Pinned albums",
                    id: shelf.Title == DownloadingShelf ? "Downloading"
                        : shelf.Title == "Podcast shows" ? "PodcastShows"
                        : shelf.Title == "Pinned albums" ? "PinnedAlbums" : null)),
            ]);
        }

        _homeShelfRows = [.. _homeShelfControls.Rows];
        LibraryShelves.Children.Clear();
        foreach (var panel in _homeShelfControls.Panels)
        {
            LibraryShelves.Children.Add(panel);
        }
    }

    /// <summary>The library's downloaded facts for search, worked out again only after the library or the lookups changed.</summary>
    private SearchExtras CurrentSearchExtras()
    {
        var entries = _library!.Entries;
        var listening = Listening();
        (int, int, int) lookups;
        lock (_metadataGate)
        {
            lookups = (_metadataMovies.Count, _metadataShows.Count, _metadataBooks.Count);
        }

        if (_librarySearchExtras is { } kept && kept.Describes(entries, listening, lookups, _settings.Metadata))
        {
            return kept;
        }

        var byPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var line = string.Join('\n', SearchMetadata(entry).OfType<string>());
            if (line.Length > 0)
            {
                byPath[entry.Path] = line;
            }
        }

        return _librarySearchExtras = new SearchExtras(entries, listening, lookups, _settings.Metadata, byPath);
    }

    /// <summary>What <paramref name="search"/> finds in <paramref name="entries"/>: the background search's answer when it was for the same, else found now.</summary>
    private IReadOnlyList<LibraryEntry> FoundFor(IReadOnlyList<LibraryEntry> entries, string search)
    {
        var extras = CurrentSearchExtras();
        return _librarySearchFound is { } found && ReferenceEquals(found.Entries, entries) && found.Text == search && ReferenceEquals(found.Extras, extras)
            ? found.Found
            : LibrarySearchIndex.For(entries).Search(search, extras.Of);
    }

    /// <summary>A change in the search box: clearing it shows the view at once; typing waits for a pause.</summary>
    private void SearchAfterPause()
    {
        if (LibrarySearch.Text.AsSpan().Trim().IsEmpty)
        {
            CancelLibrarySearch();
            ShowLibraryAtPlace();
            return;
        }

        if (_librarySearchTimer is null)
        {
            _librarySearchTimer = CreateTimer();
            _librarySearchTimer.Interval = SearchPause;
            _librarySearchTimer.IsRepeating = false;
            OnTick(_librarySearchTimer, StartLibrarySearch);
        }

        _librarySearchSettling ??= PictureStarted();
        Restart(_librarySearchTimer);
    }

    /// <summary>Filters the library for what the box holds on a background thread, replacing any search still running.</summary>
    private void StartLibrarySearch()
    {
        if (_library is null || !LibraryOpen || !_librarySearchText.Changes(LibrarySearch.Text))
        {
            SettleLibrarySearch();
            return;
        }

        var text = LibrarySearch.Text.Trim();
        var entries = LibraryArrangement.Filter(_library.Entries, ViewChoice("Search"), _player.LeftAt);
        var extras = CurrentSearchExtras();
        _librarySearchCancel?.Cancel();
        var cancel = _librarySearchCancel = new CancellationTokenSource();
        Forget(Task.Run(() => LibrarySearchIndex.For(entries).Search(text, extras.Of, cancel.Token), cancel.Token).ContinueWith(search => OnWindowThread(() =>
        {
            if (_librarySearchCancel != cancel)
            {
                return;
            }

            _librarySearchCancel = null;
            if (!search.IsCompletedSuccessfully)
            {
                SettleLibrarySearch();
                return;
            }

            if (LibrarySearch.Text.Trim() == text && _librarySearchText.Changes(LibrarySearch.Text))
            {
                _librarySearchFound = new FoundSearch(entries, text, extras, search.Result);
                ShowLibraryAtPlace();
            }

            SettleLibrarySearch();
        }), TaskScheduler.Default));
    }

    /// <summary>Shows what the box holds now, without waiting for the pause: the keyboard is moving into the results.</summary>
    private void FinishLibrarySearch()
    {
        var waiting = _librarySearchTimer?.IsRunning == true || _librarySearchCancel is not null;
        CancelLibrarySearch();
        if (waiting && _librarySearchText.Changes(LibrarySearch.Text))
        {
            ShowLibraryAtPlace();
        }
    }

    private void CancelLibrarySearch()
    {
        _librarySearchTimer?.Stop();
        _librarySearchCancel?.Cancel();
        _librarySearchCancel = null;
        SettleLibrarySearch();
    }

    private void SettleLibrarySearch()
    {
        _librarySearchSettling?.Dispose();
        _librarySearchSettling = null;
    }
}
