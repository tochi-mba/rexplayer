using Rex.Media.Settings;

namespace Rex.Media.AppCore.Transcription;

/// <summary>The state of the computer and the player that decides whether transcription runs now.</summary>
/// <param name="Playing">Something is playing (not paused or stopped).</param>
/// <param name="OnBattery">The computer runs on its battery.</param>
/// <param name="EnergySaver">Windows' energy saver is on.</param>
/// <param name="ModelInstalled">The speech model the settings name is downloaded.</param>
public readonly record struct TranscriptionMoment(bool Playing, bool OnBattery, bool EnergySaver, bool ModelInstalled);

/// <summary>What may run now, the threads each may use, and why the library waits when it does.</summary>
/// <param name="PlayingItem">What plays may be heard.</param>
/// <param name="Library">The library may be heard in the background.</param>
/// <param name="PlayingThreads">Threads for hearing what plays.</param>
/// <param name="LibraryThreads">Threads for hearing the library.</param>
/// <param name="Waiting">Why the library waits, in words a person reads; null when it runs or is off.</param>
public readonly record struct TranscriptionPlan(bool PlayingItem, bool Library, int PlayingThreads, int LibraryThreads, string? Waiting);

/// <summary>
/// When transcription runs and how hard (SUB-20): playback comes first, always. What plays is
/// heard whenever it was asked for, with a quarter of the processor's threads (never more than
/// two) while it plays, so the picture and the sound keep theirs; paused, it may use all but two.
/// Measured on a four-core, eight-thread laptop decoding 1080p HEVC on the processor at playback's
/// pace: alone, 2 of 1441 pictures came more than a picture late; beside four transcription
/// threads, 40; beside one, 3. The library is heard in the background only when it is on and not paused, not while
/// something plays unless allowed, not on battery unless allowed, never in energy saver, and with
/// half the threads (one while something plays).
/// </summary>
public static class TranscriptionConditions
{
    public static TranscriptionPlan Plan(TranscriptionSettings settings, TranscriptionMoment moment, int processors)
    {
        ArgumentNullException.ThrowIfNull(settings);
        processors = Math.Max(1, processors);
        var waiting = !settings.Library ? null
            : !moment.ModelInstalled ? "Waiting for the speech model to be downloaded"
            : settings.Paused ? "Paused"
            : moment.EnergySaver ? "Waiting while energy saver is on"
            : moment.OnBattery && !settings.OnBattery ? "Waiting for the charger"
            : moment.Playing && !settings.WhilePlaying ? "Waiting until playback stops"
            : null;
        return new TranscriptionPlan(
            moment.ModelInstalled,
            settings.Library && waiting is null,
            moment.Playing ? Math.Clamp(processors / 4, 1, 2) : Math.Max(1, processors - 2),
            moment.Playing ? 1 : Math.Max(1, processors / 2),
            waiting);
    }

    /// <summary>
    /// Whether the speech model may be downloaded now that the user turned transcription on: on a
    /// connection that costs by the byte only when they ask again, there and then (Download now).
    /// </summary>
    public static bool MayDownloadModel(bool online, bool metered, bool askedNow) => online && (askedNow || !metered);

    /// <summary>
    /// Whether what plays is heard as soon as it opens rather than once the launch has settled:
    /// when the viewer waits for its captions, the player is paused for them anyway, and putting
    /// the transcription off would let playback start (and run uncaptioned) before the wait.
    /// </summary>
    public static bool StartsAtOnce(bool requested, bool holdForSubtitles) => requested && holdForSubtitles;
}
