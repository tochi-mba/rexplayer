using Rex.Media.Video;
using Rex.Media.Vision;

namespace Rex.Media.AppCore.Visuals.Stage;

/// <summary>
/// Neon Skeleton: every bone of both hands and the body as a line of light, thickening on the kick,
/// the hands' bright with the treble and the body's with the bass; the joints as sparks, the
/// fingertips brightest; and, with both hands up, plasma arcing between matching fingertips, wilder
/// with the treble. Everything leaves trails that drift outward with the bass. The listener's own
/// picture shows faintly behind, so the light is clearly theirs.
/// </summary>
public sealed class NeonSkeletonEffect : CameraEffect
{
    /// <summary>The bones of a hand, by the points' order (wrist, then each finger from its base).</summary>
    public static IReadOnlyList<(int From, int To)> HandBones { get; } =
    [
        (0, 1), (1, 2), (2, 3), (3, 4), (0, 5), (5, 6), (6, 7), (7, 8), (5, 9), (9, 10), (10, 11), (11, 12),
        (9, 13), (13, 14), (14, 15), (15, 16), (13, 17), (0, 17), (17, 18), (18, 19), (19, 20),
    ];

    /// <summary>The bones of the body, by the pose's points: shoulders, arms, hands, trunk, legs and feet.</summary>
    public static IReadOnlyList<(int From, int To)> BodyBones { get; } =
    [
        (11, 12), (11, 13), (13, 15), (12, 14), (14, 16), (11, 23), (12, 24), (23, 24), (23, 25), (25, 27), (24, 26), (26, 28),
        (15, 17), (15, 19), (17, 19), (16, 18), (16, 20), (18, 20), (27, 29), (29, 31), (27, 31), (28, 30), (30, 32), (28, 32),
    ];

    /// <summary>Every bone as two indices into <see cref="CameraStage.Points"/>: the left hand's, the right's, then the body's.</summary>
    public static IReadOnlyList<(int From, int To)> Bones { get; } =
    [
        .. HandBones,
        .. HandBones.Select(bone => (bone.From + HandSense.Points, bone.To + HandSense.Points)),
        .. BodyBones.Select(bone => (bone.From + (HandSense.Points * 2), bone.To + (HandSense.Points * 2))),
    ];

    private static readonly StageProgram Fade = new("neon-fade", StageShaders.Prelude + """
        // t0 the last trails: kept, faded and drifting outward with the bass.
        float4 ps(Vertex v) : SV_Target
        {
            float2 drift = (v.uv - 0.5) * (0.004 + BASS * 0.01);
            float3 kept = t0.SampleLevel(clampLinear, v.uv - drift, 0).rgb;
            return float4(kept * pow(0.004, DT), 1);
        }
        """);

    private static readonly StageProgram Lines = new("neon-bones", StageShaders.Prelude + """
        // t0 the points; c[16] onward the bones, two to a row.
        struct Line { float4 position : SV_Position; float2 across : TEXCOORD0; float4 colour : COLOR0; };
        Line vsPoints(uint corner : SV_VertexID, uint instance : SV_InstanceID)
        {
            float4 row = c[16 + instance / 2];
            float2 pair = (instance & 1) ? row.zw : row.xy;
            float4 a = LANDMARK(t0, (int)pair.x);
            float4 b = LANDMARK(t0, (int)pair.y);
            float2 ea = even(toStage(a.xy));
            float2 eb = even(toStage(b.xy));
            float2 direction = normalize(eb - ea + 1e-6);
            float2 normal = float2(-direction.y, direction.x);
            bool body = pair.x >= 42;
            float width = (body ? 0.010 : 0.005) * (1 + PUNCH * 1.6 + (body ? BASS : TREBLE) * 0.8);
            float along = corner & 1;
            float side = (corner >> 1) * 2.0 - 1.0;
            float2 e = lerp(ea - direction * width, eb + direction * width, along) + normal * width * 3 * side;
            float2 uv = float2(e.x / ASPECT, e.y);
            Line stroke;
            stroke.position = a.w > 0.05 && b.w > 0.05 ? float4(uv.x * 2 - 1, 1 - uv.y * 2, 0, 1) : float4(-9, -9, 0, 1);
            stroke.across = float2(side * 3, along);
            float hue = body ? 0.02 + instance * 0.011 : (pair.x >= 21 ? 0.55 : 0.3) + (instance % 21) * 0.018;
            float level = body ? 0.16 + BASS * 0.35 : 0.2 + TREBLE * 0.45;
            stroke.colour = float4(paint(hue + TIME * 0.05) * level * (0.5 + 0.5 * min(a.w, b.w)), 1);
            return stroke;
        }
        float4 ps(Line stroke) : SV_Target
        {
            float d = abs(stroke.across.x);
            float glow = exp(-d * d * 1.3) + 0.22 * exp(-d * 0.9);
            float core = smoothstep(0.5, 0.0, d);
            return float4(stroke.colour.rgb * glow + core * 0.22 * (0.6 + BEAT * 0.6), 1);
        }
        """, Points: true);

    private static readonly StageProgram Sparks = new("neon-joints", StageShaders.Prelude + """
        // t0 the points: a spark on every joint, the fingertips largest.
        struct Spark { float4 position : SV_Position; float2 at : TEXCOORD0; float4 colour : COLOR0; };
        Spark vsPoints(uint corner : SV_VertexID, uint instance : SV_InstanceID)
        {
            float4 p = LANDMARK(t0, (int)instance);
            int finger = (int)instance % 21;
            bool hand = instance < 42;
            bool tip = hand && (finger == 4 || finger == 8 || finger == 12 || finger == 16 || finger == 20);
            float size = (tip ? 0.014 * (1 + TREBLE) : hand ? 0.006 : 0.011) * (1 + PUNCH * 0.6);
            float2 offset = float2(corner & 1, corner >> 1) * 2 - 1;
            float2 e = even(toStage(p.xy)) + offset * size;
            float2 uv = float2(e.x / ASPECT, e.y);
            Spark spark;
            spark.position = p.w > 0.05 && (hand || instance >= 53) ? float4(uv.x * 2 - 1, 1 - uv.y * 2, 0, 1) : float4(-9, -9, 0, 1);
            spark.at = offset;
            spark.colour = float4(lerp(paint(instance * 0.037 + TIME * 0.08), 1, tip ? 0.5 : 0.2) * (tip ? 0.5 : 0.3), 1);
            return spark;
        }
        float4 ps(Spark spark) : SV_Target
        {
            float d = length(spark.at);
            return float4(spark.colour.rgb * (exp(-d * d * 6) + 0.15 * exp(-d * 2)) * saturate(1.4 - d), 1);
        }
        """, Points: true);

    private static readonly StageProgram Plasma = new("neon-plasma", StageShaders.Prelude + """
        // t0 the points: with both hands up, plasma between matching fingertips.
        float4 ps(Vertex v) : SV_Target
        {
            if (c[11].x < 0.5 || c[11].z < 0.5) return float4(0, 0, 0, 1);
            float2 p = even(v.uv);
            float3 light = 0;
            [unroll] for (int k = 0; k < 5; k++)
            {
                int finger = 4 + k * 4;
                float4 a = LANDMARK(t0, LeftHand + finger);
                float4 b = LANDMARK(t0, RightHand + finger);
                float2 ea = even(toStage(a.xy));
                float2 eb = even(toStage(b.xy));
                float2 ba = eb - ea;
                float t = saturate(dot(p - ea, ba) / max(dot(ba, ba), 1e-6));
                float2 normal = normalize(float2(-ba.y, ba.x) + 1e-6);
                float wild = 0.02 + TREBLE * 0.05 + PUNCH * 0.03;
                float bend = (fbm(float2(t * 7 - TIME * 3, k * 3.1 + TIME * 1.7)) - 0.5) * 2 * wild * sin(t * 3.14159);
                float d = abs(dot(p - (ea + ba * t), normal) - bend);
                float strength = 0.00045 * (0.6 + TREBLE * 1.6 + c[11].y + c[11].w);
                light += paint(0.6 + k * 0.09 + TIME * 0.05) * strength / (d + 0.0015) * smoothstep(0, 0.05, t) * smoothstep(1, 0.95, t);
            }

            return float4(light, 1);
        }
        """);

    private static readonly StageProgram Compose = new("neon-compose", StageShaders.Prelude + """
        // t0 trails, t1 camera, t2 matte.
        float4 ps(Vertex v) : SV_Target
        {
            float matte = t2.SampleLevel(clampLinear, sceneUV(v.uv), 0).r;
            float3 camera = t1.SampleLevel(clampLinear, cameraUV(v.uv), 0).rgb;
            float3 ghost = luma(camera) * paint(0.7 + TIME * 0.02) * matte * 0.18;
            float3 room = paint(0.85) * 0.02 * (1 - length(v.uv - 0.5));
            return float4(t0.SampleLevel(clampLinear, v.uv, 0).rgb + ghost + room, 1);
        }
        """);

    private StageTexture _trails;
    private StageTexture _trailsNext;

    public override IReadOnlyList<StageProgram> Programs { get; } = [Fade, Lines, Sparks, Plasma, Compose];

    public override float Bloom => 1.3f;

    public override void Draw(CameraStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        var gpu = stage.Gpu;
        stage.Keep(ref _trails, stage.Width, stage.Height, StageFormat.Half4);
        stage.Keep(ref _trailsNext, stage.Width, stage.Height, StageFormat.Half4);

        gpu.Pass(Fade, _trailsNext, [_trails], stage.Constants());
        (_trails, _trailsNext) = (_trailsNext, _trails);

        var bones = stage.Constants();
        for (var i = 0; i < Bones.Count; i++)
        {
            bones[(StageShaders.EffectRow * 4) + (i * 2)] = Bones[i].From;
            bones[(StageShaders.EffectRow * 4) + (i * 2) + 1] = Bones[i].To;
        }

        gpu.Points(Lines, _trails, [stage.Points], Bones.Count, bones);
        gpu.Points(Sparks, _trails, [stage.Points], CameraStage.PointCount, stage.Constants());
        gpu.Pass(Plasma, _trails, [stage.Points], stage.Constants(), StageBlend.Add);
        gpu.Pass(Compose, stage.Light, [_trails, stage.Camera, stage.Matte], stage.Constants());
    }

    public override void Release(IStageGpu gpu)
    {
        CameraStage.Drop(gpu, ref _trails);
        CameraStage.Drop(gpu, ref _trailsNext);
    }
}
