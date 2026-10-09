using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Rex.Media.AppCore;
using Rex.Media.Codecs.MediaFoundation;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.Video;
using Windows.Storage.Streams;

namespace Rex.Media.App;

/// <summary>The optional, exact-frame preview above the timeline.</summary>
public sealed partial class MainWindow
{
    private CancellationTokenSource _seekPreviewWork = new();
    private string? _seekPreviewKey;
    private byte[]? _seekPreviewPng;

    private void OnSeekAreaPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_settings.SeekPreview || !_player.CanSeek || _player.Duration <= TimeSpan.Zero)
        {
            HideSeekPreview();
            return;
        }

        var x = Math.Clamp(e.GetCurrentPoint(SeekArea).Position.X, 0, SeekArea.ActualWidth);
        var fraction = SeekArea.ActualWidth > 0 ? x / SeekArea.ActualWidth : 0;
        var at = TimeSpan.FromTicks((long)(_player.Duration.Ticks * fraction));
        var left = Math.Clamp(x - (SeekPreview.Width / 2), 0, Math.Max(0, SeekArea.ActualWidth - SeekPreview.Width));
        SeekPreview.Margin = new Thickness(left, 0, 0, 30);
        SeekPreviewTime.Text = TimeText.Format(at, _player.Duration);
        SeekPreview.Visibility = Visibility.Visible;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SeekPreview, $"Timeline preview at {SeekPreviewTime.Text}");

        _seekPreviewWork.Cancel();
        _seekPreviewWork.Dispose();
        _seekPreviewWork = new CancellationTokenSource();
        var token = _seekPreviewWork.Token;
        var location = _player.Item?.Location;
        if (!HasVideo || location is null || !File.Exists(location))
        {
            SeekPreviewPicture.Visibility = Visibility.Collapsed;
            return;
        }

        // Half-second buckets keep tiny pointer movements from decoding the same picture again.
        var bucket = Math.Round(at.TotalSeconds * 2, MidpointRounding.AwayFromZero) / 2;
        var key = location + "@" + bucket.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (key == _seekPreviewKey && _seekPreviewPng is { } cached)
        {
            _ = SetSeekPreviewImageAsync(cached, token);
            return;
        }

        SeekPreviewPicture.Visibility = Visibility.Collapsed;
        _ = LoadSeekPreviewAsync(location, TimeSpan.FromSeconds(bucket), key, token);
    }

    private void OnSeekAreaPointerExited(object sender, PointerRoutedEventArgs e) => HideSeekPreview();

    private void HideSeekPreview()
    {
        _seekPreviewWork.Cancel();
        SeekPreview.Visibility = Visibility.Collapsed;
    }

    private async Task LoadSeekPreviewAsync(string location, TimeSpan at, string key, CancellationToken token)
    {
        try
        {
            await Task.Delay(180, token);
            var png = await Task.Run(() => PreviewPng(location, at, token), token);
            token.ThrowIfCancellationRequested();
            _seekPreviewKey = key;
            _seekPreviewPng = png;
            await SetSeekPreviewImageAsync(png, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Moving on invalidates the old request; it must never replace the newer preview.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or MediaFormatException or NotSupportedException or System.Runtime.InteropServices.COMException)
        {
            App.Log.Debug(LogSource, $"No timeline preview for {location}: {ex.Message}");
        }
    }

    private byte[] PreviewPng(string location, TimeSpan at, CancellationToken token)
    {
        using var source = new FileByteSource(location);
        using var demuxer = MediaRegistries.Demuxers(TimeSpan.FromSeconds(_settings.PictureSeconds)).Open(source, token);
        var (picture, _) = Snapshot.Take(demuxer, MediaRegistries.Decoders(new MfDecoderFactory(), new WicDecoderFactory()), new MediaTime(at.Ticks), token);
        using (picture)
        using (var output = new MemoryStream())
        {
            PngWriter.Write(output, picture);
            return output.ToArray();
        }
    }

    private async Task SetSeekPreviewImageAsync(byte[] png, CancellationToken token)
    {
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(png);
            await writer.StoreAsync();
            writer.DetachStream();
        }

        token.ThrowIfCancellationRequested();
        stream.Seek(0);
        var image = new BitmapImage();
        await image.SetSourceAsync(stream);
        token.ThrowIfCancellationRequested();
        SeekPreviewImage.Source = image;
        SeekPreviewPicture.Visibility = Visibility.Visible;
    }
}
