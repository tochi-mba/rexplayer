using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.Dxgi;
using Windows.Win32.Graphics.Dxgi.Common;

namespace Rex.Media.Interop.Graphics;

/// <summary>How a picture's planes are laid out for upload.</summary>
public enum VideoPlaneFormat
{
    /// <summary>8-bit luma, then interleaved 8-bit Cb and Cr at half size.</summary>
    Nv12,

    /// <summary>NV12 with 16-bit samples holding 10 bits in the top.</summary>
    P010,

    /// <summary>One plane of packed 8-bit blue, green, red, alpha.</summary>
    Bgra,
}

/// <summary>
/// Draws pictures with Direct3D 11 into a window's flip-model swap chain, or into an offscreen
/// texture that can be read back (how tests and CI see what would be on screen). Call sequencing
/// and COM plumbing only (ADR-008): the colour matrix, where the picture goes and when to draw are
/// decided by the presenter in Rex.Media.Video.D3D11.
/// </summary>
[SupportedOSPlatform("windows8.0")]
public sealed unsafe class D3D11VideoRenderer : IDisposable
{
    /// <summary>
    /// A full-window triangle from the vertex index, and two pixel shaders: YUV planes through the
    /// colour matrix rows (each a dot product with the samples and 1), and BGRA as it is. The source
    /// rectangle picks the part of the picture shown (a crop preset), and the crop scales texture
    /// coordinates when a decoder's surface is larger than the picture it holds.
    /// </summary>
    private const string Shaders = """
        cbuffer Colour : register(b0) { float4 rowR; float4 rowG; float4 rowB; float4 crop; float4 source; float4 look; };
        Texture2D luma : register(t0);
        Texture2D chroma : register(t1);
        SamplerState linearClamp : register(s0);
        SamplerState chromaSampler : register(s1);
        struct Vertex { float4 position : SV_Position; float2 uv : TEXCOORD0; };
        Vertex vs(uint id : SV_VertexID)
        {
            Vertex output;
            float2 uv = float2((id << 1) & 2, id & 2);
            output.uv = uv;
            output.position = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
            return output;
        }
        float3 pictureLook(float3 c)
        {
            if (look.x < 0.5) return c;
            float l = dot(c, float3(0.2126, 0.7152, 0.0722));
            float3 grey = float3(l, l, l);
            if (look.x < 1.5)
                return saturate((lerp(grey, c, 0.94) - 0.5) * 1.13 + 0.5 + float3(0.025, 0.004, -0.017));
            if (look.x < 2.5)
                return saturate((lerp(grey, c, 1.08) - 0.5) * 1.09 + 0.5);
            if (look.x < 3.5)
                return saturate(lerp(grey, c, 1.12) + float3(0.065, 0.022, -0.043));
            if (look.x < 4.5)
                return saturate(lerp(grey, c, 1.07) + float3(-0.025, 0.015, 0.069));
            if (look.x < 5.5)
                return grey;
            if (look.x < 6.5)
            {
                float3 sepia = float3(
                    dot(c, float3(0.393, 0.769, 0.189)),
                    dot(c, float3(0.349, 0.686, 0.168)),
                    dot(c, float3(0.272, 0.534, 0.131)));
                return saturate((lerp(c, sepia, 0.85) - 0.5) * 0.94 + 0.5);
            }
            if (look.x < 7.5)
                return saturate((lerp(grey, c, 1.7) - 0.5) * 1.14 + 0.5 + float3(0.013, -0.008, 0.035));
            return saturate(float3(l * 0.07, l * 1.23, l * 0.17));
        }
        float4 yuv(Vertex input) : SV_Target
        {
            float2 uv = (source.xy + input.uv * (source.zw - source.xy)) * crop.xy;
            float4 samples = float4(luma.Sample(linearClamp, uv).r, chroma.Sample(chromaSampler, uv).rg, 1);
            return float4(pictureLook(saturate(float3(dot(rowR, samples), dot(rowG, samples), dot(rowB, samples)))), 1);
        }
        float4 bgra(Vertex input) : SV_Target
        {
            return float4(pictureLook(luma.Sample(linearClamp, (source.xy + input.uv * (source.zw - source.xy)) * crop.xy).rgb), 1);
        }
        """;

    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11VertexShader _vertexShader;
    private readonly ID3D11PixelShader _yuvShader;
    private readonly ID3D11PixelShader _bgraShader;
    private readonly ID3D11SamplerState _sampler;
    private readonly ID3D11SamplerState _pointSampler;
    private readonly ID3D11Buffer _colour;
    private ID3D11Texture2D? _luma;
    private ID3D11Texture2D? _chroma;
    private ID3D11ShaderResourceView? _lumaView;
    private ID3D11ShaderResourceView? _chromaView;
    private (VideoPlaneFormat Format, int Width, int Height, bool Surface) _planes;
    private (float U, float V) _crop = (1, 1);
    private IDXGISwapChain1? _swapChain;
    private ID3D11Texture2D? _target;
    private ID3D11Texture2D? _staging;
    private ID3D11RenderTargetView? _targetView;
    private bool _disposed;

    private D3D11VideoRenderer(ID3D11Device device, ID3D11DeviceContext context, bool software)
    {
        _device = device;
        _context = context;
        IsSoftware = software;
        Gpu = new D3D11Gpu(device, software);

        // Decoders sharing the device call into it from their own thread.
        ((Windows.Win32.Graphics.Direct3D10.ID3D10Multithread)context).SetMultithreadProtected(true);
        var vertex = Compile("vs", "vs_4_0");
        var yuv = Compile("yuv", "ps_4_0");
        var bgra = Compile("bgra", "ps_4_0");
        ID3D11VertexShader_unmanaged* vertexShader;
        fixed (byte* code = vertex)
        {
            _device.CreateVertexShader(code, (nuint)vertex.Length, null, &vertexShader);
        }

        _vertexShader = Wrap<ID3D11VertexShader>(vertexShader);
        _yuvShader = PixelShader(yuv);
        _bgraShader = PixelShader(bgra);
        _sampler = Sampler(D3D11_FILTER.D3D11_FILTER_MIN_MAG_MIP_LINEAR);
        _pointSampler = Sampler(D3D11_FILTER.D3D11_FILTER_MIN_MAG_MIP_POINT);
        var buffer = new D3D11_BUFFER_DESC
        {
            ByteWidth = 96,
            Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
            BindFlags = D3D11_BIND_FLAG.D3D11_BIND_CONSTANT_BUFFER,
        };
        ID3D11Buffer_unmanaged* colour;
        _device.CreateBuffer(&buffer, null, &colour);
        _colour = Wrap<ID3D11Buffer>(colour);
    }

    /// <summary>True when drawing in software (WARP): asked for, or no graphics card would start.</summary>
    public bool IsSoftware { get; }

    /// <summary>The device, for decoders that can put their pictures straight onto it.</summary>
    public D3D11Gpu Gpu { get; }

    /// <summary>The graphics adapter's name, as its driver gives it.</summary>
    public string AdapterName
    {
        get
        {
            ((IDXGIDevice)_device).GetAdapter(out var adapter);
            try
            {
                return adapter.GetDesc().Description.ToString();
            }
            finally
            {
                Marshal.ReleaseComObject(adapter);
            }
        }
    }

    /// <summary>A renderer on the graphics card, or in software when asked or when the card fails.</summary>
    public static D3D11VideoRenderer Create(bool software)
    {
        if (!software && TryCreateDevice(D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE, out var device, out var context))
        {
            return new D3D11VideoRenderer(device, context, software: false);
        }

        if (!TryCreateDevice(D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_WARP, out device, out context))
        {
            throw new InvalidOperationException("Direct3D 11 is not available, not even in software.");
        }

        return new D3D11VideoRenderer(device, context, software: true);
    }

    /// <summary>Draws into a flip-model swap chain on a window from now on.</summary>
    public void AttachWindow(nint window, int width, int height)
    {
        ReleaseTarget();
        var factory = Factory();
        var description = SwapChainDescription(width, height);
        try
        {
            factory.CreateSwapChainForHwnd(_device, new HWND(window), &description, null, null, out var swapChain);
            _swapChain = swapChain;
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }

        BindBackBuffer();
    }

    /// <summary>
    /// Draws into a swap chain made for composition (a XAML swap-chain panel) from now on. Returns
    /// the swap chain's IUnknown with a reference of its own, which the caller hands to the panel
    /// and then releases.
    /// </summary>
    public nint AttachComposition(int width, int height)
    {
        ReleaseTarget();
        var factory = Factory();
        var description = SwapChainDescription(width, height);
        try
        {
            factory.CreateSwapChainForComposition(_device, &description, null, out var swapChain);
            _swapChain = swapChain;
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }

        BindBackBuffer();
        return Marshal.GetIUnknownForObject(_swapChain);
    }

    /// <summary>
    /// A composition swap chain's buffers are in physical pixels; the panel lays it out in
    /// device-independent ones. Scaling it by the inverse of the display's scale makes one buffer
    /// pixel one screen pixel, so the picture stays sharp at 150 % or 200 %.
    /// </summary>
    public void SetCompositionScale(float scaleX, float scaleY)
    {
        if (_swapChain is IDXGISwapChain2 scaled && scaleX > 0 && scaleY > 0 && OperatingSystem.IsWindowsVersionAtLeast(10))
        {
            var matrix = new DXGI_MATRIX_3X2_F { _11 = 1 / scaleX, _22 = 1 / scaleY };
            scaled.SetMatrixTransform(&matrix);
        }
    }

    private static DXGI_SWAP_CHAIN_DESC1 SwapChainDescription(int width, int height) => new()
    {
        Width = (uint)Math.Max(1, width),
        Height = (uint)Math.Max(1, height),
        Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
        SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
        BufferUsage = DXGI_USAGE.DXGI_USAGE_RENDER_TARGET_OUTPUT,
        BufferCount = 2,
        Scaling = DXGI_SCALING.DXGI_SCALING_STRETCH,
        SwapEffect = DXGI_SWAP_EFFECT.DXGI_SWAP_EFFECT_FLIP_DISCARD,
        AlphaMode = DXGI_ALPHA_MODE.DXGI_ALPHA_MODE_IGNORE,
    };

    /// <summary>The DXGI factory that made the device's adapter, so swap chains are made on the same card.</summary>
    private IDXGIFactory2 Factory()
    {
        var dxgiDevice = (IDXGIDevice)_device;
        dxgiDevice.GetAdapter(out var adapter);
        try
        {
            var factoryId = typeof(IDXGIFactory2).GUID;
            adapter.GetParent(&factoryId, out var factoryPointer);
            var factory = (IDXGIFactory2)Marshal.GetObjectForIUnknown((nint)factoryPointer);
            Marshal.Release((nint)factoryPointer);
            return factory;
        }
        finally
        {
            Marshal.ReleaseComObject(adapter);
        }
    }

    /// <summary>Draws into an offscreen picture of this size from now on, which <see cref="ReadBack"/> copies out.</summary>
    public void UseOffscreen(int width, int height)
    {
        ReleaseTarget();
        _target = Texture(width, height, DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D3D11_USAGE.D3D11_USAGE_DEFAULT, D3D11_BIND_FLAG.D3D11_BIND_RENDER_TARGET, 0);
        _staging = Texture(width, height, DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D3D11_USAGE.D3D11_USAGE_STAGING, 0, D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ);
        TargetSize = (width, height);
        BindTarget(_target);
    }

    /// <summary>The size of what is drawn into.</summary>
    public (int Width, int Height) TargetSize { get; private set; }

    /// <summary>Follows a window's new size (or makes a new offscreen picture).</summary>
    public void Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (_swapChain is null)
        {
            UseOffscreen(width, height);
            return;
        }

        _context.OMSetRenderTargets(0, null, null);
        ReleaseViewAndTarget();
        _swapChain.ResizeBuffers(0, (uint)width, (uint)height, DXGI_FORMAT.DXGI_FORMAT_UNKNOWN, 0);
        BindBackBuffer();
    }

    /// <summary>Copies a picture's planes onto the graphics card, making the textures again when its shape changed.</summary>
    public void Upload(VideoPlaneFormat format, int width, int height, ReadOnlySpan<byte> plane0, int stride0, ReadOnlySpan<byte> plane1, int stride1)
    {
        if (_planes != (format, width, height, false))
        {
            ReleasePlanes();
            var (lumaFormat, chromaFormat) = format switch
            {
                VideoPlaneFormat.Nv12 => (DXGI_FORMAT.DXGI_FORMAT_R8_UNORM, DXGI_FORMAT.DXGI_FORMAT_R8G8_UNORM),
                VideoPlaneFormat.P010 => (DXGI_FORMAT.DXGI_FORMAT_R16_UNORM, DXGI_FORMAT.DXGI_FORMAT_R16G16_UNORM),
                _ => (DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, DXGI_FORMAT.DXGI_FORMAT_UNKNOWN),
            };
            _luma = Texture(width, height, lumaFormat, D3D11_USAGE.D3D11_USAGE_DEFAULT, D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE, 0);
            _lumaView = View(_luma);
            if (chromaFormat != DXGI_FORMAT.DXGI_FORMAT_UNKNOWN)
            {
                _chroma = Texture((width + 1) / 2, (height + 1) / 2, chromaFormat, D3D11_USAGE.D3D11_USAGE_DEFAULT, D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE, 0);
                _chromaView = View(_chroma);
            }

            _planes = (format, width, height, false);
        }

        _crop = (1, 1);
        fixed (byte* luma = plane0)
        {
            _context.UpdateSubresource(_luma, 0, null, luma, (uint)stride0, 0);
        }

        if (_chroma is not null)
        {
            fixed (byte* chroma = plane1)
            {
                _context.UpdateSubresource(_chroma, 0, null, chroma, (uint)stride1, 0);
            }
        }
    }

    /// <summary>
    /// Takes a picture a decoder left on this device: its slice is copied, on the graphics card, into a
    /// texture whose planes the shader reads, and only the <paramref name="width"/> by
    /// <paramref name="height"/> the picture shows is drawn.
    /// </summary>
    public void UploadSurface(D3D11Surface surface, VideoPlaneFormat format, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(surface);
        D3D11_TEXTURE2D_DESC source;
        surface.Texture.GetDesc(&source);
        var (codedWidth, codedHeight) = ((int)source.Width, (int)source.Height);
        if (_planes != (format, codedWidth, codedHeight, true))
        {
            ReleasePlanes();
            _luma = Texture(codedWidth, codedHeight, source.Format, D3D11_USAGE.D3D11_USAGE_DEFAULT, D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE, 0);
            var (lumaFormat, chromaFormat) = format == VideoPlaneFormat.P010
                ? (DXGI_FORMAT.DXGI_FORMAT_R16_UNORM, DXGI_FORMAT.DXGI_FORMAT_R16G16_UNORM)
                : (DXGI_FORMAT.DXGI_FORMAT_R8_UNORM, DXGI_FORMAT.DXGI_FORMAT_R8G8_UNORM);
            _lumaView = PlaneView(_luma, lumaFormat);
            _chromaView = PlaneView(_luma, chromaFormat);
            _planes = (format, codedWidth, codedHeight, true);
        }

        _context.CopySubresourceRegion(_luma, 0, 0, 0, 0, surface.Texture, surface.Index, null);
        _crop = ((float)width / codedWidth, (float)height / codedHeight);
    }

    /// <summary>
    /// Clears to black and draws the last uploaded picture into a rectangle of the target. The colour
    /// matrix is three rows of four: red, green and blue from the samples as the textures hold them and
    /// 1. Chroma is interpolated between samples when <paramref name="smoothChroma"/>, else taken from
    /// the nearest, as a still of the coded picture would be.
    /// </summary>
    public void Draw(ReadOnlySpan<float> colourMatrix, int x, int y, int width, int height, bool smoothChroma = true) =>
        Draw(colourMatrix, x, y, width, height, (0, 0, 1, 1), smoothChroma);

    /// <summary>
    /// Draws the part of the picture inside <paramref name="source"/> (left, top, right, bottom, each
    /// from 0 to 1 of the picture) into the rectangle given; over what is there already, rather than on
    /// black, when not <paramref name="clear"/> (a small copy drawn over the large one).
    /// </summary>
    public void Draw(ReadOnlySpan<float> colourMatrix, int x, int y, int width, int height, (float Left, float Top, float Right, float Bottom) source, bool smoothChroma, bool clear = true, int look = 0)
    {
        if (colourMatrix.Length != 12)
        {
            throw new ArgumentException("The colour matrix has three rows of four.", nameof(colourMatrix));
        }

        var view = _targetView ?? throw new InvalidOperationException("There is nothing to draw into: attach a window or use an offscreen target first.");
        if (clear)
        {
            _context.ClearRenderTargetView(view, [0, 0, 0, 1]);
        }

        if (_lumaView is null)
        {
            return;
        }

        Span<float> constants = stackalloc float[24];
        colourMatrix.CopyTo(constants);
        (constants[12], constants[13]) = (_crop.U, _crop.V);
        (constants[16], constants[17], constants[18], constants[19]) = source;
        constants[20] = look;
        fixed (float* values = constants)
        {
            _context.UpdateSubresource(_colour, 0, null, values, 96, 0);
        }

        var viewport = new D3D11_VIEWPORT { TopLeftX = x, TopLeftY = y, Width = width, Height = height, MaxDepth = 1 };
        _context.RSSetViewports(1, &viewport);
        _context.OMSetRenderTargets(1, [view], null);
        _context.IASetInputLayout(null);
        _context.IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        _context.VSSetShader(_vertexShader, null, 0);
        _context.PSSetShader(_planes.Format == VideoPlaneFormat.Bgra ? _bgraShader : _yuvShader, null, 0);
        _context.PSSetShaderResources(0, 2, [_lumaView, _chromaView ?? _lumaView]);
        _context.PSSetSamplers(0, 2, [_sampler, smoothChroma ? _sampler : _pointSampler]);
        _context.PSSetConstantBuffers(0, 1, [_colour]);
        _context.Draw(3, 0);
    }

    /// <summary>Shows what was drawn: the swap chain flips, waiting for the display's next refresh when asked.</summary>
    public void Present(bool waitForRefresh)
    {
        if (_swapChain is null)
        {
            _context.Flush();
            return;
        }

        _swapChain.Present(waitForRefresh ? 1u : 0u, 0).ThrowOnFailure();
    }

    /// <summary>Copies the offscreen picture out as BGRA rows <paramref name="stride"/> bytes apart.</summary>
    public void ReadBack(Span<byte> bgra, int stride)
    {
        var target = _target ?? throw new InvalidOperationException("Only an offscreen target can be read back.");
        _context.CopyResource(_staging!, target);
        D3D11_MAPPED_SUBRESOURCE mapped;
        _context.Map(_staging!, 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped);
        try
        {
            var (width, height) = TargetSize;
            for (var row = 0; row < height; row++)
            {
                new ReadOnlySpan<byte>((byte*)mapped.pData + (row * mapped.RowPitch), width * 4).CopyTo(bgra[(row * stride)..]);
            }
        }
        finally
        {
            _context.Unmap(_staging!, 0);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleasePlanes();
        ReleaseTarget();
        foreach (var com in new object[] { _colour, _pointSampler, _sampler, _bgraShader, _yuvShader, _vertexShader, _context, _device })
        {
            Marshal.ReleaseComObject(com);
        }
    }

    private static bool TryCreateDevice(D3D_DRIVER_TYPE driver, out ID3D11Device device, out ID3D11DeviceContext context)
    {
        try
        {
            // Video support lets Windows' decoders use the card; a device without it still draws.
            var flags = D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_VIDEO_SUPPORT;
            if (PInvoke.D3D11CreateDevice(null, driver, default, flags, default, PInvoke.D3D11_SDK_VERSION, out device, out _, out context).Failed)
            {
                PInvoke.D3D11CreateDevice(null, driver, default, D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT, default, PInvoke.D3D11_SDK_VERSION, out device, out _, out context).ThrowOnFailure();
            }

            return true;
        }
        catch (COMException)
        {
            device = null!;
            context = null!;
            return false;
        }
    }

    private static T Wrap<T>(void* pointer)
        where T : class
    {
        var wrapped = (T)Marshal.GetObjectForIUnknown((nint)pointer);
        Marshal.Release((nint)pointer);
        return wrapped;
    }

    /// <summary>Compiles one entry point of <see cref="Shaders"/> with the compiler Windows ships.</summary>
    private static byte[] Compile(string entry, string profile)
    {
        var result = PInvoke.D3DCompile(Encoding.ASCII.GetBytes(Shaders), "rexplayer.hlsl", null, null, entry, profile, 0, 0, out var code, out var errors);
        try
        {
            if (result.Failed)
            {
                var message = errors is null ? result.ToString() : Encoding.ASCII.GetString(new ReadOnlySpan<byte>(errors.GetBufferPointer(), (int)errors.GetBufferSize()));
                throw new InvalidOperationException($"The {entry} shader did not compile: {message}");
            }

            return new ReadOnlySpan<byte>(code.GetBufferPointer(), (int)code.GetBufferSize()).ToArray();
        }
        finally
        {
            if (code is not null)
            {
                Marshal.ReleaseComObject(code);
            }

            if (errors is not null)
            {
                Marshal.ReleaseComObject(errors);
            }
        }
    }

    private ID3D11SamplerState Sampler(D3D11_FILTER filter)
    {
        var description = new D3D11_SAMPLER_DESC
        {
            Filter = filter,
            AddressU = D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_CLAMP,
            AddressV = D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_CLAMP,
            AddressW = D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_CLAMP,
            ComparisonFunc = D3D11_COMPARISON_FUNC.D3D11_COMPARISON_NEVER,
            MaxLOD = float.MaxValue,
        };
        ID3D11SamplerState_unmanaged* sampler;
        _device.CreateSamplerState(&description, &sampler);
        return Wrap<ID3D11SamplerState>(sampler);
    }

    private ID3D11PixelShader PixelShader(byte[] code)
    {
        ID3D11PixelShader_unmanaged* shader;
        fixed (byte* bytes = code)
        {
            _device.CreatePixelShader(bytes, (nuint)code.Length, null, &shader);
        }

        return Wrap<ID3D11PixelShader>(shader);
    }

    private ID3D11Texture2D Texture(int width, int height, DXGI_FORMAT format, D3D11_USAGE usage, D3D11_BIND_FLAG bind, D3D11_CPU_ACCESS_FLAG cpu)
    {
        var description = new D3D11_TEXTURE2D_DESC
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = format,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
            Usage = usage,
            BindFlags = bind,
            CPUAccessFlags = cpu,
        };
        ID3D11Texture2D_unmanaged* texture;
        _device.CreateTexture2D(&description, null, &texture);
        return Wrap<ID3D11Texture2D>(texture);
    }

    private ID3D11ShaderResourceView View(ID3D11Texture2D texture)
    {
        ID3D11ShaderResourceView_unmanaged* view;
        _device.CreateShaderResourceView(texture, null, &view);
        return Wrap<ID3D11ShaderResourceView>(view);
    }

    /// <summary>A view of one plane of a planar texture (NV12, P010) in the format its samples read as.</summary>
    private ID3D11ShaderResourceView PlaneView(ID3D11Texture2D texture, DXGI_FORMAT format)
    {
        var description = new D3D11_SHADER_RESOURCE_VIEW_DESC
        {
            Format = format,
            ViewDimension = D3D_SRV_DIMENSION.D3D_SRV_DIMENSION_TEXTURE2D,
        };
        description.Anonymous.Texture2D.MipLevels = 1;
        ID3D11ShaderResourceView_unmanaged* view;
        _device.CreateShaderResourceView(texture, &description, &view);
        return Wrap<ID3D11ShaderResourceView>(view);
    }

    private void BindBackBuffer()
    {
        var textureId = typeof(ID3D11Texture2D).GUID;
        _swapChain!.GetBuffer(0, &textureId, out var buffer);
        var backBuffer = Wrap<ID3D11Texture2D>(buffer);
        D3D11_TEXTURE2D_DESC description;
        backBuffer.GetDesc(&description);
        TargetSize = ((int)description.Width, (int)description.Height);
        BindTarget(backBuffer);
        Marshal.ReleaseComObject(backBuffer);
    }

    private void BindTarget(ID3D11Texture2D target)
    {
        ID3D11RenderTargetView_unmanaged* view;
        _device.CreateRenderTargetView(target, null, &view);
        _targetView = Wrap<ID3D11RenderTargetView>(view);
    }

    private void ReleasePlanes()
    {
        foreach (var com in new object?[] { _lumaView, _chromaView, _luma, _chroma })
        {
            if (com is not null)
            {
                Marshal.ReleaseComObject(com);
            }
        }

        (_lumaView, _chromaView, _luma, _chroma) = (null, null, null, null);
        _planes = default;
    }

    private void ReleaseViewAndTarget()
    {
        foreach (var com in new object?[] { _targetView, _target, _staging })
        {
            if (com is not null)
            {
                Marshal.ReleaseComObject(com);
            }
        }

        (_targetView, _target, _staging) = (null, null, null);
    }

    private void ReleaseTarget()
    {
        ReleaseViewAndTarget();
        if (_swapChain is not null)
        {
            Marshal.ReleaseComObject(_swapChain);
            _swapChain = null;
        }
    }
}
