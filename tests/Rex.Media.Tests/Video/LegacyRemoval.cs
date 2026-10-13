using Rex.Media.Primitives;

namespace Rex.Media.Tests.Video;

/// <summary>
/// The removal preview as it was before it was rebuilt, kept line for line as the baseline the new
/// one is measured against: a rectangle softened by comparing each pixel with the box's centre pixel
/// and a four-point border average, filled from pixels seen outside the box while the camera stays
/// still, and otherwise interpolated from the box's four edges.
/// </summary>
internal sealed class LegacyRemoval
{
    private byte[]? _clean;
    private byte[]? _known;

    public int Feather { get; init; } = 3;

    public int Tolerance { get; init; } = 50;

    /// <summary>The last preview's opacity per pixel, for scoring the mask.</summary>
    public float[] Alpha { get; private set; } = [];

    public VideoFrame Process(VideoFrame frame, (int Left, int Top, int Right, int Bottom) box)
    {
        var pixels = frame.Width * frame.Height;
        if (Alpha.Length != pixels)
        {
            Alpha = new float[pixels];
        }

        Array.Clear(Alpha);
        if (_clean is null || _known is null)
        {
            _clean = new byte[pixels * 4];
            _known = new byte[pixels];
        }
        else if (CameraMoved(frame, _clean, _known))
        {
            Array.Clear(_known);
        }

        for (var y = 0; y < frame.Height; y++)
        {
            var row = frame.Row(0, y);
            var rowOffset = y * frame.Width;
            if (y < box.Top || y >= box.Bottom)
            {
                row.CopyTo(_clean.AsSpan(rowOffset * 4, frame.Width * 4));
                _known.AsSpan(rowOffset, frame.Width).Fill(1);
            }
            else
            {
                row[..(box.Left * 4)].CopyTo(_clean.AsSpan(rowOffset * 4));
                row[(box.Right * 4)..].CopyTo(_clean.AsSpan((rowOffset + box.Right) * 4));
                _known.AsSpan(rowOffset, box.Left).Fill(1);
                _known.AsSpan(rowOffset + box.Right, frame.Width - box.Right).Fill(1);
            }
        }

        var output = VideoFrame.Rent(PixelFormat.Bgra32, frame.Width, frame.Height);
        frame.Plane(0).CopyTo(output.Plane(0));
        var (foreground, background, difference) = CompareSubjectWithBorder(frame, box);
        for (var y = box.Top; y < box.Bottom; y++)
        {
            var row = output.Row(0, y);
            for (var x = box.Left; x < box.Right; x++)
            {
                var at = y * frame.Width + x;
                var p = x * 4;
                var edge = Math.Min(Math.Min(x - box.Left, box.Right - x - 1), Math.Min(y - box.Top, box.Bottom - y - 1));
                var alpha = Feather == 0 ? 1.0 : Math.Min(1.0, (edge + 1) / (double)Feather);
                if (difference > 60)
                {
                    var foregroundDistance = ColourDistance(row, p, foreground);
                    var backgroundDistance = ColourDistance(row, p, background);
                    alpha *= Math.Clamp((backgroundDistance - foregroundDistance + Tolerance) / 100.0, 0, 1);
                }

                Alpha[at] = (float)alpha;
                if (alpha < 0.01)
                {
                    continue;
                }

                var observed = _known[at] != 0;
                for (var c = 0; c < 3; c++)
                {
                    var restored = observed ? _clean[at * 4 + c] : EstimateFromBorders(frame, box, x, y, c);
                    row[p + c] = (byte)Math.Clamp(Math.Round(row[p + c] * (1 - alpha) + restored * alpha), 0, 255);
                }
            }
        }

        return output;
    }

    private static ((int B, int G, int R) Foreground, (int B, int G, int R) Background, int Difference)
        CompareSubjectWithBorder(VideoFrame frame, (int Left, int Top, int Right, int Bottom) box)
    {
        var cx = (box.Left + box.Right) / 2;
        var cy = (box.Top + box.Bottom) / 2;
        var center = frame.Row(0, Math.Min(frame.Height - 1, cy));
        var foreground = (B: (int)center[Math.Min(frame.Width - 1, cx) * 4],
            G: (int)center[Math.Min(frame.Width - 1, cx) * 4 + 1],
            R: (int)center[Math.Min(frame.Width - 1, cx) * 4 + 2]);
        int b = 0, g = 0, r = 0, count = 0;
        void Add(int x, int y)
        {
            if ((uint)x >= (uint)frame.Width || (uint)y >= (uint)frame.Height)
            {
                return;
            }

            var row = frame.Row(0, y);
            b += row[x * 4];
            g += row[x * 4 + 1];
            r += row[x * 4 + 2];
            count++;
        }

        Add(box.Left - 1, cy);
        Add(box.Right, cy);
        Add(cx, box.Top - 1);
        Add(cx, box.Bottom);
        var background = count > 0 ? (B: b / count, G: g / count, R: r / count) : foreground;
        var difference = Math.Abs(foreground.B - background.B) + Math.Abs(foreground.G - background.G)
            + Math.Abs(foreground.R - background.R);
        return (foreground, background, difference);
    }

    private static int ColourDistance(ReadOnlySpan<byte> row, int index, (int B, int G, int R) colour) =>
        Math.Abs(row[index] - colour.B) + Math.Abs(row[index + 1] - colour.G) + Math.Abs(row[index + 2] - colour.R);

    private static double EstimateFromBorders(VideoFrame frame, (int Left, int Top, int Right, int Bottom) box, int x, int y, int channel)
    {
        double total = 0;
        double weight = 0;
        var width = box.Right - box.Left + 1;
        var height = box.Bottom - box.Top + 1;
        if (box.Left > 0)
        {
            var w = (double)(box.Right - x) / width;
            total += w * frame.Row(0, y)[(box.Left - 1) * 4 + channel];
            weight += w;
        }

        if (box.Right < frame.Width)
        {
            var w = (double)(x - box.Left + 1) / width;
            total += w * frame.Row(0, y)[box.Right * 4 + channel];
            weight += w;
        }

        if (box.Top > 0)
        {
            var w = (double)(box.Bottom - y) / height;
            total += w * frame.Row(0, box.Top - 1)[x * 4 + channel];
            weight += w;
        }

        if (box.Bottom < frame.Height)
        {
            var w = (double)(y - box.Top + 1) / height;
            total += w * frame.Row(0, box.Bottom)[x * 4 + channel];
            weight += w;
        }

        return weight > 0 ? total / weight : frame.Row(0, y)[x * 4 + channel];
    }

    private static bool CameraMoved(VideoFrame frame, byte[] history, byte[] seen)
    {
        var compared = 0;
        var changed = 0;
        for (var y = 0; y < frame.Height; y += Math.Max(1, frame.Height / 16))
        {
            var row = frame.Row(0, y);
            for (var x = 0; x < frame.Width; x += Math.Max(1, frame.Width / 20))
            {
                var at = y * frame.Width + x;
                if (seen[at] == 0)
                {
                    continue;
                }

                compared++;
                var p = x * 4;
                if (Math.Abs(row[p] - history[at * 4]) + Math.Abs(row[p + 1] - history[at * 4 + 1])
                    + Math.Abs(row[p + 2] - history[at * 4 + 2]) > 135)
                {
                    changed++;
                }
            }
        }

        return compared >= 32 && changed * 5 > compared;
    }
}
