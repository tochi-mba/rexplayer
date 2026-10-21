using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Metadata;
using Rex.Media.Settings;
using Windows.Storage.Pickers;
using Launcher = Windows.System.Launcher;

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
    private readonly Dictionary<string, EnrichedAlbum> _metadataAlbums = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EnrichedMovie> _metadataMovies = new(StringComparer.Ordinal);
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
        if (MetadataPolicy.Excludes(metadata.Excluded, path))
        {
            return null;
        }

        string? poster = null;
        if (EpisodeNames.Parse(path) is { } episode)
        {
            if (metadata.TvShows && MetadataShow(episode.Series) is { Found: true, HasPoster: true })
            {
                poster = MetadataCacheNow.PathFor(MetadataFetcher.ArtKind, MetadataMatch.Normalize(episode.Series), MetadataFetcher.ArtExtension);
            }
        }
        else if (metadata.Movies
            && _library?.Entry(path) is { Kind: Rex.Media.Library.LibraryKind.Video } entry
            && MetadataMovie(entry) is { HasPoster: true } movie)
        {
            poster = MetadataCacheNow.PathFor(MetadataFetcher.MovieArtKind,
                MetadataMatch.MovieKey(LibraryViews.MovieTitleOf(entry), LibraryViews.MovieYearOf(entry)), MetadataFetcher.ArtExtension);
        }

        return poster is not null && File.Exists(poster) ? poster : null;
    }

    /// <summary>The fetched record for a movie's file, from memory only: safe on the window's thread.</summary>
    private EnrichedMovie? MetadataMovie(Rex.Media.Library.LibraryEntry entry)
    {
        if (!_settings.Metadata.Movies)
        {
            return null;
        }

        var key = MetadataMatch.MovieKey(LibraryViews.MovieTitleOf(entry), LibraryViews.MovieYearOf(entry));
        lock (_metadataGate)
        {
            return _metadataMovies.TryGetValue(key, out var movie) && movie.Found ? movie : null;
        }
    }

    /// <summary>A movie card's extra words from the catalogue: genres and rating, or nothing.</summary>
    private string MovieBlurb(Rex.Media.Library.LibraryEntry entry) => MetadataMovie(entry)?.Blurb() ?? "";

    /// <summary>Whether the chosen rows are one movie, which the refresh entry acts on.</summary>
    private bool IsMovieRow(IReadOnlyList<LibraryRow> chosen, out Rex.Media.Library.LibraryEntry movie)
    {
        movie = null!;
        if (_settings.Metadata.Movies && _librarySource == LibrarySource.Movies
            && chosen is [{ Item: Rex.Media.Library.LibraryEntry { Kind: Rex.Media.Library.LibraryKind.Video } entry }])
        {
            movie = entry;
            return true;
        }

        return false;
    }

    /// <summary>Forgets what was fetched for a movie and fetches it again.</summary>
    private void RefreshMovieMetadata(Rex.Media.Library.LibraryEntry entry)
    {
        var title = LibraryViews.MovieTitleOf(entry);
        var year = LibraryViews.MovieYearOf(entry);
        Forget(Task.Run(() =>
        {
            try
            {
                var cache = MetadataCacheNow;
                cache.Remove(MetadataFetcher.MovieKind, MetadataMatch.MovieKey(title, year), ".json");
                cache.Remove(MetadataFetcher.MovieArtKind, MetadataMatch.MovieKey(title, year), MetadataFetcher.ArtExtension);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                App.Log.Error(MetadataLogSource, $"The cached metadata for {title} could not be removed: {ex.Message}");
            }
        }));
        lock (_metadataGate)
        {
            _metadataMovies.Remove(MetadataMatch.MovieKey(title, year));
            _metadataPlan.Add(new MetadataJob(MetadataWork.Movie, MovieJobKey(title, year)));
        }

        StartMetadataWhenAllowed();
        Say($"{title} will be fetched again");
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
        if (old.TvShows == now.TvShows && old.Movies == now.Movies && old.Music == now.Music && old.ReplaceCovers == now.ReplaceCovers
            && old.Excluded.SequenceEqual(now.Excluded, StringComparer.OrdinalIgnoreCase))
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
            // Each service's pace is kept per host, one request at a time (the loop is the only
            // caller), and an answer asking to slow down holds the whole queue back.
            var hosts = new PoliteHosts();
            var fetcher = new MetadataFetcher(cache, async (uri, inner) =>
            {
                if (hosts.Delay(uri, DateTimeOffset.Now) is { } wait && wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, inner).ConfigureAwait(false);
                }

                using var response = await http.GetAsync(uri, inner).ConfigureAwait(false);
                if (PoliteHosts.BacksOff((int)response.StatusCode))
                {
                    throw new HttpRequestException($"{uri.Host} asked for fewer requests ({(int)response.StatusCode})", null, response.StatusCode);
                }

                return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync(inner).ConfigureAwait(false) : null;
            });

            // What the cache already holds serves at once, without the network.
            var choices = MetadataChoices.Load(File.Exists(cache.ChoicesPath) ? await File.ReadAllTextAsync(cache.ChoicesPath, token).ConfigureAwait(false) : null);
            var known = fetcher.CachedShows();
            var knownAlbums = fetcher.CachedAlbums();
            LoadCachedArtists(fetcher);
            var knownMovies = fetcher.CachedMovies();
            lock (_metadataGate)
            {
                _metadataChoices = choices;
                foreach (var show in known)
                {
                    _metadataShows[MetadataMatch.Normalize(show.Name)] = show;
                }

                foreach (var album in knownAlbums)
                {
                    _metadataAlbums[MusicBrainz.AlbumKey(album.Artist, album.Album)] = album;
                }

                foreach (var movie in knownMovies)
                {
                    _metadataMovies[MetadataMatch.MovieKey(movie.Title, movie.Year)] = movie;
                }
            }

            if (known.Count > 0 || knownMovies.Count > 0)
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
            App.Log.Error(MetadataLogSource, "The metadata fetcher stopped: " + ex);
        }
    }

    /// <summary>Queues every show the settings allow and the cache does not hold yet.</summary>
    private void PlanMetadataWork()
    {
        var metadata = _settings.Metadata;
        if (_library is not { } library)
        {
            return;
        }

        var entries = library.Entries;
        if (metadata.TvShows)
        {
            foreach (var group in LibraryViews.TvShows(entries))
            {
                var key = MetadataMatch.Normalize(group.Name);
                lock (_metadataGate)
                {
                    if (_metadataShows.TryGetValue(key, out var known) && known.Current)
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

        if (metadata.Movies)
        {
            foreach (var entry in LibraryViews.Standalone(entries).Where(MovieRules.WorthLookingUp))
            {
                var title = LibraryViews.MovieTitleOf(entry);
                var year = LibraryViews.MovieYearOf(entry);
                if (title.Length == 0)
                {
                    continue;
                }

                var key = MetadataMatch.MovieKey(title, year);
                lock (_metadataGate)
                {
                    if (_metadataMovies.TryGetValue(key, out var known) && known.Current)
                    {
                        continue;
                    }
                }

                if (MetadataPolicy.Allows(metadata, MetadataWork.Movie, entry.Path))
                {
                    lock (_metadataGate)
                    {
                        _metadataPlan.Add(new MetadataJob(MetadataWork.Movie, MovieJobKey(title, year)));
                    }
                }
            }
        }

        if (metadata.Music)
        {
            foreach (var group in LibraryViews.Albums(entries.Where(AudioRules.WorthLookingUp)))
            {
                // A group without a real album tag is a bag of loose songs, not an album to look up.
                if (group.Entries[0].Album is not { Length: > 0 } album)
                {
                    continue;
                }

                var key = MusicBrainz.AlbumKey(group.Detail, album);
                var stale = false;
                lock (_metadataGate)
                {
                    if (_metadataAlbums.TryGetValue(key, out var settled))
                    {
                        // With covers being replaced, an album once passed over for carrying its
                        // own art is looked up after all; everything else settled stays settled,
                        // unless it was settled by an older build that kept fewer facts.
                        if (settled.Current && (!metadata.ReplaceCovers || !settled.LocalArt))
                        {
                            continue;
                        }

                        stale = true;
                        _metadataAlbums.Remove(key);
                    }
                }

                if (stale)
                {
                    try
                    {
                        MetadataCacheNow.Remove(MetadataFetcher.MusicKind, key, ".json");
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        App.Log.Error(MetadataLogSource, $"The cached metadata for {album} could not be removed: {ex.Message}");
                    }
                }

                if (group.Entries.Any(entry => MetadataPolicy.Allows(metadata, MetadataWork.Music, entry.Path)))
                {
                    lock (_metadataGate)
                    {
                        _metadataPlan.Add(new MetadataJob(MetadataWork.Music, AlbumJobKey(group.Detail, album)));
                    }
                }
            }

            PlanArtistWork(entries, metadata);
        }
    }

    /// <summary>An album job's key carries the artist and the album, apart but in one string.</summary>
    private static string AlbumJobKey(string? artist, string album) => (artist ?? "") + "\u001f" + album;

    private static (string? Artist, string Album) AlbumFromJobKey(string key)
    {
        var split = key.IndexOf('\u001f', StringComparison.Ordinal);
        var artist = key[..split];
        return (artist.Length > 0 ? artist : null, key[(split + 1)..]);
    }

    /// <summary>A movie job's key carries the title and the year, apart but in one string.</summary>
    private static string MovieJobKey(string title, int? year) =>
        title + "\u001f" + (year?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "");

    private static (string Title, int? Year) MovieFromJobKey(string key)
    {
        var split = key.IndexOf('\u001f', StringComparison.Ordinal);
        var year = key[(split + 1)..];
        return (key[..split], year.Length > 0 ? int.Parse(year, System.Globalization.CultureInfo.InvariantCulture) : null);
    }

    private async Task RunMetadataJobAsync(MetadataFetcher fetcher, MetadataChoices choices, MetadataJob job, CancellationToken token)
    {
        // The job may have been outlawed since it was queued.
        var allowed = job.Kind switch
        {
            MetadataWork.TvShow => _settings.Metadata.TvShows,
            MetadataWork.Music => _settings.Metadata.Music,
            MetadataWork.Movie => _settings.Metadata.Movies,
            _ => false,
        };
        if (!allowed)
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
            if (IsArtistJob(job))
            {
                ok = await FetchArtistJobAsync(fetcher, job, token).ConfigureAwait(false);
            }
            else if (job.Kind == MetadataWork.Music)
            {
                var (artist, album) = AlbumFromJobKey(job.Key);
                var carryArt = !_settings.Metadata.ReplaceCovers && AlbumFilesCarryArt(artist, album);
                ok = await fetcher.FetchAlbumAsync(artist, album, carryArt, token).ConfigureAwait(false);
            }
            else if (job.Kind == MetadataWork.Movie)
            {
                var (title, year) = MovieFromJobKey(job.Key);
                int? film;
                lock (_metadataGate)
                {
                    film = choices.ChosenMovie(title, year);
                }

                ok = await fetcher.FetchMovieAsync(title, year, token, film).ConfigureAwait(false);
            }
            else
            {
                int? chosen;
                lock (_metadataGate)
                {
                    chosen = choices.ChosenShow(job.Key);
                }

                ok = await fetcher.FetchShowAsync(job.Key, null, chosen, token).ConfigureAwait(false);
            }
        }
        catch (HttpRequestException ex)
        {
            App.Log.Debug(MetadataLogSource, $"The catalogue could not be reached for {job.Key}: {ex.Message}");
        }
        catch (TaskCanceledException) when (!token.IsCancellationRequested)
        {
            App.Log.Debug(MetadataLogSource, $"The catalogue took too long for {job.Key}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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

        if (!ok)
        {
            return;
        }

        if (IsArtistJob(job))
        {
            ArtistJobDone(fetcher, job);
        }
        else if (job.Kind == MetadataWork.Music)
        {
            var (artist, album) = AlbumFromJobKey(job.Key);
            if (fetcher.CachedAlbum(artist, album) is { } got)
            {
                lock (_metadataGate)
                {
                    _metadataAlbums[MusicBrainz.AlbumKey(artist, album)] = got;
                }

                if (got.HasCover || got.ReleaseGroupId is not null)
                {
                    RefreshLibraryFromMetadata();
                }
            }
        }
        else if (job.Kind == MetadataWork.Movie)
        {
            var (title, year) = MovieFromJobKey(job.Key);
            if (fetcher.CachedMovie(title, year) is { } movie)
            {
                lock (_metadataGate)
                {
                    _metadataMovies[MetadataMatch.MovieKey(title, year)] = movie;
                }

                RefreshLibraryFromMetadata();
            }
        }
        else if (fetcher.Cached(job.Key) is { } show)
        {
            lock (_metadataGate)
            {
                _metadataShows[MetadataMatch.Normalize(job.Key)] = show;
            }

            RefreshLibraryFromMetadata();
        }
    }

    /// <summary>
    /// Whether an album's files bring their own artwork: a cover image in the folder, or art
    /// embedded in the first file's tags. Local art always wins, so such an album is never looked
    /// up. Reads the disk, so it runs on the fetcher's thread.
    /// </summary>
    private bool AlbumFilesCarryArt(string? artist, string album)
    {
        var entries = _library?.Entries ?? [];
        var first = entries.FirstOrDefault(entry => entry.Kind == Rex.Media.Library.LibraryKind.Music
            && string.Equals(entry.Album, album, StringComparison.OrdinalIgnoreCase)
            && string.Equals(entry.FiledArtist ?? "", artist ?? "", StringComparison.OrdinalIgnoreCase));
        if (first is null)
        {
            return false;
        }

        if (Rex.Media.AppCore.Player.FolderArt.Find(first.Path) is not null)
        {
            return true;
        }

        try
        {
            using var source = new Rex.Media.IO.FileByteSource(first.Path);
            using var demuxer = Rex.Media.AppCore.MediaRegistries.Demuxers().Open(source, CancellationToken.None);
            return demuxer.Info.CoverArt is { Length: > 0 };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Rex.Media.Primitives.MediaFormatException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// An album's fetched cover file for a song, when its folder gave nothing: it joins the picture
    /// pipeline exactly like folder art, so the thumbnail cache follows it. Embedded art always
    /// wins; a cover is only ever fetched for an album whose files carry none.
    /// </summary>
    private string? FetchedCoverPath(string path)
    {
        var metadata = _settings.Metadata;
        if (!metadata.Music || MetadataPolicy.Excludes(metadata.Excluded, path)
            || _library?.Entry(path) is not { Kind: Rex.Media.Library.LibraryKind.Music, Album.Length: > 0 } entry)
        {
            return null;
        }

        var key = MusicBrainz.AlbumKey(entry.FiledArtist, entry.Album);
        lock (_metadataGate)
        {
            if (!_metadataAlbums.TryGetValue(key, out var album) || album is not { HasCover: true, LocalArt: false })
            {
                return null;
            }
        }

        var cover = MetadataCacheNow.PathFor(MetadataFetcher.CoverKind, key, MetadataFetcher.ArtExtension);
        return File.Exists(cover) ? cover : null;
    }

    /// <summary>Whether a chosen row is one album's card, which the refresh entry acts on.</summary>
    private bool IsAlbumRow(IReadOnlyList<LibraryRow> chosen, out string? artist, out string album)
    {
        (artist, album) = (null, "");
        if (_settings.Metadata.Music
            && chosen is [{ Item: LibraryGroup { Entries: [{ Kind: Rex.Media.Library.LibraryKind.Music, Album.Length: > 0 }, ..] } group }]
            && _librarySource == LibrarySource.Albums)
        {
            (artist, album) = (group.Detail, group.Entries[0].Album!);
            return true;
        }

        return false;
    }

    /// <summary>Forgets what was fetched for an album and fetches it again.</summary>
    private void RefreshAlbumMetadata(string? artist, string album)
    {
        var key = MusicBrainz.AlbumKey(artist, album);
        Forget(Task.Run(() =>
        {
            try
            {
                var cache = MetadataCacheNow;
                cache.Remove(MetadataFetcher.MusicKind, key, ".json");
                cache.Remove(MetadataFetcher.CoverKind, key, MetadataFetcher.ArtExtension);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                App.Log.Error(MetadataLogSource, $"The cached metadata for {album} could not be removed: {ex.Message}");
            }
        }));
        lock (_metadataGate)
        {
            _metadataAlbums.Remove(key);
            _metadataPlan.Add(new MetadataJob(MetadataWork.Music, AlbumJobKey(artist, album)));
        }

        StartMetadataWhenAllowed();
        Say($"{album} will be fetched again");
    }

    /// <summary>
    /// The cover the user chose for a song's album, if any: their explicit pick beats everything
    /// shown, lives outside the downloads, and is never written into their files. Reads the disk,
    /// so it is called off the window's thread.
    /// </summary>
    private string? ChosenCoverPath(string path)
    {
        if (_library?.Entry(path) is not { Kind: Rex.Media.Library.LibraryKind.Music, Album.Length: > 0 } entry)
        {
            return null;
        }

        var chosen = MetadataCacheNow.ChosenArtPath(MusicBrainz.AlbumKey(entry.FiledArtist, entry.Album));
        return File.Exists(chosen) ? chosen : null;
    }

    /// <summary>Whether the user chose a cover for this album, so the menu can offer to restore the original.</summary>
    private bool HasChosenCover(string? artist, string album) =>
        File.Exists(MetadataCacheNow.ChosenArtPath(MusicBrainz.AlbumKey(artist, album)));

    /// <summary>Puts the album's own artwork back by forgetting the user's chosen cover.</summary>
    private void RestoreAlbumCover(string? artist, string album)
    {
        try
        {
            MetadataCacheNow.RemoveChosenArt(MusicBrainz.AlbumKey(artist, album));
            Say($"{album} shows its own cover again");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Say("The chosen cover could not be removed: " + ex.Message);
        }

        ShowLibrary();
    }

    /// <summary>
    /// "Replace cover...": the catalogue's candidate covers to pick from, or any image file. The
    /// pick is kept in rexplayer's own cache - never written into the user's files - and outlives
    /// rescans and cleared downloads.
    /// </summary>
    private async Task ReplaceAlbumCoverAsync(string? artist, string album)
    {
        var status = new TextBlock { Text = "Asking the catalogue for covers…", TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap, Opacity = 0.75 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(status, "ReplaceCoverStatus");
        var results = new ListView { MaxHeight = 320, SelectionMode = ListViewSelectionMode.Single };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(results, "Covers the catalogue offers");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(results, "ReplaceCoverResults");
        var covers = new List<byte[]>();
        byte[]? pickedFile = null;
        var pickFile = new Button { Content = "Choose an image file..." };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(pickFile, "ReplaceCoverFromFile");
        pickFile.Click += async (_, _) =>
        {
            var picker = Prepared(new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary, ViewMode = PickerViewMode.Thumbnail });
            foreach (var type in new[] { ".jpg", ".jpeg", ".png", ".webp", ".bmp" })
            {
                picker.FileTypeFilter.Add(type);
            }

            if (await picker.PickSingleFileAsync() is { } file)
            {
                try
                {
                    pickedFile = await File.ReadAllBytesAsync(file.Path);
                    results.SelectedIndex = -1;
                    status.Text = $"Using {file.Name}. Save to keep it.";
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    status.Text = "That file could not be read: " + ex.Message;
                }
            }
        };

        var panel = new StackPanel { Spacing = 8, MinWidth = 320 };
        panel.Children.Add(new TextBlock
        {
            Text = "The cover you pick changes only what rexplayer shows; your files are never written to.",
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
        });
        panel.Children.Add(status);
        panel.Children.Add(results);
        panel.Children.Add(pickFile);

        async Task LoadCandidatesAsync()
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd($"rexplayer/{Version} (https://github.com/tochi-mba/rexplayer)");
                var gate = new RateGate(TimeSpan.FromSeconds(1.1));
                var fetcher = new MetadataFetcher(MetadataCacheNow, async (uri, inner) =>
                {
                    if (uri.Host.EndsWith("musicbrainz.org", StringComparison.OrdinalIgnoreCase)
                        && gate.Delay(DateTimeOffset.Now) is { } wait && wait > TimeSpan.Zero)
                    {
                        await Task.Delay(wait, inner).ConfigureAwait(false);
                    }

                    using var response = await http.GetAsync(uri, inner).ConfigureAwait(false);
                    return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync(inner).ConfigureAwait(false) : null;
                });
                var candidates = await fetcher.SearchAlbumsAsync(artist, album, CancellationToken.None);
                foreach (var candidate in candidates.Take(4))
                {
                    var bytes = await fetcher.FetchBytesAsync(MusicBrainz.CoverThumbUri(candidate.Id), CancellationToken.None);
                    if (bytes is null)
                    {
                        continue;
                    }

                    var image = new Microsoft.UI.Xaml.Controls.Image { Width = 120, Height = 120, Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill };
                    try
                    {
                        image.Source = await DecodePictureAsync(bytes, 120, CancellationToken.None);
                    }
                    catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException or NotSupportedException)
                    {
                        App.Log.Debug(MetadataLogSource, $"A cover offered for {album} could not be shown: {ex.Message}");
                        continue;
                    }
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    row.Children.Add(image);
                    row.Children.Add(new TextBlock
                    {
                        Text = candidate.Title + (candidate.Artist is { } by ? "\n" + by : "") + (candidate.Year is { } year ? "\n" + year.ToString(System.Globalization.CultureInfo.CurrentCulture) : ""),
                        VerticalAlignment = VerticalAlignment.Center,
                        TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                        MaxWidth = 220,
                    });
                    covers.Add(bytes);
                    results.Items.Add(row);
                }

                status.Text = covers.Count == 0
                    ? "The catalogue offered no covers for this album; you can still choose an image file."
                    : $"Pick a cover ({covers.Count} offered), or choose an image file.";
            }
            catch (HttpRequestException ex)
            {
                status.Text = "The catalogue could not be reached: " + ex.Message;
            }
            catch (TaskCanceledException)
            {
                status.Text = "The catalogue took too long to answer.";
            }
        }

        var loading = LoadCandidatesAsync();
        var saved = await Ask($"Replace the cover of {album}", panel, "Use this cover", "Cancel");
        await loading;
        var chosen = pickedFile ?? (results.SelectedIndex >= 0 && results.SelectedIndex < covers.Count ? covers[results.SelectedIndex] : null);
        if (!saved || chosen is null)
        {
            return;
        }

        try
        {
            MetadataCacheNow.WriteChosenArt(MusicBrainz.AlbumKey(artist, album), chosen);
            Say($"{album} shows the cover you chose");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Say("The chosen cover could not be kept: " + ex.Message);
        }

        ShowLibrary();
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
        Forget(Task.Run(() =>
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
        }));
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
        if (await PickFromCatalogueAsync($"Wrong match: {show}", show, "TVmaze", "show", (fetcher, name) => fetcher.SearchShowsAsync(name, CancellationToken.None)) is not { } pick)
        {
            return;
        }

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

    /// <summary>A library row's online-details actions: refresh, fix the match, change the cover or watch the trailer.</summary>
    private void AddMetadataMenuItems(MenuFlyout menu, IReadOnlyList<LibraryRow> chosen)
    {
        if (IsShowRow(chosen, out var show))
        {
            menu.Items.Add(MenuItem("Refresh metadata", "LibraryMenuRefreshMetadata", () => RefreshShowMetadata(show.Name)));
            menu.Items.Add(MenuItem("Wrong match...", "LibraryMenuWrongMatch", () => Forget(ChooseShowMatchAsync(show.Name))));
            menu.Items.Add(new MenuFlyoutSeparator());
        }
        else if (IsAlbumRow(chosen, out var albumArtist, out var album))
        {
            menu.Items.Add(MenuItem("Refresh metadata", "LibraryMenuRefreshMetadata", () => RefreshAlbumMetadata(albumArtist, album)));
            menu.Items.Add(MenuItem("Replace cover...", "LibraryMenuReplaceCover", () => Forget(ReplaceAlbumCoverAsync(albumArtist, album))));
            if (HasChosenCover(albumArtist, album))
            {
                menu.Items.Add(MenuItem("Restore original cover", "LibraryMenuRestoreCover", () => RestoreAlbumCover(albumArtist, album)));
            }

            menu.Items.Add(new MenuFlyoutSeparator());
        }
        else if (IsMovieRow(chosen, out var movie))
        {
            var facts = MetadataMovie(movie);
            if (facts is { TrailerKeys: [{ } trailer, ..] })
            {
                menu.Items.Add(MenuItem("Watch trailer", "LibraryMenuWatchTrailer",
                    () => _ = Launcher.LaunchUriAsync(new Uri("https://www.youtube.com/watch?v=" + trailer))));
            }

            if (facts is not null && MoreLikeThis.Rank(facts, KnownMovies()) is { Count: > 0 } alike)
            {
                var more = new MenuFlyoutSubItem { Text = "More like this" };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(more, "LibraryMenuMoreLikeThis");
                foreach (var other in alike)
                {
                    more.Items.Add(MenuItem(LibraryViews.MovieTitleOf(other), null, () => Play([other], 0, resume: true)));
                }

                menu.Items.Add(more);
            }

            menu.Items.Add(MenuItem("Refresh metadata", "LibraryMenuRefreshMetadata", () => RefreshMovieMetadata(movie)));
            menu.Items.Add(new MenuFlyoutSeparator());
        }
    }
}
