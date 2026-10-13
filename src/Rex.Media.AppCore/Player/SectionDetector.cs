using System.Globalization;

namespace Rex.Media.AppCore.Player;

/// <summary>A file's reusable features. Opening/closing hashes have absolute 32 ms frame offsets.</summary>
public sealed record EpisodeAnalysis(string Path, string Signature, TimeSpan Duration,
    IReadOnlyList<AudioLandmark> Opening, IReadOnlyList<AudioLandmark> Closing,
    IReadOnlyList<CreditFrameSample> Frames);

/// <summary>
/// Combines across-episode audio agreement with closing-credit picture evidence.
/// <para>
/// A title sequence or closing theme is the material a season repeats: the same audio, at a
/// different offset in each episode because cold opens, recaps and logos differ in length. That is
/// the repeated-segment idea of the recurring-content literature (for instance Khemiri,
/// Petrovska-Delacretaz and Chollet's repeating-object detection in radio and TV streams), here with
/// Wang's landmark fingerprints. Matches from every sibling are grouped by where they fall in this
/// episode, and the group shared by the most siblings wins: a recap of the previous episode matches
/// one sibling, the title sequence matches them all. The group's extent is the union of its
/// matches, so an episode with the full title sequence is not cut down to a sibling's short one.
/// </para>
/// <para>
/// Credits come from shared closing audio, from pictures (<see cref="CreditFrameEvidence"/>), or
/// both. A shared closing segment beats a credits-looking run that only one episode has. Where both
/// exist, the pictures set the start (text appearing is what makes credits; a theme that starts
/// over the last scene must not skip it) and the end, so a scene after the credits is kept.
/// </para>
/// </summary>
public static class SectionDetector
{
    /// <summary>A repeated opening shorter than this is a logo or a title card, not worth a button.</summary>
    public static readonly TimeSpan MinimumIntro = TimeSpan.FromSeconds(8);

    /// <summary>Repeated closing audio shorter than this is a sting, not credits.</summary>
    public static readonly TimeSpan MinimumCredits = TimeSpan.FromSeconds(8);

    /// <summary>
    /// How many of the nearest episodes an episode is compared with: enough for a majority to
    /// outvote a recap or a one-off title sequence, few enough to analyse on a laptop.
    /// </summary>
    public const int SiblingsCompared = 6;

    public static SectionProposals Detect(EpisodeAnalysis episode, IReadOnlyList<EpisodeAnalysis> siblings)
    {
        ArgumentNullException.ThrowIfNull(episode);
        ArgumentNullException.ThrowIfNull(siblings);
        if (episode.Duration < TimeSpan.FromMinutes(6) || EpisodeGrouping.Identify(episode.Path) is not { } identity)
        {
            return new SectionProposals(episode.Signature, []);
        }

        // Another copy of the same episode would match from start to end; it is not a sibling.
        var related = siblings.Where(other => EpisodeGrouping.Identify(other.Path) is { } match &&
                EpisodeGrouping.AreSiblings(identity, match))
            .DistinctBy(other => EpisodeGrouping.Identify(other.Path)!.Episode).ToArray();
        var intros = new List<(int Sibling, LandmarkMatch Match)>();
        var endings = new List<(int Sibling, LandmarkMatch Match)>();
        var openingSpan = SectionAnalysis.OpeningSpan(episode.Duration);
        for (var index = 0; index < related.Length; index++)
        {
            foreach (var intro in LandmarkMatcher.MatchAll(episode.Opening, related[index].Opening))
            {
                if (intro.FirstEnd <= openingSpan && intro.FirstEnd - intro.FirstStart >= TimeSpan.FromSeconds(4))
                {
                    intros.Add((index, intro));
                }
            }

            foreach (var ending in LandmarkMatcher.MatchAll(episode.Closing, related[index].Closing))
            {
                if (ending.FirstStart >= episode.Duration * 0.6 && ending.FirstEnd <= episode.Duration &&
                    ending.FirstEnd - ending.FirstStart >= TimeSpan.FromSeconds(4))
                {
                    endings.Add((index, ending));
                }
            }
        }

        var result = new List<SectionCandidate>();
        if (Consensus(intros, related.Length) is { } opening && opening.End - opening.Start >= MinimumIntro)
        {
            result.Add(new SectionCandidate(EpisodeSectionKind.Intro, opening.Start, opening.End,
                opening.Confidence, Repeats("Opening", opening.Support, related.Length), "shared audio"));
        }

        if (Credits(episode, endings, related.Length) is { } credits)
        {
            result.Add(credits);
        }

        return new SectionProposals(episode.Signature, result);
    }

    private static SectionCandidate? Credits(EpisodeAnalysis episode, List<(int Sibling, LandmarkMatch Match)> endings, int siblings)
    {
        var duration = episode.Duration;
        var runs = CreditFrameEvidence.Runs(episode.Frames, duration);
        var audio = Consensus(endings, siblings);
        if (audio is { } shared && shared.End - shared.Start >= MinimumCredits)
        {
            var reason = Repeats("Closing audio", shared.Support, siblings);
            var run = runs.Where(run => run.Start < shared.End && shared.Start < run.End)
                .OrderByDescending(run => run.Confidence).FirstOrDefault();
            if (run is not null)
            {
                return new SectionCandidate(EpisodeSectionKind.Credits, run.Start, run.End,
                    Math.Min(0.99, Math.Max(shared.Confidence, run.Confidence) + 0.05),
                    reason + " and credit text on screen", "shared audio + frames");
            }

            // Credits over the final scene: the audio alone, never stretched over a scene after it.
            var end = duration - shared.End <= CreditFrameEvidence.ShortTail ? duration : shared.End;
            return new SectionCandidate(EpisodeSectionKind.Credits, shared.Start, end, shared.Confidence,
                reason, "shared audio");
        }

        var best = runs.OrderByDescending(run => run.Confidence).FirstOrDefault();
        return best is not null && best.Confidence >= SectionProposals.OfferThreshold
            ? new SectionCandidate(EpisodeSectionKind.Credits, best.Start, best.End, best.Confidence,
                "Credit text on screen near the end", "video frames")
            : null;
    }

    private static string Repeats(string what, int support, int siblings) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{what} repeats in {support} of {siblings} other episode{(siblings == 1 ? "" : "s")} of this season");

    /// <summary>
    /// Groups matches by where they fall in this episode and keeps the group most siblings share.
    /// With four or more siblings, a group must be shared by at least two of them.
    /// </summary>
    private static (TimeSpan Start, TimeSpan End, double Confidence, int Support)? Consensus(
        List<(int Sibling, LandmarkMatch Match)> matches, int siblings)
    {
        var groups = new List<List<(int Sibling, LandmarkMatch Match)>>();
        foreach (var item in matches.OrderByDescending(item => item.Match.Anchors))
        {
            var home = groups.FirstOrDefault(group => group.Any(member =>
                Containment(member.Match, item.Match) >= 0.5));
            if (home is null)
            {
                groups.Add([item]);
            }
            else
            {
                home.Add(item);
            }
        }

        var chosen = groups.Select(group => (Group: group, Support: group.Select(item => item.Sibling).Distinct().Count()))
            .OrderByDescending(item => item.Support)
            .ThenByDescending(item => item.Group.Sum(member => member.Match.Anchors))
            .FirstOrDefault();
        if (chosen.Group is null || (siblings >= 4 && chosen.Support < 2))
        {
            return null;
        }

        var start = chosen.Group.Min(item => item.Match.FirstStart);
        var end = chosen.Group.Max(item => item.Match.FirstEnd);
        var strength = chosen.Group.GroupBy(item => item.Sibling)
            .Average(sibling => sibling.Max(item => item.Match.Confidence));
        var agreement = chosen.Support / (double)Math.Min(siblings, 5);
        // A single comparison cannot tell a title sequence from a recap of that episode.
        var ceiling = siblings == 1 ? 0.85 : 0.99;
        var confidence = Math.Min(ceiling, 0.45 + 0.25 * strength + 0.25 * Math.Min(1, agreement));
        return (start, end, Math.Round(confidence, 3), chosen.Support);
    }

    /// <summary>How much of the shorter match lies inside the other, on this episode's timeline.</summary>
    private static double Containment(LandmarkMatch a, LandmarkMatch b)
    {
        var overlap = Math.Min(a.FirstEnd.Ticks, b.FirstEnd.Ticks) - Math.Max(a.FirstStart.Ticks, b.FirstStart.Ticks);
        var shorter = Math.Min((a.FirstEnd - a.FirstStart).Ticks, (b.FirstEnd - b.FirstStart).Ticks);
        return overlap <= 0 ? 0 : overlap / (double)shorter;
    }
}
