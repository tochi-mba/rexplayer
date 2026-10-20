using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Metadata;
using Rex.Media.Settings;

namespace Rex.Media.App;

/// <summary>
/// The background metadata fetcher: once the library is on screen and a kind is turned on in
/// Preferences, shows are looked up one at a time at low priority, never while something plays,
/// and everything fetched lands in the cache and a small in-memory table the library rows read.
/// Nothing here runs on the startup path: the loop starts the first time the library is shown.
/// </summary>
public sealed partial class MainWindow
{
    private const string MetadataLogSource = "Metadata";

    private readonly object _metadataGate = new();
    private readonly FetchPlan _metadataPlan = new();
    private readonly Dictionary<string, EnrichedShow> _metadataShows = new(StringComparer.Ordinal);
    private MetadataCache? _metadataCache;
    private MetadataChoices? _metadataChoices;
    private CancellationTokenSource? _metadataStop;
    private Task? _metadataLoop;

    private MetadataCache MetadataCacheNow => _metadataCache ??= new MetadataCache(MetadataFolder);

    /// <summary>The fetched record for a show's name, from memory only: safe on the window's thread.</summary>
    private EnrichedShow? MetadataShow(string series)
    {
        lock (_metadataGate)
        {
            return _metadataShows.TryGetValue(MetadataMatch.Normalize(series), out var show) ? show : null;
        }
    }

    /// <summary>The catalogue's title for an episode whose file names none, from memory only.</summary>
    private string? FetchedEpisodeName(VideoEpisode episode) =>
        _settings.Metadata.TvShows && MetadataShow(episode.Series) is { Found: true } show
            ? show.EpisodeName(episode.Season, episode.Episode) : null;

    /// <summary>A show card's extra line from the catalogue: genres and rating, or nothing.</summary>
    private string MetadataBlurb(LibraryGroup group) =>
        _settings.Metadata.TvShows && group.Entries is [{ Kind: Rex.Media.Library.LibraryKind.Video }, ..]
            && MetadataShow(group.Name) is { Found: true } show && show.Blurb() is { Length: > 0 } blurb
            ? blurb : "";

    /// <summary>
    /// A fetched poster's file for a video, when local artwork gave nothing: the cache's file joins
    /// the picture pipeline exactly like a poster beside the file, so the thumbnail cache follows
    /// it. Reads the disk, so it is called off the window's thread.
    /// </summary>
    private string? FetchedPosterPath(string path)
    {
        var metadata = _settings.Metadata;
        if (!metadata.TvShows || MetadataPolicy.Excludes(metadata.Excluded, path) || EpisodeNames.Parse(path) is not { } episode)
        {
            return null;
        }

        if (MetadataShow(episode.Series) is not { Found: true, HasPoster: true })
        {
            return null;
        }

        var poster = MetadataCacheNow.PathFor(MetadataFetcher.ArtKind, MetadataMatch.Normalize(episode.Series), MetadataFetcher.ArtExtension);
        return File.Exists(poster) ? poster : null;
    }

    /// <summary>Starts the fetcher when something is turned on; called when the library shows and after Preferences save.</summary>
    private void StartMetadataWhenAllowed()
    {
        if (_metadataLoop is not null || _closed || _library is null
            || _settings.Metadata is { TvShows: false, Movies: false, Music: false })
        {
            return;
        }

        _metadataStop = new CancellationTokenSource();
        var token = _metadataStop.Token;
        _metadataLoop = Task.Run(() => MetadataLoopAsync(token), token);
    }

    private void StopMetadata()
    {
        _metadataStop?.Cancel();
        _metadataStop?.Dispose();
        _metadataStop = null;
        _metadataLoop = null;
    }

    /// <summary>A saved Preferences change restarts the loop, so new switches and exclusions take hold at once.</summary>
    private void MetadataSettingsSaved(PlayerSettings before)
    {
        var old = before.Metadata;
        var now = _settings.Metadata;
        if (old.TvShows == now.TvShows && old.Movies == now.Movies && old.Music == now.Music
            && old.TmdbApiKey == now.TmdbApiKey && old.Excluded.SequenceEqual(now.Excluded, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        StopMetadata();
        StartMetadataWhenAllowed();
    }

    private async Task MetadataLoopAsync(CancellationToken token)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"rexplayer/{Version} (https://github.com/tochi-mba/rexplayer)");
            var cache = MetadataCacheNow;
            var fetcher = new MetadataFetcher(cache, async (uri, inner) =>
            {
                using var response = await http.GetAsync(uri, inner).ConfigureAwait(false);
                return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync(inner).ConfigureAwait(false) : null;
            });

            // What the cache already holds serves at once, without the network.
            var choices = MetadataChoices.Load(File.Exists(cache.ChoicesPath) ? await File.ReadAllTextAsync(cache.ChoicesPath, token).ConfigureAwait(false) : null);
            var known = fetcher.CachedShows();
            lock (_metadataGate)
            {
                _metadataChoices = choices;
                foreach (var show in known)
                {
                    _metadataShows[MetadataMatch.Normalize(show.Name)] = show;
                }
            }

            if (known.Count > 0)
            {
                RefreshLibraryFromMetadata();
            }

            while (!token.IsCancellationRequested)
            {
                PlanMetadataWork();
                MetadataJob? next;
                lock (_metadataGate)
                {
                    next = _metadataPlan.Next(DateTimeOffset.Now, _player.IsPlaying);
                }

                if (next is not { } job)
                {
                    await Task.Delay(TimeSpan.FromSeconds(20), token).ConfigureAwait(false);
                    continue;
                }

                await RunMetadataJobAsync(fetcher, choices, job, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // The loop was stopped: the window closed, or the settings changed and it starts afresh.
        }
        catch (Exception ex)
        {
            App.Log.Error(MetadataLogSource, "The metadata fetcher stopped: " + ex.Message);
        }
    }

    /// <summary>Queues every show the settings allow and the cache does not hold yet.</summary>
    private void PlanMetadataWork()
    {
        var metadata = _settings.Metadata;
        if (!metadata.TvShows || _library is not { } library)
        {
            return;
        }

        foreach (var group in LibraryViews.TvShows(library.Entries))
        {
            var key = MetadataMatch.Normalize(group.Name);
            lock (_metadataGate)
            {
                if (_metadataShows.ContainsKey(key))
                {
                    continue;
                }
            }

            if (group.Entries.Any(entry => MetadataPolicy.Allows(metadata, MetadataWork.TvShow, entry.Path)))
            {
                lock (_metadataGate)
                {
                    _metadataPlan.Add(new MetadataJob(MetadataWork.TvShow, group.Name));
                }
            }
        }
    }

    private async Task RunMetadataJobAsync(MetadataFetcher fetcher, MetadataChoices choices, MetadataJob job, CancellationToken token)
    {
        // The job may have been outlawed since it was queued.
        if (job.Kind != MetadataWork.TvShow || !_settings.Metadata.TvShows)
        {
            lock (_metadataGate)
            {
                _metadataPlan.Drop(job);
            }

            return;
        }

        var ok = false;
        try
        {
            int? chosen;
            lock (_metadataGate)
            {
                chosen = choices.ChosenShow(job.Key);
            }

            ok = await fetcher.FetchShowAsync(job.Key, null, chosen, token).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            App.Log.Debug(MetadataLogSource, $"The catalogue could not be reached for {job.Key}: {ex.Message}");
        }
        catch (TaskCanceledException) when (!token.IsCancellationRequested)
        {
            App.Log.Debug(MetadataLogSource, $"The catalogue took too long for {job.Key}.");
        }
        catch (IOException ex)
        {
            App.Log.Error(MetadataLogSource, $"The metadata cache could not be written for {job.Key}: {ex.Message}");
        }

        lock (_metadataGate)
        {
            if (ok)
            {
                _metadataPlan.Done(job);
            }
            else
            {
                _metadataPlan.Failed(job, DateTimeOffset.Now);
            }
        }

        if (ok && fetcher.Cached(job.Key) is { } show)
        {
            lock (_metadataGate)
            {
                _metadataShows[MetadataMatch.Normalize(job.Key)] = show;
            }

            RefreshLibraryFromMetadata();
        }
    }

    /// <summary>New facts redraw the library once, on its thread, only while it is open.</summary>
    private void RefreshLibraryFromMetadata() => OnWindowThread(() =>
    {
        if (!_closed && LibraryOpen)
        {
            ShowLibrary();
        }
    });

    /// <summary>Whether a chosen row is one TV show's card, which the metadata menu entries act on.</summary>
    private bool IsShowRow(IReadOnlyList<LibraryRow> chosen, out LibraryGroup show)
    {
        show = null!;
        if (_settings.Metadata.TvShows
            && chosen is [{ Item: LibraryGroup { Entries: [{ Kind: Rex.Media.Library.LibraryKind.Video }, ..] } group }]
            && LibraryViews.EpisodeOf(group.Entries[0]) is not null)
        {
            show = group;
            return true;
        }

        return false;
    }

    /// <summary>Forgets what was fetched for a show and fetches it again.</summary>
    private void RefreshShowMetadata(string show)
    {
        var key = MetadataMatch.Normalize(show);
        _ = Task.Run(() =>
        {
            try
            {
                var cache = MetadataCacheNow;
                cache.Remove(MetadataFetcher.ShowKind, key, ".json");
                cache.Remove(MetadataFetcher.ArtKind, key, MetadataFetcher.ArtExtension);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                App.Log.Error(MetadataLogSource, $"The cached metadata for {show} could not be removed: {ex.Message}");
            }
        });
        lock (_metadataGate)
        {
            _metadataShows.Remove(key);
            _metadataPlan.Add(new MetadataJob(MetadataWork.TvShow, show));
        }

        StartMetadataWhenAllowed();
        Say($"{show} will be fetched again");
    }

    /// <summary>"Wrong match...": search the catalogue by any name and let the user pick; the pick outlives rescans.</summary>
    private async Task ChooseShowMatchAsync(string show)
    {
        var box = new TextBox { Header = "Search the catalogue for", Text = show, MinWidth = 260 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(box, "WrongMatchSearchText");
        var search = new Button { Content = "Search" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(search, "WrongMatchSearch");
        var results = new ListView { MaxHeight = 280, SelectionMode = ListViewSelectionMode.Single };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(results, "Shows the catalogue offers");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(results, "WrongMatchResults");
        var status = new TextBlock { Text = "Search sends the name above to TVmaze.", TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap, Opacity = 0.75 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(status, "WrongMatchStatus");
        var found = new List<ShowCandidate>();
        async Task SearchNowAsync()
        {
            search.IsEnabled = false;
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
                var answer = await fetcher.SearchShowsAsync(box.Text, CancellationToken.None);
                found.Clear();
                found.AddRange(answer);
                results.ItemsSource = found.Select(MetadataMatch.Describe).ToList();
                status.Text = found.Count == 0 ? "The catalogue offered nothing for that name." : $"Pick the right show ({found.Count} offered).";
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
                search.IsEnabled = true;
            }
        }

        search.Click += (_, _) => _ = SearchNowAsync();
        var panel = new StackPanel { Spacing = 8, MinWidth = 300 };
        panel.Children.Add(box);
        panel.Children.Add(search);
        panel.Children.Add(status);
        panel.Children.Add(results);
        if (!await Ask($"Wrong match: {show}", panel, "Use the selected show", "Cancel")
            || results.SelectedIndex < 0 || results.SelectedIndex >= found.Count)
        {
            return;
        }

        var pick = found[results.SelectedIndex];
        var key = MetadataMatch.Normalize(show);
        string saved;
        lock (_metadataGate)
        {
            _metadataChoices ??= new MetadataChoices();
            _metadataChoices.ChooseShow(show, pick.Id);
            saved = _metadataChoices.Save();
            _metadataShows.Remove(key);
            _metadataPlan.Add(new MetadataJob(MetadataWork.TvShow, show));
        }

        await Task.Run(() =>
        {
            try
            {
                var cache = MetadataCacheNow;
                Directory.CreateDirectory(cache.Folder);
                File.WriteAllText(cache.ChoicesPath, saved);
                cache.Remove(MetadataFetcher.ShowKind, key, ".json");
                cache.Remove(MetadataFetcher.ArtKind, key, MetadataFetcher.ArtExtension);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                App.Log.Error(MetadataLogSource, "The match choice could not be kept: " + ex.Message);
            }
        });
        StartMetadataWhenAllowed();
        Say($"{show} will be fetched as {pick.Name}");
    }
}
