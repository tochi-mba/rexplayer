using System.Runtime.Versioning;
using Rex.Media.Interop.Graphics;
using Rex.Media.Interop.Windowing;
using Rex.Media.Primitives;

namespace Rex.Media.Video.D3D11;

/// <summary>
/// Shows pictures with Direct3D 11: NV12 and P010 go to the graphics card as they are and are
/// converted there with the same <see cref="YuvTransform"/> snapshots use; other formats are
/// converted on the CPU first. Each picture is letterboxed to its display aspect. With a window or a
/// composition panel it presents to the screen; without either it draws offscreen, where
/// <see cref="ReadBack"/> sees it. The video thread presents while the window's thread resizes and
/// redraws, so every use of the device is under one lock.
/// </summary>
[SupportedOSPlatform("windows8.0")]
public sealed class D3D11Presenter : IVideoPresenter
{
    private readonly D3D11VideoRenderer _renderer;
    private readonly VideoWindow? _window;
    private readonly bool _onScreen;
    private readonly object _gate = new();
    private (int Width, int Height)? _pendingSize;
    private (float[] Matrix, int Width, int Height, Rational PixelAspect)? _last;
    private Rational? _aspect;
    private Rational? _crop;
    private PictureView _view = PictureView.Whole;
    private VideoLook _look = VideoLook.Original;
    private VideoEffect _effect = VideoEffect.Off;
    private float _effectStrength = 0.65f;
    private float _effectTime;
    private bool _navigator;

    private D3D11Presenter(D3D11VideoRenderer renderer, VideoWindow? window, bool onScreen)
    {
        _renderer = renderer;
        _window = window;
        _onScreen = onScreen;
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
        return new D3D11Presenter(renderer, window, onScreen: true);
    }

    /// <summary>
    /// Draws into a swap chain for a XAML swap-chain panel, <paramref name="width"/> by
    /// <paramref name="height"/> physical pixels. <paramref name="swapChain"/> is the swap chain's
    /// IUnknown with a reference the caller hands to the panel and then releases.
    /// </summary>
    public static D3D11Presenter ForComposition(int width, int height, out nint swapChain, bool software = false)
    {
        var renderer = D3D11VideoRenderer.Create(software);
        swapChain = renderer.AttachComposition(width, height);
        return new D3D11Presenter(renderer, null, onScreen: true);
    }

    /// <summary>Draws offscreen at this size, for tests and captures.</summary>
    public static D3D11Presenter Offscreen(int width, int height, bool software = true)
    {
        var renderer = D3D11VideoRenderer.Create(software);
        renderer.UseOffscreen(width, height);
        return new D3D11Presenter(renderer, null, onScreen: false);
    }

    /// <summary>
    /// Shows pictures stretched to <paramref name="aspect"/> and cut to <paramref name="crop"/> (null
    /// for the picture's own shape and all of it), from the next picture drawn (VID-05, VID-06).
    /// </summary>
    public void SetShape(Rational? aspect, Rational? crop)
    {
        lock (_gate)
        {
            (_aspect, _crop) = (aspect, crop);
        }
    }

    /// <summary>
    /// Shows <paramref name="view"/> of the picture (VID-07) and, when <paramref name="navigator"/>,
    /// the whole picture small in a corner, from the next picture drawn.
    /// </summary>
    public void SetView(PictureView view, bool navigator)
    {
        lock (_gate)
        {
            (_view, _navigator) = (view, navigator);
        }
    }

    /// <summary>Colour style applied by the GPU on the next frame or a paused-frame redraw.</summary>
    public void SetLook(VideoLook look)
    {
        lock (_gate)
        {
            _look = Enum.IsDefined(look) ? look : VideoLook.Original;
        }
    }

    /// <summary>Non-destructive shader effect and normalized strength, live and safe on paused frames.</summary>
    public void SetEffect(VideoEffect effect, int strength)
    {
        lock (_gate)
        {
            _effect = Enum.IsDefined(effect) ? effect : VideoEffect.Off;
            _effectStrength = VideoEffects.Strength(strength) / 100f;
        }
    }

    /// <summary>The panel's new size in physical pixels, applied before the next picture is drawn.</summary>
    public void Resize(int width, int height)
    {
        lock (_gate)
        {
            _pendingSize = (Math.Max(1, width), Math.Max(1, height));
        }
    }

    /// <summary>The display's scale (1.5 at 150 %), so a composition swap chain maps one buffer pixel to one screen pixel.</summary>
    public void SetScale(float scaleX, float scaleY)
    {
        lock (_gate)
        {
            _renderer.SetCompositionScale(scaleX, scaleY);
        }
    }

    /// <summary>
    /// Draws the last picture again at the current size (or black, before the first), for a window
    /// resized while paused, when no new picture is on its way.
    /// </summary>
    public void Redraw()
    {
        lock (_gate)
        {
            ApplySize();
            var (width, height) = _renderer.TargetSize;
            if (_last is { } last)
            {
                DrawShaped(last.Matrix, last.Width, last.Height, last.PixelAspect);
            }
            else
            {
                _renderer.Draw(new float[12], 0, 0, width, height, SmoothChroma);
            }

            _renderer.Present(waitForRefresh: false);
        }
    }

    /// <summary>Forgets the last picture and shows black, for media without pictures.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _last = null;
        }

        Redraw();
    }

    public void Present(VideoFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        lock (_gate)
        {
            ApplySize();
            Show(frame);
        }
    }

    private void ApplySize()
    {
        if (_window is { IsClosed: false } && _window.ClientSize is var size && size.Width > 0 && size.Height > 0)
        {
            _pendingSize = size;
        }

        if (_pendingSize is { } pending && pending != _renderer.TargetSize)
        {
            _renderer.Resize(pending.Width, pending.Height);
        }

        _pendingSize = null;
    }

    private void Show(VideoFrame frame)
    {
        _effectTime = frame.Pts.IsKnown ? (float)Math.Clamp(frame.Pts.TotalSeconds, 0, 1_000_000) : 0;
        var color = frame.Color.Resolve(frame.Width, frame.Height);
        if (frame.Surface is D3D11Surface surface)
        {
            var tenBit = frame.Format == PixelFormat.P010;
            _renderer.UploadSurface(surface, tenBit ? VideoPlaneFormat.P010 : VideoPlaneFormat.Nv12, frame.Width, frame.Height);
            Draw(frame, tenBit ? YuvTransform.For(color, 10).ForShader(65535.0 / 64) : YuvTransform.For(color, 8).ForShader(255));
            _renderer.Present(waitForRefresh: _onScreen);
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

        _renderer.Present(waitForRefresh: _onScreen);
    }

    /// <summary>The offscreen picture as drawn, as a BGRA frame the caller disposes.</summary>
    public VideoFrame ReadBack()
    {
        lock (_gate)
        {
            var (width, height) = _renderer.TargetSize;
            var frame = VideoFrame.Rent(PixelFormat.Bgra32, width, height);
            _renderer.ReadBack(frame.Plane(0), frame.Stride(0));
            return frame;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _renderer.Dispose();
            _window?.Dispose();
        }
    }

    private void Draw(VideoFrame frame, float[] matrix)
    {
        _last = (matrix, frame.Width, frame.Height, frame.PixelAspect);
        DrawShaped(matrix, frame.Width, frame.Height, frame.PixelAspect);
    }

    /// <summary>Draws the part of the picture the crop keeps, at the shape the aspect ratio and crop give it.</summary>
    private void DrawShaped(float[] matrix, int pictureWidth, int pictureHeight, Rational pixelAspect)
    {
        var (width, height) = _renderer.TargetSize;
        var (source, across, down) = VideoGeometry.Shape(pictureWidth, pictureHeight, pixelAspect, _aspect, _crop);
        var (x, y, fitWidth, fitHeight) = VideoLayout.FitAspect(across, down, width, height);
        var shown = _view.Within(source);
        _renderer.Draw(matrix, x, y, fitWidth, fitHeight, (shown.Left, shown.Top, shown.Right, shown.Bottom), SmoothChroma, look: (int)_look, effect: (int)_effect, strength: _effectStrength, seconds: _effectTime);
        if (_navigator && _view.IsZoomed)
        {
            var (navigatorX, navigatorY, navigatorWidth, navigatorHeight) = PictureView.Navigator(width, height, across, down);
            _renderer.Draw(matrix, (int)navigatorX, (int)navigatorY, Math.Max(1, (int)navigatorWidth), Math.Max(1, (int)navigatorHeight), (source.Left, source.Top, source.Right, source.Bottom), SmoothChroma, clear: false, look: (int)_look, effect: (int)_effect, strength: _effectStrength, seconds: _effectTime);
        }
    }
}
