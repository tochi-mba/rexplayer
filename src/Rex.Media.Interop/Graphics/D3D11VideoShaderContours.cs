namespace Rex.Media.Interop.Graphics;

/// <summary>
/// The contour measurement every outline effect shares: slopes and their noise floor at three
/// scales, in colour as well as brightness, and the surface relief of Ghostwire motion. Part of
/// <see cref="D3D11VideoShaders"/>, kept in its own file for its size.
/// </summary>
internal static class D3D11VideoShaderContours
{
    public const string Source = """
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
            // Bold outlines count directly; faint ones need spatial continuity.
            float contrast = sqrt(max(fine.z, broad.z * 0.4));
            float strong = smoothstep(0.035 / s, 0.10 / s, contrast) * smoothstep(0.35, 0.6, confidence);
            // Eleven signed, edge-aligned contrasts cancel random grain while retaining lines.
            // Colour-only boundaries use colour confidence instead of luma.
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
            // Suppress off-crest edges; colour-only boundaries retain their width.
            float centre = fineLuma * fine.z;
            float crest = smoothstep(0.5, 0.95, centre / max(max(n1.x, n2.x), 1e-12));
            crest = lerp(1, crest, fineLuma * fineLuma);
            // A wide scale catches soft boundaries in dark scenes without tracing gentle shading.
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
            // A pewter bas-relief: light raking across the picture's own shading (lifted in the
            // shadows), engraved lines at steady brightness levels where the surface slopes, and the
            // outlines catching the light.
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
            float3 base = float3(0.72, 0.69, 0.64) * (0.12 + 0.6 * lifted) * (0.3 + 0.85 * light);
            base *= 1 - contours * visibleSurface * 0.45;
            base += fineEdge * float3(0.55, 0.55, 0.52) * light;
            return saturate(base);
        }
        """;
}
