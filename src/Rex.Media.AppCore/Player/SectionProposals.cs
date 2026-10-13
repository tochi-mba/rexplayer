namespace Rex.Media.AppCore.Player;

/// <summary>A detected section, never confused with boundaries the viewer saved.</summary>
public sealed record SectionCandidate(EpisodeSectionKind Kind, TimeSpan Start, TimeSpan End,
    double Confidence, string Reason, string Source);

/// <summary>
/// What was detected in one file (<see cref="Signature"/>), and which kinds the viewer rejected. A
/// rejected section stays hidden until the viewer shows it again, even after the file is analysed
/// again.
/// </summary>
public sealed record SectionProposals(string Signature, IReadOnlyList<SectionCandidate> Candidates,
    bool IntroRejected = false, bool CreditsRejected = false)
{
    /// <summary>The confidence a detected section needs before it is offered.</summary>
    public const double OfferThreshold = 0.76;

    /// <summary>The detected <paramref name="kind"/> worth offering, or null (also when rejected).</summary>
    public SectionCandidate? Best(EpisodeSectionKind kind, TimeSpan duration) =>
        Rejected(kind) ? null : Detected(kind, duration);

    /// <summary>The detected <paramref name="kind"/> worth offering, whether or not it was rejected.</summary>
    public SectionCandidate? Detected(EpisodeSectionKind kind, TimeSpan duration) =>
        Candidates.Where(candidate => candidate.Kind == kind &&
                candidate.Confidence >= OfferThreshold && candidate.Start >= TimeSpan.Zero &&
                candidate.End > candidate.Start + TimeSpan.FromSeconds(2) && candidate.End <= duration)
            .OrderByDescending(candidate => candidate.Confidence).FirstOrDefault();

    public bool Rejected(EpisodeSectionKind kind) => kind == EpisodeSectionKind.Intro ? IntroRejected : CreditsRejected;

    /// <summary>
    /// What playback at <paramref name="position"/> can skip: the viewer's saved boundaries first,
    /// then a detected section of a kind the viewer has saved nothing for.
    /// </summary>
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
            // Even incomplete saved boundaries replace what was detected.
            if (!confirmed.Has(kind) && Best(kind, duration) is { } candidate &&
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
}
