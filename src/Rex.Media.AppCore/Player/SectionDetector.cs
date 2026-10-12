namespace Rex.Media.AppCore.Player;

/// <summary>A file's reusable features. Opening/closing hashes have absolute 32 ms frame offsets.</summary>
public sealed record EpisodeAnalysis(string Path, string Signature, TimeSpan Duration,
    IReadOnlyList<AudioLandmark> Opening, IReadOnlyList<AudioLandmark> Closing,
    IReadOnlyList<CreditFrameSample> Frames);

/// <summary>Combines independent across-episode audio agreement with late-video credits evidence.</summary>
public static class SectionDetector
{
    public static SectionProposals Detect(EpisodeAnalysis episode, IReadOnlyList<EpisodeAnalysis> siblings)
    {
        ArgumentNullException.ThrowIfNull(episode);
        ArgumentNullException.ThrowIfNull(siblings);
        if (episode.Duration < TimeSpan.FromMinutes(6) || EpisodeGrouping.Identify(episode.Path) is not { } identity)
        {
            return new SectionProposals(episode.Signature, []);
        }

        var related = siblings.Where(other => other.Path != episode.Path &&
            EpisodeGrouping.Identify(other.Path) is { } match &&
            match.Season == identity.Season && match.Show == identity.Show).ToArray();
        var result = new List<SectionCandidate>();
        var intros = new List<LandmarkMatch>();
        var endings = new List<LandmarkMatch>();
        foreach (var sibling in related)
        {
            if (LandmarkMatcher.Match(episode.Opening, sibling.Opening) is { } intro &&
                intro.FirstStart < episode.Duration / 2 &&
                intro.FirstEnd <= episode.Duration / 2 &&
                intro.FirstEnd - intro.FirstStart >= TimeSpan.FromSeconds(8))
            {
                intros.Add(intro);
            }

            if (LandmarkMatcher.Match(episode.Closing, sibling.Closing) is { } matchEnding &&
                matchEnding.FirstStart >= episode.Duration * 0.60 &&
                matchEnding.FirstEnd <= episode.Duration)
            {
                endings.Add(matchEnding);
            }
        }

        if (Consensus(intros) is { } opening)
        {
            result.Add(new SectionCandidate(EpisodeSectionKind.Intro, opening.Start, opening.End,
                opening.Confidence, $"Repeats across {opening.Support + 1} episodes of this season", "shared audio"));
        }

        var video = CreditFrameEvidence.Find(episode.Frames, episode.Duration);
        if (Consensus(endings) is { } ending)
        {
            var score = ending.Confidence;
            var end = ending.End;
            if (video is { } visual && Overlap(ending.Start, ending.End, visual.Start, visual.End) >= 0.45)
            {
                score = Math.Min(0.99, score + 0.08);
                // Preserve a post-credit scene; only refine the end inside the actual evidence.
                end = TimeSpan.FromTicks(Math.Min(ending.End.Ticks, visual.End.Ticks));
            }

            result.Add(new SectionCandidate(EpisodeSectionKind.Credits, ending.Start, end,
                score, $"Shared ending audio across {ending.Support + 1} episodes", "shared audio + frames"));
        }
        else if (video is { } visual && visual.Confidence >= SectionProposals.OfferThreshold)
        {
            result.Add(new SectionCandidate(EpisodeSectionKind.Credits, visual.Start, visual.End,
                visual.Confidence, "Late dark/bright scrolling-text pattern", "video frames"));
        }

        return new SectionProposals(episode.Signature, result);
    }

    private static (TimeSpan Start, TimeSpan End, double Confidence, int Support)? Consensus(IReadOnlyList<LandmarkMatch> matches)
    {
        if (matches.Count == 0)
        {
            return null;
        }

        var best = matches.OrderByDescending(match => match.Confidence).First();
        var agreeing = matches.Where(match => Overlap(match.FirstStart, match.FirstEnd,
            best.FirstStart, best.FirstEnd) >= 0.55).ToArray();
        var start = agreeing.Max(match => match.FirstStart);
        var end = agreeing.Min(match => match.FirstEnd);
        if (end - start < TimeSpan.FromSeconds(5))
        {
            return null;
        }

        var confidence = Math.Clamp(0.55 + agreeing.Average(match => match.Confidence) * 0.27 +
            Math.Min(0.12, (agreeing.Length - 1) * 0.06), 0, 0.99);
        return (start, end, confidence, agreeing.Length);
    }

    private static double Overlap(TimeSpan fromA, TimeSpan toA, TimeSpan fromB, TimeSpan toB)
    {
        var start = Math.Max(fromA.Ticks, fromB.Ticks);
        var end = Math.Min(toA.Ticks, toB.Ticks);
        var union = Math.Max(toA.Ticks, toB.Ticks) - Math.Min(fromA.Ticks, fromB.Ticks);
        return end <= start || union <= 0 ? 0 : (end - start) / (double)union;
    }
}
