using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Windows.Win32;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.Dxgi;
using Windows.Win32.Graphics.Dxgi.Common;

namespace Rex.Media.Interop.Graphics;

/// <summary>A stage texture's pixels, as the runner makes them.</summary>
public enum StagePixels
{
    Bgra8,
    Float1,
    Float2,
    Float4,
    Half4,
}

/// <summary>How a stage draw is combined with its target.</summary>
public enum StageBlendMode
{
    Replace,
    Add,
    Over,
}

/// <summary>
/// The camera stage's Direct3D 11 runner (ADR-0022): render-target textures, full-screen passes and
/// instanced points into a composition swap chain or an offscreen picture. Call sequencing and COM
/// plumbing only (ADR-008): which passes run, with which programs and constants, is decided by the
/// effects in AppCore. Programs are compiled once per process and kept between runs through
/// <see cref="D3D11VideoRenderer.CompiledShaders"/>. One thread draws; the binding arrays are reused,
/// so a frame allocates nothing.
/// </summary>
[SupportedOSPlatform("windows8.0")]
public sealed unsafe class D3D11Stage : IDisposable
{
    /// <summary>The most constants a draw takes, and the most inputs it reads.</summary>
    public const int MaxConstants = 256;
    public const int MaxInputs = 8;

    private const string FullScreen = """
        struct Vertex { float4 position : SV_Position; float2 uv : TEXCOORD0; };
        Vertex vs(uint id : SV_VertexID)
        {
            Vertex output;
            float2 uv = float2((id << 1) & 2, id & 2);
            output.uv = uv;
            output.position = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
            return output;
        }
        """;

    private static readonly ConcurrentDictionary<(string Source, string Entry), Lazy<byte[]>> Bytecode = new();
    private static readonly float[] Black = [0, 0, 0, 1];

    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11VertexShader _fullScreen;
    private readonly Dictionary<string, (ID3D11VertexShader? Vertex, ID3D11PixelShader Pixel)> _programs = new(StringComparer.Ordinal);
    private readonly Dictionary<int, (ID3D11Texture2D Texture, ID3D11ShaderResourceView View, ID3D11RenderTargetView Target, int Width, int Height, StagePixels Pixels)> _textures = [];
    private readonly ID3D11SamplerState[] _samplers;
    private readonly ID3D11Buffer _constants;
    private readonly ID3D11Buffer[] _constantSlot;
    private readonly ID3D11BlendState _add;
    private readonly ID3D11BlendState _over;
    private readonly ID3D11RasterizerState _noCull;
    private readonly ID3D11ShaderResourceView?[] _inputs = new ID3D11ShaderResourceView?[MaxInputs];
    private readonly ID3D11ShaderResourceView?[] _none = new ID3D11ShaderResourceView?[MaxInputs];
    private readonly ID3D11RenderTargetView[] _target = new ID3D11RenderTargetView[1];
    private readonly float[] _blendFactor = [0, 0, 0, 0];
    private IDXGISwapChain1? _swapChain;
    private ID3D11Texture2D? _offscreen;
    private ID3D11Texture2D? _staging;
    private ID3D11RenderTargetView? _output;
    private int _next = 1;
    private bool _disposed;

    private D3D11Stage(ID3D11Device device, ID3D11DeviceContext context, bool software)
    {
        (_device, _context, IsSoftware) = (device, context, software);
        var code = Code(FullScreen, "vs", "vs_5_0");
        ID3D11VertexShader_unmanaged* vertex;
        fixed (byte* bytes = code)
        {
            _device.CreateVertexShader(bytes, (nuint)code.Length, null, &vertex);
        }

        _fullScreen = Wrap<ID3D11VertexShader>(vertex);
        _samplers =
        [
            Sampler(D3D11_FILTER.D3D11_FILTER_MIN_MAG_MIP_LINEAR, D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_CLAMP),
            Sampler(D3D11_FILTER.D3D11_FILTER_MIN_MAG_MIP_LINEAR, D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_WRAP),
            Sampler(D3D11_FILTER.D3D11_FILTER_MIN_MAG_MIP_POINT, D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_CLAMP),
        ];
        var buffer = new D3D11_BUFFER_DESC
        {
            ByteWidth = MaxConstants * sizeof(float),
            Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
            BindFlags = D3D11_BIND_FLAG.D3D11_BIND_CONSTANT_BUFFER,
        };
        ID3D11Buffer_unmanaged* constants;
        _device.CreateBuffer(&buffer, null, &constants);
        _constants = Wrap<ID3D11Buffer>(constants);
        _constantSlot = [_constants];
        _add = Blend(D3D11_BLEND.D3D11_BLEND_ONE, D3D11_BLEND.D3D11_BLEND_ONE);
        _over = Blend(D3D11_BLEND.D3D11_BLEND_SRC_ALPHA, D3D11_BLEND.D3D11_BLEND_INV_SRC_ALPHA);
        // Points face whichever way their corners run: nothing is culled, so effects need not care.
        var rasterizer = new D3D11_RASTERIZER_DESC
        {
            FillMode = D3D11_FILL_MODE.D3D11_FILL_SOLID,
            CullMode = D3D11_CULL_MODE.D3D11_CULL_NONE,
            DepthClipEnable = true,
        };
        ID3D11RasterizerState_unmanaged* noCull;
        _device.CreateRasterizerState(&rasterizer, &noCull);
        _noCull = Wrap<ID3D11RasterizerState>(noCull);
    }

    /// <summary>True when drawing in software (WARP): asked for, or no graphics card would start.</summary>
    public bool IsSoftware { get; }

    /// <summary>The size of the output.</summary>
    public (int Width, int Height) TargetSize { get; private set; }

    /// <summary>A stage on the graphics card, or in software when asked or when the card fails.</summary>
    public static D3D11Stage Create(bool software)
    {
        if (!software && TryCreateDevice(D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE, out var device, out var context))
        {
            return new D3D11Stage(device, context, software: false);
        }

        if (!TryCreateDevice(D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_WARP, out device, out context))
        {
            throw new InvalidOperationException("Direct3D 11 is not available, not even in software.");
        }

        return new D3D11Stage(device, context, software: true);
    }

    /// <summary>
    /// Draws into a swap chain for a XAML swap-chain panel from now on. Returns the swap chain's
    /// IUnknown with a reference of its own, which the caller hands to the panel and then releases.
    /// </summary>
    public nint AttachComposition(int width, int height)
    {
        ReleaseOutput();
        var dxgiDevice = (IDXGIDevice)_device;
        dxgiDevice.GetAdapter(out var adapter);
        IDXGIFactory2 factory;
        try
        {
            var factoryId = typeof(IDXGIFactory2).GUID;
            adapter.GetParent(&factoryId, out var factoryPointer);
            factory = (IDXGIFactory2)Marshal.GetObjectForIUnknown((nint)factoryPointer);
            Marshal.Release((nint)factoryPointer);
        }
        finally
        {
            Marshal.ReleaseComObject(adapter);
        }

        var description = new DXGI_SWAP_CHAIN_DESC1
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

    /// <summary>Makes one buffer pixel one screen pixel at the display's scale (see <see cref="D3D11VideoRenderer.SetCompositionScale"/>).</summary>
    public void SetCompositionScale(float scaleX, float scaleY)
    {
        if (_swapChain is IDXGISwapChain2 scaled && scaleX > 0 && scaleY > 0 && OperatingSystem.IsWindowsVersionAtLeast(10))
        {
            var matrix = new DXGI_MATRIX_3X2_F { _11 = 1 / scaleX, _22 = 1 / scaleY };
            scaled.SetMatrixTransform(&matrix);
        }
    }

    /// <summary>Draws into an offscreen picture of this size from now on, which <see cref="ReadBack"/> copies out.</summary>
    public void UseOffscreen(int width, int height)
    {
        ReleaseOutput();
        (width, height) = (Math.Max(1, width), Math.Max(1, height));
        _offscreen = NewTexture(width, height, DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D3D11_USAGE.D3D11_USAGE_DEFAULT, D3D11_BIND_FLAG.D3D11_BIND_RENDER_TARGET, 0);
        _staging = NewTexture(width, height, DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D3D11_USAGE.D3D11_USAGE_STAGING, 0, D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ);
        _output = TargetView(_offscreen);
        TargetSize = (width, height);
    }

    /// <summary>Follows the panel's new size, or makes a new offscreen picture.</summary>
    public void Resize(int width, int height)
    {
        (width, height) = (Math.Max(1, width), Math.Max(1, height));
        if (_swapChain is null)
        {
            UseOffscreen(width, height);
            return;
        }

        _context.OMSetRenderTargets(0, null, null);
        if (_output is not null)
        {
            Marshal.ReleaseComObject(_output);
            _output = null;
        }

        _swapChain.ResizeBuffers(0, (uint)width, (uint)height, DXGI_FORMAT.DXGI_FORMAT_UNKNOWN, 0);
        BindBackBuffer();
    }

    /// <summary>Makes a texture the stage can draw into and read from; the number is how draws name it.</summary>
    public int CreateTexture(int width, int height, StagePixels pixels)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var texture = NewTexture(width, height, Format(pixels), D3D11_USAGE.D3D11_USAGE_DEFAULT,
            D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_FLAG.D3D11_BIND_RENDER_TARGET, 0);
        ID3D11ShaderResourceView_unmanaged* view;
        _device.CreateShaderResourceView(texture, null, &view);
        var id = _next++;
        _textures[id] = (texture, Wrap<ID3D11ShaderResourceView>(view), TargetView(texture), width, height, pixels);
        return id;
    }

    public void ReleaseTexture(int id)
    {
        if (_textures.Remove(id, out var texture))
        {
            Marshal.ReleaseComObject(texture.Target);
            Marshal.ReleaseComObject(texture.View);
            Marshal.ReleaseComObject(texture.Texture);
        }
    }

    /// <summary>Copies rows <paramref name="rowBytes"/> apart into a texture, whose own pixel size the data must have.</summary>
    public void Upload(int id, ReadOnlySpan<byte> data, int rowBytes)
    {
        var texture = Get(id);
        if (data.Length < (long)rowBytes * (texture.Height - 1) + (texture.Width * BytesPerPixel(texture.Pixels)))
        {
            throw new ArgumentException("The data are shorter than the texture they fill.", nameof(data));
        }

        fixed (byte* bytes = data)
        {
            _context.UpdateSubresource(texture.Texture, 0, null, bytes, (uint)rowBytes, 0);
        }
    }

    /// <summary>Fills a texture (the output when <paramref name="id"/> is zero) with one colour.</summary>
    public void Clear(int id, float red, float green, float blue, float alpha)
    {
        Span<float> colour = [red, green, blue, alpha];
        var view = id == 0 ? Output() : Get(id).Target;
        _context.ClearRenderTargetView(view, colour.ToArray());
    }

    /// <summary>Draws <paramref name="source"/>'s <c>ps</c> over all of <paramref name="target"/> (the output when zero).</summary>
    public void Pass(string source, int target, ReadOnlySpan<int> inputs, ReadOnlySpan<float> constants, StageBlendMode blend)
    {
        var program = Program(source, points: false);
        Bind(target, inputs, constants, blend, program.Pixel, _fullScreen, vertexReadsInputs: false);
        _context.IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        _context.Draw(3, 0);
    }

    /// <summary>Draws <paramref name="count"/> quads, each corner placed by <paramref name="source"/>'s <c>vsPoints</c>.</summary>
    public void Points(string source, int target, ReadOnlySpan<int> inputs, int count, ReadOnlySpan<float> constants, StageBlendMode blend)
    {
        if (count <= 0)
        {
            return;
        }

        var program = Program(source, points: true);
        Bind(target, inputs, constants, blend, program.Pixel, program.Vertex!, vertexReadsInputs: true);
        _context.IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLESTRIP);
        _context.DrawInstanced(4, (uint)count, 0, 0);
    }

    /// <summary>Shows what was drawn, waiting for the display's next refresh when asked.</summary>
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
        var target = _offscreen ?? throw new InvalidOperationException("Only an offscreen stage can be read back.");
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
        foreach (var id in _textures.Keys.ToArray())
        {
            ReleaseTexture(id);
        }

        foreach (var (vertex, pixel) in _programs.Values)
        {
            if (vertex is not null)
            {
                Marshal.ReleaseComObject(vertex);
            }

            Marshal.ReleaseComObject(pixel);
        }

        _programs.Clear();
        ReleaseOutput();
        foreach (var com in new object[] { _add, _over, _noCull, _constants, _samplers[0], _samplers[1], _samplers[2], _fullScreen, _context, _device })
        {
            Marshal.ReleaseComObject(com);
        }
    }

    private void Bind(int target, ReadOnlySpan<int> inputs, ReadOnlySpan<float> constants, StageBlendMode blend,
        ID3D11PixelShader pixel, ID3D11VertexShader vertex, bool vertexReadsInputs)
    {
        if (inputs.Length > MaxInputs)
        {
            throw new ArgumentException($"A draw reads at most {MaxInputs} textures.", nameof(inputs));
        }

        if (constants.Length > MaxConstants)
        {
            throw new ArgumentException($"A draw takes at most {MaxConstants} constants.", nameof(constants));
        }

        // Nothing may be read and drawn into at once: the inputs of the last draw are let go first.
        _context.PSSetShaderResources(0, MaxInputs, _none);
        _context.VSSetShaderResources(0, MaxInputs, _none);
        var (view, width, height) = target == 0 ? (Output(), TargetSize.Width, TargetSize.Height) : Target(target);
        _target[0] = view;
        _context.OMSetRenderTargets(1, _target, null);
        _context.OMSetBlendState(blend switch { StageBlendMode.Add => _add, StageBlendMode.Over => _over, _ => null }, _blendFactor, 0xffffffff);
        var viewport = new D3D11_VIEWPORT { Width = width, Height = height, MaxDepth = 1 };
        _context.RSSetViewports(1, &viewport);
        _context.RSSetState(_noCull);

        Span<float> values = stackalloc float[MaxConstants];
        values.Clear();
        constants.CopyTo(values);
        fixed (float* pointer = values)
        {
            _context.UpdateSubresource(_constants, 0, null, pointer, MaxConstants * sizeof(float), 0);
        }

        Array.Clear(_inputs);
        for (var i = 0; i < inputs.Length; i++)
        {
            if (inputs[i] == target)
            {
                throw new ArgumentException("A draw cannot read the texture it draws into.", nameof(inputs));
            }

            _inputs[i] = Get(inputs[i]).View;
        }

        _context.IASetInputLayout(null);
        _context.VSSetShader(vertex, null, 0);
        _context.PSSetShader(pixel, null, 0);
        _context.PSSetShaderResources(0, MaxInputs, _inputs);
        _context.PSSetSamplers(0, (uint)_samplers.Length, _samplers);
        _context.PSSetConstantBuffers(0, 1, _constantSlot);
        if (vertexReadsInputs)
        {
            _context.VSSetShaderResources(0, MaxInputs, _inputs);
            _context.VSSetSamplers(0, (uint)_samplers.Length, _samplers);
            _context.VSSetConstantBuffers(0, 1, _constantSlot);
        }
    }

    private (ID3D11VertexShader? Vertex, ID3D11PixelShader Pixel) Program(string source, bool points)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        if (_programs.TryGetValue(source, out var program))
        {
            return program;
        }

        var pixelCode = Code(source, "ps", "ps_5_0");
        ID3D11PixelShader_unmanaged* pixel;
        fixed (byte* bytes = pixelCode)
        {
            _device.CreatePixelShader(bytes, (nuint)pixelCode.Length, null, &pixel);
        }

        ID3D11VertexShader? vertex = null;
        if (points)
        {
            var vertexCode = Code(source, "vsPoints", "vs_5_0");
            ID3D11VertexShader_unmanaged* made;
            fixed (byte* bytes = vertexCode)
            {
                _device.CreateVertexShader(bytes, (nuint)vertexCode.Length, null, &made);
            }

            vertex = Wrap<ID3D11VertexShader>(made);
        }

        program = (vertex, Wrap<ID3D11PixelShader>(pixel));
        _programs[source] = program;
        return program;
    }

    /// <summary>Compiles (or fetches) one entry point of a stage program, shared by every stage in the process.</summary>
    private static byte[] Code(string source, string entry, string profile) =>
        Bytecode.GetOrAdd((source, entry), key => new Lazy<byte[]>(() =>
            D3D11VideoRenderer.CompiledShaders is { } compiled
                ? compiled(key.Source, key.Entry, profile, () => CompileNow(key.Source, key.Entry, profile))
                : CompileNow(key.Source, key.Entry, profile))).Value;

    private static byte[] CompileNow(string source, string entry, string profile)
    {
        var result = PInvoke.D3DCompile(Encoding.ASCII.GetBytes(source), "stage.hlsl", null, null, entry, profile, 0, 0, out var code, out var errors);
        try
        {
            if (result.Failed)
            {
                var message = errors is null ? result.ToString() : Encoding.ASCII.GetString(new ReadOnlySpan<byte>(errors.GetBufferPointer(), (int)errors.GetBufferSize()));
                throw new InvalidOperationException($"The stage's {entry} shader did not compile: {message}");
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

    private (ID3D11Texture2D Texture, ID3D11ShaderResourceView View, ID3D11RenderTargetView Target, int Width, int Height, StagePixels Pixels) Get(int id) =>
        _textures.TryGetValue(id, out var texture) ? texture : throw new ArgumentException($"There is no stage texture {id}.", nameof(id));

    private (ID3D11RenderTargetView View, int Width, int Height) Target(int id)
    {
        var texture = Get(id);
        return (texture.Target, texture.Width, texture.Height);
    }

    private ID3D11RenderTargetView Output() => _output ?? throw new InvalidOperationException("There is nothing to draw into: attach a panel or use an offscreen picture first.");

    private static DXGI_FORMAT Format(StagePixels pixels) => pixels switch
    {
        StagePixels.Float1 => DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT,
        StagePixels.Float2 => DXGI_FORMAT.DXGI_FORMAT_R32G32_FLOAT,
        StagePixels.Float4 => DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_FLOAT,
        StagePixels.Half4 => DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT,
        _ => DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
    };

    private static int BytesPerPixel(StagePixels pixels) => pixels switch
    {
        StagePixels.Float2 => 8,
        StagePixels.Float4 => 16,
        StagePixels.Half4 => 8,
        _ => 4,
    };

    private ID3D11Texture2D NewTexture(int width, int height, DXGI_FORMAT format, D3D11_USAGE usage, D3D11_BIND_FLAG bind, D3D11_CPU_ACCESS_FLAG cpu)
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

    private ID3D11RenderTargetView TargetView(ID3D11Texture2D texture)
    {
        ID3D11RenderTargetView_unmanaged* view;
        _device.CreateRenderTargetView(texture, null, &view);
        return Wrap<ID3D11RenderTargetView>(view);
    }

    private ID3D11SamplerState Sampler(D3D11_FILTER filter, D3D11_TEXTURE_ADDRESS_MODE address)
    {
        var description = new D3D11_SAMPLER_DESC
        {
            Filter = filter,
            AddressU = address,
            AddressV = address,
            AddressW = address,
            ComparisonFunc = D3D11_COMPARISON_FUNC.D3D11_COMPARISON_NEVER,
            MaxLOD = float.MaxValue,
        };
        ID3D11SamplerState_unmanaged* sampler;
        _device.CreateSamplerState(&description, &sampler);
        return Wrap<ID3D11SamplerState>(sampler);
    }

    private ID3D11BlendState Blend(D3D11_BLEND source, D3D11_BLEND destination)
    {
        var description = new D3D11_BLEND_DESC();
        description.RenderTarget._0 = new D3D11_RENDER_TARGET_BLEND_DESC
        {
            BlendEnable = true,
            SrcBlend = source,
            DestBlend = destination,
            BlendOp = D3D11_BLEND_OP.D3D11_BLEND_OP_ADD,
            SrcBlendAlpha = D3D11_BLEND.D3D11_BLEND_ONE,
            DestBlendAlpha = D3D11_BLEND.D3D11_BLEND_ONE,
            BlendOpAlpha = D3D11_BLEND_OP.D3D11_BLEND_OP_MAX,
            RenderTargetWriteMask = (byte)D3D11_COLOR_WRITE_ENABLE.D3D11_COLOR_WRITE_ENABLE_ALL,
        };
        ID3D11BlendState_unmanaged* state;
        _device.CreateBlendState(&description, &state);
        return Wrap<ID3D11BlendState>(state);
    }

    private void BindBackBuffer()
    {
        var textureId = typeof(ID3D11Texture2D).GUID;
        _swapChain!.GetBuffer(0, &textureId, out var buffer);
        var backBuffer = Wrap<ID3D11Texture2D>(buffer);
        D3D11_TEXTURE2D_DESC description;
        backBuffer.GetDesc(&description);
        TargetSize = ((int)description.Width, (int)description.Height);
        _output = TargetView(backBuffer);
        Marshal.ReleaseComObject(backBuffer);
        _context.ClearRenderTargetView(_output, Black);
    }

    private void ReleaseOutput()
    {
        foreach (var com in new object?[] { _output, _offscreen, _staging, _swapChain })
        {
            if (com is not null)
            {
                Marshal.ReleaseComObject(com);
            }
        }

        (_output, _offscreen, _staging, _swapChain) = (null, null, null, null);
    }

    private static bool TryCreateDevice(D3D_DRIVER_TYPE driver, out ID3D11Device device, out ID3D11DeviceContext context)
    {
        try
        {
            PInvoke.D3D11CreateDevice(null, driver, default, D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT, default, PInvoke.D3D11_SDK_VERSION, out device, out _, out context).ThrowOnFailure();
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
}
