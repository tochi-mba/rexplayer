namespace Rex.Media.AppCore.Player;

/// <summary>
/// The countdown of an opted-in automatic skip for one episode. Entering a section that is to be
/// skipped starts a countdown of <see cref="Delay"/> in playback time, shown on the button so the
/// viewer can cancel it; it never runs past a second before the section ends, and each section is
/// skipped (or cancelled) at most once per episode, so seeking back into it plays it.
/// </summary>
public sealed class AutomaticSkip
{
    /// <summary>How long the countdown runs, in playback time.</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromSeconds(5);

    private readonly HashSet<EpisodeSectionKind> _finished = [];

    /// <summary>The section counting down and the playback time it is skipped at, or null.</summary>
    public (EpisodeSectionKind Kind, TimeSpan Due)? Pending { get; private set; }

    /// <summary>A new episode: nothing is pending and every section may be skipped again.</summary>
    public void Reset()
    {
        Pending = null;
        _finished.Clear();
    }

    /// <summary>
    /// Follows playback at <paramref name="position"/> inside <paramref name="available"/> (null
    /// outside every section), where <paramref name="wanted"/> says whether that kind is skipped
    /// automatically. True when the skip is due now; the section is then finished.
    /// </summary>
    public bool Advance(EpisodeSkip? available, bool wanted, TimeSpan position)
    {
        if (available is null || !wanted || _finished.Contains(available.Kind))
        {
            Pending = null;
            return false;
        }

        if (Pending is not { } pending || pending.Kind != available.Kind)
        {
            var due = position + Delay;
            var latest = available.End - TimeSpan.FromSeconds(1);
            pending = (available.Kind, due < latest ? due : latest);
            Pending = pending;
        }

        if (position < pending.Due)
        {
            return false;
        }

        Finish(pending.Kind);
        return true;
    }

    /// <summary>Stops the countdown; that section plays on and is not skipped again. False when none ran.</summary>
    public bool Cancel()
    {
        if (Pending is not { } pending)
        {
            return false;
        }

        Finish(pending.Kind);
        return true;
    }

    /// <summary>The section was skipped by hand: no countdown for it afterwards.</summary>
    public void Finish(EpisodeSectionKind kind)
    {
        _finished.Add(kind);
        Pending = null;
    }
}
