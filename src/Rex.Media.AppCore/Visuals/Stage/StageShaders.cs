using Rex.Media.Video;

namespace Rex.Media.AppCore.Visuals.Stage;

/// <summary>
/// What every camera stage program shares (ADR-0022): the constant rows <see cref="CameraStage"/>
/// fills each frame (time, the music, where the camera picture sits, the palette, the hands), the
/// samplers, noise, and the helpers that turn a point the camera saw into a point on the stage. The
/// post programs that every effect ends with are here too: a bright pass, a bloom over four
/// halvings, and a filmic tone curve with a vignette and a little grain.
/// </summary>
public static class StageShaders
{
    /// <summary>Constant rows every program reads; an effect's own start at <see cref="EffectRow"/>.</summary>
    public const int SharedRows = 16;

    public const int EffectRow = SharedRows;

    /// <summary>
    /// The shared prelude. Rows: c[0] time, frame seconds, output size; c[1] bass, mid, treble,
    /// punch; c[2] loudness, beat phase, beat flash, build; c[3] where the camera picture sits (a
    /// stage point's share of the picture is uv * c[3].xy + c[3].zw); c[4] mirror, presence, motion,
    /// drop flash; c[5..8] the palette; c[9] and c[10] each hand's index tip on the stage and its
    /// velocity (stage shares a second); c[11] each hand's presence and pinch; c[12] the body's
    /// middle, its width, its presence; c[13] the stage's aspect; c[14..15] spare.
    /// </summary>
    public const string Prelude = """
        cbuffer Stage : register(b0) { float4 c[64]; };
        Texture2D t0 : register(t0);
        Texture2D t1 : register(t1);
        Texture2D t2 : register(t2);
        Texture2D t3 : register(t3);
        Texture2D t4 : register(t4);
        Texture2D t5 : register(t5);
        Texture2D t6 : register(t6);
        Texture2D t7 : register(t7);
        SamplerState clampLinear : register(s0);
        SamplerState wrapLinear : register(s1);
        SamplerState pointClamp : register(s2);
        struct Vertex { float4 position : SV_Position; float2 uv : TEXCOORD0; };
        #define TIME c[0].x
        #define DT c[0].y
        #define SIZE c[0].zw
        #define BASS c[1].x
        #define MID c[1].y
        #define TREBLE c[1].z
        #define PUNCH c[1].w
        #define LOUD c[2].x
        #define PHASE c[2].y
        #define BEAT c[2].z
        #define BUILD c[2].w
        #define MIRROR c[4].x
        #define PRESENCE c[4].y
        #define MOTION c[4].z
        #define DROP c[4].w
        #define ASPECT c[13].x
        #define LANDMARK(tex, i) tex.Load(int3(i, 0, 0))
        // Points the camera saw: 0 to 20 the left hand, 21 to 41 the right, 42 to 74 the body.
        static const int LeftHand = 0;
        static const int RightHand = 21;
        static const int Body = 42;
        float3 paint(float t)
        {
            t = frac(t) * 4;
            int i = (int)t;
            float f = t - i;
            float3 a = i == 0 ? c[5].rgb : i == 1 ? c[6].rgb : i == 2 ? c[7].rgb : c[8].rgb;
            float3 b = i == 0 ? c[6].rgb : i == 1 ? c[7].rgb : i == 2 ? c[8].rgb : c[5].rgb;
            return lerp(a, b, f * f * (3 - 2 * f));
        }
        float hash12(float2 p)
        {
            uint2 q = asuint(p);
            uint h = q.x * 0x8da6b343u ^ (q.y * 0xd8163841u + 0x9e3779b9u);
            h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
            return h * (1.0 / 4294967296.0);
        }
        float noise(float2 p)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            float2 u = f * f * (3 - 2 * f);
            return lerp(lerp(hash12(i), hash12(i + float2(1, 0)), u.x), lerp(hash12(i + float2(0, 1)), hash12(i + float2(1, 1)), u.x), u.y);
        }
        float fbm(float2 p)
        {
            float sum = 0, amplitude = 0.5;
            [unroll] for (int i = 0; i < 5; i++) { sum += amplitude * noise(p); p = p * 2.03 + 17.1; amplitude *= 0.5; }
            return sum;
        }
        // A stage point's share of the camera picture as the scene saw it (mirrored when shown as a mirror).
        float2 sceneUV(float2 uv) { return uv * c[3].xy + c[3].zw; }
        // The same point in the camera's own picture, which is never mirrored.
        float2 cameraUV(float2 uv) { float2 p = sceneUV(uv); p.x = MIRROR > 0.5 ? 1 - p.x : p.x; return p; }
        // Where a point the scene saw is on the stage.
        float2 toStage(float2 share) { return (share - c[3].zw) / c[3].xy; }
        // Distances on the stage measured in heights, so circles stay round.
        float2 even(float2 uv) { return float2(uv.x * ASPECT, uv.y); }
        float segment(float2 p, float2 a, float2 b)
        {
            float2 pa = p - a, ba = b - a;
            float h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-6));
            return length(pa - ba * h);
        }
        float luma(float3 colour) { return dot(colour, float3(0.2126, 0.7152, 0.0722)); }
        """;

    /// <summary>Takes the light above white (softly) down to half size: the start of the bloom.</summary>
    public static StageProgram Bright { get; } = new("bright", Prelude + """
        float4 ps(Vertex v) : SV_Target
        {
            float2 texel = 1.0 / SIZE;
            float3 sum = 0;
            sum += t0.SampleLevel(clampLinear, v.uv + texel * float2(-1, -1), 0).rgb;
            sum += t0.SampleLevel(clampLinear, v.uv + texel * float2(1, -1), 0).rgb;
            sum += t0.SampleLevel(clampLinear, v.uv + texel * float2(-1, 1), 0).rgb;
            sum += t0.SampleLevel(clampLinear, v.uv + texel * float2(1, 1), 0).rgb;
            sum *= 0.25;
            float bright = luma(sum);
            float knee = saturate((bright - 0.55) / 0.6);
            return float4(sum * knee * knee, 1);
        }
        """);

    /// <summary>Halves a bloom level with a tent of four taps; c[16].xy is one texel of the source.</summary>
    public static StageProgram Down { get; } = new("down", Prelude + """
        float4 ps(Vertex v) : SV_Target
        {
            float2 texel = c[16].xy;
            float3 sum = t0.SampleLevel(clampLinear, v.uv + texel * float2(-1, -1), 0).rgb
                + t0.SampleLevel(clampLinear, v.uv + texel * float2(1, -1), 0).rgb
                + t0.SampleLevel(clampLinear, v.uv + texel * float2(-1, 1), 0).rgb
                + t0.SampleLevel(clampLinear, v.uv + texel * float2(1, 1), 0).rgb;
            return float4(sum * 0.25, 1);
        }
        """);

    /// <summary>Spreads a smaller bloom level over the next larger one (added); c[16].xy is one texel of the source.</summary>
    public static StageProgram Up { get; } = new("up", Prelude + """
        float4 ps(Vertex v) : SV_Target
        {
            float2 texel = c[16].xy;
            float3 sum = t0.SampleLevel(clampLinear, v.uv, 0).rgb * 4
                + (t0.SampleLevel(clampLinear, v.uv + texel * float2(1, 0), 0).rgb
                + t0.SampleLevel(clampLinear, v.uv + texel * float2(-1, 0), 0).rgb
                + t0.SampleLevel(clampLinear, v.uv + texel * float2(0, 1), 0).rgb
                + t0.SampleLevel(clampLinear, v.uv + texel * float2(0, -1), 0).rgb) * 2
                + t0.SampleLevel(clampLinear, v.uv + texel * float2(1, 1), 0).rgb
                + t0.SampleLevel(clampLinear, v.uv + texel * float2(-1, 1), 0).rgb
                + t0.SampleLevel(clampLinear, v.uv + texel * float2(1, -1), 0).rgb
                + t0.SampleLevel(clampLinear, v.uv + texel * float2(-1, -1), 0).rgb;
            return float4(sum / 16, 1);
        }
        """);

    /// <summary>
    /// The light and its bloom to the screen: a filmic curve (light above white rolls off rather
    /// than clipping), a vignette, and grain that moves, so dark gradients never band. c[16].x is the
    /// bloom's strength, c[16].y the exposure.
    /// </summary>
    public static StageProgram Finish { get; } = new("finish", Prelude + """
        float4 ps(Vertex v) : SV_Target
        {
            float3 light = t0.SampleLevel(clampLinear, v.uv, 0).rgb;
            float3 bloom = t1.SampleLevel(clampLinear, v.uv, 0).rgb;
            float3 colour = (light + bloom * c[16].x) * c[16].y;
            colour = saturate((colour * (2.51 * colour + 0.03)) / (colour * (2.43 * colour + 0.59) + 0.14));
            float2 centred = v.uv - 0.5;
            colour *= 1 - dot(centred, centred) * 0.55;
            colour += (hash12(v.position.xy + frac(TIME) * 911) - 0.5) / 255 * 2;
            return float4(colour, 1);
        }
        """);

    /// <summary>Every shared program, for compiling them all in tests.</summary>
    public static IReadOnlyList<StageProgram> All { get; } = [Bright, Down, Up, Finish];
}
