using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Library;
using Rex.Media.Library;
using Rex.Media.Settings;

namespace Rex.Media.App;

/// <summary>
/// A show's own page: the show's artwork behind its name, a season picker, Play that resumes or
/// starts the episode to go on with, and the season's episodes as numbered lines with their
/// pictures and how much of each was watched. <see cref="ShowPage"/> makes every decision.
/// Opening a show lands on the season holding its next episode; Back leaves the show.
/// </summary>
public sealed partial class MainWindow
{
    private LibraryEpisodeTemplates? _episodeTemplates;
    private IReadOnlyList<LibraryGroup> _showPageSeasons = [];
    private string? _showPageOf;
    private string _shownSeasonPicker = "";
    private LibraryEntry? _showPageNextUp;
    private bool _pickingSeason;

    /// <summary>
    /// Before a view is drawn: on a show, picks the season to show (the one holding the next
    /// episode) when none is open, fills the season picker and sets what Play says. A show page
    /// whose season was closed by Back is left for the list of shows.
    /// </summary>
    private void ResolveShowPage(string search)
    {
        var onShow = search.Length == 0 && _librarySource == LibrarySource.TvShows && _libraryGroup is not null && _library is not null;
        if (!onShow)
        {
            LeaveShowPage();
            return;
        }

        var show = LibraryViews.RefreshGroup(LibraryViews.TvShows(_library!.Entries), _libraryGroup!);
        if (_librarySeason is null && _showPageOf is not null && string.Equals(_showPageOf, show.Name, StringComparison.OrdinalIgnoreCase))
        {
            // Back from the page's season: the page itself is what Back closes.
            _libraryGroup = null;
            LeaveShowPage();
            return;
        }

        _showPageSeasons = LibraryViews.TvSeasons(show);
        _showPageNextUp = ShowPage.NextUp(show, _player.LeftAt);
        _librarySeason ??= ShowPage.DefaultSeason(_showPageSeasons, _showPageNextUp);
        _showPageOf = _librarySeason is null ? null : show.Name;
        LibraryPlayText.Text = ShowPage.PlayLabel(_showPageNextUp, _showPageNextUp is not null && _player.LeftAt(_showPageNextUp.Path) is not null);
        ShowShowDetails(show, _librarySeason);

        var picker = string.Join("\n", _showPageSeasons.Select(season => season.Name)) + "\n" + _librarySeason?.Name;
        LibrarySeasonPicker.Visibility = _showPageSeasons.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (picker == _shownSeasonPicker)
        {
            return;
        }

        _shownSeasonPicker = picker;
        _pickingSeason = true;
        LibrarySeasonPicker.ItemsSource = _showPageSeasons.Select(season => season.Name).ToList();
        LibrarySeasonPicker.SelectedIndex = _librarySeason is null ? -1
            : Math.Max(0, _showPageSeasons.ToList().FindIndex(season => season.Name == _librarySeason.Name));
        _pickingSeason = false;
    }

    private void LeaveShowPage()
    {
        _showPageOf = null;
        _showPageNextUp = null;
        _showPageSeasons = [];
        _shownSeasonPicker = "";
        LibrarySeasonPicker.Visibility = Visibility.Collapsed;
        LibraryPlayText.Text = "Play";
        ClearShowDetails();
    }

    /// <summary>Another season chosen in the picker: the page shows its episodes.</summary>
    private void OnLibrarySeasonPicked(object sender, SelectionChangedEventArgs e)
    {
        var index = LibrarySeasonPicker.SelectedIndex;
        if (_pickingSeason || index < 0 || index >= _showPageSeasons.Count)
        {
            return;
        }

        _librarySeason = _showPageSeasons[index];
        ShowLibrary();
    }

    /// <summary>Play on a show page with nothing chosen: the episode to go on with, then the rest of its season.</summary>
    private bool PlayShowPageNextUp()
    {
        if (_showPageNextUp is not { } next || _library is null)
        {
            return false;
        }

        var queue = LibraryViews.AutoplayVideos(_library.Entries, next).ToList();
        Play(queue, queue.FindIndex(entry => string.Equals(entry.Path, next.Path, StringComparison.OrdinalIgnoreCase)), resume: true);
        return true;
    }

    /// <summary>A season's episodes, arranged as the view's choices say, as numbered episode lines.</summary>
    private List<LibraryRowGroup> EpisodeSections(IReadOnlyList<LibraryEntry> entries, LibraryViewChoice choice) =>
        [.. LibraryArrangement.Group(LibraryArrangement.Sort(entries, choice.Sort, choice.Descending), choice.Grouping, DateTime.UtcNow)
            .Select(section => new LibraryRowGroup(section.Header, section.Entries.Select(EpisodeRow)))];

    private LibraryRow EpisodeRow(LibraryEntry entry)
    {
        var episode = LibraryViews.EpisodeOf(entry);
        var left = _player.LeftAt(entry.Path);
        var row = new LibraryRow(
            episode is null ? entry.Title : ShowPage.EpisodeTitle(entry, episode),
            ShowPage.EpisodeDetail(entry, episode, left),
            entry.Duration is { } duration ? Rex.Media.AppCore.Player.TimeText.Format(duration, duration) : "",
            entry,
            hasPicture: true,
            picturePlaceholder: "▶",
            badge: episode is null ? "" : ShowPage.Number(episode),
            progress: ShowPage.Progress(entry, left),
            isPlaying: LibraryViews.SameLocation(entry.Path, _player.Playlist.Current?.Location));
        var (glyph, text) = TranscriptBadgeOf(entry);
        row.SetTranscript(glyph, text);
        row.SetSummary(TitleSections.EpisodeSynopsis(episode is null ? null : ShowFacts(episode.Series), episode));
        return row;
    }

}
