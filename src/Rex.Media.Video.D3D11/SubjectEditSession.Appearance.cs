using Rex.Media.Primitives;

namespace Rex.Media.Video.D3D11;

public sealed partial class SubjectEditSession
{
    // Coarse spatial colour and colour distributions survive small deformations that
    // destroy point-for-point RGB agreement. Distribution evidence is necessary:
    // averaging alone would confuse a patterned subject with a flat grey wall.
    private sealed class Appearance(float[] template)
    {
        public float[] Values { get; } = Describe(template, Samples);
    }

    private Appearance? _returnAppearance;

    private static float[] Describe(ReadOnlySpan<float> samples, int side)
    {
        var result = new float[96];
        DescribeInto(samples, side, result);
        return result;
    }

    private static void DescribeInto(ReadOnlySpan<float> samples, int side, Span<float> result)
    {
        result.Clear();
        for (var y = 0; y < side; y++)
        {
            for (var x = 0; x < side; x++)
            {
                var cell = ((y * 4 / side) * 4 + x * 4 / side) * 3;
                for (var c = 0; c < 3; c++)
                {
                    var value = samples[(y * side + x) * 3 + c];
                    result[cell + c] += value * 16 / (side * side);
                    // Soft bins avoid discontinuities when exposure crosses a boundary.
                    var bin = Math.Clamp(value / 17, 0, 15);
                    var low = (int)bin;
                    var fraction = bin - low;
                    result[48 + c * 16 + low] += (1 - fraction) / (side * side);
                    result[48 + c * 16 + Math.Min(15, low + 1)] += fraction / (side * side);
                }
            }
        }
    }

    private static double AppearanceDistance(VideoFrame frame, int left, int top, int width,
        int height, Appearance reference, bool sparse = false)
    {
        var side = sparse ? 8 : Samples;
        Span<int> values = stackalloc int[96];
        values.Clear();
        Span<int> columns = stackalloc int[Samples];
        for (var x = 0; x < side; x++)
            columns[x] = Math.Clamp(left + (int)((x + 0.5f) * width / side), 0, frame.Width - 1) * 4;
        for (var y = 0; y < side; y++)
        {
            var row = frame.Row(0, Math.Clamp(top + (int)((y + 0.5f) * height / side), 0, frame.Height - 1));
            for (var x = 0; x < side; x++)
            {
                var cell = ((y * 4 / side) * 4 + x * 4 / side) * 3;
                var pixel = columns[x];
                for (var c = 0; c < 3; c++)
                {
                    var value = row[pixel + c];
                    values[cell + c] += value;
                    var low = value / 17;
                    var remainder = value - low * 17;
                    values[48 + c * 16 + low] += 17 - remainder;
                    values[48 + c * 16 + Math.Min(15, low + 1)] += remainder;
                }
            }
        }

        // Accumulate integer pixels once, then normalize the small descriptor. The
        // former path sampled, copied, and divided every component several times.
        double spatial = 0, distribution = 0;
        var cellScale = 16.0 / (side * side);
        var histogramScale = 1.0 / (17 * side * side);
        for (var i = 0; i < 48; i++) spatial += Math.Abs(values[i] * cellScale - reference.Values[i]);
        for (var i = 48; i < 96; i++) distribution += Math.Abs(values[i] * histogramScale - reference.Values[i]);
        return spatial / 48 * 0.55 + distribution / 3 * 35;
    }
}
