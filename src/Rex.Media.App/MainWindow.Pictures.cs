using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Rex.Media.AppCore;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;
using Rex.Media.Codecs.MediaFoundation;
using Rex.Media.IO;
using Rex.Media.Library;
using Rex.Media.Primitives;
using Rex.Media.Settings;
using Rex.Media.Video;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace Rex.Media.App;

/// <summary>
/// A picture for every piece of media, wherever it is listed (the library, the playlist): for a
/// video its artwork as <see cref="ArtworkChoice"/> orders it, else a strong frame of its own (Windows'
/// thumbnail only when rexplayer cannot read the file); the picture itself; or for music its cover,
/// its folder's art or artwork drawn from its sound. A video's frame is cut free of the black or
/// grey bars recorded into it, so a card filled by it shows picture. Made pictures are kept on disk
/// until the file changes, and the latest few hundred in memory, so a list that redraws does not
/// make them again. Two normal requests use bounded slots; a separate slot serves an urgent hovered
/// or keyboard-focused row while those are occupied. Nothing else waits in these slots: background
/// work (transcription, section analysis) runs on threads of its own below normal priority.
/// </summary>
public sealed partial class MainWindow
{
    private const int PicturesRemembered = 400;

    private readonly SemaphoreSlim _pictureSlots = new(2, 2);
    // A separate fast lane keeps one hovered or keyboard-focused thumbnail responsive while
    // the normal visible-card workers finish slower disk reads or video decodes.
    private readonly SemaphoreSlim _priorityPictureSlot = new(1, 1);
    private readonly Dictionary<string, ImageSource> _pictureMemory = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _pictureOrder = new();

    /// <summary>A visible playlist line asks for its picture once; a file that is missing, or a stream, has none to give.</summary>
    private void OnPlaylistRowShown(Microsoft.UI.Xaml.Controls.ListViewBase sender, Microsoft.UI.Xaml.Controls.ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue && args.Item is PlaylistRow { PictureAsked: false, Missing: false } row && Path.IsPathFullyQualified(row.Location))
        {
            row.PictureAsked = true;
            var duration = _player.Item?.Location == row.Location && _player.Duration > TimeSpan.Zero ? _player.Duration : (TimeSpan?)null;
            Forget(ShowPictureAsync(row.Location, row.Kind, duration, picture => row.Picture = picture, CancellationToken.None));
        }
    }

    /// <summary>
    /// Finds or makes the picture for <paramref name="path"/> as <paramref name="art"/> asks, and hands
    /// it to <paramref name="show"/> on the window's thread, decoded <paramref name="side"/> pixels wide
    /// (the performance tier's size when null). <paramref name="missed"/> is told when it took too
    /// long, so the card may ask again the next time it is shown.
    /// </summary>
    private async Task ShowPictureAsync(string path, LibraryKind kind, TimeSpan? duration, Action<ImageSource> show, CancellationToken viewToken,
        bool urgent = false, PictureArt art = PictureArt.Natural, int? side = null, Action? missed = null)
    {
        var settings = _settings;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(viewToken);
        var token = timeout.Token;
        var lane = urgent ? _priorityPictureSlot : _pictureSlots;
        var entered = false;
        try
        {
            await lane.WaitAsync(token);
            entered = true;
            if (_closed)
            {
                return;
            }

            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var video = kind == LibraryKind.Video;
            var still = video && art == PictureArt.Natural ? await EpisodeStillAsync(path, token) : null;
            var sections = video ? _player.Memory.Sections(path) : null;
            var (key, poster) = await Task.Run(() =>
            {
                // The user's chosen cover beats everything shown; otherwise artwork beside the
                // file wins, unless they asked fetched covers to replace existing ones.
                var local = video ? VideoArtwork(path, art, still)
                    : kind == LibraryKind.Music
                        ? ChosenCoverPath(path) ?? (settings.Metadata.ReplaceCovers
                            ? FetchedCoverPath(path) ?? FolderArt.Find(path)
                            : FolderArt.Find(path) ?? FetchedCoverPath(path))
                        : null;
                return (PictureKey(path, kind, settings, local, art), local);
            }, token);
            token.ThrowIfCancellationRequested();
            if (key is null || _closed)
            {
                return;
            }

            var decodeWidth = side ?? _budget.LibraryPictureSide;
            // A picture already decoded for another performance tier must not keep the
            // old resolution when the budget changes.
            var memoryKey = key + ":" + decodeWidth.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (_pictureMemory.TryGetValue(memoryKey, out var known))
            {
                show(known);
                return;
            }
            // A local poster/cover belongs to the media. Prefer it over an arbitrary video frame.
            var image = poster is null ? null : await LocalPictureAsync(poster, decodeWidth, token);
            image ??= await ReadPictureCacheAsync(key, decodeWidth, token);
            if (image is null && video)
            {
                // A frame chosen by rexplayer, not Windows' first one: Windows' stands in only for a
                // file rexplayer cannot read.
                var made = await Task.Run(() => MadeOrNull(() => MakePicture(path, kind, duration, key, settings, token, art, sections)), token);
                image = made is null ? null : await DecodePictureAsync(made, decodeWidth, token);
            }

            image ??= await ShellPictureAsync(path, kind, decodeWidth, key, token);
            if (image is null)
            {
                var bytes = await Task.Run(() => MakePicture(path, kind, duration, key, settings, token), token);
                image = await DecodePictureAsync(bytes, decodeWidth, token);
            }
            token.ThrowIfCancellationRequested();
            if (_closed)
            {
                return;
            }
            Remember(memoryKey, image);
            show(image);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A slow codec or a row that left the view keeps its placeholder; neither is an error.
            // Only the slow one is worth asking again, the next time its card is shown.
            if (!viewToken.IsCancellationRequested && !_closed)
            {
                missed?.Invoke();
            }
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or MediaFormatException or InvalidOperationException)
        {
            App.Log.Debug(LogSource, $"No picture for {path}: {ex.Message}");
        }
        finally
        {
            if (entered)
            {
                lane.Release();
            }
        }
    }

    /// <summary>
    /// Windows' thumbnail, when it is a picture of the media: for formats Windows has no thumbnailer
    /// for (WebM, Matroska and others) it gives the file type's icon, which is no picture of it.
    /// </summary>
    private static async Task<ImageSource?> ShellPictureAsync(string path, LibraryKind kind, int decodeWidth, string key, CancellationToken token)
    {
        using var shellTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        shellTimeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            return await ReadShellPictureAsync(path, kind, (uint)Math.Min(decodeWidth, 1920), key, shellTimeout.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            // Leave time in the overall request for a frame from our own decoder.
            return null;
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            // A broken shell thumbnail provider must not prevent our own decoder from trying.
            return null;
        }
    }

    private static async Task<ImageSource?> LocalPictureAsync(string path, int decodeWidth, CancellationToken token)
    {
        try
        {
            // Decode directly from the file instead of allocating a second copy of a potentially
            // enormous poster. WIC scales the decoded bitmap to the size needed by the cards.
            var file = await StorageFile.GetFileFromPathAsync(path).AsTask(token);
            using var stream = await file.OpenReadAsync().AsTask(token);
            var header = await BitmapDecoder.CreateAsync(stream).AsTask(token);
            var limit = PictureDecodeLimit.For(header.OrientedPixelWidth, header.OrientedPixelHeight, decodeWidth);
            stream.Seek(0);
            var image = new BitmapImage { DecodePixelWidth = limit.Width, DecodePixelHeight = limit.Height };
            await image.SetSourceAsync(stream).AsTask(token);
            token.ThrowIfCancellationRequested();
            return image;
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Read the disk cache before asking a potentially slow shell provider. A damaged or
    /// unreadable cache is a miss, never a permanent empty card or an unbounded retry loop.
    /// </summary>
    private static async Task<ImageSource?> ReadPictureCacheAsync(string key, int decodeWidth, CancellationToken token)
    {
        try
        {
            var bytes = await Task.Run(() =>
            {
                var path = PictureCachePath(key);
                if (!PictureDecodeLimit.CacheBytesAllowed(new FileInfo(path).Length))
                {
                    return null;
                }

                return File.ReadAllBytes(path);
            }, token);
            return bytes is null ? null : await DecodePictureAsync(bytes, decodeWidth, token);
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            token.ThrowIfCancellationRequested();
            return null;
        }
    }

    private static string PictureCachePath(string key) =>
        App.Roots.LibraryPicture(key);

    private static async Task<ImageSource?> ReadShellPictureAsync(string path, LibraryKind kind, uint size, string key, CancellationToken token)
    {
        var mode = kind switch
        {
            LibraryKind.Music => ThumbnailMode.MusicView,
            LibraryKind.Picture => ThumbnailMode.PicturesView,
            _ => ThumbnailMode.VideosView,
        };
        using var thumbnail = await Task.Run(async () =>
        {
            var file = await StorageFile.GetFileFromPathAsync(path).AsTask(token);
            return await file.GetThumbnailAsync(mode, size, ThumbnailOptions.UseCurrentScale).AsTask(token);
        }, token);
        if (thumbnail is not { Size: > 0, Type: ThumbnailType.Image })
        {
            return null;
        }

        if (kind == LibraryKind.Video && await ShellFrameWithoutBarsAsync(thumbnail, key, token) is { } png)
        {
            return await DecodePictureAsync(png, 0, token);
        }

        thumbnail.Seek(0);
        var image = new BitmapImage();
        await image.SetSourceAsync(thumbnail).AsTask(token);
        return image;
    }

    /// <summary>
    /// Windows' frame of a video with the bars recorded into it cut away, kept on disk like a frame of
    /// rexplayer's own; null when it has no bars, so the frame is shown as it is.
    /// </summary>
    private static async Task<byte[]?> ShellFrameWithoutBarsAsync(StorageItemThumbnail thumbnail, string key, CancellationToken token)
    {
        var decoder = await BitmapDecoder.CreateAsync(thumbnail).AsTask(token);
        var (width, height) = ((int)decoder.OrientedPixelWidth, (int)decoder.OrientedPixelHeight);
        var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, new BitmapTransform(),
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(token);
        var bgra = pixels.DetachPixelData();
        return await Task.Run(() =>
        {
            using var frame = VideoFrame.Rent(PixelFormat.Bgra32, width, height);
            for (var y = 0; y < height; y++)
            {
                bgra.AsSpan(y * width * 4, width * 4).CopyTo(frame.Row(0, y));
            }

            if (PictureBorders.Find(frame) == new PictureArea(0, 0, width, height))
            {
                return null;
            }

            using var output = new MemoryStream();
            PictureBorders.WritePng(output, frame);
            var png = output.ToArray();
            try
            {
                AtomicFile.Write(PictureCachePath(key), png);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                App.Log.Debug(LogSource, "The thumbnail could not be cached: " + ex.Message);
            }

            return png;
        }, token);
    }

    /// <summary>
    /// Makes the picture with rexplayer's own decoders: a strong frame from inside a video, past its
    /// intro (<see cref="FrameChoice"/>), cut to a poster's shape when <paramref name="art"/> asks for a
    /// poster; a picture itself; or for music its folder's art or artwork drawn from its sound. Kept
    /// on disk under <paramref name="key"/> until the file changes.
    /// </summary>
    private byte[] MakePicture(string path, LibraryKind kind, TimeSpan? duration, string key, PlayerSettings settings, CancellationToken token,
        PictureArt art = PictureArt.Natural, Rex.Media.AppCore.Player.EpisodeSections? sections = null)
    {
        byte[] png;
        if (kind == LibraryKind.Music)
        {
            png = AudioArtworkPng(path, new AudioArtworkOptions(
                settings.AudioArtworkStyle,
                settings.AudioArtworkColor,
                settings.AudioArtworkDetail,
                settings.AudioArtworkContrast,
                settings.AudioArtworkUsesIdentity), settings.GenerateAudioArtwork, token);
        }
        else
        {
            png = kind == LibraryKind.Video
                ? VideoFramePng(path, duration, sections, art != PictureArt.Natural, token)
                : PreviewPng(path, TimeSpan.Zero, token);
        }

        token.ThrowIfCancellationRequested();
        try
        {
            AtomicFile.Write(PictureCachePath(key), png);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A cache is optional: display the successfully decoded image even on a
            // full disk or when another process temporarily locks its cache file.
            App.Log.Debug(LogSource, "The thumbnail could not be cached: " + ex.Message);
        }
        return png;
    }

    /// <summary>
    /// Names a picture by the file's path, size and time of change (and, for music, the artwork
    /// settings; for a video, the shape asked for), so a changed file gets a new one; null when the
    /// file is not there.
    /// </summary>
    private static string? PictureKey(string path, LibraryKind kind, PlayerSettings settings, string? local, PictureArt shape = PictureArt.Natural)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            return null;
        }

        var art = kind == LibraryKind.Music
            ? $"\n{settings.GenerateAudioArtwork}\n{settings.AudioArtworkStyle}\n{settings.AudioArtworkColor}\n{settings.AudioArtworkDetail}\n{settings.AudioArtworkContrast}\n{settings.AudioArtworkUsesIdentity}"
            : "";
        // Replacing local artwork must invalidate its thumbnail without touching the video file.
        if (local is not null)
        {
            var posterInfo = new FileInfo(local);
            art += $"\n{local}\n{posterInfo.Length}\n{posterInfo.LastWriteTimeUtc.Ticks}";
        }
        // Video thumbnails are made without the bars recorded into the picture since version 5, and
        // from a strong frame past the intro, posters cut to shape, since version 6.
        art += kind == LibraryKind.Video && shape != PictureArt.Natural ? "\n" + shape : "";
        var version = kind == LibraryKind.Video ? 6 : 4;
        var identity = Encoding.UTF8.GetBytes($"{version}\n{path}\n{file.Length}\n{file.LastWriteTimeUtc.Ticks}{art}");
        return Convert.ToHexStringLower(SHA256.HashData(identity));
    }

    private void Remember(string key, ImageSource image)
    {
        if (_pictureMemory.TryAdd(key, image))
        {
            _pictureOrder.Enqueue(key);
            while (_pictureOrder.Count > PicturesRemembered)
            {
                _pictureMemory.Remove(_pictureOrder.Dequeue());
            }
        }
    }

    private static async Task<ImageSource> DecodePictureAsync(byte[] bytes, int decodeWidth, CancellationToken token)
    {
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync().AsTask(token);
            writer.DetachStream();
        }

        token.ThrowIfCancellationRequested();
        stream.Seek(0);
        var header = await BitmapDecoder.CreateAsync(stream).AsTask(token);
        var limit = PictureDecodeLimit.For(header.OrientedPixelWidth, header.OrientedPixelHeight, decodeWidth);
        stream.Seek(0);
        var image = new BitmapImage { DecodePixelWidth = limit.Width, DecodePixelHeight = limit.Height };
        await image.SetSourceAsync(stream).AsTask(token);
        return image;
    }

    private static byte[] AudioArtworkPng(string path, AudioArtworkOptions options, bool generate, CancellationToken token)
    {
        using var source = new FileByteSource(path);
        using var demuxer = MediaRegistries.Demuxers().Open(source, token);
        if (demuxer.Info.CoverArt is { Length: > 0 } embedded)
        {
            return embedded;
        }

        if (!generate)
        {
            throw new NotSupportedException("Generated music artwork is turned off.");
        }

        var track = demuxer.Info.FirstTrack(MediaKind.Audio) ?? throw new NotSupportedException("The file has no sound to draw.");
        var opened = MediaRegistries.Decoders(new MfDecoderFactory()).CreateAudio(track);
        using var decoder = opened.Decoder ?? throw new NotSupportedException(opened.Reason);
        var decoded = new List<AudioFrame>();
        var mono = new List<float>(96_000);
        var sampleRate = track.Audio!.SampleRate;
        var wanted = checked(Math.Clamp(sampleRate, 1, 384_000) * 8);
        try
        {
            while (mono.Count < wanted && demuxer.ReadPacket(token) is { } packet)
            {
                using (packet)
                {
                    if (packet.TrackId == track.Id)
                    {
                        decoder.Decode(packet, decoded);
                    }
                }

                AddMono(decoded, mono, wanted);
            }

            if (mono.Count < wanted)
            {
                decoder.Drain(decoded);
            }

            AddMono(decoded, mono, wanted);
        }
        finally
        {
            foreach (var frame in decoded)
            {
                frame.Dispose();
            }
        }

        token.ThrowIfCancellationRequested();
        if (mono.Count == 0)
        {
            throw new MediaFormatException("The audio track gave no samples for its artwork.");
        }

        // Use the recording's own tags and name, not its parent folders. Temporary
        // library roots change on each launch and must not recolour the same song.
        var tags = demuxer.Info.Metadata;
        var artworkIdentity = string.Join("/",
            tags.GetValueOrDefault(MetadataKeys.Artist),
            tags.GetValueOrDefault(MetadataKeys.Album),
            tags.GetValueOrDefault(MetadataKeys.Title),
            Path.GetFileName(path));
        using var picture = AudioArtwork.Render([.. mono], sampleRate, artworkIdentity, options);
        using var output = new MemoryStream();
        PngWriter.Write(output, picture);
        return output.ToArray();
    }

    private static void AddMono(List<AudioFrame> frames, List<float> mono, int wanted)
    {
        foreach (var frame in frames)
        {
            using (frame)
            {
                for (var sample = 0; sample < frame.SampleCount && mono.Count < wanted; sample++)
                {
                    var sum = 0f;
                    for (var channel = 0; channel < frame.Channels; channel++)
                    {
                        sum += frame.Channel(channel)[sample];
                    }

                    mono.Add(sum / frame.Channels);
                }
            }
        }

        frames.Clear();
    }

}
