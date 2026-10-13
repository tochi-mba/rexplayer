using Rex.Media.Primitives;

namespace Rex.Media.AppCore.Player;

/// <summary>
/// Cheap, non-OCR picture features for evaluating possible closing credits. <see cref="Rows"/> holds
/// <see cref="CreditFrameEvidence.Bands"/> horizontal bands, top to bottom, of mean luma in 0..1.
/// <see cref="Strokes"/> is how often a light pixel borders a dark one along a line: thin letter
/// strokes give a high value, a lamp or a lit window a low one.
/// </summary>
public sealed record CreditFrameSample(TimeSpan At, double MeanLuma, double DarkFraction,
    double BrightFraction, IReadOnlyList<float> Rows, double Strokes = 0);

/// <summary>
/// A run of closing-credit pictures. <see cref="ReachesEnd"/> is true when nothing but a short card
/// or logo follows it, so <see cref="End"/> is the end of the file; otherwise a scene follows.
/// </summary>
public sealed record CreditRun(TimeSpan Start, TimeSpan End, double Confidence, bool ReachesEnd);

/// <summary>
/// Closing credits from picture statistics alone, independent of language, font and the GPU.
/// <para>
/// Credits are light text on a dark ground: most of the picture is dark, a small part is bright
/// (text lines), and the lines either hold still as cards or move vertically together. These are
/// the classic artificial-text cues of Lienhart and Wernicke, "Localizing and segmenting text in
/// images and videos" (IEEE TCSVT 12(4), 2002): high contrast, horizontal alignment and temporal
/// redundancy. Monochrome frames separate blocks of material (Lienhart, Kuhmünch and Effelsberg,
/// "On the detection and recognition of television commercials", IEEE ICMCS 1997), so a black frame
/// neither starts nor breaks a run. Scrolling is a vertical translation of the row profile between
/// samples about a second apart.
/// </para>
/// <para>
/// Sampling is sparse: the analysis decodes keyframes first, because encoders place keyframes at
/// scene cuts (where credits usually begin), then decodes one picture a second only inside the gaps
/// where the evidence changes (<see cref="RefinementWindows"/>). Text on black far from the end of
/// the file loses confidence, so a gag-credits scene or a title card before the last act is not
/// offered as the closing credits.
/// </para>
/// </summary>
public static class CreditFrameEvidence
{
    /// <summary>The number of horizontal bands in a row profile.</summary>
    public const int Bands = 96;

    /// <summary>Two credit pictures further apart than this belong to different runs.</summary>
    public static readonly TimeSpan MaximumGap = TimeSpan.FromSeconds(16);

    /// <summary>
    /// Whatever follows the credits for at most this long (a production logo, a copyright card) is
    /// part of them; anything longer is a scene after the credits and is never skipped.
    /// </summary>
    public static readonly TimeSpan ShortTail = EpisodeSkipPrompt.SceneAfterCreditsThreshold;

    /// <summary>Samples closer than this can be compared for scrolling.</summary>
    private static readonly TimeSpan ScrollSpacing = TimeSpan.FromSeconds(1.6);

    public static CreditFrameSample Measure(VideoFrame frame, TimeSpan at)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Surface is not null)
        {
            throw new NotSupportedException("Credit statistics require a decoded CPU picture.");
        }

        var sums = new double[Bands];
        var counts = new int[Bands];
        var count = 0;
        var dark = 0;
        var bright = 0;
        var sum = 0.0;
        var light = 0;
        var edges = 0;
        for (var y = 0; y < frame.Height; y += 4)
        {
            var line = frame.Row(0, y);
            var band = Math.Min(Bands - 1, y * Bands / frame.Height);
            var wasLight = false;
            for (var x = 0; x < frame.Width; x++)
            {
                // Malformed 10-bit pixels can contain bits beyond the nominal range.
                // Keep all luma statistics normalized even for a damaged decoded frame.
                var luma = Math.Clamp(Luma(frame.Format, line, x), 0, 255);
                var isLight = luma >= 140;
                light += isLight ? 1 : 0;
                edges += x > 0 && isLight != wasLight ? 1 : 0;
                wasLight = isLight;
                if (x % 4 != 0)
                {
                    continue;
                }

                sum += luma;
                sums[band] += luma;
                counts[band]++;
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
        }

        var rows = new float[Bands];
        for (var band = 0; band < Bands; band++)
        {
            rows[band] = counts[band] == 0 ? 0 : (float)(sums[band] / counts[band] / 255);
        }

        return new CreditFrameSample(at, sum / count / 255, dark / (double)count,
            bright / (double)count, rows, light == 0 ? 0 : Math.Min(1, edges / (double)light));
    }

    private static double Luma(PixelFormat format, Span<byte> line, int x) => format switch
    {
        PixelFormat.Bgra32 => (0.0722 * line[4 * x]) + (0.7152 * line[4 * x + 1]) + (0.2126 * line[4 * x + 2]),
        PixelFormat.P010 => (line[2 * x + 1] << 2 | line[2 * x] >> 6) / 1023.0 * 255,
        PixelFormat.I420P10 => (line[2 * x + 1] << 8 | line[2 * x]) / 1023.0 * 255,
        _ => line[x],
    };

    /// <summary>
    /// Light text on a dark ground: mostly dark, with a little bright detail made of thin strokes.
    /// On 18 annotated episodes every credit picture had a stroke density of at least 0.04; a lamp
    /// or a lit window in a dark scene is one broad bright area and scores far lower. Very small
    /// type on black lights less than 0.4 % of the picture, so there a high stroke density stands
    /// in for the missing brightness.
    /// </summary>
    public static bool LooksLikeText(CreditFrameSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        return sample.DarkFraction >= 0.55 && sample.BrightFraction <= 0.34 && sample.Strokes >= 0.03 &&
            (sample.BrightFraction >= 0.004 || (sample.DarkFraction >= 0.9 && sample.BrightFraction >= 0.001 && sample.Strokes >= 0.3));
    }

    /// <summary>A black picture: a fade or the gap between two cards.</summary>
    public static bool IsBlack(CreditFrameSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        return sample.DarkFraction >= 0.97 && sample.BrightFraction < 0.004 && !LooksLikeText(sample);
    }

    /// <summary>Positive when similar text bands have moved vertically between two pictures.</summary>
    public static double Scroll(CreditFrameSample previous, CreditFrameSample current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        if (previous.Rows.Count != current.Rows.Count || previous.Rows.Count < 16)
        {
            return 0;
        }

        var reach = previous.Rows.Count / 8;
        var best = -1.0;
        var still = 0.0;
        for (var shift = -reach; shift <= reach; shift++)
        {
            var similarity = 0.0;
            var compared = 0;
            for (var y = reach; y < current.Rows.Count - reach; y++)
            {
                similarity += 1 - Math.Min(1, Math.Abs(previous.Rows[y] - current.Rows[y + shift]) * 4);
                compared++;
            }

            similarity /= compared;
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

    /// <summary>
    /// Spans to decode at one picture a second after a keyframe pass: every gap between two
    /// neighbouring samples where the evidence changes, so each boundary is found to within about a
    /// second, and the last seconds of the file, which decide whether a scene follows the credits.
    /// </summary>
    public static IReadOnlyList<(TimeSpan Start, TimeSpan End)> RefinementWindows(
        IReadOnlyList<CreditFrameSample> keyframes, TimeSpan from, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(keyframes);
        var ordered = keyframes.OrderBy(sample => sample.At).ToArray();
        var windows = new List<(TimeSpan Start, TimeSpan End)>();
        var previousAt = from;
        var previousText = false;
        foreach (var sample in ordered)
        {
            var text = LooksLikeText(sample);
            if (text != previousText && sample.At - previousAt > TimeSpan.FromSeconds(1.2))
            {
                windows.Add((previousAt, sample.At));
            }

            previousAt = sample.At;
            previousText = text;
        }

        var tail = duration - TimeSpan.FromSeconds(ShortTail.TotalSeconds + 2);
        if (tail > previousAt)
        {
            windows.Add((tail, duration));
        }
        else if (previousText && duration - previousAt > TimeSpan.FromSeconds(1.2))
        {
            windows.Add((previousAt, duration));
        }

        // The windows are disjoint and in order. At most three minutes of pictures are ever
        // decoded; when the evidence flickers more than that, the windows nearest the end win.
        var budget = TimeSpan.FromMinutes(3);
        var kept = new List<(TimeSpan Start, TimeSpan End)>();
        foreach (var window in windows.AsEnumerable().Reverse())
        {
            if (window.End - window.Start <= budget)
            {
                kept.Add(window);
                budget -= window.End - window.Start;
            }
        }

        kept.Reverse();
        return kept;
    }

    /// <summary>Every plausible closing-credit run in the last 40 % of the file, earliest first.</summary>
    public static IReadOnlyList<CreditRun> Runs(IReadOnlyList<CreditFrameSample> frames, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (duration < TimeSpan.FromMinutes(6))
        {
            return [];
        }

        var ordered = frames.Where(frame => frame.At >= duration * 0.6 && frame.At < duration)
            .OrderBy(frame => frame.At).ToArray();
        var credit = new bool[ordered.Length];
        for (var i = 0; i < ordered.Length; i++)
        {
            var frame = ordered[i];
            var scrolls = (i > 0 && frame.At - ordered[i - 1].At <= ScrollSpacing && Scroll(ordered[i - 1], frame) > 0) ||
                (i + 1 < ordered.Length && ordered[i + 1].At - frame.At <= ScrollSpacing && Scroll(frame, ordered[i + 1]) > 0);
            // Still cards are text on near-black (every credit picture measured was over 90 % dark;
            // a dark scene with a lamp was 83 %); text on a lighter ground counts while it scrolls.
            credit[i] = LooksLikeText(frame) && (frame.DarkFraction >= 0.9 || scrolls);
        }

        var runs = new List<CreditRun>();
        var index = 0;
        while (index < ordered.Length)
        {
            if (!credit[index])
            {
                index++;
                continue;
            }

            // A fade to black just before the first card is part of the credits, up to ten seconds.
            var first = index;
            while (first > 0 && IsBlack(ordered[first - 1]) && ordered[first].At - ordered[first - 1].At <= ScrollSpacing &&
                ordered[index].At - ordered[first - 1].At <= TimeSpan.FromSeconds(10))
            {
                first--;
            }

            var last = index;
            var cursor = index + 1;
            while (cursor < ordered.Length && ordered[cursor].At - ordered[cursor - 1].At <= MaximumGap &&
                (credit[cursor] || IsBlack(ordered[cursor])))
            {
                if (credit[cursor])
                {
                    last = cursor;
                }

                cursor++;
            }

            index = last + 1;
            if (Describe(ordered, credit, (first, last), ordered[cursor - 1].At, duration) is { } run)
            {
                runs.Add(run);
            }
        }

        return runs;
    }

    /// <summary>The run most likely to be the closing credits, or null.</summary>
    public static CreditRun? Find(IReadOnlyList<CreditFrameSample> frames, TimeSpan duration) =>
        Runs(frames, duration).OrderByDescending(run => run.Confidence).FirstOrDefault();

    /// <summary>Describe a detected credit run with its continuation and duration.</summary>
    /// <param name="ordered">The samples ordered by presentation time.</param>
    /// <param name="credit">Whether each sample carries credit-like evidence.</param>
    /// <param name="span">Indices of the proposed credit segment.</param>
    /// <param name="duration">Duration of the whole episode.</param>
    /// <param name="continued">The last picture of the run's text-or-black continuation: black after
    /// the last card (a card too faint to read, a fade) still counts towards reaching the end.</param>
    private static CreditRun? Describe(CreditFrameSample[] ordered, bool[] credit, (int First, int Last) span,
        TimeSpan continued, TimeSpan duration)
    {
        var (first, last) = span;
        var texts = Enumerable.Range(first, last - first + 1).Where(i => credit[i]).ToArray();
        // Boundaries sit halfway to the neighbouring sample, at most a second away: the refinement
        // pass leaves neighbours about a second apart, so the error is about half a second.
        var before = first > 0 ? ordered[first].At - ordered[first - 1].At : TimeSpan.FromSeconds(1);
        var after = last + 1 < ordered.Length ? ordered[last + 1].At - ordered[last].At : TimeSpan.FromSeconds(1);
        var start = ordered[first].At - TimeSpan.FromTicks(Math.Min(before.Ticks, TimeSpan.TicksPerSecond) / 2);
        var end = ordered[last].At + TimeSpan.FromTicks(Math.Min(after.Ticks, TimeSpan.TicksPerSecond) / 2);
        if (texts.Length < 3 || end - start < TimeSpan.FromSeconds(6))
        {
            return null;
        }

        var tail = duration - end;
        var reachesEnd = duration - (continued > end ? continued : end) <= ShortTail;
        var darkness = texts.Average(i => ordered[i].DarkFraction);
        var evidence = Math.Clamp((darkness - 0.55) / 0.45, 0, 1);
        var length = Math.Min(1, (end - start).TotalSeconds / 40);
        // Closing credits end the file or are followed by a scene of up to a minute; text on black
        // two minutes before the end is a title card or a gag, never offered on pictures alone.
        var position = tail.TotalSeconds <= 60 ? 1 : Math.Max(0.6, 1 - (tail.TotalSeconds - 60) / 200);
        var confidence = Math.Min(0.9, 0.45 + 0.25 * evidence + 0.2 * length) * position;
        return new CreditRun(start, reachesEnd ? duration : end, Math.Round(confidence, 3), reachesEnd);
    }
}
