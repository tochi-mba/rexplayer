using System.Runtime.Versioning;
using Rex.Media.Interop.Graphics;
using Rex.Media.Interop.Windowing;
using Rex.Media.Primitives;

namespace Rex.Media.Video.D3D11;

/// <summary>
/// Shows pictures with Direct3D 11: NV12 and P010 go to the graphics card as they are and are
/// converted there with the same <see cref="YuvTransform"/> snapshots use; other formats are
/// converted on the CPU first. Each picture is letterboxed to its display aspect. With a window it
/// presents to the screen; without one it draws offscreen, where <see cref="ReadBack"/> sees it.
/// </summary>
[SupportedOSPlatform("windows8.0")]
public sealed class D3D11Presenter : IVideoPresenter
{
    private readonly D3D11VideoRenderer _renderer;
    private readonly VideoWindow? _window;

    private D3D11Presenter(D3D11VideoRenderer renderer, VideoWindow? window)
    {
        _renderer = renderer;
        _window = window;
    }

    /// <summary>Interpolate chroma between samples (smoother on screen), or take the nearest (a still of the coded picture).</summary>
    public bool SmoothChroma { get; set; } = true;

    public string Name => _renderer.IsSoftware ? "Direct3D 11 (software)" : "Direct3D 11";

    /// <summary>The device, so a decoder on the graphics card can leave its pictures where they are drawn.</summary>
    public object? Gpu => _renderer.Gpu;

    /// <summary>Draws into <paramref name="window"/>, which the presenter then owns.</summary>
    public static D3D11Presenter ForWindow(VideoWindow window, bool software = false)
    {
        ArgumentNullException.ThrowIfNull(window);
        var renderer = D3D11VideoRenderer.Create(software);
        var (width, height) = window.ClientSize;
        renderer.AttachWindow(window.Handle, width, height);
        return new D3D11Presenter(renderer, window);
    }

    /// <summary>Draws offscreen at this size, for tests and captures.</summary>
    public static D3D11Presenter Offscreen(int width, int height, bool software = true)
    {
        var renderer = D3D11VideoRenderer.Create(software);
        renderer.UseOffscreen(width, height);
        return new D3D11Presenter(renderer, null);
    }

    public void Present(VideoFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (_window is { IsClosed: false } && _window.ClientSize is var size && size != _renderer.TargetSize && size.Width > 0 && size.Height > 0)
        {
            _renderer.Resize(size.Width, size.Height);
        }

        var color = frame.Color.Resolve(frame.Width, frame.Height);
        if (frame.Surface is D3D11Surface surface)
        {
            var tenBit = frame.Format == PixelFormat.P010;
            _renderer.UploadSurface(surface, tenBit ? VideoPlaneFormat.P010 : VideoPlaneFormat.Nv12, frame.Width, frame.Height);
            Draw(frame, tenBit ? YuvTransform.For(color, 10).ForShader(65535.0 / 64) : YuvTransform.For(color, 8).ForShader(255));
            _renderer.Present(waitForRefresh: _window is not null);
            return;
        }

        switch (frame.Format)
        {
            case PixelFormat.Nv12:
                _renderer.Upload(VideoPlaneFormat.Nv12, frame.Width, frame.Height, frame.Plane(0), frame.Stride(0), frame.Plane(1), frame.Stride(1));
                Draw(frame, YuvTransform.For(color, 8).ForShader(255));
                break;
            case PixelFormat.P010:
                _renderer.Upload(VideoPlaneFormat.P010, frame.Width, frame.Height, frame.Plane(0), frame.Stride(0), frame.Plane(1), frame.Stride(1));
                Draw(frame, YuvTransform.For(color, 10).ForShader(65535.0 / 64));
                break;
            case PixelFormat.Bgra32:
                _renderer.Upload(VideoPlaneFormat.Bgra, frame.Width, frame.Height, frame.Plane(0), frame.Stride(0), default, 0);
                Draw(frame, new float[12]);
                break;
            default:
                using (var bgra = ColorConverter.ToBgra(frame))
                {
                    _renderer.Upload(VideoPlaneFormat.Bgra, bgra.Width, bgra.Height, bgra.Plane(0), bgra.Stride(0), default, 0);
                    Draw(bgra, new float[12]);
                }

                break;
        }

        _renderer.Present(waitForRefresh: _window is not null);
    }

    /// <summary>The offscreen picture as drawn, as a BGRA frame the caller disposes.</summary>
    public VideoFrame ReadBack()
    {
        var (width, height) = _renderer.TargetSize;
        var frame = VideoFrame.Rent(PixelFormat.Bgra32, width, height);
        _renderer.ReadBack(frame.Plane(0), frame.Stride(0));
        return frame;
    }

    public void Dispose()
    {
        _renderer.Dispose();
        _window?.Dispose();
    }

    private void Draw(VideoFrame frame, float[] matrix)
    {
        var (width, height) = _renderer.TargetSize;
        var (x, y, fitWidth, fitHeight) = VideoLayout.Fit(frame.Width, frame.Height, frame.PixelAspect, width, height);
        _renderer.Draw(matrix, x, y, fitWidth, fitHeight, SmoothChroma);
    }
}
