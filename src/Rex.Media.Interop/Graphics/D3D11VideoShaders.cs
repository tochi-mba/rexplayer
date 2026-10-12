namespace Rex.Media.Interop.Graphics;

/// <summary>
/// The Direct3D shader program kept apart from device lifecycle and COM interop.
/// Gradients and line styles operate exclusively on decoded video pixels.
/// </summary>
internal static class D3D11VideoShaders
{
    public const string Source = """
        cbuffer Colour : register(b0) { float4 rowR; float4 rowG; float4 rowB; float4 crop; float4 source; float4 look; float4 effect; float4 pointer; float4 motion; float4 extras; };
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
        static const float3 BrickPalette[16] =
        {
            float3(0.95, 0.95, 0.93), float3(0.63, 0.65, 0.65), float3(0.33, 0.35, 0.37), float3(0.11, 0.11, 0.12),
            float3(0.79, 0.10, 0.11), float3(0.45, 0.08, 0.10), float3(0.96, 0.50, 0.10), float3(0.98, 0.80, 0.10),
            float3(0.85, 0.76, 0.56), float3(0.40, 0.24, 0.14), float3(0.14, 0.50, 0.22), float3(0.60, 0.77, 0.20),
            float3(0.05, 0.34, 0.70), float3(0.35, 0.65, 0.88), float3(0.06, 0.15, 0.33), float3(0.93, 0.55, 0.70),
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
        float3 nearestBrick(float3 c)
        {
            float best = 1e9;
            float3 chosen = c;
            [loop] for (int i = 0; i < 16; i++)
            {
                float3 d = c - BrickPalette[i];
                float error = dot(d * d, float3(0.3, 0.59, 0.11));
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
                float3 d = c - CubePalette[i];
                float error = dot(d * d, float3(0.3, 0.59, 0.11));
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
            if (look.x < 8.5)
                return saturate(float3(l * 0.07, l * 1.23, l * 0.17));
            if (look.x < 9.5) // Teal & orange: cool shadows, warm highlights, gentle S-curve
            {
                float3 split = lerp(grey, c, 1.08) + lerp(float3(-0.02, 0.05, 0.09),
                    float3(0.09, 0.03, -0.06), smoothstep(0.15, 0.8, l));
                split = saturate(split);
                return lerp(split, smoothstep(0, 1, split), 0.35);
            }
            if (look.x < 10.5) // Golden film: warm stock, lifted blacks, highlight halation
            {
                float3 film = c * float3(1.06, 1.0, 0.86) + float3(0.035, 0.025, 0.02);
                return saturate((lerp(grey, film, 1.05) - 0.5) * 0.97 + 0.5
                    + l * l * l * float3(0.10, 0.04, 0));
            }
            if (look.x < 11.5) // Bleach bypass: retained silver, low saturation, hard contrast
                return saturate((lerp(c, grey, 0.55) - 0.5) * 1.35 + 0.5 + grey * grey * 0.12 - 0.03);
            if (look.x < 12.5) // Cross process: per-channel curves, cyan shadows, yellow highlights
            {
                float3 crossed = float3(smoothstep(0.04, 0.96, c.r), c.g * 0.92 + 0.07, c.b * 0.72 + 0.10);
                return saturate(crossed + lerp(float3(-0.03, 0.04, 0.06), float3(0.06, 0.05, -0.08), l));
            }
            // Infrared: false-colour channel swap, so foliage greens turn red and reds turn gold
            float3 infrared = float3(c.g * 1.15, c.r * 0.95, c.b * 1.05);
            return saturate(infrared + smoothstep(0.55, 1, l) * float3(0.12, 0.10, 0.08));
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
        // The previous frame is sampled from separately retained GPU planes. Decode on
        // the card as for the current image; never read back video into managed memory.
        float3 readPrior(float2 uv, bool isYuv)
        {
            float2 mapped = (source.xy + saturate(uv) * (source.zw - source.xy)) * crop.xy;
            if (isYuv)
            {
                float4 samples = float4(priorLuma.Sample(linearClamp, mapped).r,
                    priorChroma.Sample(chromaSampler, mapped).rg, 1);
                return saturate(float3(dot(rowR, samples), dot(rowG, samples), dot(rowB, samples)));
            }
            return priorLuma.Sample(linearClamp, mapped).rgb;
        }
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
        // Stationary surface view: image-derived isolines on smoothed luminance
        // plus local gradient relief. Broad sampling resolves softly shaded
        // rounded forms without requiring a moving target or previous frames.
        // Flat untextured pixels have zero contour confidence.
        float3 surfaceContours(float2 uv, bool isYuv, float3 colour, float detail,
            float fineEdge)
        {
            float2 stepUv = max(fwidth(uv) * 3.0, float2(0.0005, 0.0005))
                / max(detail, 0.25);
            float3 weights = float3(0.2126, 0.7152, 0.0722);
            float centre = dot(colour, weights);
            float left = dot(readColour(uv - float2(stepUv.x, 0), isYuv), weights);
            float right = dot(readColour(uv + float2(stepUv.x, 0), isYuv), weights);
            float top = dot(readColour(uv - float2(0, stepUv.y), isYuv), weights);
            float bottom = dot(readColour(uv + float2(0, stepUv.y), isYuv), weights);
            float luma = (centre * 4 + left + right + top + bottom) * 0.125;
            float2 slope = float2(right - left, bottom - top);
            float slopeSize = length(slope) * 0.5;
            float visibleSurface = smoothstep(0.0045 / detail, 0.068 / detail, slopeSize);
            float contours = 1 - smoothstep(0.025, 0.11,
                abs(frac(luma * (18 * detail)) - 0.5));
            float3 normal = normalize(float3(-slope.x * 6, -slope.y * 6, 0.6));
            float light = 0.6 + 0.4 * dot(normal, normalize(float3(-0.45, -0.5, 0.74)));
            // Compress bright areas while lifting real low-light surface gradients.
            // This local monotone mapping does not infer any hidden geometry.
            float3 lifted = colour / (colour + float3(0.20, 0.20, 0.20));
            float3 base = lerp(colour, lifted, 0.68)
                * (0.62 + 0.25 * saturate(light))
                + float3(0.012, 0.017, 0.031);
            // Neither band nor "relief" invents anatomy: both are driven by
            // variation that can be read from the recorded picture itself.
            base += contours * visibleSurface * float3(0.10, 0.68, 0.75);
            base += visibleSurface * (1 - contours) * float3(0.045, 0.095, 0.125);
            base += fineEdge * float3(0.17, 0.70, 0.82);
            return saturate(base);
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
            if (effect.x > 16.5 && effect.x < 17.5 && effect.y > 0)
            {
                // Ghostwire Motion: fine recorded contours + conservative two-frame
                // motion residuals. Matching the previous image at nearby texels rejects
                // slight pans and camera shake; it is not a semantic motion detector.
                float3 structure = contourDetail(uv, isYuv);
                float sensitivity = max(pointer.w, 0.25);
                float edge = smoothstep(0.006 / sensitivity, 0.16 / sensitivity, structure.z);
                float activity = 0;
                float oldEdge = 0;
                float previousDifference = 0;
                if (motion.y > 0.5 && (motion.z < 0.5 || (motion.z > 1.5 && motion.z < 2.5)))
                {
                    float2 px = max(fwidth(uv), float2(0.00022, 0.00022));
                    float3 old = readPrior(uv, isYuv);
                    previousDifference = length(colour - old);
                    // A small correspondence search suppresses a one-pixel global shift
                    // and flicker from sampling while preserving larger local deformation.
                    float mismatch = length(colour - old);
                    mismatch = min(mismatch, length(colour - readPrior(uv + float2(px.x, 0), isYuv)));
                    mismatch = min(mismatch, length(colour - readPrior(uv - float2(px.x, 0), isYuv)));
                    mismatch = min(mismatch, length(colour - readPrior(uv + float2(0, px.y), isYuv)));
                    mismatch = min(mismatch, length(colour - readPrior(uv - float2(0, px.y), isYuv)));
                    float3 oldDx = readPrior(uv + float2(px.x, 0), isYuv)
                        - readPrior(uv - float2(px.x, 0), isYuv);
                    float3 oldDy = readPrior(uv + float2(0, px.y), isYuv)
                        - readPrior(uv - float2(0, px.y), isYuv);
                    oldEdge = smoothstep(0.01 / sensitivity, 0.22 / sensitivity,
                        sqrt(dot(oldDx, oldDx) + dot(oldDy, oldDy)));
                    // Spatial-edge gating keeps global brightness changes from
                    // illuminating uniform picture regions.
                    activity = smoothstep(0.028, 0.21, mismatch)
                        * max(edge, oldEdge);
                }
                float3 ink = float3(0.013, 0.02, 0.033) + colour * 0.055;
                float3 stationary = edge * float3(0.12, 0.78, 0.81);
                // The previous-frame rim is a faint blue afterimage at its
                // actual prior position, not just a second glow on the new rim.
                float trail = motion.x * (oldEdge
                    * smoothstep(0.025, 0.18, previousDifference) + activity * 0.6)
                    * (1 - edge * 0.35);
                float3 moving = activity * float3(0.60, 0.18, 0.37)
                    + trail * float3(0.10, 0.40, 0.78);
                // Mode: 0 hybrid, 1 contours, 2 motion, 3 stationary
                // surface shape. Surface mode needs no previous frame, and
                // works just as well when paused or on a photograph.
                if (motion.z > 2.5)
                    ink = surfaceContours(uv, isYuv, colour, sensitivity, edge);
                else if (motion.z < 0.5)
                    ink += stationary + moving;
                else if (motion.z < 1.5)
                    ink += stationary;
                else
                    ink += moving;
                colour = lerp(colour, saturate(ink), effect.y);
            }

            // The eight additional styles stay single-pass. Extra options arrive in extras.xy,
            // independently of the global strength (effect.y) and detail (pointer.w).
            if (effect.x > 17.5 && effect.x < 18.5 && effect.y > 0) // Halftone
            {
                float3 coverage = 1 - saturate(colour);
                float3 ink = 0;
                float grid = lerp(180, 42, saturate((pointer.w - 0.25) / 1.5));
                float2 p = (uv - 0.5) * float2(effect.w, 1) * grid;
                [unroll] for (int channel = 0; channel < 3; channel++)
                {
                    float angle = (extras.x + channel * 30) * 0.01745329252;
                    float cs = cos(angle), sn = sin(angle);
                    float2 rotated = float2(p.x * cs - p.y * sn, p.x * sn + p.y * cs);
                    float radius = sqrt(coverage[channel]) * (0.40 + extras.y * 0.001);
                    float circle = 1 - smoothstep(radius - 0.07, radius + 0.07, length(frac(rotated) - 0.5));
                    ink[channel] = circle;
                }
                colour = lerp(colour, 1 - ink, effect.y);
            }
            if (effect.x > 18.5 && effect.x < 19.5 && effect.y > 0) // Chromatic rift
            {
                float angle = extras.x * 0.01745329252;
                float2 axis = float2(cos(angle), sin(angle));
                float drive = 1;
                if (extras.y > 0 && motion.y > 0.5)
                {
                    drive += saturate(length(colour - readPrior(uv, isYuv)) * 3) * extras.y * 0.025;
                }
                float2 shift = axis * effect.y * drive * 0.012 * pointer.w / float2(effect.w, 1);
                float3 red = readColour(uv + shift, isYuv);
                float3 blue = readColour(uv - shift, isYuv);
                colour = float3(red.r, colour.g, blue.b);
            }
            if (effect.x > 19.5 && effect.x < 20.5 && effect.y > 0) // Oil-paint sectors
            {
                // Three samples in each radial sector plus the centre. Pick the lowest
                // luminance variance, retaining locally consistent colour. At most 24 taps.
                int count = (int)clamp(extras.x, 4, 8);
                float2 texel = max(fwidth(uv), float2(0.00018, 0.00018));
                float radius = 1 + pointer.w * 5;
                float sharpness = 0.8 + extras.y * 0.03;
                float bestScore = 1e9;
                float3 chosen = colour;
                float luma0 = dot(colour, float3(0.2126, 0.7152, 0.0722));
                [loop] for (int sector = 0; sector < 8; sector++)
                {
                    if (sector >= count) break;
                    float angle = (sector + 0.5) * 6.28318530718 / count;
                    float2 ray = float2(cos(angle), sin(angle)) * texel * radius;
                    float2 crossRay = float2(-ray.y, ray.x);
                    float3 a = readColour(uv + ray * 0.4, isYuv);
                    float3 b = readColour(uv + ray + crossRay * 0.3, isYuv);
                    float3 d = readColour(uv + ray - crossRay * 0.3, isYuv);
                    float3 mean = (colour + a + b + d) * 0.25;
                    float3 weight = float3(0.2126, 0.7152, 0.0722);
                    float l1 = dot(a, weight), l2 = dot(b, weight), l3 = dot(d, weight);
                    float average = (luma0 + l1 + l2 + l3) * 0.25;
                    float variance = (luma0 * luma0 + l1 * l1 + l2 * l2 + l3 * l3) * 0.25 - average * average;
                    float score = variance * sharpness + abs(average - luma0) * 0.02;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        chosen = mean;
                    }
                }
                colour = lerp(colour, chosen, effect.y);
            }
            if (effect.x > 20.5 && effect.x < 21.5 && effect.y > 0) // Procedural glyph mosaic
            {
                float rows = lerp(110, 23, saturate((pointer.w - 0.25) / 1.5));
                float2 cells = float2(rows * effect.w * 0.54, rows);
                float2 cellId = floor(uv * cells);
                float2 local = frac(uv * cells);
                float3 sampled = readColour((cellId + 0.5) / cells, isYuv);
                float grey = dot(sampled, float3(0.2126, 0.7152, 0.0722));
                float vertical = 1 - smoothstep(0.08, 0.15, abs(local.x - 0.5));
                float cross = 1 - smoothstep(0.08, 0.15, abs(local.y - 0.5));
                float diagonal = 1 - smoothstep(0.08, 0.15, abs(local.x - local.y));
                float rim = 1 - smoothstep(0.07, 0.13,
                    min(min(local.x, 1 - local.x), min(local.y, 1 - local.y)));
                float glyph = grey < 0.20 ? rim : grey < 0.45 ? max(vertical, cross)
                    : grey < 0.70 ? diagonal : grey < 0.89 ? vertical : 0;
                glyph = saturate(glyph * (0.7 + extras.x * 0.006));
                float3 palette = extras.y < 0.5 ? float3(0.28, 1, 0.48)
                    : extras.y < 1.5 ? float3(1, 0.72, 0.27)
                    : extras.y < 2.5 ? float3(0.18, 0.16, 0.13) : sampled;
                float3 paper = extras.y > 1.5 && extras.y < 2.5 ? float3(0.94, 0.91, 0.80) : float3(0.025, 0.03, 0.045);
                colour = lerp(colour, lerp(paper, palette, glyph), effect.y);
            }
            if (effect.x > 21.5 && effect.x < 22.5 && effect.y > 0) // Thermal mapping
            {
                float temperature = saturate(dot(colour, float3(0.2126, 0.7152, 0.0722))
                    * (extras.x * 0.01) + (1 - extras.x * 0.01) * 0.5);
                float3 heat = temperature < 0.33
                    ? lerp(float3(0.02, 0.01, 0.08), float3(0.43, 0.03, 0.42), temperature / 0.33)
                    : temperature < 0.66
                    ? lerp(float3(0.43, 0.03, 0.42), float3(1, 0.40, 0.05), (temperature - 0.33) / 0.33)
                    : lerp(float3(1, 0.40, 0.05), float3(1, 1, 0.82), (temperature - 0.66) / 0.34);
                if (extras.y > 0.5 && extras.y < 1.5)
                    heat = 0.5 + 0.5 * cos((1 - temperature) * 6.2831853 + float3(0, 2.094, 4.189));
                else if (extras.y > 1.5 && extras.y < 2.5)
                    heat = lerp(float3(0.01, 0.07, 0.18), float3(0.82, 1, 0.93), temperature);
                else if (extras.y > 2.5)
                    heat = float3(temperature, temperature, temperature);
                float edges = length(pictureGradient(uv, isYuv));
                heat = saturate(heat + edges * (pointer.w - 0.25) * 0.20);
                colour = lerp(colour, heat, effect.y);
            }
            if (effect.x > 22.5 && effect.x < 23.5 && effect.y > 0) // Tape wear
            {
                float row = floor(uv.y * 480);
                float epoch = floor(effect.z * 23);
                float noise = frac(sin(row * 19.13 + epoch * 7.13) * 43758.545);
                float tracking = (noise * 2 - 1) * effect.y * pointer.w * 0.018;
                float2 shifted = saturate(uv + float2(tracking, 0));
                float3 shiftedColour = readColour(shifted, isYuv);
                float bleed = extras.x * 0.00008 * effect.y;
                float3 neighbour = readColour(shifted + float2(bleed, 0), isYuv);
                float head = 1 - smoothstep(0.001, 0.018,
                    abs(uv.y - (0.94 + 0.03 * sin(effect.z * 1.5)))) ;
                float speckle = frac(sin(dot(uv * 1900 + epoch, float2(12.9898, 78.233))) * 43758.5453);
                float3 worn = float3(neighbour.r, shiftedColour.g, shiftedColour.b)
                    * (1 - head * extras.y * 0.006) + (speckle - 0.5) * effect.y * 0.08;
                colour = lerp(colour, saturate(worn), effect.y);
            }
            if (effect.x > 23.5 && effect.x < 24.5 && effect.y > 0) // Bounded pixel sort
            {
                float2 direction = extras.y < 0.5 ? float2(0, 1)
                    : extras.y < 1.5 ? float2(0, -1)
                    : extras.y < 2.5 ? float2(1, 0) : float2(-1, 0);
                float2 pixel = max(fwidth(uv), float2(0.00018, 0.00018));
                float threshold = saturate(pointer.w * 0.45);
                float bestBrightness = dot(colour, float3(0.2126, 0.7152, 0.0722));
                float3 best = colour;
                [unroll] for (int i = 1; i <= 8; i++)
                {
                    float2 target = uv + direction * pixel * (i * extras.x * 0.018);
                    float3 sampleColour = readColour(target, isYuv);
                    float lum = dot(sampleColour, float3(0.2126, 0.7152, 0.0722));
                    if (lum > threshold && lum > bestBrightness)
                    {
                        best = sampleColour;
                        bestBrightness = lum;
                    }
                }
                colour = lerp(colour, best, effect.y);
            }
            if (effect.x > 24.5 && effect.x < 25.5 && effect.y > 0) // Motion smear
            {
                if (motion.y > 0.5)
                {
                    float2 blocks = float2(14 + extras.y * 0.45, 10 + extras.y * 0.25);
                    float2 tile = floor(uv * blocks);
                    float epoch = floor(effect.z * 12);
                    float random = frac(sin(dot(tile + epoch, float2(12.9898, 78.233))) * 43758.5453);
                    float2 offset = float2((random - 0.5) * 0.065 * effect.y, 0);
                    float3 previous = readPrior(uv + offset, isYuv);
                    float delta = length(colour - previous);
                    float gate = smoothstep(0.03 * pointer.w, 0.20 * pointer.w, delta);
                    colour = lerp(colour, previous, gate * effect.y * extras.x * 0.01);
                }
            }
            if (effect.x > 25.5 && effect.x < 26.5 && effect.y > 0) // Lumen weave
            {
                // A short line integral along the edge tangent, in both directions. Strength
                // accumulates only while the gradient keeps its orientation, so speckle drops
                // out and real outlines and folds become continuous strands. How far the
                // tangent turns along the walk is the strand's curvature, which colours it.
                float detail = max(pointer.w, 0.25);
                float3 here = contourDetail(uv, isYuv);
                float2 normal0 = here.xy / max(length(here.xy), 0.000001);
                float2 tangent0 = float2(-normal0.y, normal0.x);
                float2 px = max(fwidth(uv), float2(0.00022, 0.00022)) * 1.6;
                float strand = smoothstep(0.006 / detail, 0.16 / detail, here.z);
                float coherent = strand;
                float3 silk = colour;
                float silkWeight = 1;
                float turning = 0;
                float2 footprint = max(fwidth(uv) * 0.9, float2(0.00018, 0.00018)) / detail;
                [loop] for (int side = 0; side < 2; side++)
                {
                    float2 position = uv;
                    float2 tangent = side == 0 ? tangent0 : -tangent0;
                    float2 previousNormal = normal0;
                    float alive = 1;
                    [loop] for (int i = 1; i <= 6; i++)
                    {
                        position += tangent * px;
                        float3 g = contourGradientAt(position, isYuv, footprint);
                        float2 n = g.xy / max(length(g.xy), 0.000001);
                        // Edge direction is unsigned: a flipped gradient is the same line.
                        float agreement = abs(dot(n, previousNormal));
                        alive *= smoothstep(0.55, 0.85, agreement);
                        float w = 1 - i / 7.0;
                        coherent += smoothstep(0.006 / detail, 0.16 / detail, g.z) * alive * w;
                        silk += readColourAt(position, isYuv) * w;
                        silkWeight += w;
                        turning += (1 - agreement) * alive;
                        n = dot(n, previousNormal) < 0 ? -n : n;
                        float2 next = float2(-n.y, n.x);
                        tangent = dot(next, tangent) < 0 ? -next : next;
                        previousNormal = n;
                    }
                }
                // An isolated edge pixel has no coherent neighbours and stays dark; a gap of a
                // pixel or two inside a long strand is bridged by its neighbours.
                float thread = saturate((coherent - strand * 0.5) / 3.5) * saturate(strand * 2 + 0.25);
                float curve = saturate(turning * 1.5);
                float arc = dot((uv - 0.5) * float2(effect.w, 1), tangent0) * 140 / detail;
                float pulse = 0.55 + 0.45 * sin(arc - effect.z * extras.x * 0.12);
                float lum = dot(colour, float3(0.2126, 0.7152, 0.0722));
                silk /= silkWeight;
                float3 tint = extras.y < 0.5 ? lerp(float3(0.55, 0.95, 1), float3(1, 0.62, 0.18), curve)
                    : extras.y < 1.5 ? saturate(silk * 1.6 + 0.08)
                    : extras.y < 2.5 ? float3(0.92, 0.95, 1)
                    : lerp(float3(0.25, 0.35, 0.9), float3(0.95, 1, 0.95), lum);
                float3 woven = silk * 0.10 + float3(0.006, 0.008, 0.014)
                    + tint * thread * (pulse * 1.15 + thread * 0.4);
                colour = lerp(colour, saturate(woven), effect.y);
            }
            if (effect.x > 26.5 && effect.x < 27.5 && effect.y > 0) // Depth haze
            {
                // A depth guess from what is visible: fine, sharp texture reads as near;
                // soft, blurred or flat regions and the top of the frame read as far.
                // It is atmosphere, not a measurement of real distance.
                float detail = max(pointer.w, 0.25);
                float3 fine = contourGradient(uv, isYuv, 0.75);
                float3 broad = contourGradient(uv, isYuv, 4.0);
                float sharpness = saturate(fine.z / max(broad.z, 0.004));
                float textured = saturate((fine.z * 0.4 + broad.z * 0.6) * 6 * detail);
                float far = smoothstep(0.15, 0.85,
                    0.55 * (1 - textured) + 0.30 * (1 - sharpness) + 0.15 * (1 - uv.y));
                float3 fog = extras.y < 0.5 ? float3(0.42, 0.55, 0.75)
                    : extras.y < 1.5 ? float3(0.80, 0.66, 0.48)
                    : extras.y < 2.5 ? float3(0.70, 0.71, 0.72) : float3(0.40, 0.62, 0.48);
                float lum = dot(colour, float3(0.2126, 0.7152, 0.0722));
                float3 hazed = lerp(colour, lerp(float3(lum, lum, lum), colour, 0.4), far);
                hazed = lerp(hazed, fog, far * extras.x * 0.0085);
                hazed = lerp(hazed, saturate((hazed - 0.5) * 1.12 + 0.5), 1 - far);
                colour = lerp(colour, hazed, effect.y);
            }
            if (effect.x > 27.5 && effect.x < 28.5 && effect.y > 0 && motion.y > 0.5) // Particle veil
            {
                // Each grid cell may hold one spark, born where the picture changed since the
                // previous frame and rising about one cell over its life. The 3x3 neighbourhood
                // lets a spark cross into the cell above without being clipped.
                float rows = lerp(28, 90, extras.x * 0.01);
                float2 cells = float2(rows * effect.w, rows);
                float2 home = floor(uv * cells);
                float threshold = 0.03 * max(pointer.w, 0.25);
                float time = effect.z;
                float3 sparks = 0;
                [loop] for (int dy = -1; dy <= 1; dy++)
                {
                    [loop] for (int dx = -1; dx <= 1; dx++)
                    {
                        float2 cell = home + float2(dx, dy);
                        float h1 = frac(sin(dot(cell, float2(12.9898, 78.233))) * 43758.5453);
                        float h2 = frac(sin(dot(cell, float2(39.346, 11.135))) * 24634.6345);
                        float h3 = frac(sin(dot(cell, float2(73.156, 52.235))) * 19341.2137);
                        float2 birth = (cell + float2(h1, 0.5)) / cells;
                        float3 now = readColourAt(birth, isYuv);
                        float activity = smoothstep(threshold, threshold * 6,
                            length(now - readPriorAt(birth, isYuv)));
                        float life = frac(time * 0.7 + h2);
                        float2 spark = (cell + float2(0.5 + (h1 - 0.5) * 0.8
                            + 0.25 * sin(time * 2 + h2 * 6.2831853), 0.5 - life)) / cells;
                        float2 offset = (uv - spark) * cells;
                        float present = step(h3, 0.35 + 0.6 * extras.x * 0.01);
                        float glow = exp(-dot(offset, offset) * 30) * activity * (1 - life) * present;
                        float3 tint = extras.y < 0.5 ? lerp(float3(1, 0.85, 0.4), float3(1, 0.35, 0.05), life)
                            : extras.y < 1.5 ? float3(0.55, 0.85, 1)
                            : extras.y < 2.5 ? saturate(now * 1.4 + 0.1) : float3(1, 1, 1);
                        sparks += tint * glow;
                    }
                }
                colour = saturate(colour + sparks * effect.y * 1.6);
            }
            if (effect.x > 28.5 && effect.x < 29.5 && effect.y > 0) // Lens bloom
            {
                // Anamorphic streaks: only highlights above the threshold contribute, spread
                // widely along the horizontal with tails drifting to the tint colour, plus a
                // soft round glow.
                float threshold = saturate(1 - 0.35 * pointer.w);
                float3 weights = float3(0.2126, 0.7152, 0.0722);
                float3 tint = extras.y < 0.5 ? float3(0.35, 0.6, 1)
                    : extras.y < 1.5 ? float3(1, 0.7, 0.35) : float3(1, 1, 1);
                float reach = 0.02 + extras.x * 0.0025;
                float3 streak = 0;
                [loop] for (int i = -4; i <= 4; i++)
                {
                    float3 tap = readColourAt(uv + float2(i * 0.25 * reach, 0), isYuv);
                    float3 bright = tap * max(dot(tap, weights) - threshold, 0) / max(1 - threshold, 0.05);
                    float3 tail = extras.y > 2.5 ? bright : lerp(bright, tint * dot(bright, weights), abs(i) * 0.25);
                    streak += tail * exp(-abs(i) * 0.45);
                }
                float3 glow = 0;
                [loop] for (int k = 0; k < 8; k++)
                {
                    float angle = k * 0.78539816;
                    float3 tap = readColourAt(uv + float2(cos(angle) / effect.w, sin(angle)) * 0.012, isYuv);
                    glow += tap * max(dot(tap, weights) - threshold, 0) / max(1 - threshold, 0.05);
                }
                colour = saturate(colour + (streak * 0.25 + glow * 0.09) * effect.y * 1.4);
            }
            if (effect.x > 29.5 && effect.x < 30.5 && effect.y > 0) // Stipple engrave
            {
                // Engraver's hatching: the first family of lines runs along the local edge
                // (from a broad gradient, so it bends smoothly), with crossing families added
                // as the picture darkens. Flat regions fall back to a 45 degree hatch.
                float3 structure = contourDetail(uv, isYuv);
                float3 broad = contourGradient(uv, isYuv, 3.0);
                float2 diagonal = float2(0.70710678, 0.70710678);
                float2 along = float2(-broad.y, broad.x) / max(length(broad.xy), 0.000001);
                along = dot(along, diagonal) < 0 ? -along : along;
                float2 direction = normalize(lerp(diagonal, along, smoothstep(0.01, 0.08, broad.z)) + 0.000001);
                float lines = lerp(60, 200, extras.x * 0.01);
                float2 p = (uv - 0.5) * float2(effect.w, 1) * lines;
                float darkness = 1 - dot(colour, float3(0.2126, 0.7152, 0.0722));
                float weight = max(pointer.w, 0.25);
                float ink = 0;
                [unroll] for (int family = 0; family < 4; family++)
                {
                    float angle = family == 0 ? 0 : family == 1 ? 1.5707963 : family == 2 ? 0.78539816 : -0.78539816;
                    float cs = cos(angle), sn = sin(angle);
                    float2 d = float2(direction.x * cs - direction.y * sn, direction.x * sn + direction.y * cs);
                    float width = saturate(darkness * 4 - family) * 0.32 * weight;
                    float gap = abs(frac(dot(p, float2(-d.y, d.x))) - 0.5);
                    ink = max(ink, saturate((width - gap) / 0.08));
                }
                ink = max(ink, smoothstep(0.01 / weight, 0.2 / weight, structure.z));
                float3 paper = extras.y < 0.5 ? float3(0.95, 0.92, 0.84)
                    : extras.y < 1.5 ? float3(0.94, 0.94, 0.90)
                    : extras.y < 2.5 ? float3(0.03, 0.03, 0.035) : float3(0.88, 0.78, 0.60);
                float3 pen = extras.y < 0.5 ? float3(0.10, 0.08, 0.07)
                    : extras.y < 1.5 ? float3(0.08, 0.16, 0.42)
                    : extras.y < 2.5 ? float3(0.93, 0.93, 0.90) : float3(0.28, 0.16, 0.08);
                colour = lerp(colour, lerp(paper, pen, ink), effect.y);
            }
            if (effect.x > 30.5 && effect.x < 31.5 && effect.y > 0 && motion.y > 0.5) // Time echo
            {
                // Estimate which way this pixel's content moved by finding the nearby shift of
                // the previous frame that matches it best, then split the change into channels:
                // red where it was, green where it is, blue a step ahead. Still pixels are untouched.
                float2 px = max(fwidth(uv), float2(0.00022, 0.00022));
                float3 prior = readPrior(uv, isYuv);
                float threshold = 0.03 * max(pointer.w, 0.25);
                float gate = saturate(smoothstep(threshold, threshold * 5, length(colour - prior))
                    * (1 + extras.y * 0.03));
                float2 travel = float2(1, 0);
                float bestError = length(colour - prior);
                [loop] for (int k = 0; k < 8; k++)
                {
                    float angle = k * 0.78539816;
                    float2 candidate = float2(cos(angle), sin(angle));
                    float error = length(colour - readPriorAt(uv - candidate * px * 8, isYuv));
                    if (error < bestError)
                    {
                        bestError = error;
                        travel = candidate;
                    }
                }
                float2 reach = travel * px * (4 + extras.x * 0.6);
                float3 echo = float3(readPrior(uv + reach, isYuv).r, colour.g,
                    readColour(uv - reach, isYuv).b);
                colour = lerp(colour, saturate(echo * 1.08), gate * effect.y);
            }
            if (effect.x > 31.5 && effect.x < 32.5 && effect.y > 0) // Brick build
            {
                // Two-stud bricks in a running bond, seen from above. Each brick takes one
                // colour from the picture under it; each stud is lit from the top left and
                // casts a short shadow.
                float3 weights = float3(0.2126, 0.7152, 0.0722);
                float rows = mosaicRows(96, 18);
                float2 cells = float2(rows * effect.w, rows);
                float2 cell = floor(uv * cells);
                float2 local = frac(uv * cells);
                float shift = positiveMod(cell, float2(2, 2)).y;
                bool starts = positiveMod(cell + float2(shift, 0), float2(2, 2)).x < 0.5;
                float2 brickCentre = float2(starts ? cell.x + 1 : cell.x, cell.y + 0.5) / cells;
                float3 sampled = readColour(brickCentre, isYuv);
                float grey = dot(sampled, weights);
                float3 brick = extras.y < 0.5 ? nearestBrick(sampled)
                    : extras.y < 1.5 ? sampled
                    : extras.y < 2.5 ? (floor(grey * 5 + 0.5) / 5).xxx
                    : lerp(nearestBrick(sampled), float3(1, 1, 1), 0.35);
                float height = extras.x * 0.01;
                float across = starts ? local.x : 1 - local.x;
                float shading = 1 + 0.16 * (1 - smoothstep(0, 0.1, local.y)) - 0.24 * smoothstep(0.88, 1, local.y)
                    + (starts ? 0.10 * (1 - smoothstep(0, 0.1, local.x)) : -0.18 * smoothstep(0.88, 1, local.x));
                float seam = 1 - smoothstep(0.015, 0.05, min(across, min(local.y, 1 - local.y)));
                float2 d = local - 0.5;
                float radius = length(d);
                float stud = 1 - smoothstep(0.27, 0.30, radius);
                float rim = smoothstep(0.18, 0.29, radius) * stud;
                float facing = dot(d / max(radius, 0.0001), float2(-0.7071, -0.7071));
                float3 studColour = brick * (1.06 + rim * facing * 0.35 * (0.4 + height));
                float shadow = (1 - stud) * (1 - smoothstep(0.26, 0.36, length(d - float2(0.07, 0.07)))) * 0.45 * height;
                float3 built = lerp(brick * shading * (1 - shadow), studColour, stud);
                colour = lerp(colour, saturate(built * (1 - seam * 0.6)), effect.y);
            }
            if (effect.x > 32.5 && effect.x < 33.5 && effect.y > 0) // Stained glass
            {
                // Irregular panes from a jittered cell pattern: each pane takes the colour under
                // its centre, deepened like coloured glass and lit from behind, framed by lead.
                float3 weights = float3(0.2126, 0.7152, 0.0722);
                float rows = mosaicRows(60, 9);
                float2 grid = float2(rows * effect.w, rows);
                float2 p = uv * grid;
                float2 base = floor(p);
                float nearest = 8, second = 8;
                float2 site = p;
                [loop] for (int y = -1; y <= 1; y++)
                {
                    [loop] for (int x = -1; x <= 1; x++)
                    {
                        float2 c = base + float2(x, y);
                        float2 s = c + 0.15 + 0.7 * float2(hash12(c), hash12(c + 17.31));
                        float d = length(p - s);
                        if (d < nearest) { second = nearest; nearest = d; site = s; }
                        else if (d < second) second = d;
                    }
                }
                float width = 0.025 + extras.x * 0.0007;
                float border = (second - nearest) * 0.5;
                float lead = 1 - smoothstep(width, width + 0.025, border);
                float3 glass = readColour(site / grid, isYuv);
                float lum = dot(glass, weights);
                glass = saturate(lerp(lum.xxx, glass, 1.6) * 1.05);
                float ripple = sin(dot(p, float2(7.1, 3.3)) + hash12(site) * 6.2831853) * 0.05;
                glass *= 0.78 + ripple + extras.y * 0.0045 * (1 - saturate(nearest * 1.3));
                float3 leadColour = float3(0.06, 0.06, 0.065) + (1 - smoothstep(0, width, border)) * 0.10;
                colour = lerp(colour, saturate(lerp(glass, leadColour, lead)), effect.y);
            }
            if (effect.x > 33.5 && effect.x < 34.5 && effect.y > 0) // Cross-stitch
            {
                // Counted cross-stitch: one X of thread per cell, its colour the picture's
                // snapped to a limited set of skeins. Cells close to the fabric stay bare.
                float rows = mosaicRows(120, 26);
                float2 cells = float2(rows * effect.w, rows);
                float2 cell = floor(uv * cells);
                float2 local = frac(uv * cells);
                float3 sampled = readColour((cell + 0.5) / cells, isYuv);
                float3 thread = floor(sampled * 6 + 0.5) / 6;
                float3 fabric = extras.y < 0.5 ? float3(0.93, 0.90, 0.82)
                    : extras.y < 1.5 ? float3(0.08, 0.08, 0.09)
                    : extras.y < 2.5 ? float3(0.82, 0.76, 0.66) : float3(0.25, 0.35, 0.52);
                float hole = 1 - smoothstep(0.07, 0.13, length(min(local, 1 - local)));
                float weave = 0.04 * sin(local.x * 25.13) * sin(local.y * 25.13);
                float3 cloth = fabric * (1 + weave - hole * 0.45);
                float thickness = 0.07 + extras.x * 0.0011;
                float under = abs(local.x - local.y) * 0.7071;
                float over = abs(local.x + local.y - 1) * 0.7071;
                float lower = 1 - smoothstep(thickness - 0.02, thickness, under);
                float upper = 1 - smoothstep(thickness - 0.02, thickness, over);
                float3 lowerThread = thread * (0.86 + 0.14 * sin((local.x + local.y) * 38))
                    * (1 - 0.35 * pow(saturate(under / thickness), 2));
                float3 upperThread = thread * (0.86 + 0.14 * sin((local.x - local.y) * 38))
                    * (1 - 0.35 * pow(saturate(over / thickness), 2)) * 1.05;
                float stitched = step(0.12, length(thread - fabric));
                float3 result = lerp(cloth, lowerThread, lower * stitched);
                colour = lerp(colour, saturate(lerp(result, upperThread, upper * stitched)), effect.y);
            }
            if (effect.x > 34.5 && effect.x < 35.5 && effect.y > 0) // LED wall
            {
                // A wall of light-emitting diodes on a black panel, each cell lit by the picture
                // under it: red, green and blue triads, round lamps, amber lamps or square pixels.
                float3 weights = float3(0.2126, 0.7152, 0.0722);
                float rows = mosaicRows(140, 28);
                float2 cells = float2(rows * effect.w, rows);
                float2 cell = floor(uv * cells);
                float2 local = frac(uv * cells);
                float3 sampled = readColour((cell + 0.5) / cells, isYuv);
                float glow = extras.x * 0.01;
                float3 lit = 0;
                if (extras.y < 0.5)
                {
                    [unroll] for (int k = 0; k < 3; k++)
                    {
                        float2 offset = float2(local.x - (k * 2 + 1) / 6.0, (local.y - 0.5) * 0.42);
                        float spread = dot(offset, offset);
                        float diode = 1 - smoothstep(0.009, 0.016, spread);
                        float3 channel = k == 0 ? float3(1, 0.12, 0.06) : k == 1 ? float3(0.1, 1, 0.2) : float3(0.12, 0.25, 1);
                        lit += channel * sampled[k] * (diode * 1.6 + exp(-spread * 60) * glow);
                    }
                }
                else if (extras.y < 2.5)
                {
                    float2 offset = local - 0.5;
                    float spread = dot(offset, offset);
                    float lamp = 1 - smoothstep(0.11, 0.15, spread);
                    float3 source = extras.y < 1.5 ? sampled : float3(1, 0.62, 0.12) * dot(sampled, weights);
                    lit = source * (lamp * 1.25 * (0.92 + 0.08 * (1 - spread * 6)) + exp(-spread * 14) * glow * 0.7);
                }
                else
                {
                    float2 q = abs(local - 0.5) - 0.36;
                    float box = 1 - smoothstep(0, 0.04, length(max(q, 0)) - 0.04);
                    lit = sampled * (box * 1.1 + glow * 0.15);
                }
                colour = lerp(colour, saturate(float3(0.012, 0.012, 0.016) + sampled * glow * 0.06 + lit), effect.y);
            }
            if (effect.x > 35.5 && effect.x < 36.5 && effect.y > 0) // Digital rain
            {
                // Columns of procedural glyphs fall at their own speeds, a bright head leading
                // each stream and its trail fading behind. The picture shows through as how
                // strongly every glyph glows, so shapes stay readable inside the rain.
                float3 weights = float3(0.2126, 0.7152, 0.0722);
                float rows = mosaicRows(90, 22);
                float2 cells = float2(rows * effect.w * 0.6, rows);
                float2 cell = floor(uv * cells);
                float2 local = frac(uv * cells);
                float time = effect.z;
                float speed = (0.4 + extras.x * 0.02) * (0.6 + hash12(float2(cell.x, 1.7)) * 0.8);
                float trail = 0;
                float head = 0;
                [unroll] for (int stream = 0; stream < 2; stream++)
                {
                    float offset = hash12(float2(cell.x, 3.1 + stream * 5.3));
                    float headRow = frac(time * speed * 0.08 + offset) * (cells.y + 40) - 20;
                    float behind = headRow - cell.y;
                    float span = 6 + 14 * hash12(float2(cell.x, 9.7 + stream));
                    trail = max(trail, behind >= 0 ? exp(-behind / span) : 0);
                    head = max(head, behind >= 0 && behind < 1 ? 1 : 0);
                }
                float2 inner = (local - float2(0.15, 0.1)) / float2(0.7, 0.8);
                float2 bit = floor(inner * float2(3, 5));
                float flicker = floor(time * (1.5 + hash12(cell) * 5));
                float glyph = step(0.45, hash12(cell * 1.31 + bit * 7.17 + flicker))
                    * step(0, inner.x) * step(inner.x, 1) * step(0, inner.y) * step(inner.y, 1);
                float3 sampled = readColour((cell + 0.5) / cells, isYuv);
                float lum = dot(sampled, weights);
                float intensity = glyph * saturate(max(trail * (0.4 + 0.6 * lum), lum * 0.5));
                float3 tint = extras.y < 0.5 ? float3(0.25, 1, 0.45)
                    : extras.y < 1.5 ? float3(0.3, 0.9, 1)
                    : extras.y < 2.5 ? float3(1, 0.7, 0.2) : saturate(sampled * 1.3 + 0.05);
                float3 rain = tint * intensity + head * glyph * float3(0.8, 1, 0.85) * 0.9 + tint * 0.015;
                colour = lerp(colour, saturate(rain), effect.y);
            }
            if (effect.x > 36.5 && effect.x < 37.5 && effect.y > 0) // Dice mosaic
            {
                // Every cell is a die showing the face that matches the brightness under it:
                // on white dice a dark area shows six pips and a bright one shows one.
                float3 weights = float3(0.2126, 0.7152, 0.0722);
                float rows = mosaicRows(80, 16);
                float2 cells = float2(rows * effect.w, rows);
                float2 cell = floor(uv * cells);
                float2 local = frac(uv * cells);
                float3 sampled = readColour((cell + 0.5) / cells, isYuv);
                float lum = dot(sampled, weights);
                bool blackDice = extras.y > 0.5 && extras.y < 1.5;
                float value = blackDice ? clamp(floor(lum * 6) + 1, 1, 6) : clamp(6 - floor(lum * 6), 1, 6);
                float2 q = abs(local - 0.5) - 0.32;
                float bodyDistance = length(max(q, 0)) - 0.12;
                float body = 1 - smoothstep(-0.01, 0.01, bodyDistance);
                float radius = 0.07 + extras.x * 0.0005;
                // Odd faces have a centre pip; then the diagonal, the other diagonal and the middle pair.
                float pip = fmod(value, 2) > 0.5 ? pipAt(local, float2(0.5, 0.5), radius) : 0;
                if (value >= 2) pip = max(pip, max(pipAt(local, float2(0.27, 0.27), radius), pipAt(local, float2(0.73, 0.73), radius)));
                if (value >= 4) pip = max(pip, max(pipAt(local, float2(0.73, 0.27), radius), pipAt(local, float2(0.27, 0.73), radius)));
                if (value >= 6) pip = max(pip, max(pipAt(local, float2(0.27, 0.5), radius), pipAt(local, float2(0.73, 0.5), radius)));
                float3 face = extras.y < 0.5 ? float3(0.95, 0.94, 0.90)
                    : extras.y < 1.5 ? float3(0.07, 0.07, 0.08)
                    : extras.y < 2.5 ? saturate(lerp(lum.xxx, sampled, 1.4))
                    : float3(0.75, 0.06, 0.08) * (1.1 + 0.4 * (1 - saturate(length(local - 0.5) * 2)));
                float3 pipColour = extras.y < 0.5 ? float3(0.06, 0.06, 0.07)
                    : extras.y < 2.5 && !(extras.y > 1.5 && lum > 0.5) ? float3(0.95, 0.95, 0.92)
                    : extras.y < 2.5 ? float3(0.05, 0.05, 0.05) : float3(0.97, 0.95, 0.90);
                float shade = (1.05 - 0.12 * local.y - 0.05 * local.x) * (1 - 0.25 * smoothstep(-0.06, 0, bodyDistance));
                float3 die = lerp(face * shade, pipColour, pip);
                colour = lerp(colour, saturate(lerp(float3(0.04, 0.04, 0.045), die, body)), effect.y);
            }
            if (effect.x > 37.5 && effect.x < 38.5 && effect.y > 0) // Cube mosaic
            {
                // Puzzle cubes, each face a 3x3 grid of stickers. Every sticker takes the cube
                // colour nearest the picture under it, set in black plastic.
                float3 weights = float3(0.2126, 0.7152, 0.0722);
                float rows = mosaicRows(40, 8);
                float2 cubes = float2(rows * effect.w, rows);
                float2 stickers = cubes * 3;
                float2 sticker = floor(uv * stickers);
                float2 local = frac(uv * stickers);
                float2 cubeLocal = frac(uv * cubes);
                float3 sampled = readColour((sticker + 0.5) / stickers, isYuv);
                float lum = dot(sampled, weights);
                float3 tile = extras.y < 0.5 ? nearestCube(sampled)
                    : extras.y < 1.5 ? (floor(lum * 4 + 0.5) / 4).xxx
                    : extras.y < 2.5 ? sampled : lerp(nearestCube(sampled), float3(1, 1, 1), 0.35);
                float gap = 0.05 + extras.x * 0.0012;
                float2 q = abs(local - 0.5) - (0.4 - gap);
                float shape = 1 - smoothstep(0, 0.03, length(max(q, 0)) - 0.1);
                float2 shine = local - float2(0.3, 0.28);
                float gloss = exp(-dot(shine, shine) * 18) * 0.18;
                float frame = 1 - smoothstep(0.012, 0.03, min(min(cubeLocal.x, 1 - cubeLocal.x), min(cubeLocal.y, 1 - cubeLocal.y)));
                float3 result = lerp(float3(0.03, 0.03, 0.035), tile * (0.95 + gloss) + gloss, shape * (1 - frame));
                colour = lerp(colour, saturate(result), effect.y);
            }
            if (effect.x > 38.5 && effect.x < 39.5 && effect.y > 0) // Hex tiles
            {
                // Hexagonal tiles in a honeycomb lattice. Each takes the colour under its centre,
                // with a bevelled edge lit from the top left, set in grout.
                float3 weights = float3(0.2126, 0.7152, 0.0722);
                float rows = mosaicRows(90, 14);
                float2 scale = float2(effect.w, 1) * rows;
                float2 p = (uv - 0.5) * scale;
                float2 s = float2(1, 1.7320508);
                float2 a = positiveMod(p, s) - s * 0.5;
                float2 b = positiveMod(p - s * 0.5, s) - s * 0.5;
                float2 gv = dot(a, a) < dot(b, b) ? a : b;
                float2 q = abs(gv);
                float edge = 0.5 - max(dot(q, float2(0.5, 0.8660254)), q.x);
                float3 sampled = readColour((p - gv) / scale + 0.5, isYuv);
                float lum = dot(sampled, weights);
                float groutWidth = 0.012 + extras.x * 0.0006;
                float grout = 1 - smoothstep(groutWidth, groutWidth + 0.018, edge);
                float bevel = 1 - smoothstep(0.02, 0.09, edge);
                float light = dot(normalize(gv + 0.00001), float2(-0.6, -0.8));
                float3 groutColour = float3(0.80, 0.78, 0.74);
                float3 tile = sampled;
                if (extras.y < 0.5)
                {
                    float2 shine = gv - float2(-0.14, -0.18);
                    tile = sampled * (1 + bevel * light * 0.35) + exp(-dot(shine, shine) * 70) * 0.35;
                }
                else if (extras.y < 1.5)
                {
                    tile = lerp(sampled, lum.xxx, 0.3) * (0.92 + bevel * light * 0.2) + hash12(floor(p * 23)) * 0.10 - 0.05;
                }
                else if (extras.y < 2.5)
                {
                    tile = lerp(float3(0.30, 0.13, 0.02), float3(1, 0.78, 0.25), lum) * (1 - 0.5 * edge);
                    groutColour = float3(0.98, 0.84, 0.45);
                }
                colour = lerp(colour, saturate(lerp(tile, groutColour, grout)), effect.y);
            }
            if (effect.x > 39.5 && effect.x < 40.5 && effect.y > 0) // Retro pixels
            {
                // Big pixels with ordered (Bayer) dithering into a small fixed palette, the way
                // early handheld and home-computer screens drew pictures.
                float3 weights = float3(0.2126, 0.7152, 0.0722);
                float rows = mosaicRows(220, 56);
                float2 cells = float2(rows * effect.w, rows);
                float2 cell = floor(uv * cells);
                float2 local = frac(uv * cells);
                float3 sampled = readColour((cell + 0.5) / cells, isYuv);
                float2 m = positiveMod(cell, float2(4, 4));
                float threshold = (Bayer[(int)(m.y * 4 + m.x)] + 0.5) / 16 - 0.5;
                float dither = extras.x * 0.01 / 3;
                float3 pixel;
                if (extras.y > 0.5 && extras.y < 1.5)
                {
                    pixel = floor(saturate(sampled + threshold * dither) * 3 + 0.5) / 3;
                }
                else
                {
                    int level = (int)min(floor(saturate(dot(sampled, weights) + threshold * dither) * 4), 3);
                    int bank = extras.y < 0.5 ? 0 : extras.y < 2.5 ? 4 : 8;
                    pixel = RetroPalettes[bank + level];
                }
                float grid = 1 - 0.10 * (1 - smoothstep(0, 0.08, min(min(local.x, 1 - local.x), min(local.y, 1 - local.y))));
                colour = lerp(colour, pixel * grid, effect.y);
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
