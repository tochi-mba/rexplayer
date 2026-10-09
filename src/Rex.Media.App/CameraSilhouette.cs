using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Rex.Media.AppCore.Player;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace Rex.Media.App;

/// <summary>
/// The camera, for the silhouette visualisation (AU-18): small pictures, read as they come and
/// turned into <see cref="SilhouetteMask"/>'s outline of whoever is in front of it. Nothing is
/// recorded or kept; the camera is on only while this is, and is shared so a call app keeps it.
/// </summary>
internal sealed partial class CameraSilhouette : IAsyncDisposable
{
    public const int Width = 160;
    public const int Height = 120;

    private readonly SilhouetteMask _mask = new(Width, Height);
    private readonly object _gate = new();
    private MediaCapture? _capture;
    private MediaFrameReader? _reader;

    /// <summary>Whether the picture is flipped left for right, as a mirror shows it.</summary>
    public bool Mirror { get; set; } = true;

    /// <summary>How different from the room a pixel must be to count as the person (0 to 1).</summary>
    public double Threshold { get; set; } = 0.12;

    /// <summary>Why the camera could not be used, for the window to say; null while it works.</summary>
    public string? Problem { get; private set; }

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
        try
        {
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
            _reader = await _capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8, new BitmapSize { Width = 320, Height = 240 });
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
    }

    /// <summary>The latest outline: 255 where the person is.</summary>
    public void CopyMask(byte[] into)
    {
        lock (_gate)
        {
            _mask.Mask.CopyTo(into, 0);
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
        if (_reader is { } reader)
        {
            reader.FrameArrived -= OnFrame;
            await reader.StopAsync();
            reader.Dispose();
        }

        _capture?.Dispose();
        (_reader, _capture) = (null, null);
    }

    private void OnFrame(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
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
        var brightness = SilhouetteMask.Brightness(bytes, picture.PixelWidth, picture.PixelHeight, picture.PixelWidth * 4, Width, Height, Mirror);
        lock (_gate)
        {
            _mask.Update(brightness, Threshold);
        }
    }
}
