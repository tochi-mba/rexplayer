using Rex.Media.Primitives;

namespace Rex.Media.AppCore.Player;

/// <summary>Viewer-controlled episode chapters; never estimates or automatically skips content.</summary>
public sealed partial class PlayerController
{
    /// <summary>Only a seekable video can be marked or skipped, never a song or an unknown stream.</summary>
    public bool CanMarkEpisodeSections =>
        Item is not null && CanSeek && Info?.FirstTrack(MediaKind.Video) is not null;

    /// <summary>Saved boundaries for the current episode, even when its playback history is off.</summary>
    public EpisodeSections CurrentSections =>
        Item is { } item ? Memory.Sections(MemoryKey(item)) : new EpisodeSections();

    /// <summary>Visible only while inside a complete, valid intro or credits range.</summary>
    public EpisodeSkip? AvailableEpisodeSkip =>
        CanMarkEpisodeSections ? CurrentSections.Offer(Position, Duration) : null;

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

    /// <summary>A click seeks past precisely the user's saved segment; no automatic skipping.</summary>
    public bool SkipEpisodeSection()
    {
        if (AvailableEpisodeSkip is not { } skip)
        {
            return false;
        }

        Seek(skip.End);
        Say(skip.Kind == EpisodeSectionKind.Intro ? "Intro skipped." : "Credits skipped.");
        return true;
    }
}
