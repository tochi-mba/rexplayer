using System.Runtime.InteropServices;
using AVFoundation;
using CoreFoundation;
using CoreMedia;
using CoreVideo;
using Rex.Media.AppCore.Player;

namespace Rex.Media.App.Mac;

/// <summary>Live, in-memory camera pictures for the camera music scenes (AU-18).</summary>
internal sealed class MacCameraFeed : AVCaptureVideoDataOutputSampleBufferDelegate
{
    public const int PictureWidth = 320;
    public const int PictureHeight = 240;
    public const int MaskWidth = 240;
    public const int MaskHeight = 180;

    private readonly object _gate = new();
    private readonly byte[] _picture = new byte[PictureWidth * PictureHeight * 4];
    private readonly SilhouetteMask _mask = new(MaskWidth, MaskHeight);
    private readonly DispatchQueue _queue = new("rexplayer.camera.visualisations");
    private AVCaptureSession? _session;
    private AVCaptureDeviceInput? _input;
    private AVCaptureVideoDataOutput? _output;
    private bool _closed;

    public bool HasPicture { get; private set; }
    public bool IsLearning { get { lock (_gate) return _mask.IsLearning; } }
    public string? Problem { get; private set; }
    public double Threshold { get; set; } = 0.12;

    public void Start()
    {
        try
        {
            var device = AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video);
            if (device is null) { Problem = "No camera was found."; return; }
            _input = AVCaptureDeviceInput.FromDevice(device, out var error);
            if (_input is null) { Problem = error?.LocalizedDescription ?? "The camera could not be opened."; return; }
            _output = new AVCaptureVideoDataOutput
            {
                AlwaysDiscardsLateVideoFrames = true,
                WeakVideoSettings = new NSDictionary(
                    CVPixelBuffer.PixelFormatTypeKey,
                    NSNumber.FromUInt32((uint)CVPixelFormatType.CV32BGRA)),
            };
            _output.SetSampleBufferDelegate(this, _queue);
            _session = new AVCaptureSession { SessionPreset = AVCaptureSession.Preset640x480 };
            _session.AddInput(_input);
            _session.AddOutput(_output);
            _session.StartRunning();
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            Problem = "The camera could not be opened: " + ex.Message;
        }
    }

    public override void DidOutputSampleBuffer(AVCaptureOutput captureOutput, CMSampleBuffer sampleBuffer,
        AVCaptureConnection connection)
    {
        using var buffer = sampleBuffer.GetImageBuffer() as CVPixelBuffer;
        if (buffer is null || _closed) return;
        buffer.Lock(CVPixelBufferLock.ReadOnly);
        try
        {
            var width = (int)buffer.Width;
            var height = (int)buffer.Height;
            var stride = (int)buffer.BytesPerRow;
            var bytes = new byte[checked(stride * height)];
            Marshal.Copy(buffer.BaseAddress, bytes, 0, bytes.Length);
            lock (_gate)
            {
                if (_closed) return;
                Shrink(bytes, width, height, stride, _picture);
                _mask.UpdateColour(bytes, width, height, Threshold, mirror: true);
                HasPicture = true;
            }
        }
        finally
        {
            buffer.Unlock(CVPixelBufferLock.ReadOnly);
        }
    }

    public void CopyPicture(byte[] target) { lock (_gate) _picture.CopyTo(target, 0); }
    public void CopyMask(byte[] target) { lock (_gate) _mask.Mask.CopyTo(target, 0); }
    public void Relearn() { lock (_gate) _mask.Reset(); }

    private static void Shrink(byte[] source, int width, int height, int stride, byte[] target)
    {
        for (var y = 0; y < PictureHeight; y++)
        {
            var sy = y * height / PictureHeight;
            for (var x = 0; x < PictureWidth; x++)
            {
                var sx = x * width / PictureWidth;
                var from = sy * stride + sx * 4;
                var to = (y * PictureWidth + (PictureWidth - 1 - x)) * 4;
                source.AsSpan(from, 4).CopyTo(target.AsSpan(to, 4));
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_closed)
        {
            _closed = true;
            _session?.StopRunning();
            _output?.SetSampleBufferDelegate(null, null);
            _output?.Dispose();
            _input?.Dispose();
            _session?.Dispose();
            _queue.Dispose();
        }
        base.Dispose(disposing);
    }
}
