namespace Rex.Media.Interop.Graphics;

/// <summary>
/// The Direct3D shader program kept apart from device lifecycle and COM interop.
/// Gradients and line styles operate exclusively on decoded video pixels.
/// </summary>
internal static class D3D11VideoShaders
{
    public const string Source = """
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
                float detail = max(pointer.w, 0.25);
                float3 structure = contourDetail(uv, isYuv);
                float edge = smoothstep(0.007 / detail, 0.18 / detail, structure.z);
                float3 ink = colour * 0.22 + edge * float3(0.12, 0.95, 1);
                colour = lerp(colour, ink, effect.y);
            }

            if (effect.y > 0 && effect.x > 4.5 && effect.x < 7.5)
            {
                float3 structure = contourDetail(uv, isYuv);
                float2 gradient = structure.xy;
                float detail = max(pointer.w, 0.25);
                float edge = smoothstep(0.006 / detail, 0.16 / detail, structure.z);
                float lum = dot(colour, float3(0.2126, 0.7152, 0.0722));
                float3 styled = colour;
                if (effect.x < 5.5) // Ink trace: paper-coloured rendering with dark outlines
                {
                    float ink = smoothstep(0.025, 0.50, edge);
                    styled = saturate(float3(0.96, 0.935, 0.86) - ink * 0.9 - (1 - lum) * 0.12);
                }
                else if (effect.x < 6.5) // Topographic: isolines of actual pixel luminance
                {
                    float iso = 1 - smoothstep(0.025, 0.12,
                        abs(frac(lum * 20 * detail) - 0.5));
                    float slopes = smoothstep(0.015, 0.28, edge);
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
                // Ghostwire: capture weak internal folds, fine recorded texture and
                // high-contrast outlines as luminous filaments. Not an x-ray.
                float detail = max(pointer.w, 0.25);
                float3 structure = contourDetail(uv, isYuv);
                float2 direction = normalize(structure.xy + float2(0.000001, 0));
                float2 stepUv = max(fwidth(uv), float2(0.00025, 0.00025))
                    / detail;
                float2 offsetUv = direction * stepUv;
                float originalEnergy = contourGradient(uv, isYuv, 0.75).z;
                float prior = contourGradient(saturate(uv - offsetUv), isYuv, 0.75).z;
                float later = contourGradient(saturate(uv + offsetUv), isYuv, 0.75).z;
                float ridge = (originalEnergy >= prior && originalEnergy >= later) ? 1 : 0;
                float fineLine = smoothstep(0.006 / detail, 0.085 / detail, structure.z);
                float boldLine = smoothstep(0.035 / detail, 0.20 / detail, structure.z);
                // NMS thins strong boundaries, but a faint secondary trace preserves
                // subtle gradients that otherwise vanished inside grey objects.
                float thread = ridge * max(boldLine, fineLine * 0.78)
                    + (1 - ridge) * fineLine * 0.18;
                float luminance = dot(colour, float3(0.2126, 0.7152, 0.0722));
                float2 ghostUv = saturate(uv + direction * stepUv
                    * (1.1 + 0.25 * sin(effect.z * 0.7)));
                float3 ghostColour = readColour(ghostUv, isYuv);
                float ghostLuma = dot(ghostColour, float3(0.2126, 0.7152, 0.0722));
                float3 phantom = float3(0.012, 0.018, 0.034)
                    + float3(luminance, luminance, luminance) * 0.055
                    + float3(0.06, 0.09, 0.14) * ghostLuma * 0.16;
                phantom += thread * float3(0.21, 0.96, 0.91);
                phantom += fineLine * (1 - ridge) * float3(0.055, 0.075, 0.10);
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
                // Ghostwire Mask Mode: a dim visual layer with detailed contours
                // from the same recorded frame. Grey surface structure is still
                // visible even when there is no strong outer silhouette.
                float detail = max(pointer.w, 0.25);
                float3 structure = contourDetail(uv, isYuv);
                float edge = smoothstep(0.007 / detail, 0.16 / detail, structure.z);
                float2 normal = normalize(structure.xy + float2(0.000001, 0));
                float2 pixel = max(fwidth(uv), float2(0.00025, 0.00025)) / detail;
                float narrow = contourGradient(uv, isYuv, 0.75).z;
                float prior = contourGradient(saturate(uv - normal * pixel), isYuv, 0.75).z;
                float later = contourGradient(saturate(uv + normal * pixel), isYuv, 0.75).z;
                float ridge = (narrow >= prior && narrow >= later) ? 1 : 0;
                float filament = edge * (ridge ? 1 : 0.24);
                float luminance = dot(colour, float3(0.2126, 0.7152, 0.0722));
                float2 weave = uv * float2(250 * detail, 220 * detail);
                float fabricPattern = sin(weave.x) * sin(weave.y);
                float haze = (0.02 + luminance * 0.06) * (0.92 + 0.08 * fabricPattern);
                float3 maskColour = float3(0.012, 0.019, 0.032)
                    + colour * (0.07 + haze) + edge * float3(0.04, 0.15, 0.17);
                maskColour += filament * float3(0.22, 0.93, 0.96);
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
}
