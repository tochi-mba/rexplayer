namespace Rex.Media.Interop.Graphics;

/// <summary>
/// The Direct3D shader program kept apart from device lifecycle and COM interop. Effects operate
/// on decoded video pixels only, inside the picture, before the colour look.
/// </summary>
internal static class D3D11VideoShaders
{
    public const string Source = """
        // Each effect is compiled as its own variant (EFFECT_ONLY is its index; 0 is no effect),
        // so the shader for any one effect stays small however many effects exist.
        #ifndef EFFECT_ONLY
        #define EFFECT_ONLY 0
        #endif
        cbuffer Colour : register(b0) { float4 rowR; float4 rowG; float4 rowB; float4 crop; float4 source; float4 look; float4 effect; float4 pointer; float4 motion; float4 extras; };
        // crop: xy the part of the texture the picture fills, zw one texel of the texture.
        // effect: x the effect, y its strength (0-1), z the picture's time in seconds, w the shown
        // width over height. pointer: xy where the pointer is, z 1 while it is over the picture,
        // w the effect's detail control (0.25-1.75). motion: y 1 while a previous picture is held.
        // extras: the effect's third and fourth controls as saved.
        Texture2D luma : register(t0);
        Texture2D chroma : register(t1);
        Texture2D priorLuma : register(t2);
        Texture2D priorChroma : register(t3);
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
        // Shared by the mosaic effects: a stable per-cell random number, a modulo that
        // stays positive for negative coordinates, and nearest-colour palette snapping.
        float hash12(float2 p)
        {
            return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
        }
        float2 positiveMod(float2 x, float2 y)
        {
            return x - y * floor(x / y);
        }
        static const float3 BrickPalette[24] =
        {
            float3(0.95, 0.95, 0.93), float3(0.63, 0.65, 0.65), float3(0.33, 0.35, 0.37), float3(0.11, 0.11, 0.12),
            float3(0.79, 0.10, 0.11), float3(0.45, 0.08, 0.10), float3(0.96, 0.50, 0.10), float3(0.98, 0.80, 0.10),
            float3(0.85, 0.76, 0.56), float3(0.40, 0.24, 0.14), float3(0.14, 0.50, 0.22), float3(0.60, 0.77, 0.20),
            float3(0.05, 0.34, 0.70), float3(0.35, 0.65, 0.88), float3(0.06, 0.15, 0.33), float3(0.93, 0.55, 0.70),
            float3(0.08, 0.30, 0.16), float3(0.42, 0.47, 0.18), float3(0.80, 0.88, 0.35), float3(0.55, 0.45, 0.30),
            float3(0.66, 0.33, 0.12), float3(0.42, 0.24, 0.55), float3(0.45, 0.70, 0.62), float3(0.98, 0.93, 0.55),
        };
        static const float3 CubePalette[6] =
        {
            float3(0.95, 0.95, 0.95), float3(1.00, 0.84, 0.00), float3(0.78, 0.05, 0.10),
            float3(1.00, 0.45, 0.00), float3(0.00, 0.27, 0.68), float3(0.00, 0.60, 0.28),
        };
        static const float3 RetroPalettes[12] =
        {
            float3(0.06, 0.22, 0.06), float3(0.19, 0.38, 0.19), float3(0.55, 0.67, 0.06), float3(0.61, 0.74, 0.06),
            float3(0.08, 0.08, 0.08), float3(0.36, 0.36, 0.36), float3(0.68, 0.68, 0.68), float3(0.95, 0.95, 0.95),
            float3(0.00, 0.00, 0.00), float3(0.33, 1.00, 1.00), float3(1.00, 0.33, 1.00), float3(1.00, 1.00, 1.00),
        };
        static const float Bayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
        // Palette matching compares brightness and two colour-difference axes separately, so a
        // bright green finds a green rather than the grey of the same brightness.
        float colourDistance(float3 a, float3 b)
        {
            float3 d = a - b;
            float luma = dot(d, float3(0.299, 0.587, 0.114));
            float orange = d.r - d.b;
            float green = d.g - (d.r + d.b) * 0.5;
            return luma * luma * 2 + orange * orange + green * green;
        }
        float3 nearestBrick(float3 c)
        {
            // Toy bricks are vivid: match from a livelier version of the colour so muted
            // foliage finds a green rather than a grey.
            float grey = dot(c, float3(0.2126, 0.7152, 0.0722));
            c = saturate(lerp(grey.xxx, c, 1.6));
            float best = 1e9;
            float3 chosen = c;
            [loop] for (int i = 0; i < 24; i++)
            {
                float error = colourDistance(c, BrickPalette[i]);
                if (error < best) { best = error; chosen = BrickPalette[i]; }
            }
            return chosen;
        }
        float3 nearestCube(float3 c)
        {
            float best = 1e9;
            float3 chosen = c;
            [loop] for (int i = 0; i < 6; i++)
            {
                float error = colourDistance(c, CubePalette[i]);
                if (error < best) { best = error; chosen = CubePalette[i]; }
            }
            return chosen;
        }
        float pipAt(float2 local, float2 at, float radius)
        {
            return 1 - smoothstep(radius - 0.015, radius, length(local - at));
        }
        // Mosaic cell rows from the size control: a larger value gives fewer, bigger cells.
        float mosaicRows(float fine, float coarse)
        {
            return lerp(fine, coarse, saturate((pointer.w - 0.25) / 1.5));
        }
        static const float3 lumaWeights = float3(0.2126, 0.7152, 0.0722);
        float lumaOf(float3 c) { return dot(c, lumaWeights); }
        float3 basePictureLook(float3 c)
        {
            if (look.x < 0.5) return c;
            float l = lumaOf(c);
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
            float l = lumaOf(styled);
            styled = saturate(lerp(float3(l, l, l), styled, look.z));
            if (look.x < 0.5)
            {
                return saturate((original - 0.5) * look.z + 0.5 + (look.y - 1) * 0.25);
            }
            return saturate(lerp(original, styled, look.y));
        }
        // The effect's detail control, and the shown shape: multiplying a picture coordinate by
        // aspect() makes one unit the same length across as down, so nothing stretches on 4:3,
        // scope or rotated pictures.
        float detailControl() { return clamp(pointer.w, 0.25, 1.75); }
        float2 aspect() { return float2(max(effect.w, 0.0001), 1); }
        // A coordinate that leaves the picture is reflected back into it, so warps near the edges
        // mirror the picture instead of smearing its border pixels into streaks.
        float2 mirrored(float2 uv) { return 1 - abs(1 - 2 * frac(uv * 0.5)); }
        // A well-mixed integer hash of a lattice point, the same on every graphics card (no sine).
        float hash(float2 cell)
        {
            uint2 q = (uint2)(int2)floor(cell);
            uint h = q.x * 73856093u ^ q.y * 19349663u;
            h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
            return h * (1.0 / 4294967296.0);
        }
        // Where a picture coordinate falls in the texture, kept half a texel inside the picture so a
        // decoder's padding rows and columns never bleed into the edges.
        float2 pictureUv(float2 uv) { return (source.xy + saturate(uv) * (source.zw - source.xy)) * crop.xy; }
        float2 insideLuma(float2 t) { return clamp(t, crop.zw * 0.5, crop.xy - crop.zw * 0.5); }
        float2 insideChroma(float2 t) { return clamp(t, crop.zw, crop.xy - crop.zw); }
        float3 decodeYuv(float y, float2 cbcr)
        {
            float4 samples = float4(y, cbcr, 1);
            return saturate(float3(dot(rowR, samples), dot(rowG, samples), dot(rowB, samples)));
        }
        // No texture has mip levels, so level 0 is exact; sampling without implicit derivatives lets
        // every effect's branch be skipped by the pixels of every other effect.
        float3 readColour(float2 uv, bool isYuv)
        {
            float2 t = pictureUv(uv);
            if (isYuv)
                return decodeYuv(luma.SampleLevel(linearClamp, insideLuma(t), 0).r,
                    chroma.SampleLevel(chromaSampler, insideChroma(t), 0).rg);
            return luma.SampleLevel(linearClamp, insideLuma(t), 0).rgb;
        }
        // Luma alone in one fetch: the matrix's luma gain makes its differences match readColour's.
        float readLuma(float2 uv, bool isYuv)
        {
            float2 t = pictureUv(uv);
            // Green from luma with neutral colour is luma itself, offset included.
            if (isYuv) return dot(rowG, float4(luma.SampleLevel(linearClamp, insideLuma(t), 0).r, 0.5, 0.5, 1));
            return lumaOf(luma.SampleLevel(linearClamp, insideLuma(t), 0).rgb);
        }
        // The previous picture, decoded on the card exactly as the current one is.
        float3 readPrior(float2 uv, bool isYuv)
        {
            float2 t = pictureUv(uv);
            if (isYuv)
                return decodeYuv(priorLuma.SampleLevel(linearClamp, insideLuma(t), 0).r,
                    priorChroma.SampleLevel(chromaSampler, insideChroma(t), 0).rg);
            return priorLuma.SampleLevel(linearClamp, insideLuma(t), 0).rgb;
        }
        // One recorded pixel in picture coordinates.
        float2 pictureTexel()
        {
            float2 span = max(abs(source.zw - source.xy) * crop.xy, 0.000001);
            return max(crop.zw, 0.0000001) / span;
        }
        // Measuring steps follow the recorded pixels, or the shown ones when the picture is drawn
        // smaller, so detection sees the same detail at every window size and zoom.
        float2 measureStep(float2 pixel) { return max(pictureTexel(), pixel); }
        // A smoothed luma slope from four diagonal taps half a step away (a 3x3 footprint), the
        // taps' mean, and their saddle (what no single slope explains: texture rather than an edge).
        float4 lumaSlope(float2 uv, float2 stepUv, bool isYuv)
        {
            float a = readLuma(uv - 0.5 * stepUv, isYuv);
            float b = readLuma(uv + float2(0.5, -0.5) * stepUv, isYuv);
            float c = readLuma(uv + float2(-0.5, 0.5) * stepUv, isYuv);
            float d = readLuma(uv + 0.5 * stepUv, isYuv);
            return float4(float2(b + d - a - c, c + d - a - b) * 0.5, (a + b + c + d) * 0.25, (a + d - b - c) * 0.5);
        }
        // The same four taps measured as energies: x the slope, y the saddle left over once the best
        // plane is taken away. A straight edge has no saddle; grain, compression noise and texture do.
        float2 lumaEnergy(float2 uv, float2 stepUv, bool isYuv)
        {
            float a = readLuma(uv - 0.5 * stepUv, isYuv);
            float b = readLuma(uv + float2(0.5, -0.5) * stepUv, isYuv);
            float c = readLuma(uv + float2(-0.5, 0.5) * stepUv, isYuv);
            float d = readLuma(uv + 0.5 * stepUv, isYuv);
            float gx = (b + d - a - c) * 0.5, gy = (c + d - a - b) * 0.5, s = (a + d - b - c) * 0.5;
            return float2(gx * gx + gy * gy, s * s);
        }
        // Colour structure from four diagonal taps: luma and the two colour differences (BT.709),
        // combined as a structure tensor so boundaries between colours of equal brightness count.
        // Returns the unit direction across the edge (pointing towards the brighter side), the edge
        // energy and the saddle energy; lumaPart is the share of the energy luma alone explains.
        float4 colourStructure(float2 uv, float2 stepUv, bool isYuv, out float lumaPart)
        {
            float3 a = readColour(uv - 0.5 * stepUv, isYuv);
            float3 b = readColour(uv + float2(0.5, -0.5) * stepUv, isYuv);
            float3 c = readColour(uv + float2(-0.5, 0.5) * stepUv, isYuv);
            float3 d = readColour(uv + 0.5 * stepUv, isYuv);
            float3x3 axes = float3x3(0.2126, 0.7152, 0.0722,
                -0.1146, -0.3854, 0.5,
                0.5, -0.4542, -0.0458);
            float3 gx = mul(axes, (b + d - a - c) * 0.5);
            float3 gy = mul(axes, (c + d - a - b) * 0.5);
            float3 gs = mul(axes, (a + d - b - c) * 0.5);
            float3 w = float3(1, 2, 2);
            float jxx = dot(w, gx * gx), jyy = dot(w, gy * gy), jxy = dot(w, gx * gy);
            float spread = 0.5 * (jxx - jyy);
            float root = sqrt(spread * spread + jxy * jxy);
            float2 v1 = float2(spread + root, jxy), v2 = float2(jxy, root - spread);
            float2 v = dot(v1, v1) >= dot(v2, v2) ? v1 : v2;
            v *= dot(v, float2(gx.x, gy.x)) < 0 ? -1 : 1;
            float energy = 0.5 * (jxx + jyy) + root;
            lumaPart = saturate((gx.x * gx.x + gy.x * gy.x) / max(jxx + jyy, 1e-12));
            // A flat patch has no direction; say "across" rather than divide by nothing.
            float2 direction = dot(v, v) > 1e-20 ? v * rsqrt(dot(v, v)) : float2(1, 0);
            return float4(direction, energy, dot(w, gs * gs));
        }
        // The same measurement from luma alone, one fetch per tap: the wide scale, where colour-only
        // boundaries are already found by the fine one.
        float4 lumaStructure(float2 uv, float2 stepUv, bool isYuv)
        {
            float a = readLuma(uv - 0.5 * stepUv, isYuv);
            float b = readLuma(uv + float2(0.5, -0.5) * stepUv, isYuv);
            float c = readLuma(uv + float2(-0.5, 0.5) * stepUv, isYuv);
            float d = readLuma(uv + 0.5 * stepUv, isYuv);
            float2 g = float2(b + d - a - c, c + d - a - b) * 0.5;
            float s = (a + d - b - c) * 0.5;
            float energy = dot(g, g);
            return float4(energy > 1e-20 ? g * rsqrt(energy) : float2(1, 0), energy, s * s);
        }
        float median5(float a, float b, float c, float d, float e)
        {
            float lo1 = min(a, b), hi1 = max(a, b);
            float lo2 = min(c, d), hi2 = max(c, d);
            float lowest = max(lo1, lo2);
            float highest = min(hi1, hi2);
            float x = min(lowest, highest), y = max(lowest, highest);
            return clamp(e, x, y);
        }
        // The contours every outline effect draws, from the recorded picture alone.
        // x: confidence that a real edge passes here (soft, 0-1); y: a thin line on the edge's crest;
        // zw: the direction across the edge. Each energy is compared with the local noise floor, the
        // median saddle energy of five places around the pixel, so faint outlines on clean surfaces
        // count while grain, compression noise and banding do not. A second, wider scale catches
        // soft and out-of-focus boundaries. A weak response must continue along the edge on both
        // sides (hysteresis-style linking), so isolated specks never draw, and every decision is
        // smooth, so lines do not shimmer from one picture to the next.
        // outline: x the strong edges alone (silhouettes and clear outlines), y the crest (0-1),
        // z soft edges (out of focus or in deep shadow) found at a wide scale, w the wide-scale slope
        // against the local brightness, which stays high for several pixels around a big, coherent
        // edge in a dark scene as in a bright one (the reach of a glow).
        float4 contourField(float2 uv, float2 pixel, bool isYuv, float sensitivity, out float4 outline)
        {
            float2 stepUv = measureStep(pixel);
            float fineLuma;
            float4 fine = colourStructure(uv, stepUv, isYuv, fineLuma);
            float4 broad = lumaStructure(uv, stepUv * 2.5, isYuv);
            // The control's 100 % is the setting that looked cleanest across grainy, dark and
            // compressed test footage; 175 % reaches fine texture, 25 % keeps only bold shapes.
            float s = 0.55 * clamp(sensitivity, 0.25, 1.75);
            float floorEnergy = (0.0042 / s) * (0.0042 / s);
            float2 normal = fine.z * (broad.z + 4 * broad.w + 1e-7) >= 0.16 * broad.z * (fine.z + 4 * fine.w + 1e-7)
                ? fine.xy : broad.xy;
            float2 tangent = float2(-normal.y, normal.x);
            // Flat patches (most of a typical picture) skip the neighbourhood: nothing there can pass.
            float2 n1 = 0, n2 = 0, t1 = 0, t2 = 0;
            [branch] if (fine.z + broad.z > floorEnergy * 0.05)
            {
                n1 = lumaEnergy(uv + normal * stepUv, stepUv, isYuv);
                n2 = lumaEnergy(uv - normal * stepUv, stepUv, isYuv);
                t1 = lumaEnergy(uv + tangent * stepUv * 2, stepUv, isYuv);
                t2 = lumaEnergy(uv - tangent * stepUv * 2, stepUv, isYuv);
            }
            // The median ignores the saddles a nearby corner or edge leaves in one or two places.
            float noise = median5(fine.w, n1.y, n2.y, t1.y, t2.y) * 2.2;
            float k = 2.6 / s;
            float fineConfidence = fine.z / (fine.z + k * noise + floorEnergy);
            float broadConfidence = broad.z / (broad.z + k * max(noise, broad.w) + floorEnergy * 2);
            float confidence = max(fineConfidence, broadConfidence * 0.92);
            // Clear outlines (real contrast, well above the noise) always count; fainter responses
            // need neighbours along the edge unless they stand far above the noise.
            float contrast = sqrt(max(fine.z, broad.z * 0.4));
            float strong = smoothstep(0.035 / s, 0.10 / s, contrast) * smoothstep(0.35, 0.6, confidence);
            // Linking compares the edge's strength either side along it with its strength here, so a
            // soft edge links as well as a sharp one, and a lone speck (strong here, nothing beside
            // it) does not.
            // Averaging signed contrasts along the contour cancels uncorrelated grain while
            // retaining a faint but persistent line. Measure on both sides of the edge at
            // eleven points along its tangent, so a single bright noise pixel cannot qualify.
            // Pure colour boundaries use the colour-structure confidence instead of luma.
            float continuity = 1;
            [branch] if (fineLuma >= 0.55 && confidence > 0.20)
            {
                float across = 0;
                [loop] for (int pointIndex = -5; pointIndex <= 5; pointIndex++)
                {
                    float2 offset = tangent * stepUv * (pointIndex * 1.5);
                    across += readLuma(uv + offset + normal * stepUv, isYuv)
                        - readLuma(uv + offset - normal * stepUv, isYuv);
                }
                continuity = smoothstep(0.007, 0.013, abs(across / 11));
            }
            float weak = smoothstep(0.33, 0.67, confidence) * continuity;
            float edge = max(strong, weak);
            // Thin the edge to its crest (soft non-maximum suppression across it). Colour-only
            // boundaries have no luma crest to find, so they keep their width.
            float centre = fineLuma * fine.z;
            float crest = smoothstep(0.5, 0.95, centre / max(max(n1.x, n2.x), 1e-12));
            crest = lerp(1, crest, fineLuma * fineLuma);
            // A third, wide scale finds soft boundaries (a blurred background, a shape in deep shadow)
            // that are too gradual for the other two. Contrast is measured against the local
            // brightness, so a dark scene's edges count as much as a bright one's, while gentle shading
            // across a face stays below it. Where the brightness curve turns (the middle of the soft
            // edge) a soft line is drawn.
            float2 wideStep = stepUv * 8;
            float4 wide = lumaSlope(uv, wideStep, isYuv);
            float wideSlope = length(wide.xy);
            float2 wideNormal = wideSlope > 1e-6 ? wide.xy / wideSlope : normal;
            // Black bars recorded into the picture (letterbox or pillarbox) are not part of the scene:
            // near an exactly horizontal or vertical edge whose dark side stays black all the way to
            // the nearby border of the picture, nothing is drawn.
            float keep = 1;
            float2 facing = edge > 0 ? normal : wideNormal;
            [branch] if ((edge > 0 || wideSlope > 0.01) && max(abs(facing.x), abs(facing.y)) > 0.999)
            {
                float2 outward = abs(facing.x) > abs(facing.y) ? float2(-sign(facing.x), 0) : float2(0, -sign(facing.y));
                float2 border = outward.x != 0 ? float2(outward.x > 0 ? 0.9995 : 0.0005, uv.y)
                    : float2(uv.x, outward.y > 0 ? 0.9995 : 0.0005);
                float darkest = max(max(lumaOf(readColour(uv + outward * stepUv * 10, isYuv)),
                    lumaOf(readColour(lerp(uv, border, 0.5), isYuv))), lumaOf(readColour(border, isYuv)));
                float reach = dot(abs(border - uv), abs(outward));
                keep = darkest < 0.025 && reach < 0.3 ? 0 : 1;
                edge *= keep;
                strong *= keep;
                wideSlope *= keep;
            }
            float ahead = readLuma(uv + wideNormal * wideStep * 0.5, isYuv);
            float behind = readLuma(uv - wideNormal * wideStep * 0.5, isYuv);
            float here = readLuma(uv, isYuv);
            float bend = abs(ahead + behind - 2 * here) / max(abs(ahead - behind) * 0.5, 1e-5);
            float relative = wideSlope / (max(wide.z, 0) + 0.06);
            float softness = clamp(sensitivity, 0.25, 1.75);
            float soft = smoothstep(0.08 / softness, 0.18 / softness, relative) * (1 - smoothstep(0.06, 0.22, bend))
                * (1 - strong) * keep;
            // The wide slope reaches several pixels from a big, coherent edge; busy texture (leaves,
            // gravel) has as much saddle as slope at that scale and does not count.
            float coherent = 1 - smoothstep(0.3, 0.7, abs(wide.w) / max(wideSlope, 1e-5));
            outline = float4(strong, crest, soft, relative * coherent);
            return float4(max(edge, soft * 0.7), max(edge * crest, soft * 0.4), normal);
        }
        // Steady isolines of smoothed luma and gentle relief from the visible shading: the surface
        // view of Ghostwire motion, which needs no previous picture and holds still when paused.
        float3 surfaceContours(float2 uv, float2 pixel, bool isYuv, float3 colour, float detail, float fineEdge)
        {
            // A cool silver bas-relief: light raking across the picture's own shading (lifted in the
            // shadows), a fine contour line at steady brightness levels where the surface slopes, and
            // the outlines etched in.
            float2 stepUv = measureStep(pixel) * 3.0 / max(detail, 0.25);
            float4 measured = lumaSlope(uv, stepUv, isYuv);
            float2 slope = measured.xy;
            float smooth = (lumaOf(colour) + measured.z) * 0.5;
            float slopeSize = length(slope);
            float visibleSurface = smoothstep(0.0045 / detail, 0.068 / detail, slopeSize);
            float band = frac(smooth * (18 * detail));
            float contours = 1 - smoothstep(0.025, 0.11, abs(band - 0.5));
            float3 normal = normalize(float3(-slope.x * 8, -slope.y * 8, 0.6));
            float light = saturate(0.55 + 0.45 * dot(normal, normalize(float3(-0.45, -0.5, 0.74))) / 0.74);
            float lifted = smooth / (smooth + 0.2) * 1.2;
            float3 base = float3(0.50, 0.58, 0.68) * (0.12 + 0.6 * lifted) * (0.35 + 0.75 * light);
            base += contours * visibleSurface * float3(0.08, 0.36, 0.42);
            base += fineEdge * float3(0.18, 0.62, 0.72);
            return saturate(base);
        }
        // Geometry effects move where the picture is read from. Everything else reads in place.
        // Level-zero reads for real (not unrolled) loops: they need no screen-space
        // derivatives, so long walks compile in a moment instead of inlining every step.
        float3 readColourAt(float2 uv, bool isYuv)
        {
            float2 mapped = (source.xy + saturate(uv) * (source.zw - source.xy)) * crop.xy;
            if (isYuv)
            {
                float4 samples = float4(luma.SampleLevel(linearClamp, mapped, 0).r,
                    chroma.SampleLevel(chromaSampler, mapped, 0).rg, 1);
                return saturate(float3(dot(rowR, samples), dot(rowG, samples), dot(rowB, samples)));
            }
            return luma.SampleLevel(linearClamp, mapped, 0).rgb;
        }
        float3 readPriorAt(float2 uv, bool isYuv)
        {
            float2 mapped = (source.xy + saturate(uv) * (source.zw - source.xy)) * crop.xy;
            if (isYuv)
            {
                float4 samples = float4(priorLuma.SampleLevel(linearClamp, mapped, 0).r,
                    priorChroma.SampleLevel(chromaSampler, mapped, 0).rg, 1);
                return saturate(float3(dot(rowR, samples), dot(rowG, samples), dot(rowB, samples)));
            }
            return priorLuma.SampleLevel(linearClamp, mapped, 0).rgb;
        }
        // The contourGradient signal at a given pixel footprint, for use inside loops.
        float3 contourGradientAt(float2 uv, bool isYuv, float2 pixel)
        {
            float3 dx = readColourAt(uv + float2(pixel.x, 0), isYuv) - readColourAt(uv - float2(pixel.x, 0), isYuv);
            float3 dy = readColourAt(uv + float2(0, pixel.y), isYuv) - readColourAt(uv - float2(0, pixel.y), isYuv);
            float3 weights = float3(0.2126, 0.7152, 0.0722);
            float2 direction = float2(dot(dx, weights), dot(dy, weights));
            return float3(direction, length(direction) + sqrt(dot(dx, dx) + dot(dy, dy)) * 0.32);
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
        // Fine texture inside low-contrast regions needs a different signal from the
        // outer silhouette. Read RGB deltas as well as luminance so equal-brightness
        // colours and shallow grey folds still create true, recorded contours.
        // The sample footprint stays tied to display pixels (and source zoom).
        float3 contourGradient(float2 uv, bool isYuv, float scale)
        {
            float2 pixel = max(fwidth(uv) * scale, float2(0.00018, 0.00018))
                / max(pointer.w, 0.25);
            float3 dx = readColour(uv + float2(pixel.x, 0), isYuv)
                - readColour(uv - float2(pixel.x, 0), isYuv);
            float3 dy = readColour(uv + float2(0, pixel.y), isYuv)
                - readColour(uv - float2(0, pixel.y), isYuv);
            float3 weights = float3(0.2126, 0.7152, 0.0722);
            float2 direction = float2(dot(dx, weights), dot(dy, weights));
            float chroma = sqrt(dot(dx, dx) + dot(dy, dy));
            // For colour-only boundaries, choose a meaningful signed orientation too.
            float2 colourDirection = float2(dot(dx, float3(0.49, -0.25, -0.24)),
                dot(dy, float3(0.49, -0.25, -0.24)));
            if (dot(direction, direction) < 0.000001)
                direction = colourDirection;
            return float3(direction, length(float2(dot(dx, weights),
                dot(dy, weights))) + chroma * 0.32);
        }
        // The fine pass keeps narrow interior lines; a larger pixel footprint also
        // catches soft, low-frequency shading without artificially drawing flat areas.
        float3 contourDetail(float2 uv, bool isYuv)
        {
            float3 fine = contourGradient(uv, isYuv, 0.75);
            float3 broad = contourGradient(uv, isYuv, 2.0);
            return float3(dot(fine.xy, fine.xy) > 0.000001 ? fine.xy : broad.xy,
                max(fine.z, broad.z * 0.62));
        }
        float2 effectUv(float2 uv)
        {
            float intensity = effect.y;
            float time = effect.z;
            float movement = time * detailControl();
            float2 shape = aspect();
        #if EFFECT_ONLY == 1
            if (effect.x < 1.5) // Prism flow: two slow waves, the same size across as down
            {
                float2 p = uv * shape;
                float2 wave = float2(sin(p.y * 26 + movement * 1.7), cos(p.x * 10.1 - movement * 1.1));
                return mirrored(uv + wave * (0.019 * intensity) / shape);
            }
        #endif
        #if EFFECT_ONLY == 2
            if (effect.x < 2.5) return uv;
        #endif
        #if EFFECT_ONLY == 3
            if (effect.x < 3.5) // Pixel drift: square blocks whose rows sway with time
            {
                float rows = lerp(150, 16, intensity);
                float2 grid = float2(rows * shape.x, rows);
                float stagger = sin(floor(uv.y * grid.y) * 0.8 + movement * 2) * 0.013 * intensity;
                return saturate((floor((uv + float2(stagger, 0)) * grid) + 0.5) / grid);
            }
        #endif
        #if EFFECT_ONLY == 4
            if (effect.x < 4.5) // Kaleidoscope: mirrored wedges inside a circle that grows with strength
            {
                float2 p = (uv - 0.5) * shape;
                float r = length(p);
                float folds = clamp(round(extras.x), 2, 12);
                float wedge = 3.14159265 / folds;
                float turn = time * extras.y * 0.006;
                float angle = atan2(p.y, p.x) + 6.28318531;
                float folded = abs(fmod(angle, wedge * 2) - wedge) + turn;
                // Each wedge shows the middle of the picture at twice its size (the mirror zoom
                // control scales that), so the subject rather than the background repeats.
                float2 inside = 0.5 + float2(cos(folded), sin(folded)) * (r * 0.5 / detailControl()) / shape;
                float reach = lerp(0.12, 1.25, intensity);
                float blend = 1 - smoothstep(reach * 0.6, reach, r);
                return mirrored(lerp(uv, inside, blend));
            }
        #endif
        #if EFFECT_ONLY >= 5 && EFFECT_ONLY <= 7
            if (effect.x < 7.5) return uv; // 5-7 are contours
        #endif
        #if EFFECT_ONLY == 8
            if (effect.x < 8.5) // Liquid glass: interference between crossing sine fields
            {
                float2 p = uv * shape;
                float2 displacement = float2(
                    sin(p.y * 35 + movement * 1.3) + sin((p.x + p.y) * 17 - movement * 0.8),
                    cos(p.x * 16.3 - movement * 1.1) + sin((p.x - p.y) * 14 + movement * 0.9));
                return mirrored(uv + displacement * (0.013 * intensity) / shape);
            }
        #endif
        #if EFFECT_ONLY == 9
            if (effect.x < 9.5) // Slice shift: hashed bands that move six times a second
            {
                float band = floor(uv.y * 29);
                float epoch = floor(movement * 6);
                float displacement = hash(float2(band, epoch)) * 2 - 1;
                return mirrored(uv + float2(displacement * 0.12 * intensity, 0));
            }
        #endif
        #if EFFECT_ONLY == 10
            if (effect.x < 10.5) // Vortex: a swirl that breathes at the centre and leaves the edges still
            {
                float2 delta = (uv - 0.5) * shape;
                float radius = length(delta);
                float turn = intensity * exp(-radius * 3.3) * (4 + 1.4 * sin(movement * 0.6));
                float cs = cos(turn), sn = sin(turn);
                float2 rotated = float2(cs * delta.x - sn * delta.y, sn * delta.x + cs * delta.y);
                return mirrored(0.5 + rotated / shape);
            }
        #endif
        #if EFFECT_ONLY == 11
            if (effect.x < 11.5 && pointer.z > 0.5) // Cursor lens: magnifies under the pointer only
            {
                float2 delta = (uv - pointer.xy) * shape;
                float distance = length(delta);
                float radius = 0.23 * detailControl();
                float inside = 1 - smoothstep(radius * 0.72, radius, distance);
                return saturate(uv - delta / shape * (0.55 * intensity * inside));
            }
        #endif
            return uv;
        }
        // The chosen effect at strength above zero; pixel is one shown pixel in picture coordinates.
        float3 effectColour(float2 uv, float2 pixel, bool isYuv)
        {
            float strength = effect.y;
            float2 warped = effectUv(uv);
            float3 colour = readColour(warped, isYuv);
            float detail = detailControl();
            float2 shape = aspect();
            // Every outline effect shares one contour measurement (Topographic draws brightness
            // levels instead, so it does not need one).
            float4 field = float4(0, 0, 1, 0);
            float4 outline = 0;
        #if EFFECT_ONLY == 2 || EFFECT_ONLY == 5 || EFFECT_ONLY == 7 || EFFECT_ONLY == 13 || EFFECT_ONLY == 16 || EFFECT_ONLY == 17
            [branch] if ((effect.x > 1.5 && effect.x < 2.5) || (effect.x > 4.5 && effect.x < 5.5) || (effect.x > 6.5 && effect.x < 7.5)
                || (effect.x > 12.5 && effect.x < 13.5) || (effect.x > 15.5 && effect.x < 17.5))
                field = contourField(uv, pixel, isYuv, detail, outline);
        #endif
            // Pixel position on screen, for patterns that stay one size whatever the picture's size.
            float2 screen = uv / pixel;
        #if EFFECT_ONLY == 1
            if (effect.x < 1.5) // Prism flow's colour fringe
            {
                float2 fringe = float2(0.005, 0.0015) * strength;
                colour.r = readColour(mirrored(warped + fringe), isYuv).r;
                colour.b = readColour(mirrored(warped - fringe), isYuv).b;
            }
        #endif
        #if EFFECT_ONLY == 2
            if (effect.x < 2.5) // Neon contours
            {
                // Glowing tubes on the main outlines only (silhouettes and big shapes, which stay
                // strong at the widest scale; not texture or small features): a white-hot core inside
                // a pink bloom that spreads several pixels, over a darkened picture, humming with time.
                float hum = 0.9 + 0.1 * sin(effect.z * 3.1);
                float major = smoothstep(0.14 / detail, 0.4 / detail, outline.w);
                float core = max(outline.x, field.x) * outline.y * major;
                float bloom = smoothstep(0.1 / detail, 0.8, outline.w) * major;
                float3 neon = colour * 0.14 + bloom * 0.7 * hum * float3(1, 0.16, 0.62)
                    + core * float3(1, 0.86, 0.95);
                colour = lerp(colour, saturate(neon), strength);
            }
        #endif
        #if EFFECT_ONLY >= 5 && EFFECT_ONLY <= 7
            if (effect.x > 4.5 && effect.x < 7.5)
            {
                float lum = lumaOf(colour);
                float3 styled;
                if (effect.x < 5.5) // Ink trace: a pen sketch on paper
                {
                    // Strokes taper: clear outlines are drawn broad, faint ones as hairlines, and
                    // shadows are cross-hatched (one direction in the shade, both in the dark).
                    float stroke = max(field.y * 0.8, outline.x * smoothstep(0.2, 0.9, outline.y));
                    float hatchA = 1 - smoothstep(0.1, 0.22, abs(frac((screen.x + screen.y) / 7) - 0.5));
                    float hatchB = 1 - smoothstep(0.1, 0.22, abs(frac((screen.x - screen.y) / 7) - 0.5));
                    float hatch = hatchA * (1 - smoothstep(0.14, 0.26, lum)) + hatchB * (1 - smoothstep(0.04, 0.11, lum));
                    float ink = saturate(max(stroke, hatch * 0.3));
                    styled = float3(0.96, 0.935, 0.86) * (1 - ink * 0.86);
                }
                else if (effect.x < 6.5) // Topographic: a relief map of the picture's brightness
                {
                    // Brightness becomes height: tinted layers like a map's elevation colours,
                    // hill shading from the slope, and a thin contour line at every level.
                    // Heights follow a curve that gives the shadows as many levels as the highlights,
                    // so dark scenes are mapped too; it starts a little above black, so compression
                    // blocks in near-black never become shapes.
                    float levels = 12 * detail;
                    float height = pow(saturate((lum - 0.025) / 0.975), 0.55) * levels;
                    float4 measured = lumaSlope(uv, measureStep(pixel) * 2, isYuv);
                    float layer = saturate(floor(height) / max(levels - 1, 1));
                    float3 tint = layer < 0.25 ? lerp(float3(0.07, 0.20, 0.36), float3(0.16, 0.42, 0.40), layer * 4)
                        : layer < 0.5 ? lerp(float3(0.16, 0.42, 0.40), float3(0.45, 0.62, 0.30), layer * 4 - 1)
                        : layer < 0.75 ? lerp(float3(0.45, 0.62, 0.30), float3(0.78, 0.66, 0.38), layer * 4 - 2)
                        : lerp(float3(0.78, 0.66, 0.38), float3(0.97, 0.95, 0.90), layer * 4 - 3);
                    float3 normal = normalize(float3(-measured.xy * 12, 1));
                    float shade = 0.7 + 0.3 * dot(normal, normalize(float3(-0.5, -0.6, 0.62))) / 0.62;
                    // About one pixel wide: the level's change across one pixel.
                    float width = length(measured.xy) * 0.5 * levels * 0.55 * pow(max(lum - 0.025, 0.02), -0.45) + 0.002;
                    float isoline = 1 - smoothstep(width * 0.5, width * 1.5, abs(frac(height + 0.5) - 0.5));
                    styled = saturate(tint * shade * (1 - isoline * 0.7));
                }
                else // Chromatic contours: every fine line, coloured by the direction it faces
                {
                    float angle = atan2(field.w, field.z);
                    float3 rainbow = 0.5 + 0.5 * cos(angle + float3(0, 2.094, 4.189));
                    styled = saturate(field.y * 1.25 * (0.2 + 0.8 * rainbow));
                }
                colour = lerp(colour, styled, strength);
            }
        #endif
        #if EFFECT_ONLY == 11
            if (effect.x > 10.5 && effect.x < 11.5) // Cursor lens rim
            {
                if (pointer.z > 0.5)
                {
                    float dist = length((uv - pointer.xy) * shape);
                    float rim = 1 - smoothstep(0.003, 0.011, abs(dist - 0.23 * detail));
                    colour = saturate(colour + float3(0.23, 0.48, 0.57) * rim * strength);
                }
            }
        #endif
        #if EFFECT_ONLY == 12
            if (effect.x > 11.5 && effect.x < 12.5) // Edge gravity
            {
                // Outlines in the current picture become elastic folds along their own normals;
                // flat regions stay still.
                float2 gradient = lumaSlope(uv, measureStep(pixel), isYuv).xy;
                float gradientSize = length(gradient);
                float edge = smoothstep(0.045, 0.28, gradientSize);
                float2 normal = gradient / max(gradientSize, 0.0001);
                float phase = sin(effect.z * 1.7 * detail + uv.x * 12 + uv.y * 9);
                float2 pulled = mirrored(uv + normal / shape * (0.028 * strength * edge * phase));
                colour = lerp(colour, readColour(pulled, isYuv), edge * strength);
                colour = saturate(colour + edge * (0.11 * strength) * float3(0.11, 0.65, 0.98));
            }
        #endif
        #if EFFECT_ONLY == 13
            if (effect.x > 12.5 && effect.x < 13.5) // Ghostwire
            {
                // Visible outlines, faint folds and fine texture become luminous filaments over a
                // dark glass picture. It shows edges in the recording, nothing hidden.
                float luminance = lumaOf(colour);
                float2 ghostUv = mirrored(uv + field.zw * measureStep(pixel) * (1.1 + 0.25 * sin(effect.z * 0.7)));
                float ghostLuma = lumaOf(readColour(ghostUv, isYuv));
                float3 phantom = float3(0.012, 0.018, 0.034) + luminance * 0.055
                    + float3(0.06, 0.09, 0.14) * ghostLuma * 0.16;
                phantom += field.y * float3(0.21, 0.96, 0.91);
                phantom += (field.x - field.y) * float3(0.05, 0.16, 0.17);
                colour = lerp(colour, saturate(phantom), strength);
            }
        #endif
        #if EFFECT_ONLY == 14
            if (effect.x > 13.5 && effect.x < 14.5) // Colour spotlight
            {
                // The pointer chooses a colour, not an object; with no pointer nothing changes.
                if (pointer.z > 0.5)
                {
                    float3 chosen = readColour(pointer.xy, isYuv);
                    float deviation = length(colour - chosen);
                    float similarity = 1 - smoothstep(0.07 / detail, 0.38 / detail, deviation);
                    float grey = lumaOf(colour);
                    float3 muted = lerp(colour, float3(grey, grey, grey) * 0.7, 0.82 * strength);
                    float3 vivid = saturate(colour * (1 + strength * 0.24)
                        + similarity * strength * float3(0.03, 0.11, 0.17));
                    colour = lerp(muted, vivid, similarity);
                }
            }
        #endif
        #if EFFECT_ONLY == 15
            if (effect.x > 14.5 && effect.x < 15.5) // Relief etch
            {
                // Light falls across the picture's slope: a relief illusion, not a depth map.
                float2 slope = lumaSlope(uv, measureStep(pixel), isYuv).xy * (5 * detail);
                float3 normal = normalize(float3(-slope.x, -slope.y, 0.65));
                float directional = saturate(dot(normal, normalize(float3(-0.55, -0.4, 0.75))));
                float edge = smoothstep(0.04, 0.35, length(slope));
                float3 relief = saturate(colour * (0.35 + 0.95 * directional)
                    + edge * (directional - 0.5) * float3(0.21, 0.24, 0.22));
                colour = lerp(colour, relief, strength);
            }
        #endif
        #if EFFECT_ONLY == 16
            if (effect.x > 15.5 && effect.x < 16.5) // Ghostwire mask mode
            {
                // A dim mask of the picture: flat shapes in four tones from its brightness, solid
                // bright outlines around them and the finer contours inside as dotted lines. It does
                // not detect, remove or reveal anything.
                float luminance = lumaOf(colour);
                float tone = floor(saturate(luminance * 1.15) * 3.999) / 3;
                float3 maskColour = lerp(float3(0.03, 0.02, 0.07), float3(0.22, 0.15, 0.34), tone);
                float inner = saturate(field.y - outline.x * outline.y);
                float dots = 1 - smoothstep(0.3, 0.45, length(frac(screen / 3.5) - 0.5));
                maskColour += outline.x * outline.y * float3(0.92, 0.86, 1);
                maskColour += inner * dots * float3(0.62, 0.48, 0.90);
                colour = lerp(colour, saturate(maskColour), strength);
            }
        #endif
        #if EFFECT_ONLY == 17
            if (effect.x > 16.5 && effect.x < 17.5) // Ghostwire motion
            {
                // Contours plus a conservative two-picture motion residual: matching the previous
                // picture at nearby places rejects slight pans and shake. Mode (extras.y): 0 hybrid,
                // 1 contours, 2 motion, 3 surface shape (no previous picture needed).
                float edge = field.y;
                float mode = extras.y;
                float trails = saturate(extras.x * 0.01);
                float activity = 0;
                float oldEdge = 0;
                float previousDifference = 0;
                if (motion.y > 0.5 && (mode < 0.5 || (mode > 1.5 && mode < 2.5)))
                {
                    float2 px = pixel;
                    float3 old = readPrior(uv, isYuv);
                    float3 oldRight = readPrior(uv + float2(px.x, 0), isYuv);
                    float3 oldLeft = readPrior(uv - float2(px.x, 0), isYuv);
                    float3 oldDown = readPrior(uv + float2(0, px.y), isYuv);
                    float3 oldUp = readPrior(uv - float2(0, px.y), isYuv);
                    previousDifference = length(colour - old);
                    float mismatch = min(min(previousDifference, length(colour - oldRight)),
                        min(min(length(colour - oldLeft), length(colour - oldDown)), length(colour - oldUp)));
                    float3 oldDx = oldRight - oldLeft;
                    float3 oldDy = oldDown - oldUp;
                    oldEdge = smoothstep(0.05 / detail, 0.25 / detail, sqrt(dot(oldDx, oldDx) + dot(oldDy, oldDy)));
                    // Gating by edges keeps a global brightness change from lighting flat regions.
                    activity = smoothstep(0.045, 0.22, mismatch) * max(field.y, oldEdge);
                }
                float3 ink = float3(0.013, 0.02, 0.033) + colour * 0.055;
                // Still outlines are a pale periwinkle so the magenta of movement stands out.
                float3 stationary = edge * float3(0.50, 0.58, 0.92);
                float trail = trails * (oldEdge * smoothstep(0.025, 0.18, previousDifference) + activity * 0.6)
                    * (1 - edge * 0.35);
                float3 moving = activity * float3(0.60, 0.18, 0.37) + trail * float3(0.10, 0.40, 0.78);
                if (mode > 2.5)
                    ink = surfaceContours(uv, pixel, isYuv, colour, detail, edge);
                else if (mode < 0.5)
                    ink += stationary + moving;
                else if (mode < 1.5)
                    ink += stationary;
                else
                    ink += moving;
                colour = lerp(colour, saturate(ink), strength);
            }
        #endif
        #if EFFECT_ONLY == 18
            if (effect.x > 17.5 && effect.x < 18.5) // Halftone print
            {
                // Three screens of round dots, one for each printing ink, whose areas follow the ink
                // each colour needs. White paper stays clean; full coverage closes up solid.
                float3 coverage = 1 - saturate(colour);
                float3 ink = 0;
                float cells = lerp(180, 42, saturate((detail - 0.25) / 1.5));
                float2 p = (uv - 0.5) * shape * cells;
                float spread = lerp(0.62, 0.74, saturate(extras.y * 0.01));
                float softness = max(length(pixel * shape) * cells * 0.75, 0.02);
                [unroll] for (int channel = 0; channel < 3; channel++)
                {
                    float angle = (extras.x + channel * 30) * 0.01745329252;
                    float cs = cos(angle), sn = sin(angle);
                    float2 rotated = float2(p.x * cs - p.y * sn, p.x * sn + p.y * cs);
                    float radius = sqrt(coverage[channel]) * spread;
                    float dist = length(frac(rotated) - 0.5);
                    ink[channel] = coverage[channel] >= 0.995 ? 1 : saturate((radius - dist) / softness + 0.5) * saturate(radius / softness);
                }
                colour = lerp(colour, 1 - ink, strength);
            }
        #endif
        #if EFFECT_ONLY == 19
            if (effect.x > 18.5 && effect.x < 19.5) // Chromatic rift
            {
                float angle = extras.x * 0.01745329252;
                float2 axis = float2(cos(angle), sin(angle));
                float drive = 1;
                if (extras.y > 0 && motion.y > 0.5)
                    drive += saturate(length(colour - readPrior(uv, isYuv)) * 3) * extras.y * 0.025;
                float2 shift = axis * strength * drive * 0.012 * detail / shape;
                float3 red = readColour(mirrored(uv + shift), isYuv);
                float3 blue = readColour(mirrored(uv - shift), isYuv);
                colour = float3(red.r, colour.g, blue.b);
            }
        #endif
        #if EFFECT_ONLY == 20
            if (effect.x > 19.5 && effect.x < 20.5) // Oil paint
            {
                // Sector filter: each wedge around the pixel is averaged, and wedges that vary less
                // weigh more, so strokes follow edges instead of blurring them (sharpness sets how
                // strongly the calmest wedge wins).
                int count = (int)clamp(round(extras.x), 4, 8);
                float2 texel = measureStep(pixel);
                float radius = 2 + detail * 6;
                float sharpness = 1 + saturate(extras.y * 0.01) * 7;
                float3 sum = 0;
                float weights = 0;
                [loop] for (int sector = 0; sector < 8; sector++)
                {
                    if (sector >= count) break;
                    float angle = (sector + 0.5) * 6.28318530718 / count;
                    float2 ray = float2(cos(angle), sin(angle)) * radius;
                    float2 side = float2(-ray.y, ray.x) * (3.14159265 / count);
                    float3 a = readColour(uv + ray * 0.35 * texel, isYuv);
                    float3 b = readColour(uv + (ray * 0.8 + side * 0.45) * texel, isYuv);
                    float3 d = readColour(uv + (ray * 0.8 - side * 0.45) * texel, isYuv);
                    float3 mean = (colour + a + b + d) * 0.25;
                    float3 spreadColour = (colour * colour + a * a + b * b + d * d) * 0.25 - mean * mean;
                    float variance = dot(max(spreadColour, 0), float3(1, 1, 1));
                    float weight = 1 / (1 + pow(variance * 400, sharpness * 0.5));
                    sum += mean * weight;
                    weights += weight;
                }
                colour = lerp(colour, sum / max(weights, 1e-6), strength);
            }
        #endif
        #if EFFECT_ONLY == 21
            if (effect.x > 20.5 && effect.x < 21.5) // Glyph mosaic
            {
                float rows = lerp(110, 23, saturate((detail - 0.25) / 1.5));
                float2 cells = float2(rows * shape.x * 0.54, rows);
                float2 cellId = floor(uv * cells);
                // Each character sits inside its cell with a margin, so glyphs read as separate
                // characters rather than joining into a grid.
                float2 local = (frac(uv * cells) - 0.15) / 0.7;
                float inCell = all(local > 0) && all(local < 1) ? 1 : 0;
                float3 sampled = readColour((cellId + 0.5) / cells, isYuv);
                float grey = lumaOf(sampled);
                float mode = extras.y;
                bool paperMode = mode > 1.5 && mode < 2.5;
                // Light glyphs on a dark screen are densest where the picture is brightest; dark ink
                // on paper is densest where it is darkest.
                float density = paperMode ? 1 - grey : grey;
                float stroke = 0.08;
                float vertical = 1 - smoothstep(stroke, stroke + 0.07, abs(local.x - 0.5));
                float across = 1 - smoothstep(stroke, stroke + 0.07, abs(local.y - 0.5));
                float diagonal = 1 - smoothstep(stroke, stroke + 0.07, abs(local.x - local.y));
                float rim = 1 - smoothstep(0.07, 0.13, min(min(local.x, 1 - local.x), min(local.y, 1 - local.y)));
                float glyph = density > 0.80 ? max(rim, max(vertical, across)) : density > 0.55 ? max(vertical, across)
                    : density > 0.30 ? diagonal : density > 0.11 ? vertical : 0;
                glyph = saturate(glyph * inCell * (0.7 + extras.x * 0.006));
                float3 palette = mode < 0.5 ? float3(0.28, 1, 0.48)
                    : mode < 1.5 ? float3(1, 0.72, 0.27)
                    : mode < 2.5 ? float3(0.18, 0.16, 0.13) : sampled;
                float3 paper = paperMode ? float3(0.94, 0.91, 0.80) : float3(0.025, 0.03, 0.045);
                colour = lerp(colour, lerp(paper, palette, glyph), strength);
            }
        #endif
        #if EFFECT_ONLY == 22
            if (effect.x > 21.5 && effect.x < 22.5) // Thermal map
            {
                float range = extras.x * 0.01;
                float temperature = saturate(lumaOf(colour) * range + (1 - range) * 0.5);
                float mode = extras.y;
                float3 heat;
                if (mode < 0.5) // Iron: black, violet, orange, white
                    heat = temperature < 0.33
                        ? lerp(float3(0.02, 0.01, 0.08), float3(0.43, 0.03, 0.42), temperature / 0.33)
                        : temperature < 0.66
                        ? lerp(float3(0.43, 0.03, 0.42), float3(1, 0.40, 0.05), (temperature - 0.33) / 0.33)
                        : lerp(float3(1, 0.40, 0.05), float3(1, 1, 0.82), (temperature - 0.66) / 0.34);
                else if (mode < 1.5) // Rainbow: blue when cold through to red when hot, never wrapping
                    heat = saturate(0.5 + 0.5 * cos(6.2831853 * ((1 - temperature) * 0.67 - float3(0, 0.3333, 0.6667))));
                else if (mode < 2.5)
                    heat = lerp(float3(0.01, 0.07, 0.18), float3(0.82, 1, 0.93), temperature);
                else
                    heat = float3(temperature, temperature, temperature);
                float edges = length(lumaSlope(uv, measureStep(pixel), isYuv).xy);
                heat = saturate(heat + edges * (detail - 0.25) * 0.3);
                colour = lerp(colour, heat, strength);
            }
        #endif
        #if EFFECT_ONLY == 23
            if (effect.x > 22.5 && effect.x < 23.5) // Tape wear
            {
                // Lines that wobble together in bands, colour that bleeds to the right, a noisy
                // head-switching band near the bottom and light speckle, all following picture time.
                float row = floor(uv.y * 480);
                float epoch = floor(effect.z * 24);
                float wobble = (hash(float2(floor(row / 6), epoch)) * 2 - 1) * 0.6
                    + sin(uv.y * 9 + effect.z * 2.3) * 0.4;
                float tracking = wobble * strength * detail * 0.012 / shape.x;
                float2 shifted = mirrored(uv + float2(tracking, 0));
                float3 base = readColour(shifted, isYuv);
                float bleed = saturate(extras.x * 0.01) * 0.006 * strength / shape.x;
                float3 smeared = (readColour(mirrored(shifted + float2(bleed, 0)), isYuv)
                    + readColour(mirrored(shifted + float2(bleed * 2, 0)), isYuv)) * 0.5;
                float y = lumaOf(base);
                float3 chromaPart = lerp(base, smeared, 0.8) - lumaOf(lerp(base, smeared, 0.8));
                float3 worn = y + chromaPart;
                float headBand = smoothstep(0.93, 0.95, uv.y) * saturate(extras.y * 0.01);
                float headShift = (hash(float2(row, epoch + 7)) * 2 - 1) * 0.03 * headBand;
                worn = lerp(worn, readColour(mirrored(shifted + float2(headShift, 0)), isYuv) * 0.75, headBand);
                float speckle = hash(float2(floor(uv.x * 640) + row * 640, epoch));
                worn += (speckle > 0.9993 ? 0.5 : (speckle - 0.5) * 0.05) * strength;
                colour = lerp(colour, saturate(worn), strength);
            }
        #endif
        #if EFFECT_ONLY == 24
            if (effect.x > 23.5 && effect.x < 24.5) // Pixel sort
            {
                // Bright pixels run in the chosen direction: each pixel takes the brightest pixel
                // above its threshold from a span behind it, fading with distance.
                float mode = extras.y;
                float2 direction = mode < 0.5 ? float2(0, 1)
                    : mode < 1.5 ? float2(0, -1)
                    : mode < 2.5 ? float2(1, 0) : float2(-1, 0);
                float span = saturate(extras.x * 0.01) * 0.14;
                float threshold = saturate(detail * 0.45);
                float bestScore = lumaOf(colour);
                float3 best = colour;
                [unroll] for (int i = 1; i <= 10; i++)
                {
                    float2 target = uv - direction / shape * span * (i / 10.0);
                    float3 sampleColour = readColour(saturate(target), isYuv);
                    float lum = lumaOf(sampleColour);
                    float score = lum - i * 0.018;
                    if (lum > threshold && score > bestScore)
                    {
                        best = sampleColour;
                        bestScore = score;
                    }
                }
                colour = lerp(colour, best, strength);
            }
        #endif
        #if EFFECT_ONLY == 25
            if (effect.x > 24.5 && effect.x < 25.5) // Motion smear
            {
                if (motion.y > 0.5)
                {
                    float2 blocks = float2(14 + extras.y * 0.45, 10 + extras.y * 0.25);
                    float2 tile = floor(uv * blocks);
                    float epoch = floor(effect.z * 12);
                    float random = hash(tile + float2(epoch * 31, epoch * 17));
                    float2 offset = float2((random - 0.5) * 0.065 * strength, 0);
                    float3 previous = readPrior(mirrored(uv + offset), isYuv);
                    float delta = length(colour - previous);
                    float gate = smoothstep(0.03 * detail, 0.20 * detail, delta);
                    colour = lerp(colour, previous, gate * strength * extras.x * 0.01);
                }
            }
        #endif
        """ + "\n" + D3D11VideoShaderContinuation.Source;
}
