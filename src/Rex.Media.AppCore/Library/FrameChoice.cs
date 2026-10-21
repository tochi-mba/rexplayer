using Rex.Media.AppCore.Player;
using Rex.Media.Primitives;
using Rex.Media.Video;

namespace Rex.Media.AppCore.Library;

/// <summary>
/// Which frame of a video stands for it on a card (LIB-05). Not the first frame, which is so often
/// black, and not the opening titles, which every episode of a show shares: a moment a third of
/// the way through what lies between the intro and the credits, with two more to try when that one
/// is black, blown out or one flat colour. A frame is judged by the same picture statistics the
/// credits detection reads (<see cref="CreditFrameEvidence.Measure"/>). A poster is cut from the
/// frame at a poster's shape, around its most detailed part.
/// </summary>
public static class FrameChoice
{
    /// <summary>A poster's width over its height.</summary>
    public const double PosterAspect = 2.0 / 3.0;

    /// <summary>The opening skipped when the intro is not known: a tenth of the video, at most two minutes.</summary>
    private static readonly TimeSpan LongestOpening = TimeSpan.FromMinutes(2);

    /// <summary>Videos shorter than this have no intro to skip; their middle stands for them.</summary>
    private static readonly TimeSpan Short = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The moments to try, best first. <paramref name="introEnd"/> and <paramref name="creditsStart"/>
    /// come from the episode's sections when they are known; without them a tenth of the video (at most
    /// two minutes) is taken for the opening and its last tenth for the credits.
    /// </summary>
    public static IReadOnlyList<TimeSpan> Candidates(TimeSpan? duration, TimeSpan? introEnd = null, TimeSpan? creditsStart = null)
    {
        if (duration is not { } length || length <= TimeSpan.Zero)
        {
            return [TimeSpan.Zero];
        }

        if (length < Short)
        {
            return [length / 2];
        }

        var from = introEnd is { } intro && intro > TimeSpan.Zero && intro < length ? intro : Min(length / 10, LongestOpening);
        var to = creditsStart is { } credits && credits > from && credits <= length ? credits : length * 0.9;
        if (to <= from)
        {
            (from, to) = (length / 10, length * 0.9);
        }

        var span = to - from;
        return [from + (span * 0.3), from + (span * 0.15), from + (span * 0.5)];
    }

    /// <summary>Whether a frame is worth showing: neither black, nor blown out, nor one flat colour.</summary>
    public static bool Strong(CreditFrameSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        return sample.MeanLuma >= 0.1 && sample.DarkFraction <= 0.85 && sample.BrightFraction <= 0.9 && Contrast(sample) >= 0.04;
    }

    /// <summary>How much a frame shows, for picking the best of frames none of which is <see cref="Strong"/>.</summary>
    public static double Score(CreditFrameSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        return ((1 - sample.DarkFraction) * (1 - sample.BrightFraction)) + Contrast(sample);
    }

    /// <summary>
    /// How much detail each of <paramref name="bands"/> upright strips of <paramref name="area"/> holds
    /// (BGRA): the summed change in brightness between neighbouring pixels, sampled every few lines.
    /// </summary>
    public static double[] ColumnDetail(VideoFrame frame, PictureArea area, int bands)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentOutOfRangeException.ThrowIfLessThan(bands, 1);
        if (frame.Format != PixelFormat.Bgra32)
        {
            throw new ArgumentException("The picture must be BGRA.", nameof(frame));
        }

        var detail = new double[bands];
        for (var y = area.Y; y < area.Y + area.Height; y += 4)
        {
            var row = frame.Row(0, y);
            var last = Luma(row, area.X);
            for (var x = area.X + 1; x < area.X + area.Width; x++)
            {
                var luma = Luma(row, x);
                detail[Math.Min(bands - 1, (x - area.X) * bands / area.Width)] += Math.Abs(luma - last);
                last = luma;
            }
        }

        return detail;
    }

    /// <summary>
    /// The part of <paramref name="area"/> a poster is cut from: as tall as the picture and two thirds
    /// as wide as it is tall, placed over the run of <paramref name="detail"/> strips holding the most
    /// detail (the left-most run on a tie). A picture already narrower than a poster is kept whole.
    /// </summary>
    public static PictureArea PosterArea(PictureArea area, IReadOnlyList<double> detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        var width = (int)Math.Round(area.Height * PosterAspect);
        if (width >= area.Width || detail.Count == 0)
        {
            return area;
        }

        var run = Math.Max(1, (int)Math.Round(detail.Count * (double)width / area.Width));
        var best = 0;
        var bestSum = double.MinValue;
        for (var start = 0; start + run <= detail.Count; start++)
        {
            var sum = 0.0;
            for (var band = start; band < start + run; band++)
            {
                sum += detail[band];
            }

            if (sum > bestSum)
            {
                (best, bestSum) = (start, sum);
            }
        }

        var centre = area.X + ((best + (run / 2.0)) * area.Width / detail.Count);
        var left = (int)Math.Clamp(Math.Round(centre - (width / 2.0)), area.X, area.X + area.Width - width);
        return new PictureArea(left, area.Y, width, area.Height);
    }

    private static double Contrast(CreditFrameSample sample) => sample.Rows.Count == 0 ? 0 : sample.Rows.Max() - sample.Rows.Min();

    private static double Luma(Span<byte> row, int x) => (0.0722 * row[4 * x]) + (0.7152 * row[(4 * x) + 1]) + (0.2126 * row[(4 * x) + 2]);

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
}
