namespace Rex.Media.AppCore.Player;

/// <summary>Matched audio in two episodes, each expressed on its original timeline.</summary>
public sealed record LandmarkMatch(TimeSpan FirstStart, TimeSpan FirstEnd, TimeSpan SecondStart,
    TimeSpan SecondEnd, int Anchors, double Confidence);

/// <summary>
/// Matches spectral landmark pairs by offset voting (Wang, "An Industrial-Strength Audio Search
/// Algorithm", ISMIR 2003, section 2.3: matching hashes at a common time offset form a diagonal
/// line in the scatterplot) and rejects isolated or repetitive hits. A matching run must be
/// continuous, not merely share a repeated tone or sound effect.
/// </summary>
public static class LandmarkMatcher
{
    /// <summary>
    /// The longest silence or masking inside one matched run: about three seconds of 32 ms hops, so
    /// a title theme under dialogue, whose landmarks the speech partly hides, stays one run.
    /// </summary>
    public const int MaximumGap = 96;

    /// <summary>A hash found more often than this in either window is too common to vote.</summary>
    public const int MaximumRepeats = 6;

    /// <summary>
    /// A run needs this many matching anchors at this density: identical audio matched at about
    /// 16 anchors a second on real episodes, chance alignments at a handful in under a second.
    /// </summary>
    private const int MinimumAnchors = 16;

    private const double MinimumDensity = 2;
    private const int MinimumFrames = 64;

    /// <summary>How many of the strongest offsets are examined for runs.</summary>
    private const int Candidates = 16;

    /// <summary>The strongest shared run, or null.</summary>
    public static LandmarkMatch? Match(IReadOnlyList<AudioLandmark> first, IReadOnlyList<AudioLandmark> second) =>
        MatchAll(first, second, 1) is [var best, ..] ? best : null;

    /// <summary>
    /// Up to <paramref name="limit"/> shared runs, strongest first, none overlapping another in
    /// either episode. Two episodes can share more than one thing (a recap of the previous episode
    /// and the title sequence), each at its own offset, so the best offset alone is not enough.
    /// </summary>
    public static IReadOnlyList<LandmarkMatch> MatchAll(IReadOnlyList<AudioLandmark> first,
        IReadOnlyList<AudioLandmark> second, int limit = 3)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        if (first.Count < MinimumAnchors || second.Count < MinimumAnchors)
        {
            return [];
        }

        // A hash that recurs all through a window (hum, a held note, room tone) votes for every
        // offset at once and buries real matches; like stop words in text retrieval, it is dropped.
        // On real episodes this took the noise floor from about 70 votes per offset to about 2.
        var common = first.GroupBy(hit => hit.Hash).Where(group => group.Count() > MaximumRepeats)
            .Select(group => group.Key).ToHashSet();
        var lookup = second.GroupBy(hit => hit.Hash)
            .Where(group => group.Count() <= MaximumRepeats && !common.Contains(group.Key))
            .ToDictionary(group => group.Key, group => group.Select(hit => hit.Frame).Distinct().ToArray());
        var lowest = second.Min(hit => hit.Frame) - first.Max(hit => hit.Frame);
        var highest = second.Max(hit => hit.Frame) - first.Min(hit => hit.Frame);
        // One vote per matching hash pair at its offset, in a flat histogram (section 2.3).
        var votes = new int[highest - lowest + 3];
        foreach (var hit in first)
        {
            if (lookup.TryGetValue(hit.Hash, out var frames))
            {
                foreach (var frame in frames)
                {
                    votes[frame - hit.Frame - lowest + 1]++;
                }
            }
        }

        // The same sound in two files lands on neighbouring 32 ms hops as often as on the same one,
        // so each offset also counts the votes of the offsets either side of it.
        var peaks = new List<(int Offset, int Votes)>();
        for (var index = 1; index < votes.Length - 1; index++)
        {
            var near = votes[index - 1] + votes[index] + votes[index + 1];
            if (votes[index] >= MinimumAnchors / 4 && near >= MinimumAnchors)
            {
                peaks.Add((index - 1 + lowest, near));
            }
        }

        var chosen = new List<int>();
        foreach (var (offset, _) in peaks.OrderByDescending(peak => peak.Votes).ThenBy(peak => peak.Offset))
        {
            if (chosen.Count < Candidates && !chosen.Any(other => Math.Abs(other - offset) <= 2))
            {
                chosen.Add(offset);
            }
        }

        var anchors = chosen.ToDictionary(offset => offset, _ => new HashSet<int>());
        foreach (var hit in first)
        {
            if (!lookup.TryGetValue(hit.Hash, out var frames))
            {
                continue;
            }

            foreach (var frame in frames)
            {
                var offset = frame - hit.Frame;
                for (var near = offset - 1; near <= offset + 1; near++)
                {
                    if (anchors.TryGetValue(near, out var set))
                    {
                        set.Add(hit.Frame);
                    }
                }
            }
        }

        var found = chosen.SelectMany(offset => Runs(anchors[offset], offset)).ToList();
        var kept = new List<LandmarkMatch>();
        foreach (var match in found.OrderByDescending(match => match.Anchors).ThenBy(match => match.FirstStart))
        {
            if (kept.Count < limit && !kept.Any(other =>
                    Overlaps(other.FirstStart, other.FirstEnd, match.FirstStart, match.FirstEnd) ||
                    Overlaps(other.SecondStart, other.SecondEnd, match.SecondStart, match.SecondEnd)))
            {
                kept.Add(match);
            }
        }

        return kept;
    }

    private static IEnumerable<LandmarkMatch> Runs(HashSet<int> anchors, int offset)
    {
        var frames = anchors.Order().ToArray();
        var seconds = AudioLandmarks.SecondsPerFrame;
        for (var begin = 0; begin < frames.Length;)
        {
            var end = begin + 1;
            while (end < frames.Length && frames[end] - frames[end - 1] <= MaximumGap)
            {
                end++;
            }

            var count = end - begin;
            var from = frames[begin];
            var to = frames[end - 1] + 1;
            if (count >= MinimumAnchors && to - from >= MinimumFrames && from + offset >= 0 &&
                count / ((to - from) * seconds) >= MinimumDensity)
            {
                yield return new LandmarkMatch(
                    TimeSpan.FromSeconds(from * seconds), TimeSpan.FromSeconds(to * seconds),
                    TimeSpan.FromSeconds((from + offset) * seconds),
                    TimeSpan.FromSeconds((to + offset) * seconds),
                    count, Math.Clamp(count / 120.0, 0, 1));
            }

            begin = end;
        }
    }

    private static bool Overlaps(TimeSpan fromA, TimeSpan toA, TimeSpan fromB, TimeSpan toB) =>
        fromA < toB && fromB < toA;
}
