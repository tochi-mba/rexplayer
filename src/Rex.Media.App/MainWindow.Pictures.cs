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
using Rex.Media.Video;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace Rex.Media.App;

/// <summary>
/// A picture for every piece of media, wherever it is listed (the library, the playlist): Windows'
/// own thumbnail when it has a real one, otherwise a frame of the video itself, the picture itself,
/// or for music its cover, its folder's art or artwork drawn from its sound. Made pictures are kept
/// on disk until the file changes, and the latest few hundred in memory, so a list that redraws does
/// not make them again. At most two are made at once, each within a bound, away from the window.
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
            _ = ShowPictureAsync(row.Location, row.Kind, duration, picture => row.Picture = picture, CancellationToken.None);
        }
    }

    /// <summary>Finds or makes the picture for <paramref name="path"/>, and hands it to <paramref name="show"/> on the window's thread.</summary>
    private async Task ShowPictureAsync(string path, LibraryKind kind, TimeSpan? duration, Action<ImageSource> show, CancellationToken viewToken, bool urgent = false)
    {
        var key = PictureKey(path, kind);
        if (key is null)
        {
            return;
        }

        if (_pictureMemory.TryGetValue(key, out var known))
        {
            show(known);
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(viewToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var token = timeout.Token;
        var lane = urgent ? _priorityPictureSlot : _pictureSlots;
        var entered = false;
        try
        {
            await lane.WaitAsync(token);
            entered = true;
            if (_pictureMemory.TryGetValue(key, out known))
            {
                show(known);
                return;
            }

            // A local poster/cover belongs to the media. Prefer it over an arbitrary video frame.
            var poster = kind == LibraryKind.Video ? LocalVideoPoster(path) : null;
            var image = poster is null ? await ShellPictureAsync(path, kind, token)
                : await DecodePictureAsync(await File.ReadAllBytesAsync(poster, token), token);
            image ??= await DecodePictureAsync(await Task.Run(() => MakePicture(path, kind, duration, key, token), token), token);
            token.ThrowIfCancellationRequested();
            Remember(key, image);
            show(image);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A slow codec or a row that left the view keeps its placeholder; neither is an error.
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
    /// Use artwork the user put beside the video, without any network metadata lookups. A
    /// same-name poster takes precedence over folder artwork; no poster leaves video-frame
    /// thumbnails working exactly as before.
    /// </summary>
    private static string? LocalVideoPoster(string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (folder is null)
        {
            return null;
        }

        var stem = Path.GetFileNameWithoutExtension(path);
        foreach (var name in new[]
        {
            stem + ".poster.jpg", stem + ".jpg", stem + ".png", stem + ".webp",
            "poster.jpg", "poster.png", "folder.jpg", "cover.jpg",
        })
        {
            var candidate = Path.Combine(folder, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Windows' thumbnail, when it is a picture of the media: for formats Windows has no thumbnailer
    /// for (WebM, Matroska and others) it gives the file type's icon, which is no picture of it.
    /// </summary>
    private static async Task<ImageSource?> ShellPictureAsync(string path, LibraryKind kind, CancellationToken token)
    {
        var mode = kind switch
        {
            LibraryKind.Music => ThumbnailMode.MusicView,
            LibraryKind.Picture => ThumbnailMode.PicturesView,
            _ => ThumbnailMode.VideosView,
        };
        var size = kind == LibraryKind.Music ? 192u : 256u;
        using var thumbnail = await Task.Run(async () =>
        {
            var file = await StorageFile.GetFileFromPathAsync(path).AsTask(token);
            return await file.GetThumbnailAsync(mode, size, ThumbnailOptions.UseCurrentScale).AsTask(token);
        }, token);
        if (thumbnail is not { Size: > 0, Type: ThumbnailType.Image })
        {
            return null;
        }

        var image = new BitmapImage();
        await image.SetSourceAsync(thumbnail);
        return image;
    }

    /// <summary>
    /// Makes the picture with rexplayer's own decoders: a frame a fifth of the way into a video (at
    /// least a second, at most half a minute), a picture itself, or for music its folder's art or
    /// artwork drawn from its sound. Kept on disk under <paramref name="key"/> until the file changes.
    /// </summary>
    private byte[] MakePicture(string path, LibraryKind kind, TimeSpan? duration, string key, CancellationToken token)
    {
        var cache = Path.Combine(App.DataRoot, "cache", "library-pictures", key + ".png");
        if (File.Exists(cache))
        {
            return File.ReadAllBytes(cache);
        }

        if (kind == LibraryKind.Music && FolderArt.Find(path) is { } folderArt)
        {
            return File.ReadAllBytes(folderArt);
        }

        byte[] png;
        if (kind == LibraryKind.Music)
        {
            if (!_settings.GenerateAudioArtwork)
            {
                throw new NotSupportedException("Generated music artwork is turned off.");
            }

            png = AudioArtworkPng(path, new AudioArtworkOptions(
                _settings.AudioArtworkStyle,
                _settings.AudioArtworkColor,
                _settings.AudioArtworkDetail,
                _settings.AudioArtworkContrast,
                _settings.AudioArtworkUsesIdentity), token);
        }
        else
        {
            var at = kind == LibraryKind.Picture || duration is not { } length
                ? TimeSpan.Zero
                : TimeSpan.FromSeconds(Math.Clamp(length.TotalSeconds * 0.2, Math.Min(1, length.TotalSeconds / 2), 30));
            png = PreviewPng(path, at, token);
        }

        AtomicFile.Write(cache, png);
        return png;
    }

    /// <summary>
    /// Names a picture by the file's path, size and time of change (and, for music, the artwork
    /// settings), so a changed file gets a new one; null when the file is not there.
    /// </summary>
    private string? PictureKey(string path, LibraryKind kind)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            return null;
        }

        var art = kind == LibraryKind.Music
            ? $"\n{_settings.AudioArtworkStyle}\n{_settings.AudioArtworkColor}\n{_settings.AudioArtworkDetail}\n{_settings.AudioArtworkContrast}\n{_settings.AudioArtworkUsesIdentity}"
            : "";
        // Replacing local artwork must invalidate its thumbnail without touching the video file.
        if (kind == LibraryKind.Video && LocalVideoPoster(path) is { } local)
        {
            var posterInfo = new FileInfo(local);
            art += $"\n{local}\n{posterInfo.Length}\n{posterInfo.LastWriteTimeUtc.Ticks}";
        }
        var identity = Encoding.UTF8.GetBytes($"4\n{path}\n{file.Length}\n{file.LastWriteTimeUtc.Ticks}{art}");
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

    private static async Task<ImageSource> DecodePictureAsync(byte[] bytes, CancellationToken token)
    {
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            writer.DetachStream();
        }

        token.ThrowIfCancellationRequested();
        stream.Seek(0);
        var image = new BitmapImage { DecodePixelWidth = 320 };
        await image.SetSourceAsync(stream);
        return image;
    }

    private static byte[] AudioArtworkPng(string path, AudioArtworkOptions options, CancellationToken token)
    {
        using var source = new FileByteSource(path);
        using var demuxer = MediaRegistries.Demuxers().Open(source, token);
        var track = demuxer.Info.FirstTrack(MediaKind.Audio) ?? throw new NotSupportedException("The file has no sound to draw.");
        var opened = MediaRegistries.Decoders(new MfDecoderFactory()).CreateAudio(track);
        using var decoder = opened.Decoder ?? throw new NotSupportedException(opened.Reason);
        var decoded = new List<AudioFrame>();
        var mono = new List<float>(96_000);
        var sampleRate = track.Audio!.SampleRate;
        var wanted = sampleRate * 8;
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

        decoder.Drain(decoded);
        AddMono(decoded, mono, wanted);
        if (mono.Count == 0)
        {
            throw new MediaFormatException("The audio track gave no samples for its artwork.");
        }

        using var picture = AudioArtwork.Render([.. mono], sampleRate, path, options);
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
