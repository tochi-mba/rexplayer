using Rex.Media.Primitives;

namespace Rex.Media.AppCore.Player;

/// <summary>
/// Conservative suggestions from chapter titles embedded in the opened video. No decoder scan,
/// repeated-timestamp guess or automatic seek: the viewer must confirm the suggestions.
/// </summary>
public static class EpisodeSectionDetection
{
    /// <summary>
    /// Find an unambiguous named opening and/or credit chapter, bounded by the next actual
    /// chapter. Post-credit scenes are never included when they have their own chapter.
    /// </summary>
    public static EpisodeSections? Suggest(IReadOnlyList<Chapter> chapters, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(chapters);
        if (duration <= TimeSpan.FromSeconds(2) || chapters.Count == 0)
        {
            return null;
        }

        var ordered = chapters.Where(chapter => chapter.Start.IsKnown)
            .OrderBy(chapter => chapter.Start).ToList();
        var intros = new List<(TimeSpan Start, TimeSpan End)>();
        var credits = new List<(TimeSpan Start, TimeSpan End)>();
        for (var index = 0; index < ordered.Count; index++)
        {
            var kind = KindOf(ordered[index].Title);
            if (kind is null)
            {
                continue;
            }

            var start = ordered[index].Start.ToTimeSpan();
            var end = index + 1 < ordered.Count ? ordered[index + 1].Start.ToTimeSpan() : duration;
            if (start < TimeSpan.Zero || end <= start + TimeSpan.FromSeconds(2) || end > duration)
            {
                continue;
            }

            // An intro must belong to the opening half, credits to the closing half;
            // a mislabeled mid-episode chapter never becomes a skip range.
            if (kind == EpisodeSectionKind.Intro && end <= duration / 2 &&
                end - start <= TimeSpan.FromMinutes(15))
            {
                intros.Add((start, end));
            }
            else if (kind == EpisodeSectionKind.Credits && start >= duration / 2)
            {
                credits.Add((start, end));
            }
        }

        // Repeated chapter labels are ambiguous: do not pick an arbitrary match.
        var result = new EpisodeSections(
            IntroStart: intros.Count == 1 ? intros[0].Start : null,
            IntroEnd: intros.Count == 1 ? intros[0].End : null,
            CreditsStart: credits.Count == 1 ? credits[0].Start : null,
            CreditsEnd: credits.Count == 1 ? credits[0].End : null);
        return result == new EpisodeSections() ? null : result;
    }

    private static EpisodeSectionKind? KindOf(string? title)
    {
        var label = title?.Trim().ToLowerInvariant();
        return label switch
        {
            "intro" or "opening" or "opening credits" or "opening titles" or
                "opening theme" or "theme song" or "title sequence" => EpisodeSectionKind.Intro,
            "credits" or "end credits" or "closing credits" or "closing titles" or
                "ending" or "ending theme" or "outro" => EpisodeSectionKind.Credits,
            _ => null,
        };
    }
}
