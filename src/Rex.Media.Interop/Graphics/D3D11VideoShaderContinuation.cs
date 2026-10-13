namespace Rex.Media.Interop.Graphics;

/// <summary>Second half of the effect-specific shader code, kept separate for maintainability.</summary>
internal static class D3D11VideoShaderContinuation
{
    public const string Source = """
        #if EFFECT_ONLY == 26
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
                float2 px = max(fwidth(uv), float2(0.00022, 0.00022)) * 2.4;
                float strand = smoothstep(0.02 / detail, 0.22 / detail, here.z);
                float coherent = strand;
                float3 silk = colour;
                float silkWeight = 1;
                float turning = 0;
                float2 footprint = max(fwidth(uv) * 1.8, float2(0.00018, 0.00018)) / detail;
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
                        coherent += smoothstep(0.02 / detail, 0.22 / detail, g.z) * alive * w;
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
        #endif
        #if EFFECT_ONLY == 27
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
        #endif
        #if EFFECT_ONLY == 28
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
        #endif
        #if EFFECT_ONLY == 29
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
                colour = saturate(colour + (streak * 0.45 + glow * 0.12) * effect.y * 1.4);
            }
        #endif
        #if EFFECT_ONLY == 30
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
                float lines = lerp(40, 120, extras.x * 0.01);
                float2 p = (uv - 0.5) * float2(effect.w, 1) * lines;
                float darkness = 1 - dot(colour, float3(0.2126, 0.7152, 0.0722));
                float weight = max(pointer.w, 0.25);
                float ink = 0;
                [unroll] for (int family = 0; family < 4; family++)
                {
                    float angle = family == 0 ? 0 : family == 1 ? 1.5707963 : family == 2 ? 0.78539816 : -0.78539816;
                    float cs = cos(angle), sn = sin(angle);
                    float2 d = float2(direction.x * cs - direction.y * sn, direction.x * sn + direction.y * cs);
                    float width = saturate(darkness * 3.2 - family * 0.8) * 0.13 * weight;
                    float gap = abs(frac(dot(p, float2(-d.y, d.x))) - 0.5);
                    ink = max(ink, saturate((width - gap) / 0.08));
                }
                ink = max(ink, smoothstep(0.04 / weight, 0.3 / weight, structure.z) * 0.85);
                float3 paper = extras.y < 0.5 ? float3(0.95, 0.92, 0.84)
                    : extras.y < 1.5 ? float3(0.94, 0.94, 0.90)
                    : extras.y < 2.5 ? float3(0.03, 0.03, 0.035) : float3(0.88, 0.78, 0.60);
                float3 pen = extras.y < 0.5 ? float3(0.10, 0.08, 0.07)
                    : extras.y < 1.5 ? float3(0.08, 0.16, 0.42)
                    : extras.y < 2.5 ? float3(0.93, 0.93, 0.90) : float3(0.28, 0.16, 0.08);
                colour = lerp(colour, lerp(paper, pen, ink), effect.y);
            }
        #endif
        #if EFFECT_ONLY == 31
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
        #endif
        #if EFFECT_ONLY == 32
            if (effect.x > 31.5 && effect.x < 32.5 && effect.y > 0) // Brick build
            {
                // Two-stud bricks in a running bond, seen from above. Each brick takes one
                // colour from the picture under it; each stud is lit from the top left and
                // casts a short shadow.
                float3 weights = float3(0.2126, 0.7152, 0.0722);
                float rows = mosaicRows(64, 12);
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
        #endif
        #if EFFECT_ONLY == 33
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
        #endif
        #if EFFECT_ONLY == 34
            if (effect.x > 33.5 && effect.x < 34.5 && effect.y > 0) // Cross-stitch
            {
                // Counted cross-stitch: one X of thread per cell, its colour the picture's
                // snapped to a limited set of skeins. Cells close to the fabric stay bare.
                float rows = mosaicRows(56, 16);
                float2 cells = float2(rows * effect.w, rows);
                float2 cell = floor(uv * cells);
                float2 local = frac(uv * cells);
                float3 sampled = readColour((cell + 0.5) / cells, isYuv);
                float threadGrey = dot(sampled, float3(0.2126, 0.7152, 0.0722));
                float3 thread = floor(saturate(lerp(threadGrey.xxx, sampled, 1.3)) * 6 + 0.5) / 6;
                float3 fabric = extras.y < 0.5 ? float3(0.93, 0.90, 0.82)
                    : extras.y < 1.5 ? float3(0.08, 0.08, 0.09)
                    : extras.y < 2.5 ? float3(0.82, 0.76, 0.66) : float3(0.25, 0.35, 0.52);
                float hole = 1 - smoothstep(0.04, 0.08, length(min(local, 1 - local)));
                float weave = 0.04 * sin(local.x * 25.13) * sin(local.y * 25.13);
                float3 cloth = fabric * (1 + weave - hole * 0.2);
                float thickness = 0.16 + extras.x * 0.0014;
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
        #endif
        #if EFFECT_ONLY == 35
            if (effect.x > 34.5 && effect.x < 35.5 && effect.y > 0) // LED wall
            {
                // A wall of light-emitting diodes on a black panel, each cell lit by the picture
                // under it: red, green and blue triads, round lamps, amber lamps or square pixels.
                float3 weights = float3(0.2126, 0.7152, 0.0722);
                float rows = mosaicRows(48, 14);
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
                        float diode = 1 - smoothstep(0.016, 0.026, spread);
                        float3 channel = k == 0 ? float3(1, 0.12, 0.06) : k == 1 ? float3(0.1, 1, 0.2) : float3(0.12, 0.25, 1);
                        lit += channel * sampled[k] * (diode * 2.6 + exp(-spread * 60) * glow * 1.5);
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
        #endif
        #if EFFECT_ONLY == 36
            if (effect.x > 35.5 && effect.x < 36.5 && effect.y > 0) // Digital rain
            {
                // Columns of procedural glyphs fall at their own speeds, a bright head leading
                // each stream and its trail fading behind. The picture shows through as how
                // strongly every glyph glows, so shapes stay readable inside the rain.
                float3 weights = float3(0.2126, 0.7152, 0.0722);
                float rows = mosaicRows(40, 12);
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
                float intensity = glyph * saturate(max(trail * (0.5 + 0.5 * lum), lum * 1.25));
                float3 tint = extras.y < 0.5 ? float3(0.25, 1, 0.45)
                    : extras.y < 1.5 ? float3(0.3, 0.9, 1)
                    : extras.y < 2.5 ? float3(1, 0.7, 0.2) : saturate(sampled * 1.3 + 0.05);
                float3 rain = tint * intensity + head * glyph * float3(0.8, 1, 0.85) * 0.9 + tint * 0.015;
                colour = lerp(colour, saturate(rain), effect.y);
            }
        #endif
        #if EFFECT_ONLY == 37
            if (effect.x > 36.5 && effect.x < 37.5 && effect.y > 0) // Dice mosaic
            {
                // Every cell is a die showing the face that matches the brightness under it:
                // on white dice a dark area shows six pips and a bright one shows one.
                float3 weights = float3(0.2126, 0.7152, 0.0722);
                float rows = mosaicRows(26, 8);
                float2 cells = float2(rows * effect.w, rows);
                float2 cell = floor(uv * cells);
                float2 local = frac(uv * cells);
                float3 sampled = readColour((cell + 0.5) / cells, isYuv);
                float lum = dot(sampled, weights);
                bool blackDice = extras.y > 0.5 && extras.y < 1.5;
                float depth = pow(lum, 1.6);
                float value = blackDice ? clamp(floor(depth * 6) + 1, 1, 6) : clamp(6 - floor(depth * 6), 1, 6);
                float2 q = abs(local - 0.5) - 0.32;
                float bodyDistance = length(max(q, 0)) - 0.12;
                float body = 1 - smoothstep(-0.01, 0.01, bodyDistance);
                float radius = 0.095 + extras.x * 0.0007;
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
        #endif
        #if EFFECT_ONLY == 38
            if (effect.x > 37.5 && effect.x < 38.5 && effect.y > 0) // Cube mosaic
            {
                // Puzzle cubes, each face a 3x3 grid of stickers. Every sticker takes the cube
                // colour nearest the picture under it, set in black plastic.
                float3 weights = float3(0.2126, 0.7152, 0.0722);
                float rows = mosaicRows(24, 6);
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
        #endif
        #if EFFECT_ONLY == 39
            if (effect.x > 38.5 && effect.x < 39.5 && effect.y > 0) // Hex tiles
            {
                // Hexagonal tiles in a honeycomb lattice. Each takes the colour under its centre,
                // with a bevelled edge lit from the top left, set in grout.
                float3 weights = float3(0.2126, 0.7152, 0.0722);
                float rows = mosaicRows(52, 10);
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
        #endif
        #if EFFECT_ONLY == 40
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
        #endif
        #if EFFECT_ONLY == 41
            if (effect.x > 40.5 && effect.x < 41.5 && effect.y > 0 && motion.y > 0.5) // Motion amplify
            {
                // Estimate how this part of the picture moved since the previous frame: the shift
                // of the previous frame whose small patch best matches the current one. Then draw
                // the picture further along that motion. Pixels without a clearly better match
                // than standing still do not move, so a still or paused picture is unchanged.
                float2 px = max(fwidth(uv), float2(0.00022, 0.00022));
                float reach = 1 + pointer.w * 3;
                float3 centre = readColourAt(uv, isYuv);
                float3 east = readColourAt(uv + float2(px.x * 2, 0), isYuv);
                float3 west = readColourAt(uv - float2(px.x * 2, 0), isYuv);
                float3 south = readColourAt(uv + float2(0, px.y * 2), isYuv);
                float3 north = readColourAt(uv - float2(0, px.y * 2), isYuv);
                float stillError = 0;
                float2 moved = 0;
                float bestError = 1e9;
                [loop] for (int k = 0; k < 9; k++)
                {
                    float2 shift = k == 8 ? float2(0, 0)
                        : float2(cos(k * 0.78539816), sin(k * 0.78539816)) * px * reach;
                    float2 at = uv - shift;
                    float error = length(centre - readPriorAt(at, isYuv))
                        + length(east - readPriorAt(at + float2(px.x * 2, 0), isYuv))
                        + length(west - readPriorAt(at - float2(px.x * 2, 0), isYuv))
                        + length(south - readPriorAt(at + float2(0, px.y * 2), isYuv))
                        + length(north - readPriorAt(at - float2(0, px.y * 2), isYuv));
                    if (k == 8) stillError = error;
                    else if (error < bestError) { bestError = error; moved = shift; }
                }
                float margin = 0.02 + extras.y * 0.0012;
                float confidence = smoothstep(margin, margin * 3, stillError - bestError);
                float2 pushed = uv - moved * confidence * (extras.x * 0.04);
                colour = lerp(colour, readColour(pushed, isYuv), effect.y);
            }
        #endif
        """ + "\n" + D3D11VideoShaderInteractive.Source + "\n" + """
            return colour;
        }
        // Effects work on the decoded picture, before the colour look. Off (or no strength) reads
        // the picture once and nothing more.
        float3 effected(float2 uv, bool isYuv)
        {
            float2 pixel = max(fwidth(uv), float2(0.00002, 0.00002));
            // A branch, not the ?: operator, which would evaluate both sides.
            float3 colour;
            [branch] if (effect.x < 0.5 || effect.y <= 0)
                colour = readColour(uv, isYuv);
            else
                colour = effectColour(uv, pixel, isYuv);
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
