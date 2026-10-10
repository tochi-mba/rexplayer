namespace Rex.Media.AppCore.Player;

/// <summary>Two user-confirmed, episode-specific sections. No scene detector guesses these times.</summary>
public enum EpisodeSectionKind
{
    Intro,
    Credits,
}

/// <summary>An optional action, offered only while the playback position is inside its saved span.</summary>
public sealed record EpisodeSkip(EpisodeSectionKind Kind, TimeSpan End);

/// <summary>
/// Explicit boundaries for one media item. Partially entered or reversed boundaries are retained
/// for editing but can never skip anything. Credits have an explicit end because scenes sometimes
/// follow them. Nothing is ever skipped without a deliberate click.
/// </summary>
public sealed record EpisodeSections(
    TimeSpan? IntroStart = null,
    TimeSpan? IntroEnd = null,
    TimeSpan? CreditsStart = null,
    TimeSpan? CreditsEnd = null)
{
    /// <summary>A boundary at the viewer's current position. Incomplete spans remain editable.</summary>
    public EpisodeSections Mark(EpisodeSectionKind kind, bool start, TimeSpan at)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(at, TimeSpan.Zero);

        return (kind, start) switch
        {
            (EpisodeSectionKind.Intro, true) => this with { IntroStart = at },
            (EpisodeSectionKind.Intro, false) => this with { IntroEnd = at },
            (EpisodeSectionKind.Credits, true) => this with { CreditsStart = at },
            (EpisodeSectionKind.Credits, false) => this with { CreditsEnd = at },
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    public EpisodeSections Clear(EpisodeSectionKind kind) => kind switch
    {
        EpisodeSectionKind.Intro => this with { IntroStart = null, IntroEnd = null },
        EpisodeSectionKind.Credits => this with { CreditsStart = null, CreditsEnd = null },
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Only trustworthy saved boundaries create a skip offer, never inferred timestamps.</summary>
    public EpisodeSkip? Offer(TimeSpan position, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || position < TimeSpan.Zero || position >= duration)
        {
            return null;
        }

        return InRange(EpisodeSectionKind.Intro, IntroStart, IntroEnd, position, duration)
            ?? InRange(EpisodeSectionKind.Credits, CreditsStart, CreditsEnd, position, duration);
    }

    private static EpisodeSkip? InRange(EpisodeSectionKind kind, TimeSpan? start, TimeSpan? end, TimeSpan position, TimeSpan duration)
    {
        if (start is not { } from || end is not { } to ||
            from < TimeSpan.Zero || from >= to || to > duration ||
            to - from < TimeSpan.FromSeconds(2) ||
            position < from || position >= to - TimeSpan.FromMilliseconds(250))
        {
            return null;
        }

        return new EpisodeSkip(kind, to);
    }
}
