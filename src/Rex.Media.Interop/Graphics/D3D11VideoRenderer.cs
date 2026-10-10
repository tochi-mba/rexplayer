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
        cbuffer Colour : register(b0) { float4 rowR; float4 rowG; float4 rowB; float4 crop; float4 source; float4 look; float4 effect; float4 pointer; };
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
        float3 basePictureLook(float3 c)
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
        float3 styledLook(float3 original)
        {
            float3 styled = basePictureLook(original);
            float l = dot(styled, float3(0.2126, 0.7152, 0.0722));
            styled = saturate(lerp(float3(l, l, l), styled, look.z));
            if (look.x < 0.5)
            {
                return saturate((original - 0.5) * look.z + 0.5 + (look.y - 1) * 0.25);
            }
            return saturate(lerp(original, styled, look.y));
        }
        // All effects operate on sampled pixels, after decoding and before colour grading.
        // Sampling geometry and spatial derivatives are GPU-local; original frames stay intact.
        float2 effectUv(float2 uv)
        {
            if (effect.x < 0.5 || effect.y <= 0) return uv;
            float intensity = effect.y;
            float time = effect.z * max(pointer.w, 0.25);
            if (effect.x < 1.5)
            {
                float2 wave = float2(sin(uv.y * 26 + time * 1.7), cos(uv.x * 18 - time * 1.1));
                return saturate(uv + wave * (0.019 * intensity));
            }
            if (effect.x < 2.5) return uv;
            if (effect.x < 3.5)
            {
                float scale = lerp(150, 16, intensity) * max(pointer.w, 0.25);
                float2 grid = float2(scale * effect.w, scale);
                float stagger = sin(floor(uv.y * grid.y) * 0.8 + time * 2) * 0.013 * intensity;
                return saturate((floor((uv + float2(stagger, 0)) * grid) + 0.5) / grid);
            }

            if (effect.x < 4.5)
            {
                float2 p = (uv - 0.5) * float2(effect.w, 1);
                float r = length(p);
                float angle = atan2(p.y, p.x);
                float section = 1.0471975512; // pi / 3: six mirrored sectors
                float folded = abs(fmod(angle + 6.2831853072, section * 2) - section);
                float2 transformed = float2(cos(folded + time * 0.05), sin(folded + time * 0.05)) * r;
                float2 kaleido = saturate(0.5 + transformed / float2(effect.w, 1));
                return lerp(uv, kaleido, intensity);
            }
            // 5-7 are contours and operate on colour, so they do not warp the sample location.
            if (effect.x < 7.5) return uv;
            if (effect.x < 8.5) // Liquid glass: interference between crossing sine fields
            {
                float2 displacement = float2(
                    sin(uv.y * 35 + time * 1.3) + sin((uv.x + uv.y) * 23 - time * 0.8),
                    cos(uv.x * 29 - time * 1.1) + sin((uv.x - uv.y) * 19 + time * 0.9));
                return saturate(uv + displacement * (0.013 * intensity));
            }
            if (effect.x < 9.5) // Slice shift: moving, hashed bands rather than regular pixels
            {
                float band = floor(uv.y * 29);
                float epoch = floor(time * 6);
                float displacement = frac(sin(band * 37.719 + epoch * 11.13) * 43758.5453) * 2 - 1;
                return saturate(uv + float2(displacement * 0.12 * intensity, 0));
            }
            if (effect.x < 10.5) // Vortex: bounded polar displacement with a steady centre
            {
                float2 delta = (uv - 0.5) * float2(effect.w, 1);
                float radius = length(delta);
                float turn = intensity * 4 * exp(-radius * 3.3) + time * 0.11 * intensity;
                float cs = cos(turn), sn = sin(turn);
                float2 rotated = float2(cs * delta.x - sn * delta.y, sn * delta.x + cs * delta.y);
                return saturate(0.5 + rotated / float2(effect.w, 1));
            }
            // Cursor lens works only under a pointer. The following image-aware effect has
            // no global UV warp; it samples its original gradients in effected() instead.
            if (effect.x < 11.5 && pointer.z > 0.5)
            {
                float2 delta = (uv - pointer.xy) * float2(effect.w, 1);
                float distance = length(delta);
                float radius = 0.23 / max(pointer.w, 0.25);
                float inside = 1 - smoothstep(radius * 0.72, radius, distance);
                return saturate(uv - delta / float2(effect.w, 1) * (0.55 * intensity * inside));
            }
            return uv;
        }
        float3 readColour(float2 uv, bool isYuv)
        {
            float2 mapped = (source.xy + saturate(uv) * (source.zw - source.xy)) * crop.xy;
            if (isYuv)
            {
                float4 samples = float4(luma.Sample(linearClamp, mapped).r, chroma.Sample(chromaSampler, mapped).rg, 1);
                return saturate(float3(dot(rowR, samples), dot(rowG, samples), dot(rowB, samples)));
            }
            return luma.Sample(linearClamp, mapped).rgb;
        }
        // A four-neighbour spatial gradient, measured in real output-pixel units.
        // Derivatives adapt across window sizes, avoiding a permanently chunky outline.
        float2 pictureGradient(float2 uv, bool isYuv)
        {
            float2 stepUv = max(fwidth(uv), float2(0.001, 0.001)) / max(pointer.w, 0.25);
            float3 weights = float3(0.2126, 0.7152, 0.0722);
            float left = dot(readColour(uv - float2(stepUv.x, 0), isYuv), weights);
            float right = dot(readColour(uv + float2(stepUv.x, 0), isYuv), weights);
            float above = dot(readColour(uv - float2(0, stepUv.y), isYuv), weights);
            float below = dot(readColour(uv + float2(0, stepUv.y), isYuv), weights);
            return float2(right - left, below - above);
        }
        float3 effected(float2 uv, bool isYuv)
        {
            float2 warped = effectUv(uv);
            float3 colour = readColour(warped, isYuv);
            if (effect.y > 0 && effect.x > 0.5 && effect.x < 1.5)
            {
                float2 fringe = float2(0.005, 0.0015) * effect.y;
                colour.r = readColour(warped + fringe, isYuv).r;
                colour.b = readColour(warped - fringe, isYuv).b;
            }
            if (effect.y > 0 && effect.x > 1.5 && effect.x < 2.5)
            {
                float2 texel = float2(0.002, 0.002 * effect.w) / max(pointer.w, 0.25);
                float3 left = readColour(uv - float2(texel.x, 0), isYuv);
                float3 right = readColour(uv + float2(texel.x, 0), isYuv);
                float3 top = readColour(uv - float2(0, texel.y), isYuv);
                float3 bottom = readColour(uv + float2(0, texel.y), isYuv);
                float gx = dot(right - left, float3(0.2126, 0.7152, 0.0722));
                float gy = dot(bottom - top, float3(0.2126, 0.7152, 0.0722));
                float edge = saturate(sqrt(gx * gx + gy * gy) * 3.7);
                float3 ink = colour * 0.22 + edge * float3(0.12, 0.95, 1);
                colour = lerp(colour, ink, effect.y);
            }

            if (effect.y > 0 && effect.x > 4.5 && effect.x < 7.5)
            {
                float2 gradient = pictureGradient(uv, isYuv);
                float edge = saturate(length(gradient) * 4);
                float lum = dot(colour, float3(0.2126, 0.7152, 0.0722));
                float3 styled = colour;
                if (effect.x < 5.5) // Ink trace: paper-coloured rendering with dark outlines
                {
                    float ink = smoothstep(0.09, 0.47, edge);
                    styled = saturate(float3(0.96, 0.935, 0.86) - ink * 0.9 - (1 - lum) * 0.12);
                }
                else if (effect.x < 6.5) // Topographic: isolines of actual pixel luminance
                {
                    float iso = 1 - smoothstep(0.04, 0.12, abs(frac(lum * 14 * max(pointer.w, 0.25)) - 0.5));
                    float slopes = smoothstep(0.02, 0.2, edge);
                    float ridge = iso * slopes;
                    styled = saturate(colour * float3(0.23, 0.35, 0.48)
                        + ridge * float3(0.25, 0.92, 0.78) + edge * 0.12);
                }
                else // Chromatic contours: gradient orientation chooses the edge colour
                {
                    float angle = atan2(gradient.y, gradient.x);
                    float3 rainbow = 0.5 + 0.5 * cos(angle + float3(0, 2.094, 4.189));
                    styled = saturate(colour * 0.1 + edge * (0.3 + 0.7 * rainbow));
                }
                colour = lerp(colour, styled, effect.y);
            }
            if (effect.x > 11.5 && effect.x < 12.5 && effect.y > 0)
            {
                // Edge Gravity: derive a normal from gradients in the *current video frame*.
                // Flat regions stay still; high-contrast outlines become elastic folds that
                // pulse along their own normals. No fictional semantic/object tracking.
                float2 gradient = pictureGradient(uv, isYuv);
                float gradientSize = length(gradient);
                float edge = smoothstep(0.045, 0.28, gradientSize);
                float2 normal = gradient / max(gradientSize, 0.0001);
                float phase = sin(effect.z * 1.7 * max(pointer.w, 0.25) + uv.x * 12 + uv.y * 9);
                float2 warped = saturate(uv + normal * (0.028 * effect.y * edge * phase));
                float3 refracted = readColour(warped, isYuv);
                colour = lerp(colour, refracted, edge * effect.y);
                // Shimmer hugs edges; untextured backgrounds never illuminate.
                colour = saturate(colour + edge * (0.11 * effect.y) * float3(0.11, 0.65, 0.98));
            }
            if (effect.x > 12.5 && effect.x < 13.5 && effect.y > 0)
            {
                // Ghostwire: a glass-like phantom of the *whole* frame, woven from thin
                // luma-gradient ridges. Retain only a faint interior to suggest transparency.
                // Occluded surfaces cannot be reconstructed from ordinary video pixels.
                float2 gradient = pictureGradient(uv, isYuv);
                float energy = length(gradient);
                float2 direction = gradient / max(energy, 0.00001);
                float2 stepUv = max(fwidth(uv), float2(0.0005, 0.0005))
                    / max(pointer.w, 0.25);
                float2 offsetUv = direction * stepUv;
                float previous = length(pictureGradient(uv - offsetUv, isYuv));
                float next = length(pictureGradient(uv + offsetUv, isYuv));
                // Non-maximum suppression makes an edge into a filament instead of a
                // thick parallel band. The gradient threshold filters texture noise.
                float ridge = (energy >= previous && energy >= next) ? 1.0 : 0.0;
                float detail = max(pointer.w, 0.25);
                float thread = ridge * smoothstep(0.035 / detail, 0.19 / detail, energy);
                float luminance = dot(colour, float3(0.2126, 0.7152, 0.0722));
                // A second, dimmer displaced line makes the surface feel refractive,
                // with slow enough motion to avoid flashing between frames.
                float2 ghostUv = saturate(uv + direction * stepUv
                    * (1.1 + 0.25 * sin(effect.z * 0.7)));
                float3 ghostColour = readColour(ghostUv, isYuv);
                float ghostLuma = dot(ghostColour, float3(0.2126, 0.7152, 0.0722));
                float3 phantom = float3(0.012, 0.018, 0.034)
                    + float3(luminance, luminance, luminance) * 0.055
                    + float3(0.06, 0.09, 0.14) * ghostLuma * 0.16;
                phantom += thread * float3(0.18, 0.92, 0.88);
                phantom += smoothstep(0.06, 0.27, energy) * (1 - ridge)
                    * float3(0.12, 0.05, 0.19);
                colour = lerp(colour, saturate(phantom), effect.y);
            }
            if (effect.x > 13.5 && effect.x < 14.5 && effect.y > 0 && pointer.z > 0.5)
            {
                // Colour spotlight: the pointer chooses a *pixel colour*, not an object.
                // Similar colours are highlighted anywhere in the current picture.
                // With no pointer there is no modification and no stale selection.
                float3 chosen = readColour(pointer.xy, isYuv);
                float deviation = length(colour - chosen);
                float similarity = 1 - smoothstep(0.07 / max(pointer.w, 0.25),
                    0.38 / max(pointer.w, 0.25), deviation);
                float grey = dot(colour, float3(0.2126, 0.7152, 0.0722));
                float3 muted = lerp(colour, float3(grey, grey, grey) * 0.7, 0.82 * effect.y);
                float3 vivid = saturate(colour * (1 + effect.y * 0.24)
                    + similarity * effect.y * float3(0.03, 0.11, 0.17));
                colour = lerp(muted, vivid, similarity);
            }
            if (effect.x > 14.5 && effect.x < 15.5 && effect.y > 0)
            {
                // Relief etch: light falls across the *image gradient*. It is an
                // artistic surface-relief illusion, not an estimated depth map.
                float2 slope = pictureGradient(uv, isYuv) * (5 * max(pointer.w, 0.25));
                float3 normal = normalize(float3(-slope.x, -slope.y, 0.65));
                float directional = saturate(dot(normal, normalize(float3(-0.55, -0.4, 0.75))));
                float edge = smoothstep(0.04, 0.35, length(slope));
                float3 relief = saturate(colour * (0.35 + 0.95 * directional)
                    + edge * (directional - 0.5) * float3(0.21, 0.24, 0.22));
                colour = lerp(colour, relief, effect.y);
            }
            if (effect.x > 15.5 && effect.x < 16.5 && effect.y > 0)
            {
                // Ghostwire mask mode maps ONLY the visible frame's contrast into a
                // translucent-looking visual mask. It is not garment recognition, x-ray,
                // real transparency or reconstruction of any hidden anatomy.
                float2 gradient = pictureGradient(uv, isYuv);
                float edgeSize = length(gradient);
                float detail = max(pointer.w, 0.25);
                float edge = smoothstep(0.045 / detail, 0.22 / detail, edgeSize);
                float2 normal = gradient / max(edgeSize, 0.00001);
                float2 onePixel = max(fwidth(uv), float2(0.0005, 0.0005));
                float prior = length(pictureGradient(saturate(uv - normal * onePixel), isYuv));
                float later = length(pictureGradient(saturate(uv + normal * onePixel), isYuv));
                float filament = (edgeSize >= prior && edgeSize >= later) ? edge : edge * 0.16;
                float luminance = dot(colour, float3(0.2126, 0.7152, 0.0722));
                float2 weave = uv * float2(250 * detail, 220 * detail);
                float fabricPattern = sin(weave.x) * sin(weave.y);
                // Fine visible texture modulates a faint tinted layer. Uniform areas
                // remain subdued; strong original outlines glow like glass filaments.
                float haze = (0.02 + luminance * 0.06)
                    * (0.92 + 0.08 * fabricPattern);
                float3 maskColour = float3(0.012, 0.019, 0.032)
                    + colour * (0.07 + haze)
                    + edge * float3(0.025, 0.10, 0.12);
                maskColour += filament * float3(0.18, 0.80, 0.88);
                // Colour from the *same recorded pixel* gives the stylized mask
                // a local material-like tint without hallucinating anything behind it.
                maskColour += luminance * float3(0.009, 0.022, 0.035);
                colour = lerp(colour, saturate(maskColour), effect.y);
            }
            if (effect.x > 10.5 && effect.x < 11.5 && pointer.z > 0.5 && effect.y > 0)
            {
                // A fine highlight at the actual lens boundary communicates where the cursor is.
                float2 delta = (uv - pointer.xy) * float2(effect.w, 1);
                float dist = length(delta);
                float rim = 1 - smoothstep(0.003, 0.011, abs(dist - 0.23 / max(pointer.w, 0.25)));
                colour = saturate(colour + float3(0.23, 0.48, 0.57) * rim * effect.y);
            }
            return styledLook(saturate(colour));
        }
        float4 yuv(Vertex input) : SV_Target
        {
            return float4(effected(input.uv, true), 1);
        }
        float4 bgra(Vertex input) : SV_Target
        {
            return float4(effected(input.uv, false), 1);
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
    private ID3D11Texture2D? _analysisStaging;
    private (int Width, int Height, DXGI_FORMAT Format) _analysisSize;
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
            ByteWidth = 128,
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
    public void Draw(ReadOnlySpan<float> colourMatrix, int x, int y, int width, int height, (float Left, float Top, float Right, float Bottom) source, bool smoothChroma, bool clear = true, int look = 0, int effect = 0, float strength = 0.65f, float seconds = 0, float pointerX = 0.5f, float pointerY = 0.5f, bool pointerActive = false, float lookIntensity = 1, float lookDetail = 1, float effectDetail = 1)
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

        Span<float> constants = stackalloc float[32];
        constants.Clear();
        colourMatrix.CopyTo(constants);
        (constants[12], constants[13]) = (_crop.U, _crop.V);
        (constants[16], constants[17], constants[18], constants[19]) = source;
        constants[20] = look;
        constants[21] = Math.Clamp(lookIntensity, 0, 1.5f);
        constants[22] = Math.Clamp(lookDetail, 0.5f, 1.5f);
        constants[24] = effect;
        constants[25] = Math.Clamp(strength, 0, 1);
        constants[26] = float.IsFinite(seconds) ? seconds : 0;
        constants[27] = height > 0 ? (float)width / height : 1;
        constants[28] = Math.Clamp(pointerX, 0, 1);
        constants[29] = Math.Clamp(pointerY, 0, 1);
        constants[30] = pointerActive ? 1 : 0;
        constants[31] = Math.Clamp(effectDetail, 0.25f, 1.75f);
        fixed (float* values = constants)
        {
            _context.UpdateSubresource(_colour, 0, null, values, 128, 0);
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

    /// <summary>
    /// Copies a decoder's planar video surface to caller-owned system memory for opt-in region
    /// tracking. A staging texture is reused across frames. Neither the decoder surface nor its
    /// GPU texture is modified. This readback is deliberately not on the normal playback path.
    /// </summary>
    public void CopySurfacePlanes(D3D11Surface surface, int width, int height, bool tenBit,
        Span<byte> yPlane, int yStride, Span<byte> uvPlane, int uvStride)
    {
        ArgumentNullException.ThrowIfNull(surface);
        D3D11_TEXTURE2D_DESC source;
        surface.Texture.GetDesc(&source);
        var sourceWidth = (int)source.Width;
        var sourceHeight = (int)source.Height;
        var bytes = tenBit ? 2 : 1;
        var lumaRow = width * bytes;
        var uvRow = ((width + 1) / 2) * 2 * bytes;
        var uvRows = (height + 1) / 2;
        if (width <= 0 || height <= 0 || width > sourceWidth || height > sourceHeight
            || yStride < lumaRow || uvStride < uvRow
            || yPlane.Length < (height - 1) * yStride + lumaRow
            || uvPlane.Length < (uvRows - 1) * uvStride + uvRow)
        {
            throw new ArgumentException("The destination planes do not fit the decoded surface.");
        }

        var size = (sourceWidth, sourceHeight, source.Format);
        if (_analysisStaging is null || _analysisSize != size)
        {
            if (_analysisStaging is not null)
            {
                Marshal.ReleaseComObject(_analysisStaging);
            }

            _analysisStaging = Texture(sourceWidth, sourceHeight, source.Format,
                D3D11_USAGE.D3D11_USAGE_STAGING, 0, D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ);
            _analysisSize = size;
        }

        _context.CopySubresourceRegion(_analysisStaging, 0, 0, 0, 0,
            surface.Texture, surface.Index, null);
        D3D11_MAPPED_SUBRESOURCE mapped;
        _context.Map(_analysisStaging, 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped);
        try
        {
            var rowPitch = checked((int)mapped.RowPitch);
            for (var row = 0; row < height; row++)
            {
                new ReadOnlySpan<byte>((byte*)mapped.pData + row * rowPitch, lumaRow)
                    .CopyTo(yPlane.Slice(row * yStride, lumaRow));
            }

            var chromaStart = (byte*)mapped.pData + sourceHeight * rowPitch;
            for (var row = 0; row < uvRows; row++)
            {
                new ReadOnlySpan<byte>(chromaStart + row * rowPitch, uvRow)
                    .CopyTo(uvPlane.Slice(row * uvStride, uvRow));
            }
        }
        finally
        {
            _context.Unmap(_analysisStaging, 0);
        }
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
        if (_analysisStaging is not null)
        {
            Marshal.ReleaseComObject(_analysisStaging);
            _analysisStaging = null;
        }

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
