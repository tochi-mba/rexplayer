namespace Rex.Media.Video;

/// <summary>What a stage texture holds per pixel.</summary>
public enum StageFormat
{
    /// <summary>8-bit blue, green, red and alpha: the camera's picture, the cover.</summary>
    Bgra8,

    /// <summary>One 32-bit float: a matte, a level.</summary>
    Float1,

    /// <summary>Two 32-bit floats: a flow field, a velocity.</summary>
    Float2,

    /// <summary>Four 32-bit floats: particle positions and velocities, where precision matters.</summary>
    Float4,

    /// <summary>Four 16-bit floats: light, trails, dye; brighter than white is kept for the tone curve.</summary>
    Half4,
}

/// <summary>How a draw is combined with what its target holds.</summary>
public enum StageBlend
{
    /// <summary>The draw replaces the target.</summary>
    Replace,

    /// <summary>The draw's light is added: glows and particles brighten each other.</summary>
    Add,

    /// <summary>The draw lies over the target by its alpha.</summary>
    Over,
}

/// <summary>A texture the stage draws into or reads from, made by <see cref="IStageGpu.Texture"/>.</summary>
public readonly record struct StageTexture(int Id, int Width, int Height, StageFormat Format);

/// <summary>
/// A shader program for the stage, in HLSL: <c>ps</c> is its pixel shader; a points program also
/// has <c>vsPoints</c>, run once per corner of each point's quad (vertex 0 to 3, instance the point).
/// Programs are compiled once per process on first use and kept between runs.
/// </summary>
public sealed record StageProgram(string Name, string Source, bool Points = false);

/// <summary>
/// The graphics card as the camera stage uses it (ADR-0022): textures, full-screen passes and
/// instanced points, nothing else, so the same effects can be run by Direct3D on Windows and by
/// Metal on a Mac. A pass draws <see cref="StageProgram"/>'s <c>ps</c> over a whole target; inputs are
/// bound to <c>t0</c> upward, the constants to <c>cbuffer Stage : register(b0) { float4 c[64]; }</c>,
/// and the samplers are <c>s0</c> linear clamped, <c>s1</c> linear wrapped and <c>s2</c> nearest. A null
/// target is the stage's output, <see cref="Width"/> by <see cref="Height"/>.
/// </summary>
public interface IStageGpu
{
    /// <summary>The most constants a draw takes: 64 rows of four.</summary>
    const int MaxConstants = 256;

    /// <summary>The most input textures a draw reads.</summary>
    const int MaxInputs = 8;

    int Width { get; }

    int Height { get; }

    StageTexture Texture(int width, int height, StageFormat format);

    void Release(StageTexture texture);

    /// <summary>Copies bytes into a <see cref="StageFormat.Bgra8"/> texture, <paramref name="rowBytes"/> apart.</summary>
    void Upload(StageTexture texture, ReadOnlySpan<byte> pixels, int rowBytes);

    /// <summary>Copies floats into a float texture, <paramref name="rowValues"/> apart.</summary>
    void Upload(StageTexture texture, ReadOnlySpan<float> values, int rowValues);

    void Clear(StageTexture? target, float red, float green, float blue, float alpha);

    void Pass(StageProgram program, StageTexture? target, ReadOnlySpan<StageTexture> inputs, ReadOnlySpan<float> constants, StageBlend blend = StageBlend.Replace);

    /// <summary>Draws <paramref name="count"/> quads, each placed by the program's <c>vsPoints</c> (which may read the inputs too).</summary>
    void Points(StageProgram program, StageTexture? target, ReadOnlySpan<StageTexture> inputs, int count, ReadOnlySpan<float> constants, StageBlend blend = StageBlend.Add);
}
