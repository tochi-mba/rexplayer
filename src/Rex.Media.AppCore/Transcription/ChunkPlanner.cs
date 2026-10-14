namespace Rex.Media.AppCore.Transcription;

/// <summary>
/// A piece of a file to hear (SUB-21): the stretch whose words are kept, and the wider stretch the
/// model hears so a word cut by the edge is heard whole by the piece it starts in.
/// </summary>
public readonly record struct SpeechChunk(TimeRange Kept, TimeRange Heard);

/// <summary>
/// Which piece of a file to hear next (SUB-21). Speech models hear thirty seconds at a time, so a
/// piece keeps 26 seconds and hears two more either side. What plays is heard from where it plays,
/// running ahead of it to the end, and then the part before it is filled in; a file heard in the
/// background is heard from the top. A seek moves the next piece to the new place; the piece being
/// heard is dropped when the playhead leaves it behind.
/// </summary>
public static class ChunkPlanner
{
    /// <summary>The stretch a piece keeps.</summary>
    public static readonly TimeSpan Length = TimeSpan.FromSeconds(26);

    /// <summary>What a piece hears either side of what it keeps.</summary>
    public static readonly TimeSpan Context = TimeSpan.FromSeconds(2);

    /// <summary>A gap left shorter than this after a piece is heard with it, rather than on its own.</summary>
    public static readonly TimeSpan Sliver = TimeSpan.FromSeconds(4);

    /// <summary>How far ahead of the playhead a piece being heard is still worth finishing after a seek.</summary>
    public static readonly TimeSpan ReachAhead = TimeSpan.FromSeconds(90);

    /// <summary>
    /// The next piece to hear in a file of <paramref name="duration"/> (null while unknown) with
    /// <paramref name="done"/> heard, the playhead at <paramref name="from"/>; null when nothing is
    /// left. With the length unknown, pieces go on past the last one until the sound ends.
    /// </summary>
    public static SpeechChunk? Next(TimeRanges done, TimeSpan? duration, TimeSpan from)
    {
        ArgumentNullException.ThrowIfNull(done);
        var end = duration ?? TimeSpan.MaxValue;
        from = from < TimeSpan.Zero ? TimeSpan.Zero : from >= end ? TimeSpan.Zero : WholeSeconds(from);
        var gap = done.Gaps(new TimeRange(from, end)).FirstOrDefault();
        if (gap.Length <= TimeSpan.Zero)
        {
            // Nothing left ahead of the playhead: fill in what came before it.
            gap = done.Gaps(new TimeRange(TimeSpan.Zero, end)).FirstOrDefault();
            if (gap.Length <= TimeSpan.Zero)
            {
                return null;
            }
        }

        var keptEnd = gap.End - gap.Start <= Length + Sliver ? gap.End : gap.Start + Length;
        var heardEnd = duration is { } known && keptEnd + Context > known ? known : keptEnd + Context;
        var heardStart = gap.Start - Context < TimeSpan.Zero ? TimeSpan.Zero : gap.Start - Context;
        return new SpeechChunk(new TimeRange(gap.Start, keptEnd), new TimeRange(heardStart, heardEnd));
    }

    /// <summary>
    /// Whether <paramref name="chunk"/>, being heard, should be dropped now the playhead is at
    /// <paramref name="playhead"/>: it is kept while the playhead is in it, or not far before it with
    /// nothing unheard between them; otherwise the playhead's own stretch should be heard first.
    /// </summary>
    public static bool ShouldAbandon(SpeechChunk chunk, TimeSpan playhead, TimeRanges done)
    {
        ArgumentNullException.ThrowIfNull(done);
        if (chunk.Kept.Contains(playhead))
        {
            return false;
        }

        // Just before it, the piece hears the playhead's words anyway.
        var ahead = chunk.Kept.Start - playhead;
        return ahead < TimeSpan.Zero || ahead > ReachAhead || (ahead > Context && !done.Covers(new TimeRange(playhead, chunk.Kept.Start)));
    }

    private static TimeSpan WholeSeconds(TimeSpan time) => TimeSpan.FromSeconds(Math.Floor(time.TotalSeconds));
}
