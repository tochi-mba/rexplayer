using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Rex.Media.AppCore.Library;

namespace Rex.Media.App;

/// <summary>
/// The library's sidebar lists Home always and every other view only while it has something in it
/// (<see cref="LibrarySections"/>): an empty library shows Home's welcome alone, a library of songs
/// Home and the music views. A view that empties while open goes Home with a short notice; a saved
/// place in a view that is not listed opens on Home. Worked out when the library is shown, never at
/// launch.
/// </summary>
public sealed partial class MainWindow
{
    private readonly LibrarySections _sections = new();
    private DispatcherQueueTimer? _sectionsSettle;
    private (int Playlists, int Folders) _sectionsCounts = (-1, -1);

    private void WireSections()
    {
        _sectionsSettle = CreateTimer();
        _sectionsSettle.IsRepeating = false;
        OnTick(_sectionsSettle, ShowLibrary);
        LibrarySources.ContainerContentChanging += (_, e) =>
        {
            if (e.ItemContainer is { } container && e.ItemIndex >= 0 && e.ItemIndex < LibrarySourceItems.Length)
            {
                container.Visibility = IsSourceShown(LibrarySourceItems[e.ItemIndex].Source) ? Visibility.Visible : Visibility.Collapsed;
            }
        };

        // Keeping the first playlist lists Playlists at once.
        _player.Changed += (_, _) =>
        {
            if (LibraryOpen && _sections.Looked && _player.NamedPlaylists.Count > 0 != _sectionsCounts.Playlists > 0)
            {
                OnWindowThread(() => UpdateSections(immediate: true));
            }
        };
    }

    private static SidebarView? SectionOf(LibrarySource source) => Enum.TryParse<SidebarView>(source.ToString(), out var section) ? section : null;

    private bool IsSourceShown(LibrarySource source) => SectionOf(source) is not { } section || _sections.IsShown(section);

    /// <summary>
    /// Brings the sidebar in line with what the library holds; true when the open view is not listed
    /// and the library went Home instead (that view change builds Home itself).
    /// </summary>
    private bool UpdateSections(bool immediate)
    {
        var library = _library!;
        var entries = library.Entries;
        var playlists = _player.NamedPlaylists.Count;
        var folders = library.Folders.Count;

        // A playlist kept or a folder added or given up is the person's own doing: shown at once.
        immediate |= _sections.Looked && (playlists > 0 != _sectionsCounts.Playlists > 0 || folders != _sectionsCounts.Folders);
        _sectionsCounts = (playlists, folders);
        // Derive the sidebar from the same queries the pages render. A non-episode is not
        // necessarily a movie: short clips belong in Other videos, and a tentative special
        // may join a show only when the rest of that show is present.
        var movies = LibraryViews.Movies(entries, MovieLookupOf).ToHashSet();
        var episodes = LibraryViews.TvShows(entries).SelectMany(show => show.Entries).ToHashSet();
        var otherVideos = LibraryViews.OtherVideos(entries, MovieLookupOf).ToHashSet();
        var facts = Rex.Media.AppCore.Library.LibraryFacts.Of(entries, WithoutListening(entries), Listening(),
            movies.Contains, episodes.Contains, otherVideos.Contains, _player.LeftAt,
            LibraryViews.RecentlyPlayed(entries, _player.Memory.Recent.Where(File.Exists)).Count > 0, playlists > 0, folders > 0);
        var wasShown = _sections.Looked && IsSourceShown(_librarySource);
        var now = DateTimeOffset.UtcNow;
        var present = LibrarySections.Present(facts);
        var changed = _sections.Observe(present, _scanner?.IsBusy == true, now, immediate);
        if (_sections.NextLook is { } next)
        {
            _sectionsSettle!.Interval = TimeSpan.FromMilliseconds(Math.Max(50, (next - now).TotalMilliseconds));
            Restart(_sectionsSettle);
        }

        if (changed)
        {
            for (var index = 0; index < LibrarySourceItems.Length; index++)
            {
                if (LibrarySources.ContainerFromIndex(index) is UIElement container)
                {
                    container.Visibility = IsSourceShown(LibrarySourceItems[index].Source) ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        // A direct navigation (including Home's See all) may reach its view while
        // the sidebar is still settling the matching scan results. Keep that view
        // open when it already has content; only return Home after it truly empties.
        if (IsSourceShown(_librarySource) || (SectionOf(_librarySource) is { } current && present.Contains(current)))
        {
            return false;
        }

        // A view that emptied while open says why; a saved place in a view not listed just opens Home.
        if (wasShown)
        {
            Say($"Nothing is left in {LibrarySourceItems.First(pair => pair.Source == _librarySource).Item.Name}, so it has left the library");
        }

        _libraryGroup = null;
        _librarySeason = null;
        LibrarySources.SelectedIndex = Array.FindIndex(LibrarySourceItems, pair => pair.Source == LibrarySource.Home);
        return true;
    }
}
