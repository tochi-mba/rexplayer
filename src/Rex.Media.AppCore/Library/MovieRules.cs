using System.Text.RegularExpressions;
using Rex.Media.Library;

namespace Rex.Media.AppCore.Library;

/// <summary>What the online movie lookups said about a video's title.</summary>
public enum MovieLookup
{
    /// <summary>Not looked up: lookups are off, the folder is excluded, or its turn has not come.</summary>
    NotSearched,

    /// <summary>Looked up and matched to a film.</summary>
    Matched,

    /// <summary>Looked up, and nothing could be taken with confidence.</summary>
    Unmatched,
}

/// <summary>Where a video that is not a TV episode belongs, and the rule that put it there.</summary>
public enum VideoVerdict
{
    /// <summary>The person said "This is a movie".</summary>
    MovieByChoice,

    /// <summary>The movie lookups matched it to a film.</summary>
    MovieByMatch,

    /// <summary>A release-style name, folder or tags, and a feature's running time.</summary>
    MovieByEvidence,

    /// <summary>The person said "Not a movie".</summary>
    OtherByChoice,

    /// <summary>Named the way a camera or a phone names what it records ("IMG_1234", "PXL_...").</summary>
    CameraName,

    /// <summary>Named the way screen recorders name what they capture.</summary>
    ScreenRecording,

    /// <summary>A music video: marked as one, or "Artist - Title" and only a few minutes long.</summary>
    MusicVideo,

    /// <summary>Shorter than a feature film.</summary>
    ShortClip,

    /// <summary>Nothing says film: no match and no strong evidence.</summary>
    NoEvidence,
}

/// <summary>
/// Which videos are films (Movies) and which are everything else (Other videos). TV episodes are
/// <see cref="EpisodeNames"/>' business and are never judged here. The rules, in order:
/// <list type="number">
/// <item>The person's own choice ("This is a movie" or "Not a movie") always wins.</item>
/// <item>What plainly is not a film goes to Other videos: camera and phone names, screen
/// recordings, music videos and anything shorter than <see cref="FeatureLength"/>. A lookup is
/// never even asked about these, and a match found for one (an old cache, a homonym) does not
/// pull it in: a two-minute clip that shares a film's title is still a clip.</item>
/// <item>A lookup match makes the rest a film.</item>
/// <item>Without one, strong local evidence does: a release-style name or folder ("Title (2007)",
/// "Title.2007.1080p.BluRay.x264", "Title.2007"), or a title tag with a year, together with a
/// feature's running time. A file whose length is not known yet is judged by its name alone, so
/// a library still being read does not empty Movies.</item>
/// <item>Everything else is an other video.</item>
/// </list>
/// Nothing here is stored: the verdict is worked out from the entry and the lookups whenever a view
/// is built, so a library saved under older rules follows the current ones the moment it loads.
/// </summary>
public static partial class MovieRules
{
    /// <summary>
    /// The shortest feature film. Film academies and Wikidata's own feature-film class draw the line
    /// at 40 minutes; shorter work is a short, an episode or a clip.
    /// </summary>
    public static readonly TimeSpan FeatureLength = TimeSpan.FromMinutes(40);

    /// <summary>The longest an "Artist - Title" video can be and still be read as a music video.</summary>
    public static readonly TimeSpan MusicVideoLength = TimeSpan.FromMinutes(10);

    /// <summary>For views built without lookups: nothing has been searched.</summary>
    public static readonly Func<LibraryEntry, MovieLookup> Offline = _ => MovieLookup.NotSearched;

    /// <summary>One fixed answer per lookup state, so judging with a known answer allocates nothing.</summary>
    private static readonly Func<LibraryEntry, MovieLookup>[] Answers = [Offline, _ => MovieLookup.Matched, _ => MovieLookup.Unmatched];

    /// <summary>Whether a verdict puts the video in Movies.</summary>
    public static bool IsMovie(this VideoVerdict verdict) => verdict is VideoVerdict.MovieByChoice or VideoVerdict.MovieByMatch or VideoVerdict.MovieByEvidence;

    /// <summary>
    /// Where <paramref name="entry"/> belongs, given what the lookups said about it; null for what is
    /// not a video or is a positively identified TV episode.
    /// </summary>
    public static VideoVerdict? Judge(LibraryEntry entry, MovieLookup lookup) => Judge(entry, Answers[(int)lookup]);

    /// <summary>
    /// Where <paramref name="entry"/> belongs, asking <paramref name="lookup"/> only when the answer
    /// matters (most clips are settled by their names and lengths alone); null for what is not a
    /// standalone video.
    /// </summary>
    public static VideoVerdict? Judge(LibraryEntry entry, Func<LibraryEntry, MovieLookup> lookup)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(lookup);
        // A tentative episode (an extra, a "Special") is judged like any video; whether it joined a
        // show is the library's question (LibraryViews.Standalone), not this file's name.
        if (entry.Kind != LibraryKind.Video || EpisodeNames.Parse(entry.Path) is { Tentative: false })
        {
            return null;
        }

        if (entry.Override?.Movie is { } chosen)
        {
            return chosen ? VideoVerdict.MovieByChoice : VideoVerdict.OtherByChoice;
        }

        var normalized = entry.Path.Replace('\\', '/');
        var name = Path.GetFileNameWithoutExtension(normalized);
        var folder = Path.GetFileName(Path.GetDirectoryName(normalized)) ?? "";
        if (CameraPattern().IsMatch(name))
        {
            return VideoVerdict.CameraName;
        }

        if (ScreenPattern().IsMatch(name) || ObsPattern().IsMatch(name) || ScreenFolderPattern().IsMatch(folder))
        {
            return VideoVerdict.ScreenRecording;
        }

        if (MusicVideoMarkPattern().IsMatch(name) || (entry.Duration < MusicVideoLength && name.Contains(" - ", StringComparison.Ordinal)))
        {
            return VideoVerdict.MusicVideo;
        }

        if (entry.Duration < FeatureLength)
        {
            return VideoVerdict.ShortClip;
        }

        if (lookup(entry) == MovieLookup.Matched)
        {
            return VideoVerdict.MovieByMatch;
        }

        return HasReleaseEvidence(entry, name, folder) ? VideoVerdict.MovieByEvidence : VideoVerdict.NoEvidence;
    }

    /// <summary>Whether <paramref name="entry"/> is in Movies.</summary>
    public static bool IsMovie(LibraryEntry entry, MovieLookup lookup) => Judge(entry, lookup)?.IsMovie() == true;

    /// <summary>Whether <paramref name="entry"/> is in Movies, asking <paramref name="lookup"/> only when it matters.</summary>
    public static bool IsMovie(LibraryEntry entry, Func<LibraryEntry, MovieLookup> lookup) => Judge(entry, lookup)?.IsMovie() == true;

    /// <summary>Whether <paramref name="entry"/> is in Other videos.</summary>
    public static bool IsOther(LibraryEntry entry, MovieLookup lookup) => Judge(entry, lookup) is { } verdict && !verdict.IsMovie();

    /// <summary>Whether <paramref name="entry"/> is in Other videos, asking <paramref name="lookup"/> only when it matters.</summary>
    public static bool IsOther(LibraryEntry entry, Func<LibraryEntry, MovieLookup> lookup) => Judge(entry, lookup) is { } verdict && !verdict.IsMovie();

    /// <summary>
    /// Whether the lookups should ask about <paramref name="entry"/>: only when a match would make it
    /// a film. Camera clips, screen recordings, music videos, short clips, "Not a movie" and episodes
    /// are never sent to a service.
    /// </summary>
    public static bool WorthLookingUp(LibraryEntry entry) => Judge(entry, MovieLookup.Matched) is VideoVerdict.MovieByMatch or VideoVerdict.MovieByChoice;

    /// <summary>
    /// Whether a film in Movies should say "Not matched" and offer "Find the film...": the lookups
    /// searched and found nothing, yet it is a film by evidence or by the person's choice.
    /// </summary>
    public static bool NotMatched(LibraryEntry entry, MovieLookup lookup) => lookup == MovieLookup.Unmatched && IsMovie(entry, lookup);

    private static bool HasReleaseEvidence(LibraryEntry entry, string name, string folder) =>
        BracketYearPattern().IsMatch(name) || ReleaseTagsPattern().IsMatch(name) || SceneYearPattern().IsMatch(name)
        || BracketYearPattern().IsMatch(folder)
        || (entry.Year is not null && entry.Title != MediaLibrary.TitleOf(entry.Path));

    /// <summary>"IMG_1234", "VID_20240101_101010", "PXL_2024...", "MVI_0042", "DSC_0001", "GOPR0001", "GH010123", "DJI_0001", "20240101_101010".</summary>
    [GeneratedRegex(@"^(?:(?:IMG|VID|PXL|MVI|MOV|MAH|DSC[FN]?|DJI|GOPR|GP|G[HX]\d{2})[_\- ]?\d|\d{8}_\d{6})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CameraPattern();

    /// <summary>"Screen Recording 2024-01-05 at 10.00.00", "ScreenRecording_01", "Screencast", "Capture 3", "Recording 12".</summary>
    [GeneratedRegex(@"(?:^|[ _\-])screen[ _\-]?(?:recording|rec|capture|cast)|^screencast|^(?:capture|recording)(?:$|[ _\-]*\d+$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScreenPattern();

    /// <summary>The name a screen recorder gives by default: the date and time, "2024-01-05 10-00-00".</summary>
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}[ _]\d{2}-\d{2}-\d{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex ObsPattern();

    /// <summary>The folders screen recorders save into: "Captures", "Screen Recordings", "Screencasts".</summary>
    [GeneratedRegex(@"^(?:captures|screen ?recordings|screencasts)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScreenFolderPattern();

    /// <summary>"(Official Video)", "[Official Music Video]", "(Lyric Video)", "(Visualizer)", "(Official Audio)".</summary>
    [GeneratedRegex(@"[(\[](?:official (?:music )?video|official audio|lyrics? video|lyrics|visuali[sz]er)[)\]]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MusicVideoMarkPattern();

    /// <summary>A year in brackets after a title: "Title (2007)", "Title [2007] 1080p".</summary>
    [GeneratedRegex(@"\w.*[(\[](?:19|20)\d{2}[)\]]", RegexOptions.CultureInvariant)]
    private static partial Regex BracketYearPattern();

    /// <summary>A title, a year, then what a release names: "Title.2007.1080p.BluRay.x264", "Title 2007 WEB-DL".</summary>
    [GeneratedRegex(@"\w.*?[ ._\-](?:19|20)\d{2}[ ._\-].*?\b(?:2160p|1080p|720p|576p|480p|4K|UHD|BluRay|Blu-Ray|BDRip|BRRip|WEBRip|WEB[ ._\-]?DL|HDRip|DVDRip|HDTV|REMUX|x264|x265|H[ ._]?26[45]|HEVC|AVC|HDR)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseTagsPattern();

    /// <summary>A dotted release name ending in its year: "The.Matrix.1999".</summary>
    [GeneratedRegex(@"^[^ .]+(?:\.[^ .]+)*\.(?:19|20)\d{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex SceneYearPattern();
}
