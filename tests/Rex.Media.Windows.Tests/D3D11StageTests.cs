using Rex.Media.Video;
using Rex.Media.Video.D3D11;

namespace Rex.Media.Windows.Tests;

/// <summary>
/// The camera stage's Direct3D runner (ADR-0022), drawn for real in software (WARP) offscreen and
/// read back: passes with constants, textures drawn into and read, uploads, additive points, and
/// the refusals that keep a draw from reading what it writes.
/// </summary>
public sealed class D3D11StageTests
{
    private const string Prelude = """
        cbuffer Stage : register(b0) { float4 c[64]; };
        Texture2D t0 : register(t0);
        SamplerState clampLinear : register(s0);
        SamplerState pointClamp : register(s2);
        struct Vertex { float4 position : SV_Position; float2 uv : TEXCOORD0; };
        """;

    private static readonly StageProgram Fill = new("fill", Prelude + "float4 ps(Vertex v) : SV_Target { return c[0]; }");

    private static readonly StageProgram Copy = new("copy", Prelude + "float4 ps(Vertex v) : SV_Target { return t0.SampleLevel(pointClamp, v.uv, 0) * c[0].x; }");

    // Each point is a quad of a quarter of the output, its corner at c[instance].xy in clip space.
    private static readonly StageProgram Squares = new("squares", Prelude + """
        struct Point { float4 position : SV_Position; float4 colour : COLOR0; };
        Point vsPoints(uint corner : SV_VertexID, uint instance : SV_InstanceID)
        {
            Point p;
            float2 at = float2(corner & 1, corner >> 1);
            p.position = float4(c[instance].xy + at * 1.0, 0, 1);
            p.colour = c[8];
            return p;
        }
        float4 ps(Point p) : SV_Target { return p.colour; }
        """, Points: true);

    private static byte[] Read(D3D11StageGpu stage)
    {
        var pixels = new byte[stage.Width * stage.Height * 4];
        stage.ReadBack(pixels, stage.Width * 4);
        return pixels;
    }

    private static (byte B, byte G, byte R) At(byte[] pixels, int width, int x, int y)
    {
        var at = ((y * width) + x) * 4;
        return (pixels[at], pixels[at + 1], pixels[at + 2]);
    }

    [Fact]
    public void APassFillsTheOutputWithItsConstants()
    {
        using var stage = D3D11StageGpu.Offscreen(16, 8);
        stage.Pass(Fill, null, [], [1, 0.5f, 0, 1]);

        var pixels = Read(stage);
        Assert.Equal((16, 8), (stage.Width, stage.Height));
        Assert.True(stage.IsSoftware);
        var (b, g, r) = At(pixels, 16, 3, 3);
        Assert.Equal(255, r);
        Assert.InRange(g, 126, 129);
        Assert.Equal(0, b);
    }

    [Fact]
    public void ATextureDrawnIntoIsReadByTheNextPass()
    {
        using var stage = D3D11StageGpu.Offscreen(8, 8);
        var light = stage.Texture(8, 8, StageFormat.Half4);
        stage.Pass(Fill, light, [], [0.25f, 0.25f, 0.25f, 1]);
        // Doubled on the way out: a half-float texture holds the value exactly.
        stage.Pass(Copy, null, [light], [2]);

        var (b, g, r) = At(Read(stage), 8, 4, 4);
        Assert.InRange(r, 126, 129);
        Assert.InRange(g, 126, 129);
        Assert.InRange(b, 126, 129);
        stage.Release(light);
    }

    [Fact]
    public void UploadedPixelsAndFloatsAreWhatIsRead()
    {
        using var stage = D3D11StageGpu.Offscreen(4, 4);
        var picture = stage.Texture(4, 4, StageFormat.Bgra8);
        var pixels = new byte[4 * 4 * 4];
        for (var i = 0; i < 16; i++)
        {
            (pixels[i * 4], pixels[(i * 4) + 1], pixels[(i * 4) + 2], pixels[(i * 4) + 3]) = (10, 20, 200, 255);
        }

        stage.Upload(picture, pixels, 16);
        stage.Pass(Copy, null, [picture], [1]);
        Assert.Equal((10, 20, 200), At(Read(stage), 4, 1, 1));

        var matte = stage.Texture(4, 4, StageFormat.Float1);
        var values = new float[16];
        Array.Fill(values, 0.5f);
        stage.Upload(matte, values, 4);
        stage.Pass(Copy, null, [matte], [1]);
        // A one-channel texture reads as (value, 0, 0, 1).
        var (b, g, r) = At(Read(stage), 4, 2, 2);
        Assert.InRange(r, 126, 129);
        Assert.Equal((0, 0), (b, g));
    }

    [Fact]
    public void PointsAreDrawnOncePerInstanceAndAddTheirLight()
    {
        using var stage = D3D11StageGpu.Offscreen(8, 8);
        stage.Clear(null, 0, 0, 0, 1);
        var constants = new float[IStageGpu.MaxConstants];
        // Two quads both covering the top left quarter, each adding 0.25 of red.
        (constants[0], constants[1]) = (-1, 0);
        (constants[4], constants[5]) = (-1, 0);
        (constants[32], constants[35]) = (0.25f, 1);
        stage.Points(Squares, null, [], 2, constants);

        var pixels = Read(stage);
        Assert.InRange(At(pixels, 8, 1, 1).R, 126, 129);
        Assert.Equal(0, At(pixels, 8, 6, 6).R);

        // No points: nothing drawn, nothing compiled.
        stage.Points(Squares, null, [], 0, constants);
    }

    [Fact]
    public void AnOverDrawLiesOnTopByItsAlpha()
    {
        using var stage = D3D11StageGpu.Offscreen(4, 4);
        stage.Clear(null, 0, 0, 1, 1);
        stage.Pass(Fill, null, [], [1, 0, 0, 0.5f], StageBlend.Over);

        var (b, _, r) = At(Read(stage), 4, 1, 1);
        Assert.InRange(r, 126, 129);
        Assert.InRange(b, 126, 129);
    }

    [Fact]
    public void ADrawMayNotReadWhatItWritesNorTooManyThings()
    {
        using var stage = D3D11StageGpu.Offscreen(4, 4);
        var texture = stage.Texture(4, 4, StageFormat.Half4);
        Assert.Throws<ArgumentException>(() => stage.Pass(Copy, texture, [texture], [1]));
        Assert.Throws<ArgumentException>(() => stage.Pass(Fill, null, new StageTexture[IStageGpu.MaxInputs + 1], []));
        Assert.Throws<ArgumentException>(() => stage.Pass(Fill, null, [], new float[IStageGpu.MaxConstants + 1]));
        Assert.Throws<ArgumentException>(() => stage.Upload(texture, new byte[3], 32));
        stage.Release(texture);
        Assert.Throws<ArgumentException>(() => stage.Pass(Copy, null, [texture], [1]));
    }

    [Fact]
    public void AProgramThatDoesNotCompileSaysWhy()
    {
        using var stage = D3D11StageGpu.Offscreen(4, 4);
        var broken = new StageProgram("broken", Prelude + "float4 ps(Vertex v) : SV_Target { return nothing; }");
        var error = Assert.Throws<InvalidOperationException>(() => stage.Pass(broken, null, [], []));
        Assert.Contains("nothing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResizingAnOffscreenStageMakesANewPicture()
    {
        using var stage = D3D11StageGpu.Offscreen(4, 4);
        stage.Resize(12, 6);
        stage.Pass(Fill, null, [], [0, 1, 0, 1]);
        stage.Present(false);
        Assert.Equal((12, 6), (stage.Width, stage.Height));
        Assert.Equal(255, At(Read(stage), 12, 11, 5).G);
    }
}
