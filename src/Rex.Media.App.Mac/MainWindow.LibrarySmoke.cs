using Rex.Media.AppCore.Library;
using Rex.Media.Library;

namespace Rex.Media.App.Mac;

/// <summary>
/// The library window's scripted checks: the "mac-library" actions native fixture tests drive, the
/// line each one answers with, and the list page's screenshot. None of it runs in a release session.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Capture the native library list page for scripted macOS UI checks. The page, not the whole
    /// window, is captured so the reviewed baselines taken before the sidebar existed still match.
    /// </summary>
    private void CaptureLibraryScreenshot(string path)
    {
        if (_libraryWindow?.IsVisible != true || _libraryListPage is not { } content)
        {
            throw new InvalidOperationException("Open the library before capturing its screenshot.");
        }

        content.LayoutSubtreeIfNeeded();
        using var bitmap = content.BitmapImageRepForCachingDisplayInRect(content.Bounds)
            ?? throw new InvalidOperationException("AppKit could not capture the library.");
        content.CacheDisplay(content.Bounds, bitmap);
        using var png = bitmap.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png)
            ?? throw new InvalidOperationException("AppKit could not encode the library screenshot.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (!png.Save(NSUrl.FromFilename(Path.GetFullPath(path)), true))
        {
            throw new IOException("Could not save the library screenshot.");
        }
    }

    /// <summary>Compatibility actions for native AppKit fixture tests, never enabled in release sessions.</summary>
    private bool LibrarySmokeAction(string? action, IReadOnlyList<string> paths)
    {
        if (Environment.GetEnvironmentVariable("REXPLAYER_MAC_LIBRARY_TESTS") != "1")
        {
            return false;
        }

        if (action == "show")
        {
            ShowLibrary();
            return true;
        }

        if (action == "status" && _libraryWindow is not null)
        {
            RefreshLibrary(force: true);
            return true;
        }

        if (action == "add-folder")
        {
            return AddLibraryFolders(paths.ToList()).Ok;
        }

        if (_libraryWindow?.IsVisible != true || _libraryCategory is null || _librarySearch is null)
        {
            return false;
        }

        if (action == "sidebar")
        {
            _librarySmokeAnswer = LibrarySidebarSummary();
            return true;
        }

        if (action is not null && action.StartsWith("sidebar-select:", StringComparison.Ordinal))
        {
            // Mirror a click: only a section the sidebar lists can be chosen.
            return FindLibrarySidebarSection(action["sidebar-select:".Length..]) is { } section &&
                _librarySections.IsShown(section) && SelectLibrarySection(section);
        }

        if (action == "back")
        {
            return NavigateLibraryBack();
        }

        if (action is not null && action.StartsWith("open-album:", StringComparison.Ordinal))
        {
            // Mirror opening an album from its song: only an album the catalogue holds can open.
            var title = action["open-album:".Length..];
            return _mediaLibrary is not null && LibraryMemo.Albums(_mediaLibrary.Entries).FirstOrDefault(candidate =>
                string.Equals(candidate.Name, title, StringComparison.OrdinalIgnoreCase)) is { } album && OpenLibraryAlbum(album);
        }

        if (action is not null && action.StartsWith("open-artist:", StringComparison.Ordinal))
        {
            return OpenLibraryArtistNamed(action["open-artist:".Length..]);
        }

        if (action is "songs" or "videos" or "folders")
        {
            _librarySearch.StringValue = "";
            _libraryCategory.SelectItem(action switch { "songs" => 1, "videos" => 2, _ => 7 });
            ChangeLibraryView();
            return true;
        }

        if (action == "continue" && _mediaLibrary?.Entries.FirstOrDefault(entry =>
                entry.Kind == LibraryKind.Video) is { } unfinished)
        {
            _player.Memory.Left(unfinished.Path, TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(2));
            _librarySearch.StringValue = "";
            _libraryCategory.SelectItem(6);
            ChangeLibraryView();
            return _libraryMatchingCount == 1 &&
                LibraryEntryDescription(unfinished).Contains("Continue at 0:15", StringComparison.Ordinal);
        }

        if (action is "search-song" or "search-missing")
        {
            _librarySearch.StringValue = action == "search-song" ? "Sample Song" : "No fixture with this name";
            ChangeLibraryView();
            return true;
        }

        if (action == "clear-search")
        {
            _librarySearch.StringValue = "";
            ChangeLibraryView();
            return true;
        }

        if (action == "verify-accessibility")
        {
            return _libraryFirstPlay?.AccessibilityLabel?.StartsWith("Play: ", StringComparison.Ordinal) == true
                && _libraryFirstQueue?.AccessibilityLabel?.EndsWith(" to Up Next", StringComparison.Ordinal) == true
                && _librarySearch.AccessibilityLabel == "Search library"
                && _libraryCategory.AccessibilityLabel == "Media category";
        }

        if (action == "remove-folder" && _mediaLibrary?.Folders.FirstOrDefault() is { } folder)
        {
            if (!_mediaLibrary.RemoveFolder(folder))
            {
                return false;
            }

            _libraryScanner?.Request();
            RefreshLibrary(force: true);
            return true;
        }

        if (action == "play-first" && _libraryFirstPlay?.Enabled == true)
        {
            _libraryFirstPlay.PerformClick(_libraryFirstPlay);
            return true;
        }

        if (action == "minimum")
        {
            return ResizeLibraryWindow(MinimumLibraryPageWidth).Ok;
        }

        if (action is "capture-empty" or "capture-populated" or "capture-videos" or "capture-minimum"
            or "capture-folders" or "capture-cleared" or "capture-search-empty")
        {
            var output = Environment.GetEnvironmentVariable("REXPLAYER_MAC_LIBRARY_SCREENSHOTS");
            if (string.IsNullOrWhiteSpace(output))
            {
                return false;
            }

            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "provenance.txt"),
                "Capture origin: native AppKit library page view" + Environment.NewLine
                + "Platform: native macOS AppKit" + Environment.NewLine
                + "Application version: " + typeof(MainWindow).Assembly.GetName().Version + Environment.NewLine
                + "Source: automated smoke test, synthetic local media fixtures" + Environment.NewLine
                + "Status: not a finalized-release screenshot; do not publish as release media" + Environment.NewLine);
            CaptureLibraryScreenshot(Path.Combine(output, action["capture-".Length..] + ".png"));
            return true;
        }

        return false;
    }

    private string LibrarySmokeTitle()
    {
        // A smoke action with its own answer (the sidebar summary) rides the next title once.
        if (_librarySmokeAnswer is { } answer)
        {
            _librarySmokeAnswer = null;
            return answer;
        }

        // An album's or artist's page answers with what it shows, for as long as it shows.
        if (_libraryCurrentPage is LibraryDetailPage { Summary: var summary })
        {
            return summary;
        }

        var items = _libraryMatchingCount;
        var folders = _libraryFolderCount;
        var query = _librarySearch?.StringValue.Trim();
        return $"{items} {(items == 1 ? "item" : "items")} · {folders} {(folders == 1 ? "folder" : "folders")}"
            + (string.IsNullOrEmpty(query) ? "" : " · Search: " + query);
    }
}
