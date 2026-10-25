using CoreAnimation;
using Metal;
using Rex.Media.Primitives;

namespace Rex.Media.Video.Metal;

/// <summary>
/// Shows pictures on a Mac: NV12 or P010 planes uploaded as Metal textures and turned into RGB by
/// one fragment shader with the rows <see cref="YuvTransform"/> gives (the same rows Windows'
/// Direct3D shader uses, so both platforms show the same colours), placed by
/// <see cref="VideoLayout.Fit"/> with black bars (the same placement). The engine calls
/// <see cref="Present"/> on its video thread; the window calls <see cref="Resize"/> and
/// <see cref="Redraw"/> on its own. Picture looks and effects follow (docs/parity.md).
/// </summary>
public sealed class MetalPresenter : IVideoPresenter
{
    private const string Shaders = """
        #include <metal_stdlib>
        using namespace metal;

        struct Placed { float4 position [[position]]; float2 uv; };
        struct Colour { float4 rowR; float4 rowG; float4 rowB; };

        vertex Placed placeQuad(uint corner [[vertex_id]], constant float4& quad [[buffer(0)]],
            constant float4& uvRect [[buffer(1)]]) {
            float2 at = float2(corner & 1, corner >> 1);
            Placed placed;
            placed.position = float4(quad.x + at.x * quad.z, quad.y - at.y * quad.w, 0, 1);
            placed.uv = uvRect.xy + at * uvRect.zw;
            return placed;
        }

        fragment float4 showYuv(Placed in [[stage_in]], texture2d<float> luma [[texture(0)]],
            texture2d<float> chroma [[texture(1)]], sampler linear [[sampler(0)]], constant Colour& colour [[buffer(0)]]) {
            float4 sample = float4(luma.sample(linear, in.uv).r, chroma.sample(linear, in.uv).rg, 1);
            return float4(dot(colour.rowR, sample), dot(colour.rowG, sample), dot(colour.rowB, sample), 1);
        }
        """;

    private readonly object _gate = new();
    private readonly CAMetalLayer _layer;
    private readonly IMTLDevice _device;
    private readonly IMTLCommandQueue _queue;
    private readonly IMTLRenderPipelineState _pipeline;
    private readonly IMTLSamplerState _sampler;
    private IMTLTexture? _luma;
    private IMTLTexture? _chroma;
    private PixelFormat _format;
    private (int Width, int Height) _picture;
    private Rational _aspect = new(1, 1);
    private float[] _rows = [];
    private (ColorInfo Color, PixelFormat Format) _rowsFor;
    private (int Width, int Height) _target;
    private long _drawn;
    private double? _displayAspect;
    private double? _cropAspect;
    private double _zoom = 1;
    private double _panX;
    private double _panY;

    private MetalPresenter(CAMetalLayer layer, IMTLDevice device)
    {
        _layer = layer;
        _device = device;
        _queue = device.CreateCommandQueue() ?? throw new InvalidOperationException("Metal gave no command queue.");
        var library = device.CreateLibrary(Shaders, new MTLCompileOptions(), out var error)
            ?? throw new InvalidOperationException("The picture shader did not compile: " + error?.LocalizedDescription);
        var description = new MTLRenderPipelineDescriptor
        {
            VertexFunction = library.CreateFunction("placeQuad"),
            FragmentFunction = library.CreateFunction("showYuv"),
        };
        description.ColorAttachments[0]!.PixelFormat = layer.PixelFormat;
        _pipeline = device.CreateRenderPipelineState(description, out error)
            ?? throw new InvalidOperationException("The picture pipeline could not be made: " + error?.LocalizedDescription);
        _sampler = device.CreateSamplerState(new MTLSamplerDescriptor
        {
            MinFilter = MTLSamplerMinMagFilter.Linear,
            MagFilter = MTLSamplerMinMagFilter.Linear,
            SAddressMode = MTLSamplerAddressMode.ClampToEdge,
            TAddressMode = MTLSamplerAddressMode.ClampToEdge,
        }) ?? throw new InvalidOperationException("Metal gave no sampler.");
    }

    /// <summary>A presenter drawing into <paramref name="layer"/>, or null on a Mac without Metal.</summary>
    public static MetalPresenter? ForLayer(CAMetalLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        var device = MTLDevice.SystemDefault;
        if (device is null)
        {
            return null;
        }

        layer.Device = device;
        layer.PixelFormat = MTLPixelFormat.BGRA8Unorm;
        layer.FramebufferOnly = true;
        return new MetalPresenter(layer, device);
    }

    public string Name => "Metal (" + _device.Name + ")";

    /// <summary>Pictures shown so far, for the window's statistics and its tests.</summary>
    public long PicturesDrawn => Interlocked.Read(ref _drawn);

    /// <summary>The layer's new size in pixels; the next picture, or <see cref="Redraw"/>, fills it.</summary>
    public void Resize(int width, int height)
    {
        lock (_gate)
        {
            _target = (Math.Max(width, 1), Math.Max(height, 1));
            _layer.DrawableSize = new CoreGraphics.CGSize(_target.Width, _target.Height);
        }
    }

    /// <summary>
    /// Controls display aspect, symmetric source crop and zoom/pan without resampling decoded frames.
    /// Normalized pan [-1,1] reaches each edge of the cropped source; all updates redraw the last picture.
    /// </summary>
    public void SetViewGeometry(double? aspect, double? crop, double zoom, double panX, double panY)
    {
        lock (_gate)
        {
            _displayAspect = aspect is > 0 && double.IsFinite(aspect.Value) ? aspect : null;
            _cropAspect = crop is > 0 && double.IsFinite(crop.Value) ? crop : null;
            _zoom = double.IsFinite(zoom) ? Math.Clamp(zoom, 1, 8) : 1;
            _panX = double.IsFinite(panX) ? Math.Clamp(panX, -1, 1) : 0;
            _panY = double.IsFinite(panY) ? Math.Clamp(panY, -1, 1) : 0;
            Draw();
        }
    }

    public void Present(VideoFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Format is not (PixelFormat.Nv12 or PixelFormat.P010) || frame.Surface is not null)
        {
            return;
        }

        lock (_gate)
        {
            Upload(frame);
            Draw();
        }
    }

    /// <summary>Draws the last picture again, as after a resize while paused.</summary>
    public void Redraw()
    {
        lock (_gate)
        {
            Draw();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _luma?.Dispose();
            _chroma?.Dispose();
            _luma = _chroma = null;
        }
    }

    private void Upload(VideoFrame frame)
    {
        var tenBit = frame.Format == PixelFormat.P010;
        if (_luma is null || _format != frame.Format || _picture != (frame.Width, frame.Height))
        {
            _luma?.Dispose();
            _chroma?.Dispose();
            _luma = Texture(tenBit ? MTLPixelFormat.R16Unorm : MTLPixelFormat.R8Unorm, frame.Width, frame.Height);
            _chroma = Texture(tenBit ? MTLPixelFormat.RG16Unorm : MTLPixelFormat.RG8Unorm, (frame.Width + 1) / 2, (frame.Height + 1) / 2);
            (_format, _picture) = (frame.Format, (frame.Width, frame.Height));
        }

        Fill(_luma, frame, 0);
        Fill(_chroma!, frame, 1);
        _aspect = frame.PixelAspect;
        if (_rowsFor != (frame.Color, frame.Format))
        {
            // A texel of 1.0 is 255 in 8-bit samples, and 65535 / 64 for P010's ten bits at the top of sixteen.
            _rows = YuvTransform.For(frame.Color, tenBit ? 10 : 8).ForShader(tenBit ? 65535.0 / 64 : 255);
            _rowsFor = (frame.Color, frame.Format);
        }
    }

    private IMTLTexture Texture(MTLPixelFormat format, int width, int height) =>
        _device.CreateTexture(MTLTextureDescriptor.CreateTexture2DDescriptor(format, (nuint)width, (nuint)height, mipmapped: false))
            ?? throw new InvalidOperationException("Metal gave no texture for the picture.");

    private static unsafe void Fill(IMTLTexture texture, VideoFrame frame, int plane)
    {
        var (_, rows) = frame.Format.PlaneSize(plane, frame.Width, frame.Height);
        var bytes = frame.Plane(plane);
        fixed (byte* start = bytes)
        {
            texture.ReplaceRegion(MTLRegion.Create2D(0, 0, texture.Width, (nuint)rows), 0, (IntPtr)start, (nuint)frame.Stride(plane));
        }
    }

    private void Draw()
    {
        if (_luma is null || _chroma is null || _target.Width == 0 || _layer.NextDrawable() is not { } drawable)
        {
            return;
        }

        var sourceAspect = _picture.Width * (_aspect.Numerator > 0 ? _aspect.Value : 1) / _picture.Height;
        var cropU = 1.0;
        var cropV = 1.0;
        if (_cropAspect is { } cropped)
        {
            if (cropped > sourceAspect)
            {
                cropV = sourceAspect / cropped;
            }
            else
            {
                cropU = cropped / sourceAspect;
            }
        }

        // Crop changes the displayed content's natural aspect; an explicit aspect overrides it.
        var displayAspect = _displayAspect ?? (sourceAspect * cropU / cropV);
        var placed = VideoLayout.FitAspect((long)Math.Round(displayAspect * 100000),
            100000, _target.Width, _target.Height);
        var (left, top, width, height) = VideoLayout.ToClipSpace(placed, _target.Width, _target.Height);
        var visibleU = cropU / _zoom;
        var visibleV = cropV / _zoom;
        var u = (1.0 - cropU) / 2 + (cropU - visibleU) * (1 + _panX) / 2;
        var v = (1.0 - cropV) / 2 + (cropV - visibleV) * (1 + _panY) / 2;
        var pass = new MTLRenderPassDescriptor();
        pass.ColorAttachments[0].Texture = drawable.Texture;
        pass.ColorAttachments[0].LoadAction = MTLLoadAction.Clear;
        pass.ColorAttachments[0].ClearColor = new MTLClearColor(0, 0, 0, 1);
        pass.ColorAttachments[0].StoreAction = MTLStoreAction.Store;
        var commands = _queue.CommandBuffer() ?? throw new InvalidOperationException("Metal gave no command buffer.");
        var encoder = commands.CreateRenderCommandEncoder(pass);
        encoder.SetRenderPipelineState(_pipeline);
        unsafe
        {
            var quad = stackalloc float[] { left, top, width, height };
            encoder.SetVertexBytes((IntPtr)quad, sizeof(float) * 4, 0);
            var uv = stackalloc float[] { (float)u, (float)v, (float)visibleU, (float)visibleV };
            encoder.SetVertexBytes((IntPtr)uv, sizeof(float) * 4, 1);
            fixed (float* rows = _rows)
            {
                encoder.SetFragmentBytes((IntPtr)rows, (nuint)(sizeof(float) * _rows.Length), 0);
            }
        }

        encoder.SetFragmentTexture(_luma, 0);
        encoder.SetFragmentTexture(_chroma, 1);
        encoder.SetFragmentSamplerState(_sampler, 0);
        encoder.DrawPrimitives(MTLPrimitiveType.TriangleStrip, 0, 4);
        encoder.EndEncoding();
        commands.PresentDrawable(drawable);
        commands.Commit();
        Interlocked.Increment(ref _drawn);
    }
}
