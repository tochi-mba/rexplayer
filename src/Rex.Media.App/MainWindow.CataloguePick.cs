using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Metadata;
using Rex.Media.Library;

namespace Rex.Media.App;

/// <summary>
/// Picking the right catalogue entry by hand: "Wrong match..." for a show, "Find the film..." for a
/// film the lookups could not match (or matched wrongly). One dialog searches by any name the person
/// types and lets them pick; the pick is kept with their other match choices, so a rescan or a
/// cleared cache never undoes it.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// The search dialog: a name to search for (<paramref name="name"/> to start), the answers to pick
    /// from, and what the search sends where (<paramref name="service"/>). Null when the person
    /// cancels or picks nothing.
    /// </summary>
    private async Task<ShowCandidate?> PickFromCatalogueAsync(string title, string name, string service, string kind,
        Func<MetadataFetcher, string, Task<IReadOnlyList<ShowCandidate>>> search)
    {
        var box = new TextBox { Header = "Search the catalogue for", Text = name, MinWidth = 260 };
        AutomationProperties.SetAutomationId(box, "WrongMatchSearchText");
        var searchButton = new Button { Content = "Search" };
        AutomationProperties.SetAutomationId(searchButton, "WrongMatchSearch");
        var results = new ListView { MaxHeight = 280, SelectionMode = ListViewSelectionMode.Single };
        AutomationProperties.SetName(results, $"The {kind}s the catalogue offers");
        AutomationProperties.SetAutomationId(results, "WrongMatchResults");
        var status = new TextBlock { Text = $"Search sends the name above to {service}.", TextWrapping = TextWrapping.Wrap, Opacity = 0.75 };
        AutomationProperties.SetAutomationId(status, "WrongMatchStatus");
        var found = new List<ShowCandidate>();
        async Task SearchNowAsync()
        {
            searchButton.IsEnabled = false;
            status.Text = "Searching…";
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd($"rexplayer/{Version} (https://github.com/tochi-mba/rexplayer)");
                var fetcher = new MetadataFetcher(MetadataCacheNow, async (uri, inner) =>
                {
                    using var response = await http.GetAsync(uri, inner).ConfigureAwait(false);
                    return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync(inner).ConfigureAwait(false) : null;
                });
                var answer = await search(fetcher, box.Text);
                found.Clear();
                found.AddRange(answer);
                results.ItemsSource = found.Select(MetadataMatch.Describe).ToList();
                status.Text = found.Count == 0 ? "The catalogue offered nothing for that name." : $"Pick the right {kind} ({found.Count} offered).";
            }
            catch (HttpRequestException ex)
            {
                status.Text = "The catalogue could not be reached: " + ex.Message;
            }
            catch (TaskCanceledException)
            {
                status.Text = "The catalogue took too long to answer.";
            }
            finally
            {
                searchButton.IsEnabled = true;
            }
        }

        searchButton.Click += (_, _) => Forget(SearchNowAsync());
        var panel = new StackPanel { Spacing = 8, MinWidth = 300 };
        panel.Children.Add(box);
        panel.Children.Add(searchButton);
        panel.Children.Add(status);
        panel.Children.Add(results);
        return await Ask(title, panel, $"Use the selected {kind}", "Cancel") && results.SelectedIndex >= 0 && results.SelectedIndex < found.Count
            ? found[results.SelectedIndex]
            : null;
    }

    /// <summary>"Find the film..." for one film in Movies, while movie lookups are on.</summary>
    private void AddFindFilmMenuItem(MenuFlyout menu, IReadOnlyList<LibraryEntry> entries)
    {
        if (_settings.Metadata.Movies && entries is [{ } film] && MovieLookupOf(film) is var lookup && MovieRules.IsMovie(film, lookup))
        {
            menu.Items.Add(MenuItem(lookup == MovieLookup.Matched ? "Wrong match..." : "Find the film...", "LibraryMenuFindFilm", () => Forget(FindFilmAsync(film))));
        }
    }

    /// <summary>Searches Wikidata for the film by any name, keeps the pick, and fetches its details in the background.</summary>
    private async Task FindFilmAsync(LibraryEntry film)
    {
        var title = LibraryViews.MovieTitleOf(film);
        var year = LibraryViews.MovieYearOf(film);
        if (await PickFromCatalogueAsync($"Find the film: {title}", title, "Wikidata", "film", (fetcher, name) => fetcher.SearchMoviesAsync(name, CancellationToken.None)) is not { } pick)
        {
            return;
        }

        var key = MetadataMatch.MovieKey(title, year);
        string saved;
        lock (_metadataGate)
        {
            // Before the fetcher has read the choices, they are read here, so this pick never drops the others.
            _metadataChoices ??= MetadataChoices.Load(File.Exists(MetadataCacheNow.ChoicesPath) ? File.ReadAllText(MetadataCacheNow.ChoicesPath) : null);
            _metadataChoices.ChooseMovie(title, year, pick.Id);
            saved = _metadataChoices.Save();
            _metadataMovies.Remove(key);
            _metadataPlan.Add(new MetadataJob(MetadataWork.Movie, MovieJobKey(title, year)));
        }

        await Task.Run(() =>
        {
            try
            {
                var cache = MetadataCacheNow;
                Directory.CreateDirectory(cache.Folder);
                File.WriteAllText(cache.ChoicesPath, saved);
                cache.Remove(MetadataFetcher.MovieKind, key, ".json");
                cache.Remove(MetadataFetcher.MovieArtKind, key, MetadataFetcher.ArtExtension);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                App.Log.Error(MetadataLogSource, "The film choice could not be kept: " + ex.Message);
            }
        });
        StartMetadataWhenAllowed();
        Say($"{title} will be fetched as {pick.Name}");
    }
}
