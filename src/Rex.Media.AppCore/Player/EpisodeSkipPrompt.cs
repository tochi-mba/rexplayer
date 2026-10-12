namespace Rex.Media.AppCore.Player;

/// <summary>What one button next to the picture does while playback is inside a section.</summary>
public enum EpisodeSkipAction
{
    SkipIntro,
    SkipCredits,
    NextEpisode,
}

/// <summary>
/// The skip offer the window shows: one main action, an optional second one, where skipping goes,
/// whether the section was detected or saved by the viewer, and the countdown to an automatic skip.
/// </summary>
public sealed record EpisodeSkipPrompt(
    EpisodeSectionKind Kind,
    EpisodeSkipAction Primary,
    EpisodeSkipAction? Secondary,
    TimeSpan Target,
    bool Detected,
    double? Confidence,
    int? AutomaticInSeconds)
{
    /// <summary>
    /// Credits that end this close to the end of the file have nothing after them worth watching
    /// (a production and a distributor logo took up to 18 s on the measured episodes); a longer
    /// remainder is a scene after the credits or a preview of the next episode.
    /// </summary>
    public static readonly TimeSpan SceneAfterCreditsThreshold = TimeSpan.FromSeconds(20);

    /// <summary>How long an opted-in automatic skip waits, in playback time, so it can be cancelled.</summary>
    public static readonly TimeSpan AutomaticDelay = TimeSpan.FromSeconds(5);

    /// <summary>The words on a button; short, and true for every case the action covers.</summary>
    public static string Label(EpisodeSkipAction action) => action switch
    {
        EpisodeSkipAction.SkipIntro => "Skip intro",
        EpisodeSkipAction.SkipCredits => "Skip credits",
        EpisodeSkipAction.NextEpisode => "Next episode",
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    /// <summary>The main button's text; while an automatic skip counts down, it says so.</summary>
    public string PrimaryText => AutomaticInSeconds is not { } seconds ? Label(Primary) : Primary switch
    {
        EpisodeSkipAction.SkipIntro => $"Skipping intro in {seconds}",
        EpisodeSkipAction.SkipCredits => $"Skipping credits in {seconds}",
        _ => $"Next episode in {seconds}",
    };

    /// <summary>
    /// The offer for <paramref name="skip"/>. At credits with a following episode, "Next episode"
    /// leads unless a scene follows the credits, when "Skip credits" goes to that scene and "Next
    /// episode" stands beside it. <paramref name="automaticAt"/> is the playback time an automatic
    /// skip is due, or null when none is. <paramref name="detected"/> is the proposal the skip came
    /// from, or null when it came from boundaries the viewer saved.
    /// </summary>
    public static EpisodeSkipPrompt For(EpisodeSkip skip, SectionCandidate? detected, TimeSpan duration,
        TimeSpan position, bool hasNextEpisode, TimeSpan? automaticAt)
    {
        ArgumentNullException.ThrowIfNull(skip);
        int? countdown = automaticAt is { } due
            ? (int)Math.Max(1, Math.Ceiling((due - position).TotalSeconds))
            : null;
        var confidence = detected?.Confidence;
        if (skip.Kind == EpisodeSectionKind.Intro)
        {
            return new EpisodeSkipPrompt(skip.Kind, EpisodeSkipAction.SkipIntro, null, skip.End,
                confidence is not null, confidence, countdown);
        }

        var sceneAfter = duration - skip.End >= SceneAfterCreditsThreshold;
        var (primary, secondary) = (hasNextEpisode, sceneAfter) switch
        {
            (true, false) => (EpisodeSkipAction.NextEpisode, (EpisodeSkipAction?)null),
            (true, true) => (EpisodeSkipAction.SkipCredits, EpisodeSkipAction.NextEpisode),
            _ => (EpisodeSkipAction.SkipCredits, null),
        };
        return new EpisodeSkipPrompt(skip.Kind, primary, secondary, skip.End, confidence is not null,
            confidence, countdown);
    }
}
