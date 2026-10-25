namespace Rex.Media.AppCore.Visuals;

/// <summary>A colour as red, green and blue, 0 to 255 each.</summary>
public readonly record struct Tint(byte R, byte G, byte B)
{
    /// <summary>The colour as "#RRGGBB", which the window's cache and a test read alike.</summary>
    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>
/// The colour a page header is tinted with, taken from an album cover or an artist photo: the
/// pixels are gathered into coarse buckets (four bits a channel), each bucket scores by how many
/// pixels it holds and how colourful they are, so a small vivid patch beats a large grey one but
/// not a large vivid one, and near-black, near-white and see-through pixels hardly count. The
/// winning bucket's average is then set at a depth where white words stay readable over it.
/// Pure and allocation-light; the window runs it once per picture, off its thread, on a few
/// hundred pixels of a picture decoded small, and caches the answer.
/// </summary>
public static class DominantColour
{
    /// <summary>What a picture with no usable pixels is tinted with: rexplayer's own dark slate.</summary>
    public static readonly Tint Fallback = new(0x20, 0x25, 0x2D);

    /// <summary>The lightest a header tint may be, as relative luminance, so white words over it stay readable.</summary>
    public const double MostLuminance = 0.18;

    /// <summary>The darkest a header tint may be, so it still reads as a colour and not as black.</summary>
    public const double LeastLuminance = 0.05;

    /// <summary>
    /// The dominant colour of pixels laid out as blue, green, red and alpha (BGRA, four bytes
    /// each), as Windows decodes them; <see cref="Fallback"/> when none is usable.
    /// </summary>
    public static Tint Pick(ReadOnlySpan<byte> bgra)
    {
        if (bgra.Length % 4 != 0)
        {
            throw new ArgumentException("Pixels come four bytes each.", nameof(bgra));
        }

        Span<double> scores = stackalloc double[4096];
        Span<int> counts = stackalloc int[4096];
        Span<long> red = stackalloc long[4096];
        Span<long> green = stackalloc long[4096];
        Span<long> blue = stackalloc long[4096];
        scores.Clear();
        counts.Clear();
        red.Clear();
        green.Clear();
        blue.Clear();
        for (var at = 0; at < bgra.Length; at += 4)
        {
            byte b = bgra[at], g = bgra[at + 1], r = bgra[at + 2], a = bgra[at + 3];
            if (a < 128)
            {
                continue;
            }

            var bucket = ((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4);
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            var saturation = max == 0 ? 0 : (max - min) / (double)max;
            // Near-black and near-white say little about a cover's colour, but still count a little,
            // so a black-and-white cover gets a grey of its own rather than the fallback.
            var weight = max < 24 || min > 232 ? 0.05 : 0.25 + saturation;
            scores[bucket] += weight;
            counts[bucket]++;
            red[bucket] += r;
            green[bucket] += g;
            blue[bucket] += b;
        }

        var best = -1;
        for (var bucket = 0; bucket < scores.Length; bucket++)
        {
            if (counts[bucket] > 0 && (best < 0 || scores[bucket] > scores[best]))
            {
                best = bucket;
            }
        }

        return best < 0
            ? Fallback
            : new Tint((byte)(red[best] / counts[best]), (byte)(green[best] / counts[best]), (byte)(blue[best] / counts[best]));
    }

    /// <summary>
    /// The colour moved, keeping its hue, to a depth between <see cref="LeastLuminance"/> and
    /// <see cref="MostLuminance"/>: a pale cover tints the header a deeper shade of itself.
    /// </summary>
    public static Tint ForHeader(Tint colour)
    {
        var luminance = Luminance(colour);
        if (luminance <= 0)
        {
            return Fallback;
        }

        var target = Math.Clamp(luminance, LeastLuminance, MostLuminance);
        if (target == luminance)
        {
            return colour;
        }

        // Luminance is linear in light, so scaling the linear channels scales it. The deepest
        // tint asked for is below even pure blue's share of white, so no channel passes full
        // brightness on the way.
        var linear = new[] { Linear(colour.R), Linear(colour.G), Linear(colour.B) };
        var scale = target / luminance;
        return new Tint(Encoded(Math.Min(1, linear[0] * scale)), Encoded(Math.Min(1, linear[1] * scale)), Encoded(Math.Min(1, linear[2] * scale)));
    }

    /// <summary>Relative luminance (WCAG): 0 for black, 1 for white.</summary>
    public static double Luminance(Tint colour) =>
        (0.2126 * Linear(colour.R)) + (0.7152 * Linear(colour.G)) + (0.0722 * Linear(colour.B));

    /// <summary>A colour read back from "#RRGGBB", or null for anything else.</summary>
    public static Tint? Parse(string? text) =>
        text is ['#', _, _, _, _, _, _] && uint.TryParse(text.AsSpan(1), System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? new Tint((byte)(value >> 16), (byte)(value >> 8), (byte)value)
            : null;

    private static double Linear(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static byte Encoded(double linear)
    {
        var value = linear <= 0.0031308 ? linear * 12.92 : (1.055 * Math.Pow(linear, 1 / 2.4)) - 0.055;
        return (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
    }
}
