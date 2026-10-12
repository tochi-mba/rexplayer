namespace Rex.Media.AppCore.Player;

/// <summary>Matched audio in two episodes, each expressed on its original timeline.</summary>
public sealed record LandmarkMatch(TimeSpan FirstStart, TimeSpan FirstEnd, TimeSpan SecondStart,
    TimeSpan SecondEnd, int Anchors, double Confidence);

/// <summary>
/// Matches spectral landmark pairs by offset voting and rejects isolated or repetitive hits.
/// A matching run must be continuous, not merely share a repeated tone or sound effect.
/// </summary>
public static class LandmarkMatcher
{
    public static LandmarkMatch? Match(IReadOnlyList<AudioLandmark> first, IReadOnlyList<AudioLandmark> second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        if (first.Count < 8 || second.Count < 8)
        {
            return null;
        }

        var lookup = second.GroupBy(hit => hit.Hash).ToDictionary(
            group => group.Key, group => group.Select(hit => hit.Frame).Distinct().Take(32).ToArray());
        var offsets = new Dictionary<int, HashSet<int>>();
        foreach (var hit in first)
        {
            if (!lookup.TryGetValue(hit.Hash, out var frames))
            {
                continue;
            }

            foreach (var frame in frames)
            {
                var offset = frame - hit.Frame;
                if (!offsets.TryGetValue(offset, out var anchors))
                {
                    anchors = [];
                    offsets.Add(offset, anchors);
                }

                anchors.Add(hit.Frame);
            }
        }

        if (offsets.Count == 0)
        {
            return null;
        }

        var best = offsets.OrderByDescending(pair => pair.Value.Count).First();
        if (best.Value.Count < 8)
        {
            return null;
        }

        var framesSorted = best.Value.Order().ToArray();
        var bestStart = 0;
        var bestCount = 0;
        for (var begin = 0; begin < framesSorted.Length;)
        {
            var end = begin + 1;
            while (end < framesSorted.Length && framesSorted[end] - framesSorted[end - 1] <= 48)
            {
                end++;
            }

            if (end - begin > bestCount)
            {
                bestStart = begin;
                bestCount = end - begin;
            }

            begin = end;
        }

        if (bestCount < 8)
        {
            return null;
        }

        var from = framesSorted[bestStart];
        var to = framesSorted[bestStart + bestCount - 1] + 1;
        if (to - from < 64 || from + best.Key < 0)
        {
            return null;
        }

        var seconds = AudioLandmarks.SecondsPerFrame;
        return new LandmarkMatch(
            TimeSpan.FromSeconds(from * seconds), TimeSpan.FromSeconds(to * seconds),
            TimeSpan.FromSeconds((from + best.Key) * seconds),
            TimeSpan.FromSeconds((to + best.Key) * seconds),
            bestCount, Math.Clamp(bestCount / 45.0, 0, 1));
    }
}
