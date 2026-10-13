using Rex.Media.Primitives;

namespace Rex.Media.TestKit;

/// <summary>
/// Synthetic pictures for choosing a subject from a click: textured objects of known extent on a
/// flat or a textured background, so a test can say exactly which box the click should give.
/// Every pixel is a pure function of its position, so the pictures are the same on every run.
/// </summary>
public static class SubjectScenes
{
    /// <summary>A rectangular object of one base colour with a checked, noisy surface.</summary>
    public readonly record struct Thing(int Left, int Top, int Width, int Height, int Blue, int Green, int Red, int Period = 6);

    /// <summary>A BGRA picture of <paramref name="things"/> over a background; later things cover earlier ones.</summary>
    public static VideoFrame Draw(int width, int height, bool texturedBackground, params Thing[] things)
    {
        ArgumentNullException.ThrowIfNull(things);
        var frame = VideoFrame.Rent(PixelFormat.Bgra32, width, height);
        frame.Pts = MediaTime.FromSeconds(1);
        for (var y = 0; y < height; y++)
        {
            var row = frame.Row(0, y);
            for (var x = 0; x < width; x++)
            {
                var (b, g, r) = texturedBackground ? Foliage(x, y) : Wall(x, y);
                foreach (var thing in things)
                {
                    if (x >= thing.Left && x < thing.Left + thing.Width && y >= thing.Top && y < thing.Top + thing.Height)
                    {
                        (b, g, r) = Surface(thing, x - thing.Left, y - thing.Top);
                    }
                }

                row[x * 4] = Clamp(b);
                row[x * 4 + 1] = Clamp(g);
                row[x * 4 + 2] = Clamp(r);
                row[x * 4 + 3] = 255;
            }
        }

        return frame;
    }

    /// <summary>Intersection over union of two boxes, each as left, top, width and height.</summary>
    public static double Overlap((double Left, double Top, double Width, double Height) a, (double Left, double Top, double Width, double Height) b)
    {
        var width = Math.Max(0, Math.Min(a.Left + a.Width, b.Left + b.Width) - Math.Max(a.Left, b.Left));
        var height = Math.Max(0, Math.Min(a.Top + a.Height, b.Top + b.Height) - Math.Max(a.Top, b.Top));
        var shared = width * height;
        return shared / (a.Width * a.Height + b.Width * b.Height - shared);
    }

    private static (int B, int G, int R) Wall(int x, int y)
    {
        var grain = Noise(x, y, 1) / 40;
        return (84 + grain, 78 + grain, 70 + grain);
    }

    private static (int B, int G, int R) Foliage(int x, int y)
    {
        var leaves = (int)(30 * Math.Sin(x / 7.0) * Math.Cos(y / 9.0) + 18 * Math.Sin((x + 2 * y) / 13.0)) + Noise(x, y, 2) / 6;
        return (55 + leaves / 2, 105 + leaves, 60 + leaves / 2);
    }

    private static (int B, int G, int R) Surface(Thing thing, int x, int y)
    {
        var check = ((x / thing.Period + y / thing.Period) % 2 == 0 ? 26 : -26) + Noise(x, y, 3) / 8;
        return (thing.Blue + check, thing.Green + check, thing.Red + check);
    }

    private static int Noise(int x, int y, int seed)
    {
        var h = unchecked((uint)(x * 374761393 + y * 668265263 + seed * 1442695041));
        h = unchecked((h ^ (h >> 13)) * 1274126177);
        return (int)((h ^ (h >> 16)) & 0xFF) - 128;
    }

    private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);
}
