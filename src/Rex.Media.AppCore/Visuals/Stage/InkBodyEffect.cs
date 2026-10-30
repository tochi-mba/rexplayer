using Rex.Media.Video;

namespace Rex.Media.AppCore.Visuals.Stage;

/// <summary>
/// Ink Body: the listener pours ink into a fluid. A stable-fluids simulation (Stam, "Stable
/// Fluids", SIGGRAPH 1999) runs at a quarter of the stage: the outline's own motion and the
/// fingertips stir it, the kick pushes out from the body, and the pressure is solved each frame so
/// the ink swirls rather than smears. Ink pours from the outline's edge in the palette's colours,
/// a pinched fingertip pours it bright, and the camera's picture is a dim stage underneath.
/// </summary>
public sealed class InkBodyEffect : CameraEffect
{
    /// <summary>Pressure solver iterations a frame: enough for the swirls to hold on a laptop's graphics.</summary>
    public const int Iterations = 18;

    private static readonly StageProgram AdvectVelocity = new("ink-advect-velocity", StageShaders.Prelude + """
        // t0 velocity (sim pixels a second), t1 matte, t2 flow. c[16].xy one sim texel, c[16].zw sim size.
        float4 ps(Vertex v) : SV_Target
        {
            float2 texel = c[16].xy;
            float2 velocity = t0.SampleLevel(clampLinear, v.uv, 0).xy;
            float2 back = v.uv - velocity * DT * texel;
            velocity = t0.SampleLevel(clampLinear, back, 0).xy * pow(0.55, DT);

            // The outline's own motion: the flow is in matte pixels a frame at about 30 frames a second.
            float2 scene = sceneUV(v.uv);
            float matte = t1.SampleLevel(clampLinear, scene, 0).r;
            float2 flow = t2.SampleLevel(clampLinear, scene, 0).xy;
            float2 flowStage = flow / float2(192, 144) / c[3].xy * 30;
            velocity += flowStage * c[16].zw * matte * 0.35;

            // The fingertips stir it where they move.
            float2 here = even(v.uv);
            [unroll] for (int h = 0; h < 2; h++)
            {
                float4 tip = c[9 + h];
                float present = c[11][h * 2];
                float d = length(here - even(tip.xy));
                velocity += tip.zw * c[16].zw * present * exp(-d * d / 0.0025) * 0.5;
            }

            // The kick pushes out from the body; the middle keeps the ink turning.
            float2 centre = c[12].w > 0.5 ? c[12].xy : float2(0.5, 0.55);
            float2 away = even(v.uv) - even(centre);
            float ring = exp(-pow(length(away) - 0.25, 2) / 0.02);
            velocity += normalize(away + 1e-5) * PUNCH * ring * c[16].z * 0.8;
            float swirl = fbm(v.uv * 3 + TIME * 0.05) - 0.5;
            velocity += float2(-away.y, away.x) * swirl * MID * c[16].z * 0.6;
            return float4(velocity, 0, 1);
        }
        """);

    private static readonly StageProgram Divergence = new("ink-divergence", StageShaders.Prelude + """
        float4 ps(Vertex v) : SV_Target
        {
            float2 texel = c[16].xy;
            float l = t0.SampleLevel(pointClamp, v.uv - float2(texel.x, 0), 0).x;
            float r = t0.SampleLevel(pointClamp, v.uv + float2(texel.x, 0), 0).x;
            float t = t0.SampleLevel(pointClamp, v.uv - float2(0, texel.y), 0).y;
            float b = t0.SampleLevel(pointClamp, v.uv + float2(0, texel.y), 0).y;
            return float4(0.5 * (r - l + b - t), 0, 0, 1);
        }
        """);

    private static readonly StageProgram Jacobi = new("ink-jacobi", StageShaders.Prelude + """
        // t0 pressure, t1 divergence.
        float4 ps(Vertex v) : SV_Target
        {
            float2 texel = c[16].xy;
            float l = t0.SampleLevel(pointClamp, v.uv - float2(texel.x, 0), 0).x;
            float r = t0.SampleLevel(pointClamp, v.uv + float2(texel.x, 0), 0).x;
            float t = t0.SampleLevel(pointClamp, v.uv - float2(0, texel.y), 0).x;
            float b = t0.SampleLevel(pointClamp, v.uv + float2(0, texel.y), 0).x;
            float divergence = t1.SampleLevel(pointClamp, v.uv, 0).x;
            return float4((l + r + t + b - divergence) * 0.25, 0, 0, 1);
        }
        """);

    private static readonly StageProgram Project = new("ink-project", StageShaders.Prelude + """
        // t0 velocity, t1 pressure: what is left once the pressure's push is taken away swirls.
        float4 ps(Vertex v) : SV_Target
        {
            float2 texel = c[16].xy;
            float l = t1.SampleLevel(pointClamp, v.uv - float2(texel.x, 0), 0).x;
            float r = t1.SampleLevel(pointClamp, v.uv + float2(texel.x, 0), 0).x;
            float t = t1.SampleLevel(pointClamp, v.uv - float2(0, texel.y), 0).x;
            float b = t1.SampleLevel(pointClamp, v.uv + float2(0, texel.y), 0).x;
            float2 velocity = t0.SampleLevel(pointClamp, v.uv, 0).xy - 0.5 * float2(r - l, b - t);
            return float4(velocity, 0, 1);
        }
        """);

    private static readonly StageProgram AdvectInk = new("ink-advect-ink", StageShaders.Prelude + """
        // t0 ink, t1 velocity, t2 matte. c[16].xy one velocity texel.
        float4 ps(Vertex v) : SV_Target
        {
            float2 velocity = t1.SampleLevel(clampLinear, v.uv, 0).xy;
            float3 ink = t0.SampleLevel(clampLinear, v.uv - velocity * DT * c[16].xy, 0).rgb * pow(0.2, DT);

            // Ink pours from the outline's edge, coloured along the body and through the song.
            float2 scene = sceneUV(v.uv);
            float2 step = float2(1.5 / 192, 1.5 / 144);
            float m = t2.SampleLevel(clampLinear, scene, 0).r;
            float edge = abs(t2.SampleLevel(clampLinear, scene + float2(step.x, 0), 0).r - t2.SampleLevel(clampLinear, scene - float2(step.x, 0), 0).r)
                + abs(t2.SampleLevel(clampLinear, scene + float2(0, step.y), 0).r - t2.SampleLevel(clampLinear, scene - float2(0, step.y), 0).r);
            float3 colour = paint(v.uv.y * 0.6 + TIME * 0.04 + BASS * 0.2);
            ink += colour * saturate(edge) * (0.6 + LOUD + BEAT * 0.8) * DT * 5;

            // A pinch pours it bright from the fingertip.
            float2 here = even(v.uv);
            [unroll] for (int h = 0; h < 2; h++)
            {
                float4 tip = c[9 + h];
                float d = length(here - even(tip.xy));
                float pour = c[11][h * 2] * (0.6 + c[11][h * 2 + 1] * 2);
                ink += paint(0.15 + h * 0.5 + TIME * 0.07) * pour * exp(-d * d / 0.0009) * DT * 12;
            }

            return float4(min(ink, 3), 1);
        }
        """);

    private static readonly StageProgram Compose = new("ink-compose", StageShaders.Prelude + """
        // t0 ink, t1 camera, t2 matte.
        float4 ps(Vertex v) : SV_Target
        {
            float3 camera = t1.SampleLevel(clampLinear, cameraUV(v.uv), 0).rgb;
            float matte = t2.SampleLevel(clampLinear, sceneUV(v.uv), 0).r;
            float3 ink = t0.SampleLevel(clampLinear, v.uv, 0).rgb;
            // The listener stays visible through the ink, the room fades into the dark.
            float3 stage = camera * lerp(0.05, 0.32, matte) * lerp(float3(0.7, 0.8, 1), 1, matte);
            // Faint ink drops away and bright filaments stay, so the swirls read against the dark.
            ink = ink * ink / (ink + 0.12);
            float3 colour = stage + ink * (1.2 + BEAT * 0.5) + paint(0.9) * DROP * 0.4;
            return float4(colour, 1);
        }
        """);

    private StageTexture _velocity;
    private StageTexture _velocityNext;
    private StageTexture _pressure;
    private StageTexture _pressureNext;
    private StageTexture _divergence;
    private StageTexture _ink;
    private StageTexture _inkNext;

    public override IReadOnlyList<StageProgram> Programs { get; } = [AdvectVelocity, Divergence, Jacobi, Project, AdvectInk, Compose];

    public override float Bloom => 1.1f;

    public override void Draw(CameraStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        var gpu = stage.Gpu;
        var (simWidth, simHeight) = (Math.Max(64, stage.Width / 4), Math.Max(36, stage.Height / 4));
        var (inkWidth, inkHeight) = (Math.Max(128, stage.Width / 2), Math.Max(72, stage.Height / 2));
        stage.Keep(ref _velocity, simWidth, simHeight, StageFormat.Half4);
        stage.Keep(ref _velocityNext, simWidth, simHeight, StageFormat.Half4);
        stage.Keep(ref _pressure, simWidth, simHeight, StageFormat.Half4);
        stage.Keep(ref _pressureNext, simWidth, simHeight, StageFormat.Half4);
        stage.Keep(ref _divergence, simWidth, simHeight, StageFormat.Half4);
        stage.Keep(ref _ink, inkWidth, inkHeight, StageFormat.Half4);
        stage.Keep(ref _inkNext, inkWidth, inkHeight, StageFormat.Half4);

        var c = Sim(stage, simWidth, simHeight);
        gpu.Pass(AdvectVelocity, _velocityNext, [_velocity, stage.Matte, stage.Flow], c);
        (_velocity, _velocityNext) = (_velocityNext, _velocity);
        gpu.Pass(Divergence, _divergence, [_velocity], c);
        gpu.Clear(_pressure, 0, 0, 0, 0);
        for (var i = 0; i < Iterations; i++)
        {
            gpu.Pass(Jacobi, _pressureNext, [_pressure, _divergence], c);
            (_pressure, _pressureNext) = (_pressureNext, _pressure);
        }

        gpu.Pass(Project, _velocityNext, [_velocity, _pressure], c);
        (_velocity, _velocityNext) = (_velocityNext, _velocity);

        gpu.Pass(AdvectInk, _inkNext, [_ink, _velocity, stage.Matte], c);
        (_ink, _inkNext) = (_inkNext, _ink);
        gpu.Pass(Compose, stage.Light, [_ink, stage.Camera, stage.Matte], stage.Constants());
    }

    /// <summary>The simulation's constants: one texel and its size, for the passes that step across it.</summary>
    private static Span<float> Sim(CameraStage stage, int width, int height)
    {
        var c = stage.Constants();
        (c[64], c[65], c[66], c[67]) = (1f / width, 1f / height, width, height);
        return c;
    }

    public override void Release(IStageGpu gpu)
    {
        CameraStage.Drop(gpu, ref _velocity);
        CameraStage.Drop(gpu, ref _velocityNext);
        CameraStage.Drop(gpu, ref _pressure);
        CameraStage.Drop(gpu, ref _pressureNext);
        CameraStage.Drop(gpu, ref _divergence);
        CameraStage.Drop(gpu, ref _ink);
        CameraStage.Drop(gpu, ref _inkNext);
    }
}
