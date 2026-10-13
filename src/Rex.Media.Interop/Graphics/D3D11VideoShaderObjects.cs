namespace Rex.Media.Interop.Graphics;

/// <summary>
/// The fourth set of effects: digital objects that live in the picture and react to it (a ball
/// that squashes on outlines, fireflies that settle on highlights, ink that floods dark areas),
/// plus lenses and selections driven by the pointer or by the picture's own motion.
/// </summary>
internal static class D3D11VideoShaderObjects
{
    public const string Source = """
        #if EFFECT_ONLY == 55
            if (effect.x > 54.5 && effect.x < 55.5 && effect.y > 0) // Pinball
            {
                // A glowing ball bounces around the frame. Over a strong outline it squashes flat
                // and flares, as if it had hit something; its passing pushes the picture aside and
                // it leaves a fading trail. The pointer, when over the picture, is a bumper.
                float t = effect.z * (0.08 + extras.x * 0.002);
                float2 ball = bouncePath(t, float2(0.37, 0.53), float2(0.08, 0.1));
                float radius = 0.03 + 0.05 * pointer.w;
                float3 under = contourDetail(ball, isYuv);
                float hit = smoothstep(0.03, 0.2, under.z);
                float2 bump = pointer.z > 0.5 ? (ball - pointer.xy) * aspect() : float2(1, 1);
                float bumped = pointer.z > 0.5 ? 1 - smoothstep(radius, radius * 2.5, length(bump)) : 0;
                float2 delta = (uv - ball) * aspect();
                float2 squashAxis = under.xy / max(length(under.xy), 0.0001);
                float along = dot(delta, squashAxis);
                float across = dot(delta, float2(-squashAxis.y, squashAxis.x));
                float r = length(float2(along * (1 + hit * 0.8), across / (1 + hit * 0.4)));
                float body = 1 - smoothstep(radius * 0.85, radius, r);
                float shine = exp(-dot(delta - float2(-radius * 0.3, -radius * 0.3), delta - float2(-radius * 0.3, -radius * 0.3)) * 2500);
                float push = exp(-r * r / (radius * radius * 3)) * (1 - body);
                float3 picture = readColour(mirrored(uv + delta / max(r, 0.0001) * push * 0.04 / aspect()), isYuv);
                float trailLen = 0;
                [loop] for (int i = 1; i <= 8; i++)
                {
                    float2 past = bouncePath(t - i * 0.01 * (0.4 + extras.y * 0.012), float2(0.37, 0.53), float2(0.08, 0.1));
                    float d = length((uv - past) * aspect());
                    trailLen = max(trailLen, (1 - smoothstep(0, radius * (1 - i * 0.1), d)) * (1 - i / 9.0));
                }
                float3 ballColour = lerp(float3(1, 0.35, 0.1), float3(1, 0.9, 0.5), hit + bumped);
                float3 lit = picture * (1 + push * 0.4) + ballColour * trailLen * 0.6;
                lit = lerp(lit, ballColour * (0.8 + hit * 0.6 + bumped) + shine, body);
                colour = lerp(colour, saturate(lit), effect.y);
            }
        #endif
        #if EFFECT_ONLY == 56
            if (effect.x > 55.5 && effect.x < 56.5 && effect.y > 0) // Fireflies
            {
                // Each firefly drifts from its home cell toward the brightest nearby spot, so
                // over time they gather on highlights and hover around lit outlines. The picture
                // dims to dusk so their glow carries.
                float rows = 6 + 12 * pointer.w;
                float2 cells = float2(rows * effect.w, rows);
                float2 home = floor(uv * cells);
                float t = effect.z * (0.3 + extras.x * 0.01);
                float3 glow = 0;
                [loop] for (int dy = -1; dy <= 1; dy++)
                    [loop] for (int dx = -1; dx <= 1; dx++)
                    {
                        float2 cell = home + float2(dx, dy);
                        float h1 = hash(cell), h2 = hash(cell + 3.3), h3 = hash(cell + 7.7);
                        float2 anchor = (cell + float2(h1, h2)) / cells;
                        // Climb toward brightness: compare four points around the anchor.
                        float2 stepUv = 0.6 / cells;
                        float e = lumaOf(readColourAt(anchor + float2(stepUv.x, 0), isYuv));
                        float w = lumaOf(readColourAt(anchor - float2(stepUv.x, 0), isYuv));
                        float s = lumaOf(readColourAt(anchor + float2(0, stepUv.y), isYuv));
                        float n = lumaOf(readColourAt(anchor - float2(0, stepUv.y), isYuv));
                        float2 climb = float2(e - w, s - n);
                        float2 drift = float2(sin(t + h3 * 6.28), cos(t * 1.3 + h1 * 6.28)) * 0.25;
                        float2 fly = anchor + (climb * 3 + drift) / cells;
                        float bright = lumaOf(readColourAt(fly, isYuv));
                        float pulse = 0.55 + 0.45 * sin(t * 4 + h2 * 6.28);
                        float d = length((uv - fly) * aspect() * cells.y);
                        glow += float3(0.8, 1, 0.35) * exp(-d * d * 14) * pulse * (0.4 + bright) * step(0.65, h3);
                    }
                float3 dusk = colour * (0.18 + extras.y * 0.004) * float3(0.7, 0.8, 1);
                colour = lerp(colour, saturate(dusk + glow), effect.y);
            }
        #endif
        #if EFFECT_ONLY == 57
            if (effect.x > 56.5 && effect.x < 57.5 && effect.y > 0) // Electric edges
            {
                // Arcs crawl along the outlines nearest the pointer: outline pixels within reach
                // flicker with travelling noise, brightest close in, and the picture dims around.
                float2 origin = pointer.z > 0.5 ? pointer.xy : float2(0.5 + 0.3 * sin(effect.z * 0.6), 0.5 + 0.3 * cos(effect.z * 0.45));
                float r = length((uv - origin) * aspect());
                float reach = 0.1 + 0.3 * pointer.w;
                float near = 1 - smoothstep(reach * 0.5, reach, r);
                float3 rim = contourDetail(uv, isYuv);
                float edge = smoothstep(0.12 / pointer.w, 0.4 / pointer.w, rim.z);
                float crawl = hash(floor(uv * float2(90 * effect.w, 90) + floor(effect.z * (8 + extras.x * 0.3))));
                float arc = edge * edge * near * step(0.8 - extras.y * 0.004, crawl);
                float3 bolt = lerp(float3(0.35, 0.55, 1), float3(0.9, 0.95, 1), crawl) * arc * 1.3;
                float3 dim = colour * (0.35 + near * 0.4);
                colour = lerp(colour, saturate(dim + bolt + near * edge * float3(0.1, 0.15, 0.3)), effect.y);
            }
        #endif
        #if EFFECT_ONLY == 58
            if (effect.x > 57.5 && effect.x < 58.5 && effect.y > 0) // Time lens
            {
                // A round window at the pointer shows the picture a moment ago instead of now,
                // so moving things inside it lag behind and the rim glows where they differ.
                // Without a previous picture it shows the present, which is what it would be.
                float2 origin = pointer.z > 0.5 ? pointer.xy : float2(0.5, 0.5);
                float r = length((uv - origin) * aspect());
                float radius = 0.12 + 0.2 * pointer.w;
                float inside = 1 - smoothstep(radius - 0.01, radius, r);
                float ring = smoothstep(radius - 0.02, radius - 0.005, r) * (1 - smoothstep(radius, radius + 0.01, r));
                float3 past = motion.y > 0.5 ? readPrior(uv, isYuv) : colour;
                float difference = length(colour - past);
                float3 tint = extras.y < 0.5 ? float3(0.9, 0.95, 1.1) : extras.y < 1.5 ? float3(1.1, 1.0, 0.85) : float3(1, 1, 1);
                float3 window = past * tint * (1 - extras.x * 0.002);
                float3 rimColour = lerp(float3(0.3, 0.5, 0.8), float3(1, 0.9, 0.5), saturate(difference * 4));
                colour = lerp(colour, saturate(lerp(colour, window, inside) + ring * rimColour + inside * difference * float3(0.3, 0.2, 0)), effect.y);
            }
        #endif
        #if EFFECT_ONLY == 59
            if (effect.x > 58.5 && effect.x < 59.5 && effect.y > 0) // Melt
            {
                // The picture runs downward like wax: each column slides at its own pace, the
                // bright areas melt furthest, and a drip edge forms where a run catches up.
                float columns = 40 + 80 * pointer.w;
                float column = floor(uv.x * columns * effect.w);
                float pace = 0.3 + 0.7 * hash(float2(column, 1.1));
                float t = frac(effect.z * (0.02 + extras.x * 0.001) * pace);
                float warmth = lumaOf(readColourAt(float2(uv.x, saturate(uv.y - 0.3)), isYuv));
                float run = t * (0.15 + extras.y * 0.006) * (0.5 + warmth);
                float2 source = float2(uv.x, uv.y - run * smoothstep(0.0, 0.6, uv.y));
                float3 melted = readColour(saturate(source), isYuv);
                float drip = smoothstep(0.0, 0.02, run) * (1 - smoothstep(0.0, 0.03, abs(frac(uv.y * 12 + hash(float2(column, 2.2))) - 0.5)));
                colour = lerp(colour, saturate(melted * (1 - drip * 0.15) + drip * 0.05), effect.y);
            }
        #endif
        #if EFFECT_ONLY == 60
            if (effect.x > 59.5 && effect.x < 60.5 && effect.y > 0) // Jigsaw
            {
                // The picture cut into jigsaw pieces with tabs and sockets. Pieces close to the
                // pointer lift off the board, casting a shadow and sliding a little.
                float rows = 4 + 8 * pointer.w;
                float2 cells = float2(rows * effect.w, rows);
                float2 p = uv * cells;
                float2 cell = floor(p);
                float2 local = frac(p);
                // Tab on the right and bottom edges of each piece when its hash says so; the
                // neighbour then has the matching socket.
                float tabR = step(0.5, hash(cell + 1.1)) * 2 - 1;
                float tabB = step(0.5, hash(cell + 2.2)) * 2 - 1;
                float tabL = -(step(0.5, hash(cell - float2(1, 0) + 1.1)) * 2 - 1);
                float tabT = -(step(0.5, hash(cell - float2(0, 1) + 2.2)) * 2 - 1);
                float knob = 0.12;
                float2 owner = cell;
                float dR = length(float2(local.x - 1, local.y - 0.5) * float2(1, 1));
                float dB = length(float2(local.x - 0.5, local.y - 1));
                float dL = length(float2(local.x, local.y - 0.5));
                float dT = length(float2(local.x - 0.5, local.y));
                if (dR < knob) owner = tabR > 0 ? cell : cell + float2(1, 0);
                else if (dB < knob) owner = tabB > 0 ? cell : cell + float2(0, 1);
                else if (dL < knob) owner = tabL > 0 ? cell : cell - float2(1, 0);
                else if (dT < knob) owner = tabT > 0 ? cell : cell - float2(0, 1);
                float2 origin = pointer.z > 0.5 ? pointer.xy : float2(-1, -1);
                float lift = 1 - smoothstep(0.08, 0.2 + extras.x * 0.002, length(((owner + 0.5) / cells - origin) * aspect()));
                float2 slide = (float2(hash(owner + 5.5), hash(owner + 6.6)) - 0.5) * lift * 0.02 * extras.y * 0.02;
                float3 piece = readColour(saturate(uv - slide), isYuv) * (1 + lift * 0.15);
                float seam = min(min(dR, dB), min(dL, dT));
                float width = 0.045;
                // Straight edges, except across a knob's mouth, which the tab bridges.
                float edgeLine = (1 - smoothstep(0.0, width, min(min(local.x, 1 - local.x), min(local.y, 1 - local.y)))) * step(knob, seam);
                // A knob's rim is a cut only inside the piece its tab pushes into.
                float arc = max(max(step(tabR, 0) * (1 - smoothstep(0, width, abs(dR - knob))) * step(local.x, 1),
                                    step(tabB, 0) * (1 - smoothstep(0, width, abs(dB - knob)))),
                                max(step(tabL, 0) * (1 - smoothstep(0, width, abs(dL - knob))),
                                    step(tabT, 0) * (1 - smoothstep(0, width, abs(dT - knob)))));
                float cut = max(edgeLine, arc);
                float shadow = lift * (1 - smoothstep(0, 0.03, min(local.x, local.y))) * 0.5;
                colour = lerp(colour, saturate(piece * (1 - cut * 0.7) * (1 - shadow)), effect.y);
            }
        #endif
        #if EFFECT_ONLY == 61
            if (effect.x > 60.5 && effect.x < 61.5 && effect.y > 0) // Colour magnet
            {
                // The pointer picks the colour under it; everything of that colour is drawn toward
                // the pointer and glows, while the rest sits still. Away from the picture the
                // magnet rests at the centre.
                float2 origin = pointer.z > 0.5 ? pointer.xy : float2(0.5, 0.5);
                float3 target = readColourAt(origin, isYuv);
                float2 delta = (uv - origin) * aspect();
                float r = length(delta);
                float tolerance = 0.12 + 0.25 * pointer.w;
                float same = 1 - smoothstep(tolerance * 0.5, tolerance, length(colour - target));
                float pull = same * (0.05 + extras.x * 0.003) * smoothstep(0.02, 0.3, r);
                float2 from = uv + delta / max(r, 0.0001) * pull / aspect();
                float3 pulled = readColour(mirrored(from), isYuv);
                float stillSame = 1 - smoothstep(tolerance * 0.5, tolerance, length(pulled - target));
                float3 glow = target * stillSame * (0.2 + extras.y * 0.004);
                colour = lerp(colour, saturate(lerp(colour, pulled, same) + glow), effect.y);
            }
        #endif
        #if EFFECT_ONLY == 62
            if (effect.x > 61.5 && effect.x < 62.5 && effect.y > 0) // Sonar ping
            {
                // A ping spreads from the pointer. As the ring crosses an outline the outline
                // lights up, then fades behind the ring, so the room is revealed edge by edge.
                float2 origin = pointer.z > 0.5 ? pointer.xy : float2(0.5, 0.5);
                float r = length((uv - origin) * aspect());
                float period = 2 + extras.x * 0.04;
                float age = frac(effect.z / period);
                float radius = age * 0.9;
                float3 rim = contourDetail(uv, isYuv);
                float edge = smoothstep(0.02 / pointer.w, 0.15 / pointer.w, rim.z);
                float ring = exp(-abs(r - radius) * 60);
                float behind = step(r, radius) * exp(-(radius - r) * (2 + extras.y * 0.08));
                float3 tint = float3(0.2, 1, 0.6);
                float3 dark = colour * 0.08;
                colour = lerp(colour, saturate(dark + tint * (edge * behind * 1.2 + ring * 0.6) + ring * edge * 0.6), effect.y);
            }
        #endif
        #if EFFECT_ONLY == 63
            if (effect.x > 62.5 && effect.x < 63.5 && effect.y > 0) // Bokeh
            {
                // Soft areas are treated as far and go out of focus: their highlights bloom into
                // hexagonal discs of the lens, while crisp detail stays sharp.
                float3 fine = contourGradient(uv, isYuv, 0.75);
                float3 broad = contourGradient(uv, isYuv, 3.0);
                float near = smoothstep(0.1, 0.5, saturate(fine.z / max(broad.z, 0.004)) * saturate(fine.z * 10 * pointer.w));
                float blurRadius = (0.01 + extras.x * 0.0004) * (1 - near);
                float3 sum = 0; float weight = 0;
                [loop] for (uint i = 0; i < 18; i++)
                {
                    uint ring = i / 6;
                    float angle = (i - ring * 6) * 1.0471976 + ring * 0.35;
                    float ringR = (1 + ring) / 3.0;
                    float2 tap = uv + float2(cos(angle), sin(angle)) * blurRadius * ringR / aspect();
                    float3 c = readColourAt(saturate(tap), isYuv);
                    float w = 1 + smoothstep(0.6, 1, lumaOf(c)) * extras.y * 0.08;
                    sum += c * w; weight += w;
                }
                colour = lerp(colour, sum / weight, effect.y);
            }
        #endif
        #if EFFECT_ONLY == 64
            if (effect.x > 63.5 && effect.x < 64.5 && effect.y > 0) // Ink spill
            {
                // Ink spreads from the pointer and floods the darker areas first, feeling its way
                // around bright ones; the flood breathes with time so it is never static.
                float2 origin = pointer.z > 0.5 ? pointer.xy : float2(0.5, 0.5);
                float r = length((uv - origin) * aspect());
                float spread = (0.15 + extras.x * 0.004) * (0.8 + 0.2 * sin(effect.z * 1.2));
                float l = lumaOf(colour);
                float resistance = smoothstep(0.35, 0.8, l) * (0.5 + pointer.w * 0.5);
                float grain = (hash(floor(uv * float2(200 * effect.w, 200))) - 0.5) * 0.06;
                float flood = 1 - smoothstep(spread * (1 - resistance) - 0.04, spread * (1 - resistance) + 0.02 + grain, r);
                float3 ink = extras.y < 0.5 ? float3(0.03, 0.02, 0.08) : extras.y < 1.5 ? float3(0.05, 0.12, 0.35) : extras.y < 2.5 ? float3(0.35, 0.03, 0.05) : float3(0.9, 0.88, 0.8);
                float edgeWet = (1 - smoothstep(0, 0.03, abs(r - spread * (1 - resistance)))) * 0.5;
                colour = lerp(colour, saturate(lerp(colour, ink + colour * 0.1, flood) + edgeWet * ink * 2), effect.y);
            }
        #endif
        #if EFFECT_ONLY == 65
            if (effect.x > 64.5 && effect.x < 65.5 && effect.y > 0) // Motion outline
            {
                // Marching ants around whatever moved since the previous picture, like a live
                // selection: the moving region's edge is found by comparing the change here with
                // the change a few pixels away. A still picture shows no ants.
                float2 px = max(fwidth(uv), float2(0.00022, 0.00022)) * (2 + pointer.w * 2);
                float here = 0, around = 0;
                [branch] if (motion.y > 0.5)
                {
                    here = smoothstep(0.07, 0.16, length(colour - readPriorAt(uv, isYuv)));
                    [loop] for (int k = 0; k < 8; k++)
                    {
                        float2 off = float2(cos(k * 0.785398), sin(k * 0.785398)) * px;
                        around = max(around, smoothstep(0.07, 0.16, length(readColourAt(uv + off, isYuv) - readPriorAt(uv + off, isYuv))));
                    }
                }
                float outline = saturate(around - here);
                float ants = step(0.5, frac((uv.x + uv.y) * 90 - effect.z * (2 + extras.x * 0.1)));
                float3 lineColour = extras.y < 0.5 ? lerp(float3(0, 0, 0), float3(1, 1, 1), ants) : extras.y < 1.5 ? lerp(float3(0.1, 0.1, 0.4), float3(1, 0.9, 0.2), ants) : lerp(float3(0.5, 0, 0), float3(1, 0.3, 0.3), ants);
                float3 dimmed = lerp(colour, lumaOf(colour).xxx * 0.6, 0.35);
                colour = lerp(colour, saturate(lerp(dimmed, lineColour, outline) + here * float3(0.05, 0.05, 0.1)), effect.y);
            }
        #endif
        #if EFFECT_ONLY == 66
            if (effect.x > 65.5 && effect.x < 66.5 && effect.y > 0) // Heat haze
            {
                // Air shimmers above the brightest parts of the picture: pixels sample from a
                // wobbling position whose strength comes from the brightness below them.
                float heat = 0;
                [loop] for (int i = 1; i <= 6; i++)
                    heat += lumaOf(readColourAt(saturate(uv + float2(0, i * 0.03)), isYuv)) / 6;
                heat = smoothstep(0.3, 0.8, heat) * (0.5 + pointer.w * 0.5);
                float t = effect.z * (1 + extras.x * 0.05);
                float2 wobble = float2(sin(uv.y * 80 + t * 3) + sin(uv.y * 37 - t * 2.3), cos(uv.x * 60 + t * 2.1)) * 0.004 * (0.5 + extras.y * 0.01) * heat;
                float3 shimmer = readColour(mirrored(uv + wobble), isYuv);
                colour = lerp(colour, shimmer * (1 + heat * 0.05), effect.y);
            }
        #endif
        #if EFFECT_ONLY == 67
            if (effect.x > 66.5 && effect.x < 67.5 && effect.y > 0) // Confetti
            {
                // Confetti falls across the picture, each piece tumbling; near the pointer the
                // pieces are blown aside, and they catch the picture's light as they pass.
                float2 origin = pointer.z > 0.5 ? pointer.xy : float2(-1, -1);
                float rows = 5 + 9 * pointer.w;
                float2 cells = float2(rows * effect.w, rows);
                float t = effect.z * (0.15 + extras.x * 0.004);
                float3 paper = 0; float cover = 0;
                [loop] for (int dy = -1; dy <= 1; dy++)
                    [loop] for (int dx = -1; dx <= 1; dx++)
                    {
                        float2 cell = floor(uv * cells) + float2(dx, dy);
                        float h1 = hash(cell), h2 = hash(cell + 3.1), h3 = hash(cell + 6.2);
                        float fall = frac(t * (0.6 + h1 * 0.8) + h2);
                        float2 pos = (cell + float2(h1 + 0.15 * sin(t * 3 + h3 * 6.28), fall)) / cells;
                        float2 away = (pos - origin) * aspect();
                        float blow = exp(-length(away) * 8) * (extras.y * 0.003);
                        pos += away / max(length(away), 0.0001) * blow / aspect();
                        float2 d = (uv - pos) * cells;
                        float spin = t * 6 + h3 * 6.28;
                        float2 rd = float2(d.x * cos(spin) - d.y * sin(spin), d.x * sin(spin) + d.y * cos(spin));
                        float piece = step(abs(rd.x), 0.22) * step(abs(rd.y), 0.08 + 0.1 * abs(sin(spin * 0.5))) * step(0.3, h2);
                        float3 tint = 0.6 + 0.4 * cos(h1 * 6.28 + float3(0, 2.094, 4.189));
                        paper = lerp(paper, tint * (0.5 + 0.5 * lumaOf(colour)), piece * (1 - cover));
                        cover = max(cover, piece);
                    }
                colour = lerp(colour, lerp(colour, paper, cover), effect.y);
            }
        #endif
        """;
}
