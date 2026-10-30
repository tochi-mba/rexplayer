using Rex.Media.Video;

namespace Rex.Media.AppCore.Visuals.Stage;

/// <summary>
/// Echo Clones: the listener's last moments as a cascade of outlines behind them, a tenth of a
/// second apart, spreading wider with the bass and jumping on the beat, each in its own colour of
/// the palette. The listener stays in front in their own colours, rimmed with light.
/// </summary>
public sealed class EchoClonesEffect : CameraEffect
{
    /// <summary>How many past outlines are kept.</summary>
    public const int Echoes = 6;

    /// <summary>Seconds between the outlines kept.</summary>
    public const float Interval = 0.1f;

    private static readonly StageProgram Store = new("echo-store", StageShaders.Prelude + """
        float4 ps(Vertex v) : SV_Target { return float4(t0.SampleLevel(clampLinear, v.uv, 0).r, 0, 0, 1); }
        """);

    private static readonly StageProgram Compose = new("echo-compose", StageShaders.Prelude + """
        // t0..t5 the outlines, newest first; t6 the camera; t7 the matte now. c[16].x the spread.
        float echo(Texture2D outline, float2 uv, float k)
        {
            float2 direction = float2(sin(TIME * 0.3 + k * 0.7), -0.35 - 0.15 * cos(TIME * 0.2));
            float2 at = sceneUV(uv - direction * (k + 1) * c[16].x * float2(1 / ASPECT, 1));
            return outline.SampleLevel(clampLinear, at, 0).r;
        }
        float4 ps(Vertex v) : SV_Target
        {
            float3 light = paint(0.8) * 0.03 * (1.2 - length(v.uv - 0.5));
            float echoes[6];
            echoes[0] = echo(t0, v.uv, 0);
            echoes[1] = echo(t1, v.uv, 1);
            echoes[2] = echo(t2, v.uv, 2);
            echoes[3] = echo(t3, v.uv, 3);
            echoes[4] = echo(t4, v.uv, 4);
            echoes[5] = echo(t5, v.uv, 5);
            [unroll] for (int k = 5; k >= 0; k--)
            {
                float fade = 1 - k / 7.0;
                float3 colour = paint(k / 6.0 + TIME * 0.06);
                // Each outline glows at its edge and fills softly inside.
                float m = echoes[k];
                float rim = m * (1 - m) * 4;
                light = lerp(light, colour * (0.25 + LOUD * 0.6) * fade, m * 0.7) + colour * rim * (0.6 + BEAT) * fade;
            }

            float2 scene = sceneUV(v.uv);
            float now = t7.SampleLevel(clampLinear, scene, 0).r;
            float3 camera = t6.SampleLevel(clampLinear, cameraUV(v.uv), 0).rgb;
            float2 step = float2(2.0 / 192, 2.0 / 144);
            float edge = abs(t7.SampleLevel(clampLinear, scene + float2(step.x, 0), 0).r - t7.SampleLevel(clampLinear, scene - float2(step.x, 0), 0).r)
                + abs(t7.SampleLevel(clampLinear, scene + float2(0, step.y), 0).r - t7.SampleLevel(clampLinear, scene - float2(0, step.y), 0).r);
            light = lerp(light, camera * 1.05, now) + paint(TIME * 0.06) * saturate(edge) * (1.2 + PUNCH * 2);
            return float4(light, 1);
        }
        """);

    private readonly StageTexture[] _outlines = new StageTexture[Echoes];
    private float _sinceStored = float.MaxValue;
    private int _newest = -1;

    public override IReadOnlyList<StageProgram> Programs { get; } = [Store, Compose];

    public override float Bloom => 0.8f;

    public override void Draw(CameraStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        var gpu = stage.Gpu;
        for (var i = 0; i < Echoes; i++)
        {
            stage.Keep(ref _outlines[i], stage.Matte.Width, stage.Matte.Height, StageFormat.Float1);
        }

        _sinceStored += stage.Dt;
        if (_sinceStored >= Interval || _newest < 0)
        {
            _newest = (_newest + 1) % Echoes;
            gpu.Pass(Store, _outlines[_newest], [stage.Matte], stage.Constants());
            if (_sinceStored == float.MaxValue)
            {
                // The first outline fills every echo, so nothing old and empty shows at the start.
                for (var i = 0; i < Echoes; i++)
                {
                    if (i != _newest)
                    {
                        gpu.Pass(Store, _outlines[i], [stage.Matte], stage.Constants());
                    }
                }
            }

            _sinceStored = 0;
        }

        var c = stage.Constants();
        var pulse = stage.Pulse!;
        c[StageShaders.EffectRow * 4] = 0.035f + (pulse.Bass * 0.05f) + (pulse.Punch * 0.03f);
        Span<StageTexture> inputs = stackalloc StageTexture[8];
        for (var k = 0; k < Echoes; k++)
        {
            inputs[k] = _outlines[((_newest - k) % Echoes + Echoes) % Echoes];
        }

        (inputs[6], inputs[7]) = (stage.Camera, stage.Matte);
        gpu.Pass(Compose, stage.Light, inputs, c);
    }

    public override void Release(IStageGpu gpu)
    {
        for (var i = 0; i < Echoes; i++)
        {
            CameraStage.Drop(gpu, ref _outlines[i]);
        }

        (_newest, _sinceStored) = (-1, float.MaxValue);
    }
}
