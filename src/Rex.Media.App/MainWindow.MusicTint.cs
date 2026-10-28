using Rex.Media.AppCore.Design;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Media;
using Rex.Media.AppCore.Player;
using Rex.Media.AppCore.Visuals;
using Rex.Media.Library;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace Rex.Media.App;

/// <summary>
/// The colour a music page's header is tinted with, taken from its cover or photo. The picture is
/// decoded at 24 by 24 pixels and <see cref="DominantColour"/> chooses, all on a worker thread; each
/// picture's answer is kept for the session, so a page opened again is tinted at once.
/// </summary>
public sealed partial class MainWindow
{
    private const int TintSide = 24;

    private readonly Dictionary<string, Tint> _tints = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The header tint for <paramref name="art"/>'s artwork (or <paramref name="photo"/>, an artist's
    /// fetched photo, when given); the fallback slate when no picture can be read.
    /// </summary>
    private async Task<Tint> HeaderTintAsync(LibraryEntry? art, string? photo, CancellationToken token)
    {
        var key = photo ?? art?.Path;
        if (key is null)
        {
            return DominantColour.Fallback;
        }

        lock (_tints)
        {
            if (_tints.TryGetValue(key, out var known))
            {
                return known;
            }
        }

        var settings = _settings;
        var tint = await Task.Run(async () =>
        {
            try
            {
                using var stream = await TintSourceAsync(art, photo, settings, token).ConfigureAwait(false);
                if (stream is null)
                {
                    return DominantColour.Fallback;
                }

                var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(token).ConfigureAwait(false);
                var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
                    new BitmapTransform { ScaledWidth = TintSide, ScaledHeight = TintSide, InterpolationMode = BitmapInterpolationMode.Fant },
                    ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(token).ConfigureAwait(false);
                return DominantColour.ForHeader(DominantColour.Pick(pixels.DetachPixelData()));
            }
            catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
            {
                App.Log.Debug(LogSource, $"No header colour for {key}: {ex.Message}");
                return DominantColour.Fallback;
            }
        }, token);

        lock (_tints)
        {
            _tints[key] = tint;
        }

        return tint;
    }

    /// <summary>The picture to read: the photo, the cover chosen or beside the files or fetched, the thumbnail made before, or the file's own art.</summary>
    private async Task<IRandomAccessStream?> TintSourceAsync(LibraryEntry? art, string? photo, Rex.Media.Settings.PlayerSettings settings, CancellationToken token)
    {
        var file = photo;
        if (file is null && art is { Kind: LibraryKind.Music })
        {
            file = ChosenCoverPath(art.Path) ?? (settings.Metadata.ReplaceCovers
                ? FetchedCoverPath(art.Path) ?? FolderArt.Find(art.Path)
                : FolderArt.Find(art.Path) ?? FetchedCoverPath(art.Path));
            if (file is null && PictureKey(art.Path, art.Kind, settings, null) is { } key && File.Exists(PictureCachePath(key)))
            {
                file = PictureCachePath(key);
            }
        }

        if (file is not null)
        {
            var stored = await StorageFile.GetFileFromPathAsync(file).AsTask(token).ConfigureAwait(false);
            return await stored.OpenReadAsync().AsTask(token).ConfigureAwait(false);
        }

        if (art is null || !File.Exists(art.Path))
        {
            return null;
        }

        var media = await StorageFile.GetFileFromPathAsync(art.Path).AsTask(token).ConfigureAwait(false);
        var thumbnail = await media.GetThumbnailAsync(ThumbnailMode.MusicView, 64, ThumbnailOptions.UseCurrentScale).AsTask(token).ConfigureAwait(false);
        return thumbnail is { Type: ThumbnailType.Image } ? thumbnail : null;
    }

    /// <summary>
    /// A music hero's plate for a tint: the colour deepened only as far as light words need
    /// (<see cref="Legibility"/>), a shade deeper at the bottom. It never fades into the page, which
    /// is light in a light window, so every word on it reads in either theme.
    /// </summary>
    private static Brush PlateBrush(Tint tint)
    {
        var plate = Legibility.Toward(Argb(tint), DesignTokens.DarkInk[0], Legibility.LightWords, Legibility.BodyContrast);
        var deeper = Legibility.Over(DesignTokens.WithAlpha(DesignTokens.DarkInk[0], 0x66), plate);
        var brush = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(0, 1) };
        brush.GradientStops.Add(new GradientStop { Color = RexTheme.Color(plate), Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = RexTheme.Color(deeper), Offset = 1 });
        return brush;
    }
}
