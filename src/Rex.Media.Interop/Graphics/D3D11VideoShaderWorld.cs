namespace Rex.Media.Interop.Graphics;

/// <summary>
/// The fifth set of effects: weather and light over the picture, projections that re-map it
/// (tiny planet, Droste, reflection), page-like interactions and hand-made textures.
/// </summary>
internal static class D3D11VideoShaderWorld
{
    public const string Source = """
        #if EFFECT_ONLY == 68
            if (effect.x > 67.5 && effect.x < 68.5 && effect.y > 0) // Snow settle
            {
                // Flakes fall in three depths, and snow builds up on surfaces that face up: strong
                // horizontal outlines, and the band just above them. The drift deepens with time.
                float t = effect.z;
                float flakes = 0;
                [loop] for (int layer = 0; layer < 3; layer++)
                {
                    float scale = (20 + layer * 25) * (0.6 + pointer.w * 0.4);
                    float2 cells = float2(scale * effect.w, scale);
                    float2 p = uv * cells + float2(sin(t * 0.3 + layer) * 2, -t * (1.5 + layer * 0.6) * (0.5 + extras.x * 0.01));
                    float2 cell = floor(p);
                    float2 offset = (float2(hash(cell + 1.7), hash(cell + 2.9)) - 0.5) * 0.6;
                    float size = 0.06 + 0.03 * (2 - layer);
                    flakes += step(0.55, hash(cell + layer * 13.1)) * (1 - smoothstep(size * 0.5, size, length(frac(p) - 0.5 - offset))) * (1 - layer * 0.25);
                }
                float depth = 0.25 + 0.75 * saturate(frac(t * 0.02 * (0.5 + extras.y * 0.01)) * 1.2);
                float3 here = contourGradient(uv, isYuv, 3.0);
                float3 below = contourGradient(uv + float2(0, 0.004 + 0.012 * depth), isYuv, 3.0);
                float flat = smoothstep(0.55, 0.9, abs(below.y) / (abs(below.x) + abs(below.y) + 0.0001));
                float flatHere = smoothstep(0.55, 0.9, abs(here.y) / (abs(here.x) + abs(here.y) + 0.0001));
                float ledge = max(smoothstep(0.1, 0.3, abs(here.y)) * flatHere, smoothstep(0.1, 0.3, abs(below.y)) * flat);
                float settle = saturate(ledge * depth * (0.7 + 0.3 * hash(floor(uv * float2(300 * effect.w, 300)))) * 1.5);
                float3 cold = colour * float3(0.85, 0.9, 1.0) * 0.9;
                colour = lerp(colour, saturate(lerp(cold, float3(0.96, 0.97, 1), settle) + flakes * 0.9), effect.y);
            }
        #endif
        #if EFFECT_ONLY == 69
            if (effect.x > 68.5 && effect.x < 69.5 && effect.y > 0) // Thunderstorm
            {
                // The scene darkens under driving rain; every few seconds lightning may strike,
                // flashing the whole picture and drawing a jagged bolt down from the sky.
                float t = effect.z;
                float2 rp = float2(uv.x * effect.w + uv.y * 0.25, uv.y) * float2(120 * pointer.w, 6);
                rp.y -= t * (6 + extras.x * 0.12);
                float2 rc = floor(rp);
                float streak = step(0.85, hash(rc)) * (1 - smoothstep(0, 0.5, abs(frac(rp.x) - 0.5))) * frac(rp.y + hash(rc + 3.1));
                float epoch = floor(t * 0.4);
                float since = frac(t * 0.4) * 2.5 - hash(float2(epoch, 5.5)) * 1.5;
                float flash = (since > 0 ? exp(-since * 6) : 0) * step(0.65 - extras.y * 0.006, hash(float2(epoch, 9.1)));
                flash *= 0.7 + 0.3 * step(0.5, frac(since * 20));
                float jag = (hash(float2(floor(uv.y * 20), epoch)) - 0.5) * 0.04 + (hash(float2(floor(uv.y * 60), epoch + 1)) - 0.5) * 0.012;
                float boltX = hash(float2(epoch, 2.2)) * 0.8 + 0.1 + jag + uv.y * (hash(float2(epoch, 4.4)) - 0.5) * 0.2;
                float bolt = (1 - smoothstep(0.001, 0.004, abs(uv.x - boltX))) * step(uv.y, 0.4 + hash(float2(epoch, 7.7)) * 0.4) * flash * 2;
                float3 storm = colour * float3(0.55, 0.6, 0.7) * 0.6 + flash * (colour * 1.2 + 0.08);
                storm += streak * float3(0.6, 0.65, 0.75) * 0.35 + bolt * float3(0.85, 0.9, 1);
                colour = lerp(colour, saturate(storm), effect.y);
            }
        #endif
        #if EFFECT_ONLY == 70
            if (effect.x > 69.5 && effect.x < 70.5 && effect.y > 0) // Stage lights
            {
                // Three coloured spotlights hang above the frame and sweep across it. Their beams
                // show in the haze and their pools light the picture; the first follows the pointer.
                float3 lit = colour * (0.12 + extras.y * 0.002);
                float3 haze = 0;
                [loop] for (int i = 0; i < 3; i++)
                {
                    float2 source = float2(0.2 + 0.3 * i, -0.1);
                    float sway = sin(effect.z * (0.5 + 0.17 * i) * (0.5 + extras.x * 0.01) + i * 2.1) * 0.35;
                    float2 target = pointer.z > 0.5 && i == 0 ? pointer.xy : float2(0.2 + 0.3 * i + sway, 0.85);
                    float2 axis = normalize((target - source) * aspect());
                    float2 rel = (uv - source) * aspect();
                    float along = dot(rel, axis);
                    float across = abs(dot(rel, float2(-axis.y, axis.x)));
                    float width = (0.04 + 0.06 * pointer.w) * along + 0.01;
                    float beam = (1 - smoothstep(width * 0.7, width, across)) * step(0, along);
                    float pool = 1 - smoothstep(0.08, 0.16 + 0.06 * pointer.w, length((uv - target) * aspect()));
                    float3 tint = i == 0 ? float3(1, 0.35, 0.5) : i == 1 ? float3(0.35, 0.6, 1) : float3(1, 0.85, 0.4);
                    lit += colour * tint * (beam * 0.8 + pool * 1.2);
                    haze += tint * beam * 0.12;
                }
                colour = lerp(colour, saturate(lit + haze), effect.y);
            }
        #endif
        #if EFFECT_ONLY == 71
            if (effect.x > 70.5 && effect.x < 71.5 && effect.y > 0) // Hologram
            {
                // The picture as a projected hologram: one colour of light, outlines brighter than
                // flat areas, scanlines, a slow rolling band, flicker, and the odd horizontal tear.
                float t = effect.z;
                float tear = step(0.97, hash(float2(floor(uv.y * 40), floor(t * 8)))) * (hash(float2(floor(t * 8), 3.3)) - 0.5) * 0.05 * (extras.y * 0.02);
                float2 at = float2(uv.x + tear, uv.y);
                float l = lumaOf(readColour(mirrored(at), isYuv));
                float edge = smoothstep(0.03, 0.2, contourDetail(at, isYuv).z);
                float scan = 0.75 + 0.25 * sin(uv.y * 400 * pointer.w * 3.14159);
                float band = smoothstep(0.0, 0.05, abs(frac(uv.y * 0.8 - t * 0.15) - 0.5));
                float flicker = 0.9 + 0.1 * sin(t * 40) * hash(float2(floor(t * 10), 1));
                float3 tint = extras.x < 0.5 ? float3(0.3, 0.9, 1) : extras.x < 1.5 ? float3(0.4, 1, 0.5) : float3(1, 0.4, 0.8);
                float3 holo = tint * (l * 0.8 + edge * 0.9) * scan * flicker * (0.85 + 0.15 * band) + tint * (0.03 + (1 - band) * 0.15);
                colour = lerp(colour, saturate(holo), effect.y);
            }
        #endif
        #if EFFECT_ONLY == 72
            if (effect.x > 71.5 && effect.x < 72.5 && effect.y > 0) // Paper layers
            {
                // The picture cut from a few sheets of coloured paper stacked by brightness. Each
                // sheet casts a soft shadow onto the one below, and the paper has a faint fibre.
                float levels = 3 + floor(extras.x * 0.05);
                float l = lumaOf(colour);
                float layer = floor(min(l, 0.999) * levels);
                float2 off = float2(0.004, 0.006) * (0.5 + pointer.w);
                float upper = floor(min(lumaOf(readColourAt(uv - off, isYuv)), 0.999) * levels);
                float shadow = saturate(upper - layer) * 0.35;
                float3 sheet = saturate(colour / max(l, 0.02) * ((layer + 0.5) / levels));
                sheet = lerp(sheet, ((layer + 0.5) / levels).xxx, extras.y * 0.01);
                float fibre = (hash(floor(uv * float2(500 * effect.w, 500))) - 0.5) * 0.04;
                colour = lerp(colour, saturate(sheet * (1 - shadow) + fibre), effect.y);
            }
        #endif
        #if EFFECT_ONLY == 73
            if (effect.x > 72.5 && effect.x < 73.5 && effect.y > 0) // Droste
            {
                // Inside a circle round the pointer (or the centre), the ring of picture there repeats
                // inward forever, each copy smaller, twisting into a spiral and zooming slowly.
                float2 centre = pointer.z > 0.5 ? pointer.xy : float2(0.5, 0.5);
                float2 d = (uv - centre) * aspect();
                float r = max(length(d), 0.00001);
                float outer = 0.15 + 0.25 * pointer.w;
                float ratio = log(1 / (0.3 + extras.x * 0.004));
                float x = -log(r / outer) + effect.z * 0.1 + (extras.y * 0.01) * atan2(d.y, d.x) / 6.28318 * ratio;
                float wrapped = x - ratio * floor(x / ratio);
                float2 source = centre + d / r * outer * exp(-wrapped) / aspect();
                float frame = 1 - smoothstep(0, 0.04, min(wrapped, ratio - wrapped));
                float3 inner = readColour(saturate(source), isYuv) * (1 - frame * 0.6);
                colour = lerp(colour, r < outer ? inner : colour, effect.y);
            }
        #endif
        #if EFFECT_ONLY == 74
            if (effect.x > 73.5 && effect.x < 74.5 && effect.y > 0) // Reflection pool
            {
                // Below the waterline the picture becomes still water reflecting what is above it,
                // darker and bluer with depth, with ripples that travel across the surface.
                float waterline = 0.45 + extras.x * 0.004;
                [branch] if (uv.y > waterline)
                {
                    float depth = uv.y - waterline;
                    float t = effect.z;
                    float wave = (sin(uv.y * 90 * pointer.w / (depth * 4 + 0.2) - t * 3) * 0.003 + sin(uv.x * 60 + t * 2) * 0.0015) * (0.5 + extras.y * 0.01);
                    float3 mirror = readColourAt(float2(saturate(uv.x + wave), saturate(waterline - depth + wave * 0.5)), isYuv);
                    float3 water = mirror * float3(0.65, 0.75, 0.85) * (0.95 - depth * 0.6) + (1 - smoothstep(0, 0.004, depth)) * 0.2;
                    colour = lerp(colour, saturate(water), effect.y);
                }
            }
        #endif
        #if EFFECT_ONLY == 75
            if (effect.x > 74.5 && effect.x < 75.5 && effect.y > 0) // Disintegrate
            {
                // The picture breaks into grains that blow away, starting at the pointer (or the
                // left edge) and sweeping outward over time. Each pixel shows its own grain if it
                // has not left yet, or a grain that has drifted into it from upwind.
                float grains = 40 + 120 * pointer.w;
                float2 cells = float2(grains * effect.w, grains);
                float sweep = frac(effect.z * (0.03 + extras.x * 0.0008)) * 1.8;
                float2 origin = pointer.z > 0.5 ? pointer.xy : float2(-0.1, 0.5);
                float2 wind = normalize(float2(1, -0.35 - extras.y * 0.006));
                float2 cell = floor(uv * cells);
                float progress = saturate((sweep - length(((cell + 0.5) / cells - origin) * aspect())) * 2 + (hash(cell) - 0.5) * 0.3);
                float3 result = progress > 0 ? colour * 0.06 : colour;
                float stepLen = 1.5 / cells.y;
                [loop] for (int k = 1; k <= 8; k++)
                {
                    float2 q = uv - wind * k * stepLen / aspect();
                    float2 c = floor(q * cells);
                    float p = saturate((sweep - length(((c + 0.5) / cells - origin) * aspect())) * 2 + (hash(c) - 0.5) * 0.3);
                    if (p > 0 && abs(p * 0.25 - k * stepLen) < stepLen * 0.6)
                    {
                        result = readColourAt((c + 0.5) / cells, isYuv) * (1 - p * 0.8);
                        break;
                    }
                }
                colour = lerp(colour, result, effect.y);
            }
        #endif
        #if EFFECT_ONLY == 76
            if (effect.x > 75.5 && effect.x < 76.5 && effect.y > 0) // Aurora
            {
                // The scene turns to night and aurora curtains ripple across the upper sky: green at
                // their lower edge, violet above. They show where the picture is smooth, not on detail.
                float t = effect.z * (0.1 + extras.x * 0.003);
                float3 night = colour * float3(0.3, 0.38, 0.55);
                float smooth = 1 - smoothstep(0.03, 0.12, contourDetail(uv, isYuv).z);
                float sky = (0.35 + 0.65 * smooth) * (1 - smoothstep(0.35, 0.8, uv.y));
                float x = uv.x * effect.w;
                float bandY = 0.25 + sin(x * 6 + t * 2 + sin(x * 3 - t) * 1.5) * 0.08;
                float curtain = uv.y < bandY ? exp(-(bandY - uv.y) * 6) : exp(-(uv.y - bandY) * 60);
                float rays = 0.7 + 0.18 * sin(x * 37 * pointer.w + sin(x * 5 + t * 2) * 3) + 0.12 * sin(x * 83 * pointer.w - t * 1.3);
                float3 glow = lerp(float3(0.2, 1, 0.5), float3(0.6, 0.3, 1), saturate((bandY - uv.y) * 4));
                float intensity = curtain * rays * (0.6 + extras.y * 0.008);
                colour = lerp(colour, saturate(night + glow * intensity * sky + glow * intensity * 0.05), effect.y);
            }
        #endif
        #if EFFECT_ONLY == 77
            if (effect.x > 76.5 && effect.x < 77.5 && effect.y > 0) // Page curl
            {
                // The pointer peels the picture back from the bottom-right corner like a page. The
                // folded flap shows the page's shaded back, and the uncovered area the sheet beneath.
                float2 corner = float2(1, 1);
                float2 grab = pointer.z > 0.5 ? pointer.xy : float2(0.75 + 0.1 * sin(effect.z * 0.6), 0.7 + 0.1 * cos(effect.z * 0.5));
                float2 dir = normalize((corner - grab) * aspect() + 0.00001);
                float s = dot((uv - (corner + grab) * 0.5) * aspect(), dir);
                float soft = 0.02 + 0.04 * pointer.w;
                float3 beneath = lerp(float3(0.93, 0.91, 0.86), colour * 0.4, extras.y * 0.01);
                float3 result;
                if (s >= 0)
                    result = beneath * (1 - 0.3 * exp(-s / soft));
                else
                {
                    float2 flap = uv + 2 * (-s) * dir / aspect();
                    bool onFlap = all(flap >= 0) && all(flap <= 1);
                    float3 back = readColourAt(saturate(flap), isYuv) * 0.45 + float3(0.4, 0.39, 0.37);
                    back *= 0.75 + 0.35 * saturate(-s / (soft * 4));
                    float3 front = colour * (1 - 0.35 * exp(s / soft) * (extras.x * 0.01));
                    result = onFlap ? back : front;
                }
                colour = lerp(colour, saturate(result), effect.y);
            }
        #endif
        #if EFFECT_ONLY == 78
            if (effect.x > 77.5 && effect.x < 78.5 && effect.y > 0) // Bubbles
            {
                // Soap bubbles float up across the picture, each a thin iridescent film that magnifies
                // what is behind it. The pointer pops the bubbles it touches.
                float rows = 4 + 6 * pointer.w;
                float2 cells = float2(rows * effect.w, rows);
                float t = effect.z * (0.1 + extras.x * 0.003);
                float column = floor(uv.x * cells.x);
                float bestD = 2;
                float2 bestPos = 0;
                float bestHue = 0;
                [loop] for (int dx = -1; dx <= 1; dx++)
                    [loop] for (int k = 0; k < 3; k++)
                    {
                        float2 id = float2(column + dx, k);
                        float h1 = hash(id), h2 = hash(id + 3.7), h3 = hash(id + 8.1);
                        float radius = (0.25 + 0.35 * h3) / rows * (0.5 + extras.y * 0.01);
                        float2 pos = float2((id.x + 0.2 + 0.6 * h1) / cells.x + 0.02 * sin(t * 6 + h3 * 6.28), frac(h2 - t * (0.3 + 0.5 * h1)) * 1.3 - 0.15);
                        bool popped = pointer.z > 0.5 && length((pos - pointer.xy) * aspect()) < radius * 1.2;
                        float d = length((uv - pos) * aspect()) / radius;
                        if (!popped && d < bestD) { bestD = d; bestPos = pos; bestHue = h3; }
                    }
                [branch] if (bestD < 1)
                {
                    float3 film = 0.5 + 0.5 * cos(6.28 * (bestD * bestD * 1.5 + t * 0.2 + bestHue) + float3(0, 2.1, 4.2));
                    float rim = smoothstep(0.7, 1, bestD);
                    float3 behind = readColourAt(saturate(bestPos + (uv - bestPos) * (0.75 + 0.25 * bestD)), isYuv);
                    float2 glint = (uv - bestPos) * aspect() / max(0.0001, length((uv - bestPos) * aspect()) / bestD);
                    float spark = exp(-dot(glint - float2(-0.4, -0.4), glint - float2(-0.4, -0.4)) * 30) * 0.6;
                    colour = lerp(colour, saturate(behind * 0.92 + film * (0.12 + rim * 0.45) + spark), effect.y);
                }
            }
        #endif
        #if EFFECT_ONLY == 79
            if (effect.x > 78.5 && effect.x < 79.5 && effect.y > 0) // Tiny planet
            {
                // The picture wrapped into a little planet: its bottom edge becomes the ground at the
                // centre and its top the sky around it. The pointer, or time, spins the planet.
                float2 p = (uv - 0.5) * aspect();
                float r = length(p);
                float spin = pointer.z > 0.5 ? (pointer.x - 0.5) * 6.28318 : effect.z * 0.1 * (extras.x * 0.02);
                float u = frac(atan2(p.x, p.y) / 6.28318 + 0.5 + spin / 6.28318);
                u = 1 - abs(2 * u - 1);
                float size = 0.12 + 0.3 * pointer.w;
                float v = saturate(2 * atan(r / size) / 3.14159);
                if (extras.y > 0.5) v = 1 - v;
                colour = lerp(colour, readColour(float2(u, 1 - v), isYuv), effect.y);
            }
        #endif
        #if EFFECT_ONLY == 80
            if (effect.x > 79.5 && effect.x < 80.5 && effect.y > 0) // Stencil graffiti
            {
                // The picture sprayed through a stencil onto a brick wall: flat paint tones with fuzzy
                // spray edges, and the wall showing through where there is no paint.
                float l = smoothstep(0.08, 0.75, lumaOf(colour)) + (hash(floor(uv * float2(700 * effect.w, 700))) - 0.5) * 0.12 * pointer.w;
                float2 bricks = float2(9 * effect.w, 18);
                float2 b = uv * bricks;
                b.x += step(1, fmod(floor(b.y), 2)) * 0.5;
                float2 local = frac(b);
                float mortar = 1 - smoothstep(0.03, 0.07, min(min(local.x, 1 - local.x) * 0.5, min(local.y, 1 - local.y)));
                float3 brick = float3(0.55, 0.3, 0.22) * (0.8 + 0.4 * hash(floor(b)));
                float3 wall = lerp(brick, float3(0.7, 0.68, 0.64), mortar) * (0.7 + extras.y * 0.003);
                float3 dark = extras.x < 0.5 ? float3(0.05, 0.05, 0.06) : extras.x < 1.5 ? float3(0.05, 0.05, 0.06) : extras.x < 2.5 ? float3(0.08, 0.1, 0.3) : float3(0.1, 0.0, 0.2);
                float3 mid = extras.x < 0.5 ? wall : extras.x < 1.5 ? float3(0.8, 0.1, 0.12) : extras.x < 2.5 ? float3(0.95, 0.85, 0.2) : float3(1.0, 0.1, 0.7);
                float3 light = extras.x < 2.5 ? wall : float3(0.2, 1.0, 0.9);
                float3 painted = l < 0.3 ? dark : l < 0.6 ? mid : light;
                colour = lerp(colour, painted, effect.y);
            }
        #endif
        """;
}
