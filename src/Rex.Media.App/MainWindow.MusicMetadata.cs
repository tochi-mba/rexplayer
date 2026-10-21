using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Metadata;
using Rex.Media.Library;
using Rex.Media.Settings;

namespace Rex.Media.App;

/// <summary>
/// Music's share of the metadata fetcher: albums (facts and, to fill a gap, covers) and then
/// artists (facts, a photo and a summary), all under the Music switch and its folder exclusions,
/// on the same one-at-a-time queue and per-host pace as everything else. Artists are queued after
/// the albums, the ones with the most songs first. The pages read the records from memory only.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Marks an artist's job apart from an album's, whose key holds artist and album apart.</summary>
    private const char ArtistJobMark = '\u001e';

    private readonly Dictionary<string, EnrichedArtist> _metadataArtists = new(StringComparer.Ordinal);

    /// <summary>The fetched record for an artist, from memory only: safe on the window's thread; null with music lookups off.</summary>
    private EnrichedArtist? MetadataArtist(string name)
    {
        if (!_settings.Metadata.Music)
        {
            return null;
        }

        lock (_metadataGate)
        {
            return _metadataArtists.TryGetValue(MetadataFetcher.ArtistKey(name), out var artist) && artist.MusicBrainzId is not null ? artist : null;
        }
    }

    /// <summary>The fetched record for an album, from memory only; null with music lookups off or no match.</summary>
    private EnrichedAlbum? MetadataAlbum(string? artist, string album)
    {
        if (!_settings.Metadata.Music)
        {
            return null;
        }

        lock (_metadataGate)
        {
            return _metadataAlbums.TryGetValue(MusicBrainz.AlbumKey(artist, album), out var found) && found.ReleaseGroupId is not null ? found : null;
        }
    }

    /// <summary>An artist's fetched photo on disk, or null; reads the disk, so it is called off the window's thread.</summary>
    private string? ArtistPhotoPath(string name)
    {
        if (MetadataArtist(name) is not { HasPhoto: true })
        {
            return null;
        }

        var path = MetadataCacheNow.PathFor(MetadataFetcher.ArtistPhotoKind, MetadataFetcher.ArtistKey(name), MetadataFetcher.ArtExtension);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Fills the artists' lookup table from the cache, on the fetcher's thread at its start.</summary>
    private void LoadCachedArtists(MetadataFetcher fetcher)
    {
        var known = fetcher.CachedArtists();
        lock (_metadataGate)
        {
            foreach (var artist in known)
            {
                _metadataArtists[MetadataFetcher.ArtistKey(artist.Name)] = artist;
            }
        }
    }

    /// <summary>
    /// Queues every artist the cache does not hold in this build's shape, the ones with the most
    /// songs first, when at least one of their songs lies outside the excluded folders.
    /// </summary>
    private void PlanArtistWork(IReadOnlyList<LibraryEntry> entries, MetadataSettings metadata)
    {
        foreach (var artist in LibraryViews.Artists(entries).Where(artist => artist.Name != LibraryViews.UnknownArtist).OrderByDescending(artist => artist.Entries.Count))
        {
            lock (_metadataGate)
            {
                if (_metadataArtists.TryGetValue(MetadataFetcher.ArtistKey(artist.Name), out var known) && known.Current)
                {
                    continue;
                }
            }

            if (artist.Entries.Any(entry => MetadataPolicy.Allows(metadata, MetadataWork.Music, entry.Path)))
            {
                lock (_metadataGate)
                {
                    _metadataPlan.Add(new MetadataJob(MetadataWork.Music, ArtistJobMark + artist.Name));
                }
            }
        }
    }

    private static bool IsArtistJob(MetadataJob job) => job.Kind == MetadataWork.Music && job.Key.Length > 1 && job.Key[0] == ArtistJobMark;

    /// <summary>Runs an artist's job; true when it settled.</summary>
    private static Task<bool> FetchArtistJobAsync(MetadataFetcher fetcher, MetadataJob job, CancellationToken token) =>
        fetcher.FetchArtistAsync(job.Key[1..], token);

    /// <summary>Takes a settled artist into memory and redraws the library when there is something new to show.</summary>
    private void ArtistJobDone(MetadataFetcher fetcher, MetadataJob job)
    {
        var name = job.Key[1..];
        if (fetcher.CachedArtist(name) is not { } artist)
        {
            return;
        }

        lock (_metadataGate)
        {
            _metadataArtists[MetadataFetcher.ArtistKey(name)] = artist;
        }

        if (artist.MusicBrainzId is not null)
        {
            RefreshLibraryFromMetadata();
        }
    }
}
