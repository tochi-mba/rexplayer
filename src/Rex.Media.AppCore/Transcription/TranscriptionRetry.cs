using Rex.Media.Primitives;

namespace Rex.Media.AppCore.Transcription;

/// <summary>
/// When a file whose hearing failed is tried again (SUB-20). A file that is busy or out of reach
/// (being copied, on a drive that went to sleep) is tried again after 1, 5, 30 minutes, 2 and 12
/// hours; one that cannot be read is tried once more after half an hour, in case it was still
/// being written, and then left until the user asks. Retry from the library starts afresh.
/// </summary>
public static class TranscriptionRetry
{
    private static readonly TimeSpan[] PassingWaits =
    [
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(12),
    ];

    private static readonly TimeSpan LastingWait = TimeSpan.FromMinutes(30);

    /// <summary>What went wrong, in words a person reads, and whether it may pass by itself.</summary>
    public static (string Reason, bool Passing) Classify(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error switch
        {
            FileNotFoundException or DirectoryNotFoundException => ("The file is no longer there", true),
            UnauthorizedAccessException => ("rexplayer is not allowed to read the file", false),
            MediaFormatException => ("The file could not be read: " + error.Message, false),
            IOException => ("The file was busy or out of reach: " + error.Message, true),
            NotSupportedException => ("Its sound is in a format rexplayer cannot decode", false),
            InvalidDataException => ("The speech model could not hear it: " + error.Message, false),
            _ => ("Something went wrong: " + error.Message, false),
        };
    }

    /// <summary>The record of a file whose hearing failed with <paramref name="error"/> at <paramref name="now"/>, counting the failures in a row.</summary>
    public static TranscriptRecord Failed(TranscriptRecord record, Exception error, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(record);
        var (reason, passing) = Classify(error);
        var attempts = (record.State == TranscriptState.Failed ? record.Attempts : 0) + 1;
        TimeSpan? wait = passing
            ? attempts <= PassingWaits.Length ? PassingWaits[attempts - 1] : null
            : attempts == 1 ? LastingWait : null;
        return record with { State = TranscriptState.Failed, Reason = reason, Attempts = attempts, NextTry = now + wait };
    }

    /// <summary>Whether a file with <paramref name="record"/> may be heard in the background at <paramref name="now"/>.</summary>
    public static bool MayTry(TranscriptRecord? record, DateTimeOffset now) => record switch
    {
        null => true,
        { State: TranscriptState.Partial } => true,
        { State: TranscriptState.Failed, NextTry: { } next } => now >= next,
        _ => false,
    };

    /// <summary>The record as the user's Retry leaves it: tried again at once, from a clean slate.</summary>
    public static TranscriptRecord Retry(TranscriptRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return record.State == TranscriptState.Failed ? record with { Attempts = 0, NextTry = DateTimeOffset.MinValue } : record;
    }
}
