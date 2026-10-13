using Rex.Media.Primitives;

namespace Rex.Media.AppCore.Player;

/// <summary>Confirmed boundaries and high-confidence detected sections; automatic skipping is strictly opt-in.</summary>
public sealed partial class PlayerController
{
    /// <summary>Only a seekable video can be marked or skipped, never a song or an unknown stream.</summary>
    public bool CanMarkEpisodeSections =>
        Item is not null && CanSeek && Info?.FirstTrack(MediaKind.Video) is not null;

    /// <summary>Saved boundaries for the current episode, even when its playback history is off.</summary>
    public EpisodeSections CurrentSections =>
        Item is { } item ? Memory.Sections(MemoryKey(item)) : new EpisodeSections();

    /// <summary>One-click skip offered from confirmed boundaries or sufficiently confident detection, without asking for confirmation.</summary>
    public EpisodeSkip? AvailableEpisodeSkip =>
        CanMarkEpisodeSections ? (CurrentSections.Offer(Position, Duration) ?? _activeProposals?.Offer(CurrentSections, Position, Duration)) : null;

    /// <summary>Record an exact start or end at the current frame, or the explicit file end.</summary>
    public bool MarkEpisodeSection(EpisodeSectionKind kind, bool start, bool atVideoEnd = false)
    {
        if (!CanMarkEpisodeSections || Item is not { } item)
        {
            Say("Open a seekable video before marking intro or credits.");
            return false;
        }

        var position = atVideoEnd ? Duration : Position;
        var updated = CurrentSections.Mark(kind, start, position);
        Memory.SetSections(MemoryKey(item), updated);
        var label = kind == EpisodeSectionKind.Intro ? "Intro" : "Credits";
        Say($"{label} {(start ? "start" : "end")} marked at {TimeText.Format(position, Duration)}.");
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Remove only this episode's chosen interval; other episodes are untouched.</summary>
    public bool ClearEpisodeSection(EpisodeSectionKind kind)
    {
        if (!CanMarkEpisodeSections || Item is not { } item)
        {
            return false;
        }

        Memory.SetSections(MemoryKey(item), CurrentSections.Clear(kind));
        Say((kind == EpisodeSectionKind.Intro ? "Intro" : "Credits") + " markers cleared.");
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Optional hints from labeled container chapters; never a time guessed from duration.</summary>
    public EpisodeSections? ChapterSectionSuggestions =>
        CanMarkEpisodeSections && Info is { } info ? EpisodeSectionDetection.Suggest(info.Chapters, Duration) : null;

    /// <summary>Only an explicit user confirmation commits missing chapter-derived ranges.</summary>
    public bool ApplyChapterSectionSuggestions()
    {
        if (!CanMarkEpisodeSections || Item is not { } item || ChapterSectionSuggestions is not { } suggested)
        {
            return false;
        }

        var saved = CurrentSections;
        var updated = saved with
        {
            IntroStart = saved.IntroStart is null && saved.IntroEnd is null ? suggested.IntroStart : saved.IntroStart,
            IntroEnd = saved.IntroStart is null && saved.IntroEnd is null ? suggested.IntroEnd : saved.IntroEnd,
            CreditsStart = saved.CreditsStart is null && saved.CreditsEnd is null ? suggested.CreditsStart : saved.CreditsStart,
            CreditsEnd = saved.CreditsStart is null && saved.CreditsEnd is null ? suggested.CreditsEnd : saved.CreditsEnd,
        };
        if (updated == saved)
        {
            return false;
        }

        Memory.SetSections(MemoryKey(item), updated);
        Say("Chapter section markers saved. Playback will never skip them without a click.");
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// One click (or the shortcut) does what the skip button offers: skips the intro, skips the
    /// credits to whatever follows them, or plays the next episode when nothing follows them.
    /// Detection never alters saved boundaries.
    /// </summary>
    public bool SkipEpisodeSection()
    {
        if (SkipPrompt is not { } prompt)
        {
            return false;
        }

        _automaticSkip.Finish(prompt.Kind);
        if (prompt.Primary == EpisodeSkipAction.NextEpisode && PlayNextEpisode())
        {
            return true;
        }

        Seek(prompt.Target);
        Say(prompt.Kind == EpisodeSectionKind.Intro ? "Intro skipped." : "Credits skipped.");
        return true;
    }
}
