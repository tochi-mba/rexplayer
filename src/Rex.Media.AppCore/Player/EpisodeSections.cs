namespace Rex.Media.AppCore.Player;

/// <summary>The two sections of an episode that can be skipped.</summary>
public enum EpisodeSectionKind
{
    Intro,
    Credits,
}

/// <summary>An optional action, offered only while the playback position is inside its saved span.</summary>
public sealed record EpisodeSkip(EpisodeSectionKind Kind, TimeSpan End);

/// <summary>
/// The boundaries the viewer saved for one file. Partly entered or reversed boundaries are kept for
/// editing but skip nothing, with one exception: credits marked with a start alone run to the end
/// of the file, which is where credits usually end. An end marked before the end of the file keeps
/// a scene that follows the credits.
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

    /// <summary>What playback at <paramref name="position"/> can skip by the saved boundaries, or null.</summary>
    public EpisodeSkip? Offer(TimeSpan position, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || position < TimeSpan.Zero || position >= duration)
        {
            return null;
        }

        return InRange(EpisodeSectionKind.Intro, IntroStart, IntroEnd, position, duration)
            ?? InRange(EpisodeSectionKind.Credits, CreditsStart, CreditsEnd ?? (CreditsStart is null ? null : duration), position, duration);
    }

    /// <summary>The span the saved boundaries of <paramref name="kind"/> skip, or null when they skip nothing.</summary>
    public (TimeSpan Start, TimeSpan End)? Span(EpisodeSectionKind kind, TimeSpan duration)
    {
        var (start, end) = kind switch
        {
            EpisodeSectionKind.Intro => (IntroStart, IntroEnd),
            EpisodeSectionKind.Credits => (CreditsStart, CreditsEnd ?? (CreditsStart is null ? null : duration)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        return start is { } from && end is { } to && Valid(from, to, duration) ? (from, to) : null;
    }

    /// <summary>Whether any boundary of <paramref name="kind"/> is saved, complete or not.</summary>
    public bool Has(EpisodeSectionKind kind) => kind == EpisodeSectionKind.Intro
        ? IntroStart is not null || IntroEnd is not null
        : CreditsStart is not null || CreditsEnd is not null;

    private static EpisodeSkip? InRange(EpisodeSectionKind kind, TimeSpan? start, TimeSpan? end, TimeSpan position, TimeSpan duration)
    {
        if (start is not { } from || end is not { } to || !Valid(from, to, duration) ||
            position < from || position >= to - TimeSpan.FromMilliseconds(250))
        {
            return null;
        }

        return new EpisodeSkip(kind, to);
    }

    private static bool Valid(TimeSpan from, TimeSpan to, TimeSpan duration) =>
        from >= TimeSpan.Zero && to <= duration && to - from >= TimeSpan.FromSeconds(2);
}
