using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Rex.Media.AppCore.Player;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace Rex.Media.App;

/// <summary>
/// The camera, for the visualisations that use it (AU-18): its pictures read as they come, kept as
/// a small colour picture (for the beat edit) and turned into <see cref="SilhouetteMask"/>'s outline
/// of whoever is in front of it (for the silhouette). Nothing is recorded or kept: each picture
/// replaces the last, in memory, and goes when the camera closes. The camera is shared, so a call
/// app keeps it; it is on only while a visualisation that uses it shows.
/// <para>
/// Closing may come while the camera is still starting: whichever finishes second turns it off, so
/// the camera can never be left on behind the user's back.
/// </para>
/// </summary>
internal sealed partial class CameraFeed : IAsyncDisposable
{
    /// <summary>The outline's size.</summary>
    public const int MaskWidth = 160;
    public const int MaskHeight = 120;

    /// <summary>The colour picture's size: enough for the beat edit to fill the stage cleanly.</summary>
    public const int PictureWidth = 320;
    public const int PictureHeight = 240;

    private readonly SilhouetteMask _mask = new(MaskWidth, MaskHeight);
    private readonly byte[] _picture = new byte[PictureWidth * PictureHeight * 4];
    private readonly object _gate = new();
    private readonly SemaphoreSlim _lifetime = new(1, 1);
    private MediaCapture? _capture;
    private MediaFrameReader? _reader;
    private bool _closed;

    /// <summary>Whether the pictures are flipped left for right, as a mirror shows them.</summary>
    public bool Mirror { get; set; } = true;

    /// <summary>How different from the room a pixel must be to count as the person (0 to 1).</summary>
    public double Threshold { get; set; } = 0.12;

    /// <summary>Why the camera could not be used, for the window to say; null while it works.</summary>
    public string? Problem { get; private set; }

    /// <summary>Whether a picture has arrived yet.</summary>
    public bool HasPicture { get; private set; }

    public bool IsLearning
    {
        get
        {
            lock (_gate)
            {
                return _mask.IsLearning;
            }
        }
    }

    public async Task StartAsync()
    {
        await _lifetime.WaitAsync();
        try
        {
            if (_closed)
            {
                return;
            }

            var groups = await MediaFrameSourceGroup.FindAllAsync();
            var group = groups.FirstOrDefault(candidate => candidate.SourceInfos.Any(info => info.SourceKind == MediaFrameSourceKind.Color));
            if (group is null)
            {
                Problem = "No camera was found.";
                return;
            }

            _capture = new MediaCapture();
            await _capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                SourceGroup = group,
                SharingMode = MediaCaptureSharingMode.SharedReadOnly,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                StreamingCaptureMode = StreamingCaptureMode.Video,
            });
            var source = _capture.FrameSources.Values.First(candidate => candidate.Info.SourceKind == MediaFrameSourceKind.Color);
            _reader = await _capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8, new BitmapSize { Width = 640, Height = 480 });
            _reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
            _reader.FrameArrived += OnFrame;
            var status = await _reader.StartAsync();
            if (status != MediaFrameReaderStartStatus.Success)
            {
                Problem = status == MediaFrameReaderStartStatus.ExclusiveControlNotAvailable ? "Another app has the camera." : $"The camera did not start ({status}).";
            }
        }
        catch (UnauthorizedAccessException)
        {
            Problem = "Windows does not let apps use the camera. Turn it on in Settings, Privacy & security, Camera, \"Let desktop apps access your camera\".";
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            Problem = "The camera could not be opened: " + ex.Message;
        }
        finally
        {
            _lifetime.Release();
        }
    }

    /// <summary>The latest outline: 255 where the person is, <see cref="MaskWidth"/> by <see cref="MaskHeight"/>.</summary>
    public void CopyMask(byte[] into)
    {
        lock (_gate)
        {
            _mask.Mask.CopyTo(into, 0);
        }
    }

    /// <summary>The latest colour picture, BGRA, <see cref="PictureWidth"/> by <see cref="PictureHeight"/>.</summary>
    public void CopyPicture(byte[] into)
    {
        ArgumentNullException.ThrowIfNull(into);
        lock (_gate)
        {
            _picture.CopyTo(into, 0);
        }
    }

    /// <summary>Watches the room again, as when the camera has moved.</summary>
    public void Relearn()
    {
        lock (_gate)
        {
            _mask.Reset();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifetime.WaitAsync();
        try
        {
            _closed = true;
            if (_reader is { } reader)
            {
                reader.FrameArrived -= OnFrame;
                try
                {
                    await reader.StopAsync();
                }
                catch (Exception ex) when (ex is COMException or InvalidOperationException)
                {
                    Problem = "The camera could not stop normally: " + ex.Message;
                }
                finally
                {
                    reader.Dispose();
                }
            }

            _capture?.Dispose();
            (_reader, _capture) = (null, null);
            lock (_gate)
            {
                Array.Clear(_picture);
                _mask.Reset();
                HasPicture = false;
            }
        }
        finally
        {
            _lifetime.Release();
        }
    }

    private void OnFrame(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        try
        {
            ReadFrame(sender);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException)
        {
            Problem = "The camera frame could not be read: " + ex.Message;
        }
    }

    private void ReadFrame(MediaFrameReader sender)
    {
        using var frame = sender.TryAcquireLatestFrame();
        if (frame?.VideoMediaFrame?.SoftwareBitmap is not { } bitmap)
        {
            return;
        }

        using var converted = bitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8 ? null : SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8);
        var picture = converted ?? bitmap;
        var bytes = new byte[picture.PixelWidth * picture.PixelHeight * 4];
        picture.CopyToBuffer(bytes.AsBuffer());
        var mirror = Mirror;
        var brightness = SilhouetteMask.Brightness(bytes, picture.PixelWidth, picture.PixelHeight, picture.PixelWidth * 4, MaskWidth, MaskHeight, mirror);
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            Shrink(bytes, picture.PixelWidth, picture.PixelHeight, _picture, mirror);
            _mask.Update(brightness, Threshold);
            HasPicture = true;
        }
    }

    /// <summary>The camera's picture at <see cref="PictureWidth"/> by <see cref="PictureHeight"/>, each pixel the average of those it covers.</summary>
    private static void Shrink(byte[] source, int width, int height, byte[] target, bool mirror)
    {
        for (var y = 0; y < PictureHeight; y++)
        {
            var (top, bottom) = (y * height / PictureHeight, Math.Max((y * height / PictureHeight) + 1, (y + 1) * height / PictureHeight));
            for (var x = 0; x < PictureWidth; x++)
            {
                var (left, right) = (x * width / PictureWidth, Math.Max((x * width / PictureWidth) + 1, (x + 1) * width / PictureWidth));
                int b = 0, g = 0, r = 0, n = 0;
                for (var sy = top; sy < Math.Min(height, bottom); sy++)
                {
                    for (var sx = left; sx < Math.Min(width, right); sx++)
                    {
                        var from = ((sy * width) + sx) * 4;
                        (b, g, r, n) = (b + source[from], g + source[from + 1], r + source[from + 2], n + 1);
                    }
                }

                var to = ((y * PictureWidth) + (mirror ? PictureWidth - 1 - x : x)) * 4;
                n = Math.Max(1, n);
                (target[to], target[to + 1], target[to + 2], target[to + 3]) = ((byte)(b / n), (byte)(g / n), (byte)(r / n), 255);
            }
        }
    }
}
