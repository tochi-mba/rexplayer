using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;
using Rex.Media.Primitives;
using Rex.Media.Settings;

namespace Rex.Media.AppCore.Transcription;

/// <summary>Why a library file is not transcribed in the background, or None when it is.</summary>
public enum TranscriptionSkip
{
    None,

    /// <summary>Outside the folders chosen, or in one left out.</summary>
    OutOfScope,

    /// <summary>A picture: there is nothing to hear.</summary>
    Picture,

    /// <summary>A song, which is sung rather than spoken.</summary>
    Music,

    /// <summary>Its details are not read yet; it is looked at again once they are.</summary>
    NotReadYet,

    /// <summary>Too short to hold speech worth a subtitle.</summary>
    TooShort,

    /// <summary>It turned out to have no sound.</summary>
    NoSound,

    /// <summary>Not a file on this computer.</summary>
    NotLocal,

    /// <summary>Still being downloaded; it is heard once it is whole.</summary>
    Downloading,
}

/// <summary>What a file holds, as transcription sees it.</summary>
public enum SpeechSource
{
    /// <summary>Moving pictures: a film, an episode, a clip. Heard without being asked.</summary>
    Video,

    /// <summary>An audiobook. Heard without being asked.</summary>
    Audiobook,

    /// <summary>A podcast episode. Heard without being asked.</summary>
    Podcast,

    /// <summary>A voice recording: heard only when asked.</summary>
    Recording,

    /// <summary>A song, or sound not recognised as speech: never heard without being asked, and a song's words come from its lyrics.</summary>
    Other,
}

/// <summary>
/// Which files are heard without being asked (SUB-20). Videos, audiobooks and podcasts are; songs
/// never are (their words come from the lyrics lookup), nor are voice recordings or sound not
/// recognised, which are heard only when asked. A song whose cover is kept as a one-frame video
/// track is a song, not a video. A file still downloading is never heard until it is whole. In the
/// background the scope is the whole library or the chosen folders, less the folders left out;
/// where a chosen folder and a left-out one hold each other, the one nearer the file wins.
/// Audiobooks can be included, and a file without sound is not tried again.
/// </summary>
public static class TranscriptionEligibility
{
    /// <summary>Anything shorter is passed over.</summary>
    public static readonly TimeSpan Shortest = TimeSpan.FromSeconds(5);

    /// <summary>What a file holds: moving pictures win; else the shelf its sound is on.</summary>
    public static SpeechSource SourceOf(bool movingPictures, AudioShelf? shelf) =>
        movingPictures ? SpeechSource.Video
        : shelf switch
        {
            AudioShelf.Audiobooks => SpeechSource.Audiobook,
            AudioShelf.Podcasts => SpeechSource.Podcast,
            AudioShelf.Recordings => SpeechSource.Recording,
            _ => SpeechSource.Other,
        };

    /// <summary>
    /// What the file that plays holds: moving pictures in its tracks (a cover kept as a one-frame
    /// track is not); else the shelf the library keeps it on (<paramref name="libraryShelf"/>); else,
    /// for a file from outside the library, what its own tags say it is.
    /// </summary>
    public static SpeechSource SourceOf(MediaInfo? info, AudioShelf? libraryShelf) =>
        SourceOf(info?.FirstMovingVideo() is not null, libraryShelf ?? MediaStagePolicy.ShelfFromTags(
            info?.Metadata.GetValueOrDefault(MetadataKeys.MediaType), info?.Metadata.GetValueOrDefault(MetadataKeys.Genre)));

    /// <summary>Whether <paramref name="source"/> is heard without being asked: videos, audiobooks and podcasts.</summary>
    public static bool HeardOnItsOwn(SpeechSource source) => source is SpeechSource.Video or SpeechSource.Audiobook or SpeechSource.Podcast;

    /// <summary>Whether the file at <paramref name="path"/> is still arriving: the library says so, or a downloader's name marks it ("film.avi.!qB").</summary>
    public static bool StillDownloading(string path, LibraryEntry? entry)
    {
        ArgumentNullException.ThrowIfNull(path);
        return entry is { Downloading: true } || Downloads.WholeNameOf(path) is not null;
    }

    /// <summary>
    /// Whether the item playing should be heard. A direct request wins. Otherwise only videos,
    /// audiobooks and podcasts are, and only while "what plays" is on or the background has chosen
    /// it: opening a song must not silently spend processor time turning lyrics into captions, and a
    /// file still downloading is left until it is whole.
    /// </summary>
    public static bool Playing(bool asked, SpeechSource source, bool whatPlays, bool backgroundEligible, bool downloading) =>
        asked || (!downloading && HeardOnItsOwn(source) && (whatPlays || backgroundEligible));

    /// <summary>What Transcribe says instead of starting on what plays, or null when it starts.</summary>
    public static string? Refusal(SpeechSource source, bool downloading) =>
        downloading ? "This file is still downloading. Transcribe it once it is complete."
        : source == SpeechSource.Other ? "Songs are not transcribed. Find or save lyrics instead; audiobooks can still be transcribed."
        : null;

    /// <summary>
    /// Whether the user can ask for a library file to be heard now: a video, or sound on the
    /// Audiobooks, Podcasts or Recordings shelf (<paramref name="shelf"/>); never a song, and never
    /// while it is still downloading.
    /// </summary>
    public static bool MayAsk(LibraryEntry entry, AudioShelf? shelf)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return !StillDownloading(entry.Path, entry)
            && (entry.Kind == LibraryKind.Video || (entry.Kind == LibraryKind.Music && SourceOf(false, shelf) != SpeechSource.Other));
    }

    /// <summary>Whether <paramref name="path"/> is in the background transcription's scope.</summary>
    public static bool InScope(string path, TranscriptionSettings settings, IReadOnlyList<string> libraryFolders)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(libraryFolders);
        var included = settings.Scope == TranscriptionScope.WholeLibrary ? libraryFolders : settings.Folders;
        var nearestIn = Nearest(path, included);
        var nearestOut = Nearest(path, settings.Excluded);
        return nearestIn >= 0 && nearestIn > nearestOut;
    }

    /// <summary>Why <paramref name="entry"/> is not heard in the background, or None when it is.</summary>
    public static TranscriptionSkip Of(LibraryEntry entry, TranscriptionSettings settings, IReadOnlyList<string> libraryFolders, TranscriptRecord? record,
        bool audiobook = false)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return Uri.TryCreate(entry.Path, UriKind.Absolute, out var uri) && !uri.IsFile ? TranscriptionSkip.NotLocal
            : !InScope(entry.Path, settings, libraryFolders) ? TranscriptionSkip.OutOfScope
            : entry.Kind == LibraryKind.Picture ? TranscriptionSkip.Picture
            : StillDownloading(entry.Path, entry) ? TranscriptionSkip.Downloading
            : entry.Kind == LibraryKind.Music && !(settings.IncludeAudiobooks && audiobook)
                ? TranscriptionSkip.Music
            : record?.State == TranscriptState.NoSound ? TranscriptionSkip.NoSound
            // A video read before covers were told from moving pictures may be a song: read it again first.
            : !entry.Probed || (entry.Kind == LibraryKind.Video && entry.TagReading < MediaLibrary.TagReading) ? TranscriptionSkip.NotReadYet
            : entry.Duration is { } length && length < Shortest ? TranscriptionSkip.TooShort
            : TranscriptionSkip.None;
    }

    /// <summary>How a reason reads in a tooltip; empty for None.</summary>
    public static string Describe(TranscriptionSkip skip) => skip switch
    {
        TranscriptionSkip.OutOfScope => "Not in the folders chosen for transcription",
        TranscriptionSkip.Picture => "A picture has no speech",
        TranscriptionSkip.Music => "Songs are not transcribed; audiobooks can be included in Preferences",
        TranscriptionSkip.NotReadYet => "Waiting for the library to read its details",
        TranscriptionSkip.TooShort => "Too short to transcribe",
        TranscriptionSkip.NoSound => "It has no sound to transcribe",
        TranscriptionSkip.NotLocal => "Only files on this computer are transcribed",
        TranscriptionSkip.Downloading => "Waiting for it to finish downloading",
        _ => "",
    };

    /// <summary>
    /// The length of the nearest folder in <paramref name="folders"/> holding <paramref name="path"/>,
    /// or -1; where a folder is both chosen and left out, left out wins.
    /// </summary>
    private static int Nearest(string path, IReadOnlyList<string> folders) =>
        folders.Where(folder => MediaLibrary.Holds(folder, path)).Select(folder => folder.TrimEnd('\\', '/').Length).DefaultIfEmpty(-1).Max();
}
