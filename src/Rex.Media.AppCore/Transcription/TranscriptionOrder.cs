using Rex.Media.Library;
using Rex.Media.Settings;
using Rex.Media.AppCore.Library;

namespace Rex.Media.AppCore.Transcription;

/// <summary>One file waiting to be heard.</summary>
/// <param name="Path">Where it is.</param>
/// <param name="Signature">Who it is, for its record.</param>
/// <param name="Duration">How long it is, when known, for the time left.</param>
/// <param name="Share">How much of it was heard before, 0 to 1.</param>
/// <param name="Asked">The user asked for it, so it is heard even while the library's transcription is off or paused.</param>
public sealed record QueuedFile(string Path, TranscriptSignature Signature, TimeSpan? Duration, double Share, bool Asked = false);

/// <summary>
/// The order the library is heard in (SUB-20). What the user asked for comes first, in the order
/// asked; then the background: files already part-heard (so no work is wasted), then videos left
/// part-way through (the likeliest to be watched next), then the newest, then by name. Done files,
/// files that cannot be heard, and failed ones not due again are left out. What plays is not here:
/// it is always heard first, ahead of the whole queue.
/// </summary>
public static class TranscriptionOrder
{
    /// <summary>
    /// The queue for <paramref name="entries"/>: <paramref name="asked"/> are paths the user asked
    /// for (always in, whatever the scope); the rest only while library transcription is on.
    /// <paramref name="find"/> gives a file's record, <paramref name="leftPartWay"/> whether it
    /// was stopped part-way through.
    /// </summary>
    public static IReadOnlyList<QueuedFile> Order(
        IReadOnlyList<LibraryEntry> entries,
        IReadOnlyList<string> asked,
        TranscriptionSettings settings,
        IReadOnlyList<string> libraryFolders,
        Func<TranscriptSignature, TranscriptRecord?> find,
        Func<string, bool> leftPartWay,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(asked);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(find);
        ArgumentNullException.ThrowIfNull(leftPartWay);
        var byPath = entries.DistinctBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);
        var queue = new List<QueuedFile>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in asked)
        {
            if (byPath.TryGetValue(path, out var entry) && entry.Kind != LibraryKind.Picture
                && find(Sign(entry)) is var record && record?.State is not (TranscriptState.Done or TranscriptState.NoSound) && taken.Add(path))
            {
                queue.Add(Queued(entry, record) with { Asked = true });
            }
        }

        if (!settings.Library)
        {
            return queue;
        }

        var audioContext = AudioContext.Of(entries);
        var background = entries
            .Where(entry => !taken.Contains(entry.Path))
            .Select(entry => (Entry: entry, Record: find(Sign(entry))))
            .Where(pair => TranscriptionEligibility.Of(pair.Entry, settings, libraryFolders, pair.Record,
                AudioRules.Judge(pair.Entry, MusicLookup.NotSearched, audioContext)?.Shelf == AudioShelf.Audiobooks) == TranscriptionSkip.None
                && TranscriptionRetry.MayTry(pair.Record, now))
            .OrderByDescending(pair => pair.Record?.State == TranscriptState.Partial)
            .ThenByDescending(pair => leftPartWay(pair.Entry.Path))
            .ThenByDescending(pair => pair.Entry.Added)
            .ThenBy(pair => pair.Entry.Path, StringComparer.OrdinalIgnoreCase);
        queue.AddRange(background.Where(pair => taken.Add(pair.Entry.Path)).Select(pair => Queued(pair.Entry, pair.Record)));
        return queue;
    }

    /// <summary>The signature of a library file, from what the library knows of it.</summary>
    public static TranscriptSignature Sign(LibraryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return TranscriptSignature.Of(entry.Size, entry.Modified);
    }

    /// <summary>How much sound is left to hear in <paramref name="queue"/>, for the time left; files of unknown length count as none.</summary>
    public static TimeSpan SoundLeft(IReadOnlyList<QueuedFile> queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        return queue.Aggregate(TimeSpan.Zero, (sum, file) => sum + ((file.Duration ?? TimeSpan.Zero) * (1 - file.Share)));
    }

    private static QueuedFile Queued(LibraryEntry entry, TranscriptRecord? record) =>
        new(entry.Path, Sign(entry), entry.Duration, record?.State == TranscriptState.Partial ? record.Share : 0);
}
