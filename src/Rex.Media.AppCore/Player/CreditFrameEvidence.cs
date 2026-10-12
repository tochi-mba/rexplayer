using Rex.Media.Primitives;

namespace Rex.Media.AppCore.Player;

/// <summary>Cheap, non-OCR picture features for evaluating possible closing credits.</summary>
public sealed record CreditFrameSample(TimeSpan At, double MeanLuma, double DarkFraction,
    double BrightFraction, IReadOnlyList<float> Rows);

/// <summary>
/// Samples every sixteenth pixel of a CPU-decoded frame and checks row-profile translation.
/// Does not depend on language, font, GPU resources, or screen capture.
/// </summary>
public static class CreditFrameEvidence
{
    public static CreditFrameSample Measure(VideoFrame frame, TimeSpan at)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Surface is not null)
        {
            throw new NotSupportedException("Credit statistics require a decoded CPU picture.");
        }

        var rows = new float[frame.Height];
        var count = 0;
        var dark = 0;
        var bright = 0;
        var sum = 0.0;
        for (var y = 0; y < frame.Height; y += 4)
        {
            var line = frame.Row(0, y);
            var rowSum = 0.0;
            var rowCount = 0;
            for (var x = 0; x < frame.Width; x += 4)
            {
                var value = frame.Format switch
                {
                    PixelFormat.Bgra32 => (0.0722 * line[4 * x]) + (0.7152 * line[4 * x + 1]) + (0.2126 * line[4 * x + 2]),
                    PixelFormat.P010 => (line[2 * x + 1] << 2 | line[2 * x] >> 6) / 1023.0 * 255,
                    PixelFormat.I420P10 => (line[2 * x + 1] << 8 | line[2 * x]) / 1023.0 * 255,
                    _ => line[x],
                };
                // Malformed 10-bit pixels can contain bits beyond the nominal range.
                // Keep all luma statistics normalized even for a damaged decoded frame.
                var luma = Math.Clamp(value, 0, 255);
                sum += luma;
                rowSum += luma;
                rowCount++;
                count++;
                if (luma <= 52)
                {
                    dark++;
                }

                if (luma >= 195)
                {
                    bright++;
                }
            }

            rows[y] = rowCount == 0 ? 0 : (float)(rowSum / rowCount / 255);
        }

        return new CreditFrameSample(at, sum / count / 255, dark / (double)count,
            bright / (double)count, rows);
    }

    /// <summary>Positive when similar text bands have moved vertically across successive frames.</summary>
    public static double Scroll(CreditFrameSample previous, CreditFrameSample current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        if (previous.Rows.Count != current.Rows.Count || previous.Rows.Count < 8)
        {
            return 0;
        }

        var best = -1.0;
        var still = 0.0;
        for (var shift = -8; shift <= 8; shift += 4)
        {
            var similarity = 0.0;
            var compared = 0;
            for (var y = 8; y < current.Rows.Count - 8; y += 4)
            {
                var a = previous.Rows[y];
                var b = current.Rows[y + shift];
                similarity += 1 - Math.Min(1, Math.Abs(a - b) * 4);
                compared++;
            }

            similarity /= Math.Max(1, compared);
            if (shift == 0)
            {
                still = similarity;
            }
            else
            {
                best = Math.Max(best, similarity);
            }
        }

        return best > still + 0.025 && best > 0.7 ? best : 0;
    }

    /// <summary>Returns the longest plausible ending-credit run, allowing a post-credit scene.</summary>
    public static (TimeSpan Start, TimeSpan End, double Confidence)? Find(
        IReadOnlyList<CreditFrameSample> frames, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (duration < TimeSpan.FromMinutes(6) || frames.Count < 8)
        {
            return null;
        }

        var ordered = frames.OrderBy(frame => frame.At).ToArray();
        var runs = new List<(int Start, int End, double Evidence)>();
        var begin = -1;
        var score = 0.0;
        for (var i = 0; i <= ordered.Length; i++)
        {
            var frame = i < ordered.Length ? ordered[i] : null;
            var late = frame is not null && frame.At >= duration * 0.6 && frame.At < duration;
            var looksLikeCredits = late && frame!.DarkFraction >= 0.55 &&
                frame.BrightFraction is >= 0.004 and <= 0.34 &&
                (i == 0 || CreditFrameEvidence.Scroll(ordered[i - 1], frame) > 0 ||
                    frame.DarkFraction > 0.77);
            if (looksLikeCredits)
            {
                if (begin < 0)
                {
                    begin = i;
                }

                score += frame!.DarkFraction;
            }
            else if (begin >= 0)
            {
                if (i - begin >= 6)
                {
                    runs.Add((begin, i - 1, score / (i - begin)));
                }

                begin = -1;
                score = 0;
            }
        }

        if (runs.Count == 0)
        {
            return null;
        }

        var run = runs.OrderByDescending(item => ordered[item.End].At - ordered[item.Start].At).First();
        var start = ordered[run.Start].At;
        var end = ordered[run.End].At + TimeSpan.FromSeconds(1);
        var tail = Math.Max(0, (duration - end).TotalSeconds);
        var confidence = Math.Clamp(0.38 + (run.Evidence - 0.55) * 0.75 + (end - start).TotalSeconds / 240.0 -
            tail / 500.0, 0, 0.88);
        return (start, end > duration ? duration : end, confidence);
    }
}
