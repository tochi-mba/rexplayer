namespace Rex.Media.Interop.Graphics;

/// <summary>
/// The third set of effects: ones that follow the pointer, read the picture's own motion or
/// structure, or rebuild it as a mesh. Each is one EFFECT_ONLY variant inside effectColour.
/// </summary>
internal static class D3D11VideoShaderInteractive
{
    public const string Source = """
        #if EFFECT_ONLY == 42
            if (effect.x > 41.5 && effect.x < 42.5 && effect.y > 0) // Flow field
            {
                // Where the picture moved since the previous frame (the best-matching shift of a
                // small patch), drawn as streaks along the motion over a dimmed picture. Streak hue
                // follows direction, length follows how confident the match is. Still areas stay dark,
                // and a picture with no previous one (the first after a seek) shows just the base.
                float2 px = max(fwidth(uv), float2(0.00022, 0.00022));
                float reach = 1 + pointer.w * 3;
                float3 centre = readColourAt(uv, isYuv);
                float3 east = readColourAt(uv + float2(px.x * 2, 0), isYuv);
                float3 south = readColourAt(uv + float2(0, px.y * 2), isYuv);
                float stillError = 0, bestError = 0;
                float2 moved = float2(1, 0);
                [branch] if (motion.y > 0.5)
                {
                    bestError = 1e9;
                    [loop] for (int k = 0; k < 9; k++)
                    {
                        float2 shift = k == 8 ? float2(0, 0) : float2(cos(k * 0.78539816), sin(k * 0.78539816)) * px * reach;
                        float2 at = uv - shift;
                        float error = length(centre - readPriorAt(at, isYuv))
                            + length(east - readPriorAt(at + float2(px.x * 2, 0), isYuv))
                            + length(south - readPriorAt(at + float2(0, px.y * 2), isYuv));
                        if (k == 8) stillError = error; else if (error < bestError) { bestError = error; moved = shift; }
                    }
                }
                float confidence = smoothstep(0.03, 0.12, stillError - bestError);
                float2 dir = moved / max(length(moved), 0.000001);
                // Streak: a hashed line pattern aligned with the motion, scrolling with time.
                float2 cells = float2(60 * effect.w, 60) * (0.5 + extras.x * 0.01);
                float along = dot(uv * cells, dir) - effect.z * 6;
                float across = dot(uv * cells, float2(-dir.y, dir.x));
                float streak = smoothstep(0.35, 0.0, abs(frac(across) - 0.5)) * step(0.3, frac(along * 0.5 + hash(floor(across))));
                float angle = atan2(dir.y, dir.x);
                float3 hue = 0.5 + 0.5 * cos(angle + float3(0, 2.094, 4.189));
                float3 base = extras.y < 0.5 ? colour * 0.25 : extras.y < 1.5 ? colour : float3(0.02, 0.02, 0.03);
                colour = lerp(colour, saturate(base + hue * streak * confidence * 1.4 + hue * confidence * 0.15), effect.y);
            }
        #endif
        #if EFFECT_ONLY == 43
            if (effect.x > 42.5 && effect.x < 43.5 && effect.y > 0) // Ripple touch
            {
                // Rings spread from the pointer (the centre when it is away) and bend the picture
                // like water. Ring spacing is the detail control; speed the third.
                float2 origin = pointer.z > 0.5 ? pointer.xy : float2(0.5, 0.5);
                float2 delta = (uv - origin) * aspect();
                float r = length(delta);
                float wave = sin(r * (60 * pointer.w) - effect.z * (2 + extras.x * 0.08)) * exp(-r * (1.5 + extras.y * 0.04));
                float2 bent = uv + delta / max(r, 0.0001) * wave * 0.02 * effect.y / aspect();
                float3 water = readColour(mirrored(bent), isYuv);
                float crest = saturate(wave) * exp(-r * 2.5);
                colour = lerp(colour, saturate(water + crest * float3(0.25, 0.3, 0.35)), effect.y);
            }
        #endif
        #if EFFECT_ONLY == 44
            if (effect.x > 43.5 && effect.x < 44.5 && effect.y > 0) // Torchlight
            {
                // The room goes dark; a torch beam follows the pointer. Inside the beam the
                // picture is lit normally and its outlines catch a little extra light.
                float2 origin = pointer.z > 0.5 ? pointer.xy : float2(0.5, 0.5);
                float r = length((uv - origin) * aspect());
                float radius = 0.12 + 0.18 * pointer.w;
                float beam = 1 - smoothstep(radius * 0.6, radius, r);
                float ambient = 0.03 + extras.y * 0.002;
                float3 rim = contourDetail(uv, isYuv);
                float edge = smoothstep(0.02, 0.2, rim.z) * beam;
                float3 tint = extras.x < 34 ? float3(1, 0.93, 0.8) : extras.x < 67 ? float3(0.85, 0.95, 1) : float3(1, 1, 1);
                float3 lit = colour * (ambient + beam * 1.1) * tint + edge * 0.18;
                colour = lerp(colour, saturate(lit), effect.y);
            }
        #endif
        #if EFFECT_ONLY == 45
            if (effect.x > 44.5 && effect.x < 45.5 && effect.y > 0) // Motion isolate
            {
                // Only what moves keeps its colour; the still scene fades to a dim monochrome, so
                // the moving subjects stand out. A small shift search ignores slight camera shake.
                float2 px = max(fwidth(uv), float2(0.00022, 0.00022)) * (1 + pointer.w);
                float mismatch = 0;
                [branch] if (motion.y > 0.5)
                {
                    mismatch = length(colour - readPriorAt(uv, isYuv));
                    mismatch = min(mismatch, length(colour - readPriorAt(uv + float2(px.x, 0), isYuv)));
                    mismatch = min(mismatch, length(colour - readPriorAt(uv - float2(px.x, 0), isYuv)));
                    mismatch = min(mismatch, length(colour - readPriorAt(uv + float2(0, px.y), isYuv)));
                    mismatch = min(mismatch, length(colour - readPriorAt(uv - float2(0, px.y), isYuv)));
                }
                float moving = smoothstep(0.02, 0.06 + extras.x * 0.002, mismatch);
                float l = lumaOf(colour);
                float3 still = l.xxx * (0.25 + extras.y * 0.005);
                if (extras.y > 100.5) still = colour * 0.3;
                float3 live = saturate(colour * 1.1 + moving * float3(0.08, 0.04, 0));
                colour = lerp(colour, lerp(still, live, moving), effect.y);
            }
        #endif
        #if EFFECT_ONLY == 46
            if (effect.x > 45.5 && effect.x < 46.5 && effect.y > 0) // Cel shade
            {
                // A cartoon cel: shading flattened to a few bands with the hue kept, and a dark
                // ink line on every strong outline.
                float bands = 3 + floor(extras.x * 0.05);
                float l = max(lumaOf(colour), 0.001);
                float banded = (floor(pow(l, 0.9) * bands) + 0.5) / bands;
                float3 flat = saturate(colour * (banded / l));
                flat = saturate(lerp(l.xxx, flat, 1.15));
                float3 rim = contourDetail(uv, isYuv);
                float ink = smoothstep(0.05 / pointer.w, 0.22 / pointer.w, rim.z);
                float3 inkColour = extras.y < 0.5 ? float3(0.05, 0.04, 0.06) : extras.y < 1.5 ? float3(0.12, 0.05, 0.02) : extras.y < 2.5 ? float3(0.02, 0.06, 0.16) : flat * 0.35;
                colour = lerp(colour, lerp(flat, inkColour, ink * 0.9), effect.y);
            }
        #endif
        #if EFFECT_ONLY == 47
            if (effect.x > 46.5 && effect.x < 47.5 && effect.y > 0) // Terrain mesh
            {
                // The picture as a wireframe landscape seen from a low angle: brightness lifts the
                // mesh, so bright things rise as hills, and the lines take the picture's colour.
                float tilt = 0.35 + extras.x * 0.004;
                float2 ground = float2(uv.x, lerp(uv.y, 1 - (1 - uv.y) * (1 - uv.y), tilt));
                float3 sampled = readColour(ground, isYuv);
                float2 blur = float2(0.012, 0.012);
                float smoothLuma = (lumaOf(sampled) * 2 + lumaOf(readColourAt(ground + blur, isYuv)) + lumaOf(readColourAt(ground - blur, isYuv))
                    + lumaOf(readColourAt(ground + float2(blur.x, -blur.y), isYuv)) + lumaOf(readColourAt(ground - float2(blur.x, -blur.y), isYuv))) / 6;
                float height = smoothLuma * (0.05 + extras.y * 0.0015);
                float2 lifted = float2(ground.x, ground.y + height);
                float rows = 24 * pointer.w;
                float2 cells = float2(rows * effect.w, rows * (1 + tilt));
                float2 g = lifted * cells;
                float2 d = abs(frac(g) - 0.5);
                float wire = smoothstep(0.40, 0.48, max(d.x, d.y));
                float3 lineColour = saturate(sampled * 1.3 + 0.15);
                float3 fill = sampled * 0.12;
                float fog = smoothstep(0.0, 0.6, 1 - uv.y) * 0.4;
                colour = lerp(colour, saturate(lerp(fill, lineColour, wire) * (1 - fog) + fog * float3(0.02, 0.03, 0.06)), effect.y);
            }
        #endif
        #if EFFECT_ONLY == 48
            if (effect.x > 47.5 && effect.x < 48.5 && effect.y > 0) // Gravity well
            {
                // The picture is pulled toward the pointer and twisted around it, strongest close
                // in and fading out with distance; the pull breathes slowly.
                float2 origin = pointer.z > 0.5 ? pointer.xy : float2(0.5, 0.5);
                float2 delta = (uv - origin) * aspect();
                float r = max(length(delta), 0.0001);
                float radius = 0.15 + 0.25 * pointer.w;
                float pull = exp(-r / radius) * (0.8 + 0.2 * sin(effect.z * 1.5));
                float twist = pull * (extras.y * 0.03) * sign(extras.x - 50.5);
                float cs = cos(twist), sn = sin(twist);
                float2 spun = float2(cs * delta.x - sn * delta.y, sn * delta.x + cs * delta.y);
                float2 drawn = spun * (1 - pull * (0.2 + abs(extras.x - 50) * 0.008) * effect.y);
                colour = lerp(colour, readColour(mirrored(origin + drawn / aspect()), isYuv) * (1 - pull * 0.25), effect.y);
            }
        #endif
        #if EFFECT_ONLY == 49
            if (effect.x > 48.5 && effect.x < 49.5 && effect.y > 0) // Scan sweep
            {
                // A scanner line sweeps across; behind it the picture shows as a chosen analysis
                // (heat, edges, negative or x-ray style) and ahead of it as normal. With the pointer
                // over the picture the line follows it instead.
                float scan = pointer.z > 0.5 ? pointer.x : frac(effect.z * (0.05 + extras.x * 0.004));
                float behind = extras.y > 1.5 && extras.y < 2.5 ? step(scan, uv.x) : step(uv.x, scan);
                float glow = exp(-abs(uv.x - scan) * 90) * (0.6 + 0.4 * sin(uv.y * 80 + effect.z * 12));
                float l = saturate((lumaOf(colour) - 0.5) * pointer.w + 0.5);
                float3 rim = contourDetail(uv, isYuv);
                float edge = smoothstep(0.03 / pointer.w, 0.2 / pointer.w, rim.z);
                float3 analysed = extras.y < 0.5
                    ? lerp(float3(0.0, 0.0, 0.2), lerp(float3(0.9, 0.1, 0.0), float3(1, 1, 0.6), l), l)
                    : extras.y < 1.5 ? float3(0.02, 0.03, 0.04) + edge * float3(0.2, 1, 0.6)
                    : extras.y < 2.5 ? saturate((0.5 - colour) * pointer.w + 0.5)
                    : saturate((1 - l).xxx * float3(0.6, 0.8, 1) + edge * 0.3);
                colour = lerp(colour, saturate(lerp(colour, analysed, behind) + glow * float3(0.3, 1, 0.5)), effect.y);
            }
        #endif
        #if EFFECT_ONLY == 50
            if (effect.x > 49.5 && effect.x < 50.5 && effect.y > 0) // Shatter
            {
                // The picture breaks into glass shards. Near the pointer (or with time, when it is
                // away) the shards slide apart and tilt, showing dark gaps; far away they sit flush.
                float2 origin = pointer.z > 0.5 ? pointer.xy : float2(0.5, 0.5);
                float rows = 8 + 20 * pointer.w;
                float2 grid = float2(rows * effect.w, rows);
                float2 p = uv * grid;
                float2 base = floor(p);
                float nearest = 8, second = 8;
                float2 site = p;
                [loop] for (int y = -1; y <= 1; y++)
                    [loop] for (int x = -1; x <= 1; x++)
                    {
                        float2 c = base + float2(x, y);
                        float2 s = c + 0.1 + 0.8 * float2(hash(c), hash(c + 17.31));
                        float d = length(p - s);
                        if (d < nearest) { second = nearest; nearest = d; site = s; }
                        else if (d < second) second = d;
                    }
                float r = length((site / grid - origin) * aspect());
                float burst = pointer.z > 0.5 ? exp(-r * (3 + extras.y * 0.05)) : 0.5 + 0.5 * sin(effect.z * 0.8 + hash(site) * 6.28);
                float2 away = (site / grid - origin) * aspect();
                float2 slide = away / max(length(away), 0.0001) * burst * (0.01 + extras.x * 0.0008) * effect.y;
                float2 shard = uv - slide / aspect() + (hash(site + 3.7) - 0.5) * burst * 0.01;
                float gap = 1 - smoothstep(0.02, 0.06 + burst * 0.1, (second - nearest) * 0.5);
                float3 glass = readColour(mirrored(shard), isYuv) * (1 + (hash(site + 9.1) - 0.5) * 0.25 * burst);
                colour = lerp(colour, saturate(lerp(glass, float3(0.03, 0.03, 0.04), gap * (0.5 + burst * 0.5))), effect.y);
            }
        #endif
        #if EFFECT_ONLY == 51
            if (effect.x > 50.5 && effect.x < 51.5 && effect.y > 0) // Parallax
            {
                // Three depth layers guessed from sharpness: crisp detail is near, soft is far. Each
                // layer slides a different amount with the pointer, so the picture seems to turn.
                float2 gaze = pointer.z > 0.5 ? (pointer.xy - 0.5) : float2(sin(effect.z * 0.7), cos(effect.z * 0.5)) * 0.3;
                float3 fine = contourGradient(uv, isYuv, 0.75);
                float3 broad = contourGradient(uv, isYuv, 3.0);
                float near = smoothstep(0.1, 0.6, saturate(fine.z / max(broad.z, 0.004)) * saturate(fine.z * 8 * pointer.w));
                float shift = (0.01 + extras.x * 0.0004) * effect.y;
                float3 far = readColour(mirrored(uv + gaze * shift * 0.3), isYuv);
                float3 close = readColour(mirrored(uv - gaze * shift), isYuv);
                float3 layered = lerp(far * (1 - extras.y * 0.002), close, near);
                colour = lerp(colour, saturate(layered), effect.y);
            }
        #endif
        #if EFFECT_ONLY == 52
            if (effect.x > 51.5 && effect.x < 52.5 && effect.y > 0) // Rain on glass
            {
                // Drops run down a fogged pane in front of the picture, each refracting what is
                // behind it; the pointer wipes the fog clear where it passes.
                float2 cells = float2(14 * effect.w, 10) * (0.6 + pointer.w * 0.4);
                float2 p = uv * cells;
                float2 cell = floor(p);
                float t = effect.z * (0.3 + extras.x * 0.01);
                float2 local = frac(p) - 0.5;
                float drop = 0, rim = 0;
                float2 refractDir = 0;
                [loop] for (int i = -1; i <= 1; i++)
                {
                    float2 c = cell + float2(0, i);
                    float lane = hash(c * 1.7) - 0.5;
                    float fall = frac(t * (0.5 + hash(c)) + hash(c + 5.1));
                    float2 centre = float2(lane * 0.7, 0.5 - fall - i);
                    float2 d = (local - centre) * float2(1, 0.7);
                    float radius = 0.08 + 0.08 * hash(c + 2.3);
                    float inside = 1 - smoothstep(radius * 0.8, radius, length(d));
                    rim += inside * smoothstep(radius * 0.5, radius * 0.95, length(d)) * (d.y < 0 ? 0.35 : -0.25);
                    drop = max(drop, inside);
                    refractDir += d * inside;
                    float trail = (1 - smoothstep(0.02, 0.05, abs(d.x))) * step(0, d.y) * (1 - smoothstep(0, 0.6, d.y)) * 0.5;
                    drop = max(drop, trail * step(0.3, hash(c + 7.7)));
                }
                float2 origin = pointer.z > 0.5 ? pointer.xy : float2(-1, -1);
                float wiped = 1 - smoothstep(0.1, 0.25, length((uv - origin) * aspect()));
                float fog = saturate(extras.y * 0.01) * (1 - drop) * (1 - wiped);
                float3 soft = (readColourAt(uv + float2(0.006, 0), isYuv) + readColourAt(uv - float2(0.006, 0), isYuv)
                    + readColourAt(uv + float2(0, 0.008), isYuv) + readColourAt(uv - float2(0, 0.008), isYuv)) * 0.25;
                float3 fogged = lerp(colour, soft * 0.9 + 0.08, fog);
                float3 through = readColour(mirrored(uv - refractDir * 0.08), isYuv) * 1.08;
                colour = lerp(colour, saturate(lerp(fogged, through, drop) + rim), effect.y);
            }
        #endif
        #if EFFECT_ONLY == 53
            if (effect.x > 52.5 && effect.x < 53.5 && effect.y > 0) // Zoom burst
            {
                // Streaks race outward from the pointer (or the centre), longer further out, like a
                // zoom pulled during the exposure. A sharp island stays around the origin.
                float2 origin = pointer.z > 0.5 ? pointer.xy : float2(0.5, 0.5);
                float2 delta = uv - origin;
                float r = length(delta * aspect());
                float amount = (0.05 + extras.x * 0.004) * smoothstep(0.03, 0.2 + 0.3 * (1 - pointer.w * 0.5), r) * effect.y;
                float3 sum = 0;
                [loop] for (int i = 0; i < 12; i++)
                {
                    float s = 1 - amount * (i / 11.0) * (extras.y > 0.5 && extras.y < 1.5 ? -1 : 1);
                    sum += readColourAt(mirrored(origin + delta * s), isYuv);
                }
                float3 burst = sum / 12;
                if (extras.y > 1.5) burst = saturate(burst + smoothstep(0.7, 1, lumaOf(burst)) * 0.4);
                colour = lerp(colour, burst, effect.y);
            }
        #endif
        #if EFFECT_ONLY == 54
            if (effect.x > 53.5 && effect.x < 54.5 && effect.y > 0) // Low poly
            {
                // The picture as a mesh of flat triangles: a jittered grid split along the diagonal,
                // each triangle filled with the colour at its centre and lit as a facet.
                float rows = mosaicRows(40, 9);
                float2 cells = float2(rows * effect.w, rows);
                float2 p = uv * cells;
                float2 cell = floor(p);
                float2 local = frac(p);
                float flip = step(0.5, hash(cell + 11.3));
                float upper = flip > 0.5 ? step(local.x, local.y) : step(1 - local.x, local.y);
                float2 centre = cell + (flip > 0.5
                    ? (upper > 0.5 ? float2(0.33, 0.67) : float2(0.67, 0.33))
                    : (upper > 0.5 ? float2(0.67, 0.67) : float2(0.33, 0.33)));
                float2 jitter = (float2(hash(centre), hash(centre + 4.2)) - 0.5) * 0.3;
                float3 facet = readColour(saturate((centre + jitter) / cells), isYuv);
                float shade = 1 + (hash(centre + 8.8) - 0.5) * (extras.y * 0.005);
                float diag = flip > 0.5 ? abs(local.x - local.y) : abs(1 - local.x - local.y);
                float edgeDist = min(min(min(local.x, 1 - local.x), min(local.y, 1 - local.y)), diag * 0.7071);
                float wire = (1 - smoothstep(0.0, 0.04, edgeDist)) * extras.x * 0.01;
                colour = lerp(colour, saturate(facet * shade * (1 - wire * 0.6) + wire * 0.08), effect.y);
            }
        #endif
        """;
}
