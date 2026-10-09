using System.Text;
using Rex.Media.Audio;
using Rex.Media.Primitives;
using Rex.Media.Settings;

namespace Rex.Media.AppCore.Library;

/// <summary>
/// Makes cover art from the sound itself when a track has no artwork. The same identity and samples
/// always make the same picture; the palette follows spectral balance, the central shape follows
/// dynamics, and its orbiting strokes follow the waveform.
/// </summary>
public static class AudioArtwork
{
    private const int TransformSize = 2048;
    private const int EnvelopePoints = 96;

    /// <summary>Renders square BGRA artwork from mono samples with the default look.</summary>
    public static VideoFrame Render(float[] samples, int sampleRate, string identity, int size = 256)
        => Render(samples, sampleRate, identity, new AudioArtworkOptions(), size);

    /// <summary>Renders square BGRA artwork from mono samples with a user-selected look.</summary>
    public static VideoFrame Render(float[] samples, int sampleRate, string identity, AudioArtworkOptions options, int size = 256)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 64);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, 1024);
        if (samples.Length == 0)
        {
            throw new ArgumentException("Artwork needs at least one sound sample.", nameof(samples));
        }

        options = options.Normalize();
        var seed = Hash(options.UseIdentity ? identity : "", samples) ^ ((ulong)options.Style * 0x9E3779B97F4A7C15UL);
        var envelope = Envelope(samples);
        var spectrum = Spectrum(samples, seed);
        var centroid = Centroid(spectrum);
        var frame = VideoFrame.Rent(PixelFormat.Bgra32, size, size);
        var pixels = frame.Plane(0);
        var stride = frame.Stride(0);
        var color = options.Color / 100.0;
        var detail = options.Detail / 100.0;
        var contrast = options.Contrast / 100.0;
        var hue = ((seed >> 8) % 360 + (centroid * 110) + ((int)options.Style * 29)) % 360;
        var hueGap = options.Style switch
        {
            ArtworkStyle.Orbit => 145,
            ArtworkStyle.Wave => 38,
            ArtworkStyle.Minimal => 18,
            _ => 75,
        };
        var secondHue = (hue + hueGap + (spectrum[0] * 80)) % 360;
        var first = Hsv(hue, Math.Clamp(0.72 * color, 0, 1), Math.Clamp(0.12 + (0.1 * contrast), 0.04, 0.42));
        var second = Hsv(secondHue, Math.Clamp(0.84 * color, 0, 1), Math.Clamp(0.18 + (0.16 * contrast), 0.06, 0.58));

        for (var y = 0; y < size; y++)
        {
            var row = pixels.Slice(y * stride, size * 4);
            for (var x = 0; x < size; x++)
            {
                var dx = ((2.0 * x) / (size - 1)) - 1;
                var dy = ((2.0 * y) / (size - 1)) - 1;
                var radius = Math.Sqrt((dx * dx) + (dy * dy));
                var turn = (Math.Atan2(dy, dx) + Math.PI) / (2 * Math.PI);
                var mix = options.Style == ArtworkStyle.Wave
                    ? Math.Clamp((0.65 * (dy + 1) / 2) + (0.18 * Math.Sin((turn + radius) * Math.PI * 4)), 0, 1)
                    : Math.Clamp((0.58 * radius) + (0.3 * turn), 0, 1);
                var light = Math.Clamp((0.7 + (0.2 * contrast)) - ((0.3 + (0.25 * contrast)) * Math.Clamp(radius, 0, 1)), 0.15, 1.1);
                row[(x * 4) + 0] = (byte)Math.Clamp(((first.B * (1 - mix)) + (second.B * mix)) * light, 0, 255);
                row[(x * 4) + 1] = (byte)Math.Clamp(((first.G * (1 - mix)) + (second.G * mix)) * light, 0, 255);
                row[(x * 4) + 2] = (byte)Math.Clamp(((first.R * (1 - mix)) + (second.R * mix)) * light, 0, 255);
                row[(x * 4) + 3] = 255;
            }
        }

        var random = new Random(unchecked((int)seed));
        var particleBase = options.Style switch
        {
            ArtworkStyle.Orbit => 104,
            ArtworkStyle.Minimal => 18,
            _ => 72,
        };
        var particles = Math.Max(0, (int)Math.Round(particleBase * detail));
        for (var i = 0; i < particles; i++)
        {
            var angle = random.NextDouble() * Math.PI * 2;
            var distance = (options.Style == ArtworkStyle.Orbit ? 0.55 + (0.42 * random.NextDouble()) : Math.Sqrt(random.NextDouble())) * size * 0.48;
            var energy = spectrum[i % spectrum.Length];
            var particleColor = Hsv(hue + (i * 137.508), Math.Clamp((0.45 + (0.45 * energy)) * color, 0, 1), 0.75 + (0.25 * energy));
            Dot(frame, (size / 2.0) + (Math.Cos(angle) * distance), (size / 2.0) + (Math.Sin(angle) * distance), 0.8 + (energy * 3), particleColor, (0.08 + (energy * 0.35)) * contrast);
        }

        (double X, double Y)? previous = null;
        var ringColor = Hsv(hue + 155, Math.Clamp(0.58 * color, 0, 1), 1);
        var envelopeStep = detail < 0.5 ? 4 : detail < 1 ? 2 : 1;
        for (var i = 0; i <= EnvelopePoints; i += envelopeStep)
        {
            var point = i % EnvelopePoints;
            var angle = (point * Math.PI * 2 / EnvelopePoints) - (Math.PI / 2);
            var energy = envelope[point];
            var band = spectrum[point % spectrum.Length];
            var baseRadius = options.Style == ArtworkStyle.Minimal ? 0.27 : options.Style == ArtworkStyle.Orbit ? 0.23 : 0.19;
            var radius = size * (baseRadius + ((options.Style == ArtworkStyle.Minimal ? 0.08 : 0.2) * energy) + (0.08 * band));
            var current = ((size / 2.0) + (Math.Cos(angle) * radius), (size / 2.0) + (Math.Sin(angle) * radius));
            if (previous is { } from)
            {
                Line(frame, from.X, from.Y, current.Item1, current.Item2, 1.3 + (2.2 * band), ringColor, Math.Clamp((0.35 + (0.4 * energy)) * contrast, 0, 1));
            }

            previous = current;
        }

        var ribbons = options.Style switch
        {
            ArtworkStyle.Orbit => 1,
            ArtworkStyle.Minimal => 0,
            ArtworkStyle.Wave => Math.Clamp((int)Math.Round(2 + (detail * 3)), 1, 6),
            _ => Math.Clamp((int)Math.Round(1 + (detail * 2)), 1, 5),
        };
        for (var ribbon = 0; ribbon < ribbons; ribbon++)
        {
            var ribbonColor = Hsv(hue + 35 + (ribbon * 62), Math.Clamp(0.62 * color, 0, 1), 1);
            (double X, double Y)? from = null;
            for (var x = 0; x < size; x++)
            {
                var share = (double)x / (size - 1);
                var sample = samples[(int)(share * (samples.Length - 1))];
                sample = float.IsFinite(sample) ? Math.Clamp(sample, -1, 1) : 0;
                var center = options.Style == ArtworkStyle.Wave ? 0.5 + ((ribbon - ((ribbons - 1) / 2.0)) * 0.09) : 0.72 + (ribbon * 0.075);
                var y = (size * center) + (sample * size * (0.055 + (ribbon * 0.012)));
                if (from is { } prior)
                {
                    Line(frame, prior.X, prior.Y, x, y, 1 + (ribbon * 0.7), ribbonColor, Math.Clamp(0.25 * contrast, 0, 0.75));
                }

                from = (x, y);
            }
        }

        return frame;
    }

    private static double[] Envelope(float[] samples)
    {
        var result = new double[EnvelopePoints];
        for (var point = 0; point < result.Length; point++)
        {
            var from = point * samples.Length / result.Length;
            var to = Math.Max(from + 1, (point + 1) * samples.Length / result.Length);
            var sum = 0.0;
            for (var i = from; i < Math.Min(to, samples.Length); i++)
            {
                var sample = float.IsFinite(samples[i]) ? samples[i] : 0;
                sum += sample * sample;
            }

            result[point] = Math.Clamp(Math.Sqrt(sum / Math.Max(1, to - from)) * 2.4, 0, 1);
        }

        return result;
    }

    private static float[] Spectrum(float[] samples, ulong seed)
    {
        var window = new float[TransformSize];
        var start = samples.Length > window.Length ? (int)(seed % (ulong)(samples.Length - window.Length)) : 0;
        for (var i = 0; i < window.Length; i++)
        {
            var sample = samples[(start + i) % samples.Length];
            window[i] = float.IsFinite(sample) ? sample : 0;
        }

        var magnitudes = new float[TransformSize / 2];
        Fft.Magnitudes(window, magnitudes);
        var bands = new float[24];
        for (var band = 0; band < bands.Length; band++)
        {
            var from = Math.Max(1, (int)Math.Pow(magnitudes.Length, (double)band / bands.Length));
            var to = Math.Max(from + 1, (int)Math.Pow(magnitudes.Length, (double)(band + 1) / bands.Length));
            bands[band] = Math.Clamp(magnitudes[from..Math.Min(to, magnitudes.Length)].ToArray().DefaultIfEmpty().Average() * 7, 0, 1);
        }

        return bands;
    }

    private static double Centroid(float[] spectrum)
    {
        var total = spectrum.Sum(value => (double)value);
        return total == 0 ? 0.5 : spectrum.Select((value, index) => value * index).Sum() / total / (spectrum.Length - 1);
    }

    private static ulong Hash(string identity, float[] samples)
    {
        const ulong Offset = 14695981039346656037;
        const ulong Prime = 1099511628211;
        var hash = Offset;
        foreach (var value in Encoding.UTF8.GetBytes(identity))
        {
            hash = (hash ^ value) * Prime;
        }

        var step = Math.Max(1, samples.Length / 4096);
        for (var i = 0; i < samples.Length; i += step)
        {
            var sample = float.IsFinite(samples[i]) ? Math.Clamp(samples[i], -1, 1) : 0;
            hash = (hash ^ (ushort)((sample + 1) * 32767.5f)) * Prime;
        }

        return hash;
    }

    private static void Line(VideoFrame frame, double x1, double y1, double x2, double y2, double width, (byte R, byte G, byte B) color, double alpha)
    {
        var steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(x2 - x1), Math.Abs(y2 - y1))));
        for (var step = 0; step <= steps; step++)
        {
            var share = (double)step / steps;
            Dot(frame, x1 + ((x2 - x1) * share), y1 + ((y2 - y1) * share), width, color, alpha);
        }
    }

    private static void Dot(VideoFrame frame, double cx, double cy, double radius, (byte R, byte G, byte B) color, double alpha)
    {
        var left = Math.Max(0, (int)Math.Floor(cx - radius));
        var right = Math.Min(frame.Width - 1, (int)Math.Ceiling(cx + radius));
        var top = Math.Max(0, (int)Math.Floor(cy - radius));
        var bottom = Math.Min(frame.Height - 1, (int)Math.Ceiling(cy + radius));
        for (var y = top; y <= bottom; y++)
        {
            var row = frame.Row(0, y);
            for (var x = left; x <= right; x++)
            {
                var distance = Math.Sqrt(((x - cx) * (x - cx)) + ((y - cy) * (y - cy)));
                var amount = Math.Clamp((radius + 0.7 - distance) * alpha, 0, 1);
                var offset = x * 4;
                row[offset + 0] = Blend(row[offset + 0], color.B, amount);
                row[offset + 1] = Blend(row[offset + 1], color.G, amount);
                row[offset + 2] = Blend(row[offset + 2], color.R, amount);
            }
        }
    }

    private static byte Blend(byte behind, byte light, double amount) =>
        (byte)Math.Clamp(behind + ((255 - behind) * light / 255.0 * amount), 0, 255);

    private static (byte R, byte G, byte B) Hsv(double hue, double saturation, double value)
    {
        hue = ((hue % 360) + 360) % 360;
        var chroma = value * saturation;
        var x = chroma * (1 - Math.Abs(((hue / 60) % 2) - 1));
        var (r, g, b) = hue switch
        {
            < 60 => (chroma, x, 0.0),
            < 120 => (x, chroma, 0.0),
            < 180 => (0.0, chroma, x),
            < 240 => (0.0, x, chroma),
            < 300 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x),
        };
        var match = value - chroma;
        return ((byte)((r + match) * 255), (byte)((g + match) * 255), (byte)((b + match) * 255));
    }
}

/// <summary>User-tunable controls for sound-derived artwork.</summary>
public sealed record AudioArtworkOptions(
    ArtworkStyle Style = ArtworkStyle.Prism,
    int Color = 100,
    int Detail = 100,
    int Contrast = 100,
    bool UseIdentity = true)
{
    internal AudioArtworkOptions Normalize() => this with
    {
        Style = Enum.IsDefined(Style) ? Style : ArtworkStyle.Prism,
        Color = Math.Clamp(Color, 0, 200),
        Detail = Math.Clamp(Detail, 0, 200),
        Contrast = Math.Clamp(Contrast, 0, 200),
    };
}
