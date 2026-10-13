namespace Rex.Media.Interop.Graphics;

/// <summary>
/// The colour looks: one branch per <c>VideoLook</c> value, in enum order, applied after any effect.
/// Each returns the fully styled colour; <c>styledLook</c> then mixes it with the original by the
/// look's intensity and richness controls.
/// </summary>
internal static class D3D11VideoShaderLooks
{
    public const string Source = """
        // A soft S-curve around mid grey: contrast without clipping either end.
        float3 filmCurve(float3 c, float amount)
        {
            return lerp(c, c * c * (3 - 2 * c), amount);
        }
        // Split toning: push shadows one way and highlights another, by brightness.
        float3 splitTone(float3 c, float l, float3 shadows, float3 highlights)
        {
            return c + lerp(shadows, highlights, smoothstep(0.15, 0.8, l));
        }
        float3 basePictureLook(float3 c)
        {
            if (look.x < 0.5) return c;
            float l = lumaOf(c);
            float3 grey = float3(l, l, l);
            if (look.x < 1.5) // Cinema
                return saturate((lerp(grey, c, 0.94) - 0.5) * 1.13 + 0.5 + float3(0.025, 0.004, -0.017));
            if (look.x < 2.5) // Clear
                return saturate((lerp(grey, c, 1.08) - 0.5) * 1.09 + 0.5);
            if (look.x < 3.5) // Sunset
                return saturate(lerp(grey, c, 1.12) + float3(0.065, 0.022, -0.043));
            if (look.x < 4.5) // Arctic
                return saturate(lerp(grey, c, 1.07) + float3(-0.025, 0.015, 0.069));
            if (look.x < 5.5) // Monochrome
                return grey;
            if (look.x < 6.5) // Vintage
            {
                float3 sepia = float3(
                    dot(c, float3(0.393, 0.769, 0.189)),
                    dot(c, float3(0.349, 0.686, 0.168)),
                    dot(c, float3(0.272, 0.534, 0.131)));
                return saturate((lerp(c, sepia, 0.85) - 0.5) * 0.94 + 0.5);
            }
            if (look.x < 7.5) // Neon
                return saturate((lerp(grey, c, 1.7) - 0.5) * 1.14 + 0.5 + float3(0.013, -0.008, 0.035));
            if (look.x < 8.5) // Night vision
                return saturate(float3(l * 0.07, l * 1.23, l * 0.17));
            if (look.x < 9.5) // Teal & orange: cool shadows, warm highlights, gentle S-curve
            {
                float3 split = saturate(splitTone(lerp(grey, c, 1.08), l, float3(-0.02, 0.05, 0.09), float3(0.09, 0.03, -0.06)));
                return filmCurve(split, 0.35);
            }
            if (look.x < 10.5) // Golden film: warm stock, lifted blacks, highlight halation
            {
                float3 film = c * float3(1.06, 1.0, 0.86) + float3(0.035, 0.025, 0.02);
                return saturate((lerp(grey, film, 1.05) - 0.5) * 0.97 + 0.5 + l * l * l * float3(0.10, 0.04, 0));
            }
            if (look.x < 11.5) // Bleach bypass: retained silver, low saturation, hard contrast
                return saturate((lerp(c, grey, 0.55) - 0.5) * 1.35 + 0.5 + grey * grey * 0.12 - 0.03);
            if (look.x < 12.5) // Cross process: per-channel curves, cyan shadows, yellow highlights
            {
                float3 crossed = float3(smoothstep(0.04, 0.96, c.r), c.g * 0.92 + 0.07, c.b * 0.72 + 0.10);
                return saturate(splitTone(crossed, l, float3(-0.03, 0.04, 0.06), float3(0.06, 0.05, -0.08)));
            }
            if (look.x < 13.5) // Infrared: false colour, so foliage greens turn red and reds turn gold
            {
                float3 infrared = float3(c.g * 1.15, c.r * 0.95, c.b * 1.05);
                return saturate(infrared + smoothstep(0.55, 1, l) * float3(0.12, 0.10, 0.08));
            }
            if (look.x < 14.5) // Slide film: deep saturation, strong curve, slightly cool shadows
                return saturate(splitTone(filmCurve(lerp(grey, c, 1.35), 0.6), l, float3(-0.02, -0.01, 0.03), float3(0.02, 0.01, -0.02)));
            if (look.x < 15.5) // Faded print: lifted blacks, soft highlights, a magenta cast in the mids
            {
                float3 faded = c * 0.78 + 0.13;
                return saturate(lerp(grey, faded, 0.8) + float3(0.04, -0.01, 0.03) * (1 - abs(l - 0.5) * 2));
            }
            if (look.x < 16.5) // Noir: hard black and white, crushed shadows, bright skin and skies
            {
                float noir = smoothstep(0.08, 0.92, pow(l, 1.15));
                return noir.xxx;
            }
            if (look.x < 17.5) // Moonlight: day for night, deep blue, highlights kept
            {
                float3 night = lerp(grey, c, 0.45) * float3(0.55, 0.68, 1.05) * 0.72;
                return saturate(night + smoothstep(0.75, 1, l) * float3(0.15, 0.15, 0.12));
            }
            if (look.x < 18.5) // Cyberpunk: magenta shadows, cyan highlights, lifted saturation
                return saturate(splitTone(lerp(grey, c, 1.3), l, float3(0.12, -0.04, 0.14), float3(-0.08, 0.08, 0.12)));
            if (look.x < 19.5) // Desert: warm, dusty, muted greens
            {
                float3 dust = lerp(grey, c, 0.78) * float3(1.08, 0.98, 0.84) + float3(0.04, 0.02, 0);
                dust.g = lerp(dust.g, (dust.r + dust.b) * 0.5, 0.25);
                return saturate(dust);
            }
            if (look.x < 20.5) // Forest: green shadows, soft warm light, gentle contrast
                return saturate(filmCurve(splitTone(lerp(grey, c, 1.05), l, float3(-0.03, 0.05, 0.0), float3(0.05, 0.03, -0.04)), 0.25));
            if (look.x < 21.5) // Pastel: lifted, airy, low contrast with soft colour
                return saturate(lerp(grey, c, 0.82) * 0.82 + 0.16);
            if (look.x < 22.5) // Negative
                return 1 - c;
            if (look.x < 23.5) // Solarize: tones above the middle fold back down
                return saturate(c < 0.5 ? c * 2 : 2 - c * 2) * 0.92;
            if (look.x < 24.5) // Posterize: five flat levels per channel, with the hue kept
                return floor(c * 4.999) / 4;
            if (look.x < 25.5) // Duotone: deep purple shadows, warm orange highlights
                return lerp(float3(0.22, 0.05, 0.38), float3(1.0, 0.72, 0.35), smoothstep(0.05, 0.95, l));
            if (look.x < 26.5) // Cyanotype: Prussian blue shadows on paper-white highlights, like a sun print
                return lerp(float3(0.04, 0.16, 0.42), float3(0.93, 0.95, 0.98), smoothstep(0.05, 0.95, l));
            if (look.x < 27.5) // Technicolor: three-strip dye separation, each primary pushed apart
            {
                float3 strip = float3(c.r * 1.25 - (c.g + c.b) * 0.12, c.g * 1.18 - (c.r + c.b) * 0.09, c.b * 1.2 - (c.r + c.g) * 0.1);
                return saturate(filmCurve(saturate(strip), 0.4) + float3(0.02, 0, 0));
            }
            if (look.x < 28.5) // Silver gelatin: warm-toned black and white with soft, open highlights
            {
                float silver = pow(l, 0.9);
                return saturate(lerp(float3(0.03, 0.025, 0.02), float3(0.98, 0.95, 0.88), silver) + silver * silver * 0.03);
            }
            if (look.x < 29.5) // Super 8: soft, warm, low contrast with a green-magenta tilt
            {
                float3 soft = (lerp(grey, c, 0.85) - 0.5) * 0.85 + 0.5;
                return saturate(soft * float3(1.08, 0.98, 0.88) + float3(0.03, 0.0, 0.04) * (1 - abs(l - 0.5) * 2));
            }
            if (look.x < 30.5) // Autumn: greens turn amber, reds deepen, warm light
            {
                float3 turned = float3(c.r + c.g * 0.25, c.g * 0.72 + c.r * 0.12, c.b * 0.7);
                return saturate(lerp(grey, turned, 1.15) + float3(0.04, 0.01, -0.03));
            }
            if (look.x < 31.5) // Candlelight: dark, warm, orange highlights, blue drained from shadows
            {
                float3 warm = c * float3(1.15, 0.85, 0.55) * 0.8;
                return saturate(splitTone(warm, l, float3(-0.02, -0.03, -0.06), float3(0.12, 0.05, -0.02)));
            }
            if (look.x < 32.5) // Overcast: flat cool light, no deep shadows or bright highlights
                return saturate((lerp(grey, c, 0.8) - 0.5) * 0.8 + 0.5 + float3(-0.03, 0.0, 0.05));
            if (look.x < 33.5) // Spring: fresh lifted greens and bright, airy light
            {
                float3 fresh = float3(c.r * 0.95, c.g * 1.12 + 0.02, c.b * 0.95);
                return saturate(lerp(grey, fresh, 1.1) * 1.05 + 0.03);
            }
            if (look.x < 34.5) // Dusk: violet shadows, pink highlights, a little haze
            {
                float3 haze = lerp(grey, c, 0.9) * 0.92 + 0.04;
                return saturate(splitTone(haze, l, float3(0.04, -0.03, 0.12), float3(0.12, 0.02, 0.06)));
            }
            if (look.x < 35.5) // Acid: every hue turned a third of the way round the wheel
                return saturate(lerp(grey, c.gbr, 1.3));
            if (look.x < 36.5) // Ultraviolet: violet shadows, glowing white highlights, like a black light
            {
                float3 uvTone = lerp(float3(0.12, 0.02, 0.3), float3(0.75, 0.55, 1.0), smoothstep(0.1, 0.7, l));
                return saturate(lerp(uvTone, float3(1, 1, 1), smoothstep(0.7, 1, l)) + (c - grey) * 0.5);
            }
            if (look.x < 37.5) // Comic: hard contrast and strong colour, close to a printed page
            {
                float3 hard = saturate((lerp(grey, c, 1.6) - 0.5) * 1.6 + 0.5);
                return floor(hard * 6.999) / 6;
            }
            if (look.x < 38.5) // Chrome: polished metal, grey with a cool sheen and bright reflections
            {
                float sheen = smoothstep(0.3, 0.95, l);
                return saturate(lerp(float3(0.04, 0.05, 0.08), float3(0.9, 0.95, 1.0), pow(l, 1.3)) + sheen * sheen * 0.15 + (c - grey) * 0.15);
            }
            // Vivid: more colour and a touch more contrast, nothing stylised
            return saturate((lerp(grey, c, 1.4) - 0.5) * 1.08 + 0.5);
        }
        """;
}
