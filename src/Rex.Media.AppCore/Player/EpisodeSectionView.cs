namespace Rex.Media.AppCore.Player;

/// <summary>
/// How one section of the playing episode stands, for the window: the span drawn on the timeline
/// (<see cref="Start"/> to <see cref="End"/>, null when nothing is), whether it is the viewer's own
/// (<see cref="Saved"/>) or detected, the detection behind it even when hidden, and which commands
/// apply. The viewer's boundaries always come first; a detection is drawn only while the viewer has
/// saved nothing for that section and has not hidden it.
/// </summary>
public sealed record EpisodeSectionView(
    EpisodeSectionKind Kind,
    TimeSpan? Start,
    TimeSpan? End,
    bool Saved,
    SectionCandidate? Detected,
    bool Hidden,
    bool HasSavedBoundary)
{
    /// <summary>"intro" or "credits", for the words around it.</summary>
    public string Noun => Kind == EpisodeSectionKind.Intro ? "intro" : "credits";

    /// <summary>Whether "Hide the detected ..." applies: something was detected and nothing is saved over it.</summary>
    public bool CanHide => Detected is not null && !HasSavedBoundary;

    /// <summary>One view per section, intro first.</summary>
    public static IReadOnlyList<EpisodeSectionView> For(EpisodeSections saved, SectionProposals? proposals, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(saved);
        return [.. new[] { EpisodeSectionKind.Intro, EpisodeSectionKind.Credits }.Select(kind =>
        {
            var detected = proposals?.Detected(kind, duration);
            var hidden = proposals?.Rejected(kind) == true;
            if (saved.Span(kind, duration) is { } span)
            {
                return new EpisodeSectionView(kind, span.Start, span.End, true, detected, hidden, true);
            }

            var shown = !saved.Has(kind) && !hidden ? detected : null;
            return new EpisodeSectionView(kind, shown?.Start, shown?.End, false, detected, hidden, saved.Has(kind));
        })];
    }
}
