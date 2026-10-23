using System.Text.RegularExpressions;
using Rex.Media.Library;
using Rex.Media.Primitives;

namespace Rex.Media.AppCore.Library;

/// <summary>What the online music lookups said about the album an audio file is on.</summary>
public enum MusicLookup
{
    /// <summary>Not looked up: lookups are off, the folder is excluded, the album has art of its own, or its turn has not come.</summary>
    NotSearched,

    /// <summary>Looked up and matched to a release in the catalogue.</summary>
    Matched,

    /// <summary>Looked up, and nothing could be taken with confidence.</summary>
    Unmatched,
}

/// <summary>Where an audio file belongs.</summary>
public enum AudioShelf
{
    /// <summary>Songs, Albums, Artists and Genres.</summary>
    Music,

    /// <summary>The Audiobooks view: books, each gathered from its parts.</summary>
    Audiobooks,

    /// <summary>The Podcasts view: episodes, gathered by show.</summary>
    Podcasts,

    /// <summary>The Recordings view: voice memos and what recorders save.</summary>
    Recordings,

    /// <summary>Clips, ringtones and sound effects, a small group at the foot of the Recordings view.</summary>
    Sounds,
}

/// <summary>The rule that decided an audio file's shelf, in the order the rules are weighed.</summary>
public enum AudioRule
{
    /// <summary>1. The person moved it ("Move to Music", "Move to Audiobooks" and the rest).</summary>
    Choice,

    /// <summary>2. An .m4b file.</summary>
    Format,

    /// <summary>2 and 3. Its genre: Audiobook, Audio book, Spoken word or Speech; or Podcast.</summary>
    Genre,

    /// <summary>2 and 3. The tags' own mark of an audiobook or a podcast episode (the media kind, the podcast flag, a feed address).</summary>
    MediaMark,

    /// <summary>4. The music lookups matched its album.</summary>
    Match,

    /// <summary>5. The folder it is in, at any depth.</summary>
    Folder,

    /// <summary>6. Its name: a recorder's, or a book part's on a long tagged file.</summary>
    Name,

    /// <summary>6. An episode code ("Ep 12", "#123", "E045") that files beside it share.</summary>
    EpisodeCode,

    /// <summary>6. Chapter marks on a long file.</summary>
    Chapters,

    /// <summary>6. No music tags, in a recorder's format, and shorter than <see cref="AudioRules.LongestRecording"/>.</summary>
    RecorderFormat,

    /// <summary>7. Music tags and a song's length, or a long mix's.</summary>
    Tags,

    /// <summary>8. Nothing else applied: music tags make it music, a short clip a sound, the rest a recording.</summary>
    Fallback,
}

/// <summary>An audio file's shelf and the rule that put it there.</summary>
public readonly record struct AudioVerdict(AudioShelf Shelf, AudioRule Rule)
{
    /// <summary>Whether it is music.</summary>
    public bool IsMusic => Shelf == AudioShelf.Music;
}

/// <summary>
/// What the rules need to know about the library as a whole: the folders where several files carry
/// an episode code, which makes them a podcast's episodes (one file alone is not enough).
/// </summary>
public sealed class AudioContext
{
    /// <summary>A library with nothing in it to compare: no folder of episodes or related book parts.</summary>
    public static readonly AudioContext Empty = new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.Ordinal));

    private readonly HashSet<string> _episodeFolders;
    private readonly HashSet<string> _audiobookAlbums;

    private AudioContext(HashSet<string> episodeFolders, HashSet<string> audiobookAlbums)
    {
        _episodeFolders = episodeFolders;
        _audiobookAlbums = audiobookAlbums;
    }

    /// <summary>The context of <paramref name="entries"/>.</summary>
    public static AudioContext Of(IEnumerable<LibraryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var audio = entries.Where(entry => entry.Kind == LibraryKind.Music).ToList();
        return new AudioContext(
            [.. audio.Where(AudioRules.HasEpisodeCode)
                .GroupBy(entry => AudioRules.FolderOf(entry.Path), StringComparer.OrdinalIgnoreCase)
                .Where(folder => folder.Count() > 1)
                .Select(folder => folder.Key)],
            [.. audio.Where(AudioRules.IsAudiobookAnchor)
                .Select(AudioRules.AlbumKey)
                .Where(key => key is not null)
                .Select(key => key!) ]);
    }

    /// <summary>Whether several files in <paramref name="folder"/> carry an episode code.</summary>
    public bool IsEpisodeFolder(string folder) => _episodeFolders.Contains(folder);

    /// <summary>Whether another file on this album positively identifies the collection as a book.</summary>
    public bool IsAudiobookAlbum(LibraryEntry entry) =>
        AudioRules.AlbumKey(entry) is { } key && _audiobookAlbums.Contains(key);
}

/// <summary>
/// Which audio files are music and which are audiobooks, podcasts, recordings or sounds. Videos and
/// pictures are never judged here, so the sound inside a video, a video podcast included, stays with
/// its video. Every audio file lands on exactly one shelf. The first rule that applies wins:
/// <list type="number">
/// <item>The person's own move.</item>
/// <item>Audiobooks: an .m4b, an Audiobook, Audio book, Spoken word or Speech genre, or the tags'
/// audiobook mark.</item>
/// <item>Podcasts: a Podcast genre, or the tags' podcast flag or feed address.</item>
/// <item>Music: a match from the music lookups.</item>
/// <item>The folder, at any depth: Audiobooks, Audio books or Books; Podcasts; Voice Memos,
/// Recordings, Voice Notes or WhatsApp Voice Notes; Ringtones, Notifications, Alarms, Samples, SFX
/// or Sound effects. Music tags with a song's length beat a Recordings or Sounds folder (a song kept
/// among recordings is still a song), but not an Audiobooks or Podcasts folder.</item>
/// <item>The name and marks: a recorder's name; "Part 3 of 12", "Chapter 7", "CD1" or "Disc 2" on a
/// file over <see cref="LongestSong"/> with a book title and author but no track number; an episode
/// code on a file over <see cref="ShortestEpisode"/> that files beside it share; chapter marks on a
/// file over <see cref="LongestSong"/>; no music tags in a recorder's format (.m4a, .amr, .3gp,
/// .opus) under <see cref="LongestRecording"/>.</item>
/// <item>Music tags with a length from <see cref="ShortestSong"/> to <see cref="LongestSong"/>;
/// longer tagged files stay music as long mixes and sets (<see cref="IsLongMix"/>).</item>
/// <item>The fallback: music tags make it music; else under <see cref="LongestSound"/> it is a sound;
/// else a recording. A file whose details are not read yet stays music until they are, so a library
/// being read does not empty Songs.</item>
/// </list>
/// Music tags are an artist with a title or an album, or a track number; a file named
/// "Artist - Title" counts as tagged. Nothing here is stored: the verdict is worked out from the entry
/// whenever a view is built, so a library saved under older rules follows the current ones at once.
/// </summary>
public static partial class AudioRules
{
    /// <summary>The shortest song: shorter tagged files are music only by the fallback.</summary>
    public static readonly TimeSpan ShortestSong = TimeSpan.FromSeconds(30);

    /// <summary>The longest single song; tagged music beyond it is a long mix or set.</summary>
    public static readonly TimeSpan LongestSong = TimeSpan.FromMinutes(20);

    /// <summary>The longest sound: notification tones, effects and samples are shorter.</summary>
    public static readonly TimeSpan LongestSound = TimeSpan.FromSeconds(10);

    /// <summary>The shortest file an episode code counts on.</summary>
    public static readonly TimeSpan ShortestEpisode = TimeSpan.FromMinutes(10);

    /// <summary>The longest file a recorder's format alone makes a recording.</summary>
    public static readonly TimeSpan LongestRecording = TimeSpan.FromHours(2);

    /// <summary>For views built without lookups: nothing has been searched.</summary>
    public static readonly Func<LibraryEntry, MusicLookup> Offline = _ => MusicLookup.NotSearched;

    /// <summary>
    /// Where <paramref name="entry"/> belongs, given what the lookups said about its album and the
    /// library around it; null for what is not an audio file (videos and pictures).
    /// </summary>
    public static AudioVerdict? Judge(LibraryEntry entry, MusicLookup lookup, AudioContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Kind != LibraryKind.Music)
        {
            return null;
        }

        if (ChosenShelf(entry.Override?.Audio) is { } chosen)
        {
            return new AudioVerdict(chosen, AudioRule.Choice);
        }

        var normalized = entry.Path.Replace('\\', '/');
        var extension = Path.GetExtension(normalized);
        var genres = GenreNames.Split(entry.Genre);
        if (string.Equals(extension, ".m4b", StringComparison.OrdinalIgnoreCase))
        {
            return new AudioVerdict(AudioShelf.Audiobooks, AudioRule.Format);
        }

        if (genres.Any(AudiobookGenrePattern().IsMatch))
        {
            return new AudioVerdict(AudioShelf.Audiobooks, AudioRule.Genre);
        }

        if (string.Equals(entry.MediaType, MetadataKeys.Audiobook, StringComparison.OrdinalIgnoreCase))
        {
            return new AudioVerdict(AudioShelf.Audiobooks, AudioRule.MediaMark);
        }

        // Common audiobook rips are MP3 or M4A without the dedicated media-kind tag.
        if (BookLabelPattern().IsMatch(MediaLibrary.TitleOf(entry.Path))
            || (entry.Title is { } title && BookLabelPattern().IsMatch(title))
            || (entry.Album is { } album && BookLabelPattern().IsMatch(album)))
        {
            return new AudioVerdict(AudioShelf.Audiobooks, AudioRule.Name);
        }

        if ((context ?? AudioContext.Empty).IsAudiobookAlbum(entry)
            || AudiobookPartPattern().IsMatch(MediaLibrary.TitleOf(entry.Path))
            || AudiobookPartPattern().IsMatch(entry.Title))
        {
            return new AudioVerdict(AudioShelf.Audiobooks, AudioRule.Name);
        }

        if (genres.Any(PodcastGenrePattern().IsMatch))
        {
            return new AudioVerdict(AudioShelf.Podcasts, AudioRule.Genre);
        }

        if (string.Equals(entry.MediaType, MetadataKeys.Podcast, StringComparison.OrdinalIgnoreCase))
        {
            return new AudioVerdict(AudioShelf.Podcasts, AudioRule.MediaMark);
        }

        if (lookup == MusicLookup.Matched)
        {
            return new AudioVerdict(AudioShelf.Music, AudioRule.Match);
        }

        var tagged = HasMusicTags(entry);
        var songLength = entry.Duration is { } length && length >= ShortestSong && length <= LongestSong;
        if (FolderShelf(normalized) is { } byFolder && (byFolder is AudioShelf.Audiobooks or AudioShelf.Podcasts || !(tagged && songLength)))
        {
            return new AudioVerdict(byFolder, AudioRule.Folder);
        }

        if (MarkedByName(entry, normalized, extension, tagged, context ?? AudioContext.Empty) is { } byName)
        {
            return byName;
        }

        if (tagged && entry.Duration >= ShortestSong)
        {
            return new AudioVerdict(AudioShelf.Music, AudioRule.Tags);
        }

        return new AudioVerdict(
            tagged || !entry.Probed ? AudioShelf.Music : entry.Duration < LongestSound ? AudioShelf.Sounds : AudioShelf.Recordings,
            AudioRule.Fallback);
    }

    /// <summary>Whether <paramref name="entry"/> is music.</summary>
    public static bool IsMusic(LibraryEntry entry, MusicLookup lookup, AudioContext? context = null) => Judge(entry, lookup, context)?.IsMusic == true;

    /// <summary>The entries without the audio that is not music: music, videos and pictures, in the order given.</summary>
    public static IReadOnlyList<LibraryEntry> WithoutOtherAudio(IReadOnlyCollection<LibraryEntry> entries, Func<LibraryEntry, MusicLookup> lookup)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(lookup);
        var context = AudioContext.Of(entries);
        return [.. entries.Where(entry => Judge(entry, lookup(entry), context) is null or { IsMusic: true })];
    }

    /// <summary>
    /// Whether the music lookups should ask about <paramref name="entry"/>'s album: only when a match
    /// would make it music. Audiobooks, podcasts and what the person moved out of Music are never sent.
    /// </summary>
    public static bool WorthLookingUp(LibraryEntry entry) => Judge(entry, MusicLookup.Matched)?.IsMusic == true;

    /// <summary>
    /// Whether music is a long mix or set: tagged and longer than <see cref="LongestSong"/> (a DJ mix,
    /// a live set, a long classical movement). It is grouped as "Long mixes and sets" and kept out of
    /// the most-played rankings, so one long play does not outweigh a hundred songs.
    /// </summary>
    public static bool IsLongMix(LibraryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Duration > LongestSong && HasMusicTags(entry);
    }

    /// <summary>The entries a most-played ranking weighs: all but the long mixes and sets.</summary>
    public static IEnumerable<LibraryEntry> Ranked(IEnumerable<LibraryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return entries.Where(entry => !IsLongMix(entry));
    }

    /// <summary>The store's name for <paramref name="shelf"/>, for <see cref="DetailOverride.Audio"/>.</summary>
    public static string StoredName(AudioShelf shelf) => shelf switch
    {
        AudioShelf.Audiobooks => AudioShelves.Audiobooks,
        AudioShelf.Podcasts => AudioShelves.Podcasts,
        AudioShelf.Recordings => AudioShelves.Recordings,
        AudioShelf.Sounds => AudioShelves.Sounds,
        _ => AudioShelves.Music,
    };

    /// <summary>Whether <paramref name="entry"/>'s name carries an episode code: "Ep 12", "Episode 3", "#123", "E045".</summary>
    internal static bool HasEpisodeCode(LibraryEntry entry) => EpisodeCodePattern().IsMatch(MediaLibrary.TitleOf(entry.Path));

    /// <summary>The folder a file is in, the same whichever way its separators lean.</summary>
    internal static string FolderOf(string path) => path.Replace('\\', '/') is var normalized && normalized.LastIndexOf('/') is var slash and >= 0 ? normalized[..slash] : "";

    /// <summary>A stable album-and-folder key; the folder prevents unrelated albums with the same title joining.</summary>
    internal static string? AlbumKey(LibraryEntry entry) => string.IsNullOrWhiteSpace(entry.Album) ? null
        : ArtistNames.Key(entry.Album) + "\n" + FolderOf(entry.Path).ToUpperInvariant();

    /// <summary>Whether this file is strong evidence that all files on its album are audiobook parts.</summary>
    internal static bool IsAudiobookAnchor(LibraryEntry entry)
    {
        var extension = Path.GetExtension(entry.Path);
        var name = MediaLibrary.TitleOf(entry.Path);
        return string.Equals(extension, ".m4b", StringComparison.OrdinalIgnoreCase)
            || GenreNames.Split(entry.Genre).Any(AudiobookGenrePattern().IsMatch)
            || string.Equals(entry.MediaType, MetadataKeys.Audiobook, StringComparison.OrdinalIgnoreCase)
            || BookLabelPattern().IsMatch(name)
            || (entry.Title is { } title && (BookLabelPattern().IsMatch(title) || AudiobookPartPattern().IsMatch(title)))
            || (entry.Album is { } album && BookLabelPattern().IsMatch(album))
            || IsWholeBook(entry);
    }

    private static AudioShelf? ChosenShelf(string? stored) => stored switch
    {
        AudioShelves.Music => AudioShelf.Music,
        AudioShelves.Audiobooks => AudioShelf.Audiobooks,
        AudioShelves.Podcasts => AudioShelf.Podcasts,
        AudioShelves.Recordings => AudioShelf.Recordings,
        AudioShelves.Sounds => AudioShelf.Sounds,
        _ => null,
    };

    /// <summary>The shelf the folders a file is in name, the nearest first; null when none does.</summary>
    private static AudioShelf? FolderShelf(string normalized)
    {
        foreach (var folder in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)[..^1].Reverse())
        {
            if (AudiobookFolderPattern().IsMatch(folder))
            {
                return AudioShelf.Audiobooks;
            }

            if (PodcastFolderPattern().IsMatch(folder))
            {
                return AudioShelf.Podcasts;
            }

            if (RecordingFolderPattern().IsMatch(folder))
            {
                return AudioShelf.Recordings;
            }

            if (SoundFolderPattern().IsMatch(folder))
            {
                return AudioShelf.Sounds;
            }
        }

        return null;
    }

    /// <summary>Rule 6: the name and marks; null when none applies.</summary>
    private static AudioVerdict? MarkedByName(LibraryEntry entry, string normalized, string extension, bool tagged, AudioContext context)
    {
        var name = MediaLibrary.TitleOf(entry.Path);
        if (RecorderPattern().IsMatch(name) || MessengerPattern().IsMatch(name))
        {
            return new AudioVerdict(AudioShelf.Recordings, AudioRule.Name);
        }

        var longFile = entry.Duration > LongestSong;
        if (longFile && entry.Track is null && entry.Album is not null && entry.FiledArtist is not null && BookPartPattern().IsMatch(name))
        {
            return new AudioVerdict(AudioShelf.Audiobooks, AudioRule.Name);
        }

        if (entry.Duration > ShortestEpisode && EpisodeCodePattern().IsMatch(name) && context.IsEpisodeFolder(FolderOf(normalized)))
        {
            return new AudioVerdict(AudioShelf.Podcasts, AudioRule.EpisodeCode);
        }

        if (longFile && entry.Chapters is { Count: > 1 })
        {
            return new AudioVerdict(AudioShelf.Audiobooks, AudioRule.Chapters);
        }

        // A whole book in one file may only have author/title and album tags. Use a conservative
        // fallback so a long DJ mix or numbered track does not become a book.
        if (IsWholeBook(entry))
        {
            return new AudioVerdict(AudioShelf.Audiobooks, AudioRule.Name);
        }

        return !tagged && entry.Duration < LongestRecording && RecorderFormatPattern().IsMatch(extension)
            ? new AudioVerdict(AudioShelf.Recordings, AudioRule.RecorderFormat)
            : null;
    }

    /// <summary>Music tags: an artist with a title of its own or an album, or a track number; or a name shaped "Artist - Title".</summary>
    private static bool HasMusicTags(LibraryEntry entry) =>
        entry.Track is not null
        || ((entry.Artist ?? entry.AlbumArtist) is not null && (entry.Album is not null || entry.Title != MediaLibrary.TitleOf(entry.Path)))
        || SongNamePattern().IsMatch(MediaLibrary.TitleOf(entry.Path));

    private static bool IsWholeBook(LibraryEntry entry) => entry.Duration >= TimeSpan.FromHours(2) && entry.Track is null
        && entry.Genre is null && entry.FiledArtist is not null && entry.Album is not null
        && ArtistNames.Key(entry.Title) == ArtistNames.Key(entry.Album)
        && !MusicSetPattern().IsMatch(entry.Title) && !MusicSetPattern().IsMatch(entry.Album);

    /// <summary>The audiobook genres: "Audiobook", "Audiobooks", "Audio book", "Spoken word", "Spoken &amp; Audio", "Speech".</summary>
    [GeneratedRegex(@"^(?:audio[ _-]?books?|books? & spoken(?: word)?|spoken(?: ?word| & audio)?|speech)(?::\s*\S.*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AudiobookGenrePattern();

    /// <summary>Explicit audiobook wording in an album, title or filename.</summary>
    [GeneratedRegex(@"(?:^|[^\p{L}])(?:audio[ _.-]*books?|unabridged|abridged)(?:$|[^\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BookLabelPattern();

    /// <summary>Common ripped-book names: "(Roald Dahl) Charlie ... (Part 3) - 01".</summary>
    [GeneratedRegex(@"^\s*\([^)]*[\p{L}][^)]*\)\s*.+(?:\(|\b)(?:part|pt|chapter|ch|disc|disk|cd)[ _.-]*\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AudiobookPartPattern();

    /// <summary>A live concert or DJ mix is not inferred to be an audiobook from its length.</summary>
    [GeneratedRegex(@"(?:^|[^\p{L}])(?:mix|dj|live|concert|set|session|compilation)(?:$|[^\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MusicSetPattern();

    /// <summary>"Podcast", "Podcasts".</summary>
    [GeneratedRegex(@"^podcasts?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PodcastGenrePattern();

    /// <summary>The folders books are kept in: "Audiobooks", "Audio Books", "Books".</summary>
    [GeneratedRegex(@"(?:^|[^\p{L}])audio[\s_.-]*books?(?:$|[^\p{L}])|^books$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AudiobookFolderPattern();

    /// <summary>The folders podcast apps download into: "Podcasts".</summary>
    [GeneratedRegex(@"^podcasts?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PodcastFolderPattern();

    /// <summary>The folders recorders save into: "Voice Memos", "Recordings", "Voice Notes", "WhatsApp Voice Notes".</summary>
    [GeneratedRegex(@"^(?:voice ?memos|recordings|voice ?notes|whatsapp voice notes)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RecordingFolderPattern();

    /// <summary>The folders sounds live in: "Ringtones", "Notifications", "Alarms", "Samples", "SFX", "Sound effects".</summary>
    [GeneratedRegex(@"^(?:ringtones|notifications|alarms|samples|sfx|sound ?effects)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SoundFolderPattern();

    /// <summary>
    /// Recorders' names: "Recording", "Recording (2)" (the Sound Recorder's), "Recording 001", "New
    /// Recording 3", "Voice 003", "Voice_012", "REC_20240105_101010", "REC001".
    /// </summary>
    [GeneratedRegex(@"^(?:new[ _])?(?:recording(?:[ _]?\(\d+\))?|(?:recording|voice|rec)[ _\-]*\d+(?:[ _\-.]\d+)*)$|^rec_", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RecorderPattern();

    /// <summary>A messenger's audio and voice notes: "AUD-20240105-WA0001", "PTT-20240105-WA0003".</summary>
    [GeneratedRegex(@"^(?:AUD|PTT)-\d{8}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MessengerPattern();

    /// <summary>A book part's name: "Part 03 of 12", "Pt 2", "Chapter 7", "CD1", "Disc 2".</summary>
    [GeneratedRegex(@"(?:^|[^\p{L}])(?:(?:part|pt)[ _\-.]*\d+(?:[ _\-]*of[ _\-]*\d+)?|chapter[ _\-.]*\d+|(?:cd|dis[ck])[ _\-.]*\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BookPartPattern();

    /// <summary>An episode code: "Ep 12", "Ep.12", "Episode 3", "#123", "E045".</summary>
    [GeneratedRegex(@"(?:^|[^\p{L}\d])(?:ep(?:isode)?[ ._\-]*\d+|#\d+|e\d{2,4})(?:$|[^\p{L}\d])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EpisodeCodePattern();

    /// <summary>The formats phones and recorders save in: .m4a, .amr, .3gp, .opus.</summary>
    [GeneratedRegex(@"^\.(?:m4a|amr|3gp|opus)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RecorderFormatPattern();

    /// <summary>A name shaped like a song: "Artist - Title".</summary>
    [GeneratedRegex(@"^\S.*\s-\s.*\S$", RegexOptions.CultureInvariant)]
    private static partial Regex SongNamePattern();
}
