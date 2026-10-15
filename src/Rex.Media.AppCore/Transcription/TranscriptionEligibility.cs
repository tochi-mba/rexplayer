using Rex.Media.Library;
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

    /// <summary>A song, which is sung rather than spoken, unless music is included.</summary>
    Music,

    /// <summary>Its details are not read yet; it is looked at again once they are.</summary>
    NotReadYet,

    /// <summary>Too short to hold speech worth a subtitle.</summary>
    TooShort,

    /// <summary>It turned out to have no sound.</summary>
    NoSound,

    /// <summary>Not a file on this computer.</summary>
    NotLocal,
}

/// <summary>
/// Which of the library's files are heard in the background (SUB-20). The scope is the whole
/// library or the chosen folders, less the folders left out; where a chosen folder and a left-out
/// one hold each other, the one nearer the file wins. Pictures are never heard, songs only when
/// music is included, and a file that turned out to have no sound is not tried again.
/// </summary>
public static class TranscriptionEligibility
{
    /// <summary>Anything shorter is passed over.</summary>
    public static readonly TimeSpan Shortest = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Whether the item playing should be heard automatically. A direct request always wins, but
    /// the broad "what plays" setting applies only to media with pictures: opening a song must not
    /// silently spend processor time turning lyrics into captions. A library item already selected
    /// by the background policy remains eligible (including music the user explicitly included).
    /// </summary>
    public static bool Playing(bool asked, bool hasPictures, bool whatPlays, bool backgroundEligible) =>
        asked || (hasPictures && whatPlays) || backgroundEligible;

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
    public static TranscriptionSkip Of(LibraryEntry entry, TranscriptionSettings settings, IReadOnlyList<string> libraryFolders, TranscriptRecord? record)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return Uri.TryCreate(entry.Path, UriKind.Absolute, out var uri) && !uri.IsFile ? TranscriptionSkip.NotLocal
            : !InScope(entry.Path, settings, libraryFolders) ? TranscriptionSkip.OutOfScope
            : entry.Kind == LibraryKind.Picture ? TranscriptionSkip.Picture
            : entry.Kind == LibraryKind.Music && !settings.IncludeMusic ? TranscriptionSkip.Music
            : record?.State == TranscriptState.NoSound ? TranscriptionSkip.NoSound
            : !entry.Probed ? TranscriptionSkip.NotReadYet
            : entry.Duration is { } length && length < Shortest ? TranscriptionSkip.TooShort
            : TranscriptionSkip.None;
    }

    /// <summary>How a reason reads in a tooltip; empty for None.</summary>
    public static string Describe(TranscriptionSkip skip) => skip switch
    {
        TranscriptionSkip.OutOfScope => "Not in the folders chosen for transcription",
        TranscriptionSkip.Picture => "A picture has no speech",
        TranscriptionSkip.Music => "Music is not transcribed unless you include it in Preferences",
        TranscriptionSkip.NotReadYet => "Waiting for the library to read its details",
        TranscriptionSkip.TooShort => "Too short to transcribe",
        TranscriptionSkip.NoSound => "It has no sound to transcribe",
        TranscriptionSkip.NotLocal => "Only files on this computer are transcribed",
        _ => "",
    };

    /// <summary>
    /// The length of the nearest folder in <paramref name="folders"/> holding <paramref name="path"/>,
    /// or -1; where a folder is both chosen and left out, left out wins.
    /// </summary>
    private static int Nearest(string path, IReadOnlyList<string> folders) =>
        folders.Where(folder => MediaLibrary.Holds(folder, path)).Select(folder => folder.TrimEnd('\\', '/').Length).DefaultIfEmpty(-1).Max();
}
