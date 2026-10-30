using System.Runtime.Versioning;
using Rex.Media.Interop.Graphics;

namespace Rex.Media.Video.D3D11;

/// <summary>
/// The camera stage's <see cref="IStageGpu"/> on Direct3D 11: maps the portable stage's textures,
/// formats and blends onto <see cref="D3D11Stage"/>. The stage owns the device; one thread draws.
/// </summary>
[SupportedOSPlatform("windows8.0")]
public sealed class D3D11StageGpu : IStageGpu, IDisposable
{
    private readonly D3D11Stage _stage;
    private readonly int[] _inputs = new int[IStageGpu.MaxInputs];

    private D3D11StageGpu(D3D11Stage stage) => _stage = stage;

    public int Width => _stage.TargetSize.Width;

    public int Height => _stage.TargetSize.Height;

    public bool IsSoftware => _stage.IsSoftware;

    /// <summary>
    /// Draws into a swap chain for a XAML swap-chain panel. <paramref name="swapChain"/> is its
    /// IUnknown with a reference the caller hands to the panel and then releases.
    /// </summary>
    public static D3D11StageGpu ForComposition(int width, int height, out nint swapChain, bool software = false)
    {
        var stage = D3D11Stage.Create(software);
        swapChain = stage.AttachComposition(width, height);
        return new D3D11StageGpu(stage);
    }

    /// <summary>Draws offscreen at this size, for tests and recordings.</summary>
    public static D3D11StageGpu Offscreen(int width, int height, bool software = true)
    {
        var stage = D3D11Stage.Create(software);
        stage.UseOffscreen(width, height);
        return new D3D11StageGpu(stage);
    }

    public void SetScale(float scaleX, float scaleY) => _stage.SetCompositionScale(scaleX, scaleY);

    public void Resize(int width, int height) => _stage.Resize(width, height);

    public void Present(bool waitForRefresh) => _stage.Present(waitForRefresh);

    public void ReadBack(Span<byte> bgra, int stride) => _stage.ReadBack(bgra, stride);

    public StageTexture Texture(int width, int height, StageFormat format) =>
        new(_stage.CreateTexture(width, height, Pixels(format)), width, height, format);

    public void Release(StageTexture texture) => _stage.ReleaseTexture(texture.Id);

    public void Upload(StageTexture texture, ReadOnlySpan<byte> pixels, int rowBytes) => _stage.Upload(texture.Id, pixels, rowBytes);

    public void Upload(StageTexture texture, ReadOnlySpan<float> values, int rowValues) =>
        _stage.Upload(texture.Id, System.Runtime.InteropServices.MemoryMarshal.AsBytes(values), rowValues * sizeof(float));

    public void Clear(StageTexture? target, float red, float green, float blue, float alpha) =>
        _stage.Clear(target?.Id ?? 0, red, green, blue, alpha);

    public void Pass(StageProgram program, StageTexture? target, ReadOnlySpan<StageTexture> inputs, ReadOnlySpan<float> constants, StageBlend blend = StageBlend.Replace)
    {
        ArgumentNullException.ThrowIfNull(program);
        _stage.Pass(program.Source, target?.Id ?? 0, Ids(inputs), constants, Mode(blend));
    }

    public void Points(StageProgram program, StageTexture? target, ReadOnlySpan<StageTexture> inputs, int count, ReadOnlySpan<float> constants, StageBlend blend = StageBlend.Add)
    {
        ArgumentNullException.ThrowIfNull(program);
        _stage.Points(program.Source, target?.Id ?? 0, Ids(inputs), count, constants, Mode(blend));
    }

    public void Dispose() => _stage.Dispose();

    private ReadOnlySpan<int> Ids(ReadOnlySpan<StageTexture> inputs)
    {
        if (inputs.Length > _inputs.Length)
        {
            throw new ArgumentException($"A draw reads at most {IStageGpu.MaxInputs} textures.", nameof(inputs));
        }

        for (var i = 0; i < inputs.Length; i++)
        {
            _inputs[i] = inputs[i].Id;
        }

        return _inputs.AsSpan(0, inputs.Length);
    }

    private static StagePixels Pixels(StageFormat format) => format switch
    {
        StageFormat.Float1 => StagePixels.Float1,
        StageFormat.Float2 => StagePixels.Float2,
        StageFormat.Float4 => StagePixels.Float4,
        StageFormat.Half4 => StagePixels.Half4,
        _ => StagePixels.Bgra8,
    };

    private static StageBlendMode Mode(StageBlend blend) => blend switch
    {
        StageBlend.Add => StageBlendMode.Add,
        StageBlend.Over => StageBlendMode.Over,
        _ => StageBlendMode.Replace,
    };
}
