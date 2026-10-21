using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Rex.Media.AppCore;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Metadata;
using Rex.Media.AppCore.Player;
using Rex.Media.Codecs.MediaFoundation;
using Rex.Media.IO;
using Rex.Media.Library;
using Rex.Media.Primitives;
using Rex.Media.Video;

namespace Rex.Media.App;

/// <summary>What a card's picture is: the media's own, a show's poster, or a movie's poster.</summary>
internal enum PictureArt
{
    Natural,
    ShowPoster,
    MoviePoster,
}

/// <summary>
/// Shows', episodes' and movies' artwork (LIB-05): the order <see cref="ArtworkChoice"/> decides,
/// an episode's catalogue still fetched once into the metadata cache, frames chosen by
/// <see cref="FrameChoice"/>, and what a card shows before its picture comes: a tint of the show's
/// own and, on a show's page, its poster blurred behind the episode number. A card that leaves the
/// view lets its request go, so what is on screen is served first.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The metadata cache's kind for episode stills.</summary>
    private const string StillKind = "still";

    /// <summary>The side a placeholder's poster is decoded at: so small that, stretched over a tile, it is a blur.</summary>
    private const int BlurSide = 24;

    private static readonly Lazy<HttpClient> StillClient = new(() =>
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"rexplayer/{Version} (https://github.com/tochi-mba/rexplayer)");
        return http;
    });

    private readonly ConditionalWeakTable<FrameworkElement, LibraryRow> _collageRows = new();
    private readonly HashSet<string> _stillsMissed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Brush> _tints = new(StringComparer.OrdinalIgnoreCase);
    private string? _backdropShow;
    private ImageSource? _backdrop;

    /// <summary>
    /// The picture file a video shows, as <see cref="ArtworkChoice"/> orders it for <paramref name="art"/>;
    /// null when a frame of the video is made instead. Reads the disk, so it runs off the window's thread.
    /// </summary>
    private string? VideoArtwork(string path, PictureArt art, string? still)
    {
        var episode = EpisodeNames.Parse(path);
        return art switch
        {
            PictureArt.ShowPoster => ArtworkChoice.ShowPoster(path, episode?.Season ?? 1, FetchedPosterPath(path), File.Exists).Path,
            PictureArt.Natural when episode is not null => ArtworkChoice.EpisodePicture(path, still, File.Exists).Path,
            _ => ArtworkChoice.MoviePoster(path, FetchedPosterPath(path), File.Exists).Path,
        };
    }

    /// <summary>
    /// The catalogue's still of the episode at <paramref name="path"/>, from the metadata cache, fetched
    /// into it the first time it is shown; null when TV enrichment is off, the show is unknown, it has
    /// no still or the network cannot give it. Like the metadata fetcher, it waits while something plays.
    /// </summary>
    private async Task<string?> EpisodeStillAsync(string path, CancellationToken token)
    {
        var metadata = _settings.Metadata;
        if (!metadata.TvShows || EpisodeNames.Parse(path) is not { Episode: { } number } episode
            || MetadataShow(episode.Series) is not { Found: true } show || show.EpisodeStill(episode.Season, number) is not { } address
            || !Uri.TryCreate(address, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || MetadataPolicy.Excludes(metadata.Excluded, path))
        {
            return null;
        }

        var key = ArtworkChoice.StillKey(MetadataMatch.Normalize(episode.Series), episode.Season, number);
        var cache = MetadataCacheNow;
        var file = cache.PathFor(StillKind, key, MetadataFetcher.ArtExtension);
        if (await Task.Run(() => File.Exists(file), token))
        {
            return file;
        }

        if (_player.IsPlaying || !_stillsMissed.Add(address))
        {
            return null;
        }

        try
        {
            using var response = await StillClient.Value.GetAsync(uri, token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(token);
            await Task.Run(() => cache.Write(StillKind, key, MetadataFetcher.ArtExtension, bytes), token);
            _stillsMissed.Remove(address);
            return file;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException
            || (ex is TaskCanceledException && !token.IsCancellationRequested))
        {
            App.Log.Debug(LogSource, $"No still for {Path.GetFileName(path)}: {ex.Message}");
            return null;
        }
    }

    /// <summary>A picture made by rexplayer, or null for a file it cannot read, which Windows' own thumbnail may still show.</summary>
    private static byte[]? MadeOrNull(Func<byte[]> make)
    {
        try
        {
            return make();
        }
        catch (Exception ex) when (ex is MediaFormatException or NotSupportedException or COMException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            App.Log.Debug(LogSource, "rexplayer made no picture, so Windows is asked: " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// A strong frame of the video as a PNG, past its intro and before its credits (<see cref="FrameChoice"/>),
    /// cut free of recorded bars and, for a <paramref name="poster"/>, to a poster's shape around its
    /// most detailed part. Only the key picture at each moment tried is decoded.
    /// </summary>
    private byte[] VideoFramePng(string path, TimeSpan? duration, EpisodeSections? sections, bool poster, CancellationToken token)
    {
        using var source = new FileByteSource(path);
        using var demuxer = MediaRegistries.Demuxers(TimeSpan.FromSeconds(_settings.PictureSeconds)).Open(source, token);
        var decoders = MediaRegistries.Decoders(new MfDecoderFactory(), new WicDecoderFactory());
        var length = duration ?? (demuxer.Info.Duration.IsKnown ? demuxer.Info.Duration.ToTimeSpan() : null);
        VideoFrame? best = null;
        var bestScore = double.MinValue;
        try
        {
            foreach (var at in FrameChoice.Candidates(length, sections?.IntroEnd, sections?.CreditsStart))
            {
                token.ThrowIfCancellationRequested();
                var (picture, _) = Snapshot.Take(demuxer, decoders, new MediaTime(at.Ticks), token, nearest: true);
                var bgra = picture;
                if (picture.Format != PixelFormat.Bgra32)
                {
                    using (picture)
                    {
                        bgra = ColorConverter.ToBgra(picture);
                    }
                }

                var sample = CreditFrameEvidence.Measure(bgra, at);
                var score = FrameChoice.Strong(sample) ? double.MaxValue : FrameChoice.Score(sample);
                if (score <= bestScore)
                {
                    bgra.Dispose();
                    continue;
                }

                best?.Dispose();
                (best, bestScore) = (bgra, score);
                if (score == double.MaxValue)
                {
                    break;
                }
            }

            var frame = best ?? throw new MediaFormatException("The video track gave no picture.");
            var inner = PictureBorders.Find(frame);
            var area = poster ? FrameChoice.PosterArea(inner, FrameChoice.ColumnDetail(frame, inner, 24)) : inner;
            using var output = new MemoryStream();
            PngWriter.Write(output, frame, area);
            return output.ToArray();
        }
        finally
        {
            best?.Dispose();
        }
    }

    /// <summary>The picture a card asks for: a show's poster for a show, a movie's poster on a poster card, else the media's own.</summary>
    private static PictureArt PictureArtOf(LibraryRow row, bool posterCard) => row.Item switch
    {
        LibraryGroup { Entries: [{ Kind: LibraryKind.Video } first, ..] } when LibraryViews.EpisodeOf(first) is not null => PictureArt.ShowPoster,
        LibraryEntry { Kind: LibraryKind.Video } entry when posterCard && LibraryViews.EpisodeOf(entry) is null => PictureArt.MoviePoster,
        _ => PictureArt.Natural,
    };

    /// <summary>Whether a list shows poster cards (Movies, TV shows), whose movies want a poster's shape.</summary>
    private bool ShowsPosters(ListViewBase list) =>
        _cardTemplates is { } templates && ReferenceEquals(list.ItemTemplate, templates["LibraryMovieTemplate"]);

    /// <summary>
    /// Loads a card's picture, with a request of its own the card can let go when it leaves the view.
    /// One that took too long may be asked again the next time the card is shown.
    /// </summary>
    private async Task LoadRowPictureAsync(LibraryRow row, LibraryEntry entry, bool urgent, CancellationToken view)
    {
        DressPlaceholder(row, entry);
        row.PictureWork?.Cancel();
        using var work = CancellationTokenSource.CreateLinkedTokenSource(view);
        row.PictureWork = work;
        try
        {
            await ShowPictureAsync(entry.Path, entry.Kind, entry.Duration, picture => row.Picture = picture, work.Token,
                urgent, row.Shape, missed: () => row.PictureAsked = row.Picture is not null);
        }
        finally
        {
            if (row.PictureWork == work)
            {
                row.PictureWork = null;
            }
        }
    }

    /// <summary>A card left the view before its picture came: its request goes, and it asks again when it is shown again.</summary>
    private static void LetPictureGo(LibraryRow? row)
    {
        if (row is { Picture: null, PictureWork: { } work })
        {
            work.Cancel();
            row.PictureAsked = false;
        }
    }

    /// <summary>A collage tile was given another row: the row it showed lets its request go.</summary>
    private void FollowCollageTile(FrameworkElement tile, LibraryRow row)
    {
        if (_collageRows.TryGetValue(tile, out var before) && !ReferenceEquals(before, row))
        {
            LetPictureGo(before);
        }

        _collageRows.AddOrUpdate(tile, row);
    }

    /// <summary>
    /// What a video's card shows before its picture: a tint of its show's own and, on a show's page,
    /// the show's poster blurred behind the episode number, so a collage reads well at once.
    /// </summary>
    private void DressPlaceholder(LibraryRow row, LibraryEntry entry)
    {
        var series = row.Item is LibraryGroup group && row.Shape == PictureArt.ShowPoster ? group.Name
            : entry.Kind == LibraryKind.Video ? LibraryViews.EpisodeOf(entry)?.Series : null;
        if (series is null)
        {
            return;
        }

        if (!_tints.TryGetValue(series, out var tint))
        {
            var (red, green, blue) = ArtworkChoice.Tint(series);
            tint = new SolidColorBrush(Windows.UI.Color.FromArgb(255, red, green, blue));
            _tints[series] = tint;
        }

        row.Tint = tint;
        if (row.Item is not LibraryEntry || _showPageOf is not { } show || _libraryGroup is not { Entries: [var first, ..] })
        {
            return;
        }

        if (_backdropShow == show)
        {
            row.Backdrop = _backdrop;
            return;
        }

        (_backdropShow, _backdrop) = (show, null);
        Forget(ShowBackdropAsync(show, first));
    }

    /// <summary>The show's poster, decoded tiny so it is a blur, behind every episode tile of its page still waiting for a picture.</summary>
    private async Task ShowBackdropAsync(string show, LibraryEntry first)
    {
        await ShowPictureAsync(first.Path, first.Kind, first.Duration, picture =>
        {
            if (_backdropShow != show)
            {
                return;
            }

            _backdrop = picture;
            foreach (var row in _libraryRows.Where(row => row.Item is LibraryEntry && row.Picture is null))
            {
                row.Backdrop = picture;
            }
        }, _libraryPictures.Token, art: PictureArt.ShowPoster, side: BlurSide);
        if (_backdropShow == show && _backdrop is null)
        {
            // Cancelled with its view, or too slow: the next tile shown asks again.
            _backdropShow = null;
        }
    }
}
