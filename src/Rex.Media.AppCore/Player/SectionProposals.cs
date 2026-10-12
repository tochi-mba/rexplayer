namespace Rex.Media.AppCore.Player;

/// <summary>A suggested interval, never confused with a user's saved markers.</summary>
public sealed record SectionCandidate(EpisodeSectionKind Kind, TimeSpan Start, TimeSpan End,
    double Confidence, string Reason, string Source);

/// <summary>
/// File-bound suggestions, approval state, and explicit rejection. A rejected section does not
/// reappear until the user restores it, even when the cached fingerprint is refreshed.
/// </summary>
public sealed record SectionProposals(string Signature, IReadOnlyList<SectionCandidate> Candidates,
    bool IntroRejected = false, bool CreditsRejected = false)
{
    public const double OfferThreshold = 0.76;

    public SectionCandidate? Best(EpisodeSectionKind kind, TimeSpan duration)
    {
        if (kind == EpisodeSectionKind.Intro ? IntroRejected : CreditsRejected)
        {
            return null;
        }

        return Candidates.Where(candidate => candidate.Kind == kind &&
                candidate.Confidence >= OfferThreshold && candidate.Start >= TimeSpan.Zero &&
                candidate.End > candidate.Start + TimeSpan.FromSeconds(2) && candidate.End <= duration)
            .OrderByDescending(candidate => candidate.Confidence).FirstOrDefault();
    }

    public EpisodeSkip? Offer(EpisodeSections confirmed, TimeSpan position, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(confirmed);
        if (confirmed.Offer(position, duration) is { } chosen)
        {
            return chosen;
        }

        if (position < TimeSpan.Zero || position >= duration)
        {
            return null;
        }

        foreach (var kind in new[] { EpisodeSectionKind.Intro, EpisodeSectionKind.Credits })
        {
            // Even incomplete manually entered boundaries suppress machine guesses.
            if (kind == EpisodeSectionKind.Intro
                    ? confirmed.IntroStart is not null || confirmed.IntroEnd is not null
                    : confirmed.CreditsStart is not null || confirmed.CreditsEnd is not null)
            {
                continue;
            }

            if (Best(kind, duration) is { } candidate &&
                position >= candidate.Start && position < candidate.End - TimeSpan.FromMilliseconds(250))
            {
                return new EpisodeSkip(kind, candidate.End);
            }
        }

        return null;
    }

    public SectionProposals Reject(EpisodeSectionKind kind, bool rejected = true) => kind switch
    {
        EpisodeSectionKind.Intro => this with { IntroRejected = rejected },
        EpisodeSectionKind.Credits => this with { CreditsRejected = rejected },
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public SectionProposals Adjust(SectionCandidate changed) =>
        this with { Candidates = [.. Candidates.Where(item => item.Kind != changed.Kind), changed] };
}
