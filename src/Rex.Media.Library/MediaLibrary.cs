using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rex.Media.Primitives;

namespace Rex.Media.Library;

/// <summary>What kind of media a library file holds.</summary>
public enum LibraryKind
{
    Music,
    Video,
    Picture,
}

/// <summary>A file as the disk describes it: its size and when it last changed.</summary>
public readonly record struct LibraryFile(string Path, long Size, DateTime Modified)
{
    /// <summary>The file is sparse: laid out at its full size with parts not yet written, as torrent clients make them.</summary>
    public bool Sparse { get; init; }

    /// <summary>The file is a cloud placeholder whose bytes are not on this computer; it is never opened to look at it, since that would fetch it.</summary>
    public bool Placeholder { get; init; }
}

/// <summary>A named point in a library file: where a chapter starts, and its title.</summary>
public sealed record LibraryChapter(TimeSpan Start, string Title)
{
    /// <summary>Chapters as <see cref="LibraryEntry.ChapterMarks"/> keeps them.</summary>
    public static string Encode(IEnumerable<LibraryChapter> chapters) =>
        string.Join('\n', chapters.Select(chapter => chapter.Start.Ticks.ToString(CultureInfo.InvariantCulture) + "\t" + chapter.Title.ReplaceLineEndings(" ").Replace('\t', ' ')));

    /// <summary>The chapters <see cref="Encode"/> wrote; a line that is not one is passed over.</summary>
    public static IReadOnlyList<LibraryChapter> Decode(string marks) =>
        [.. marks.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t', 2))
            .Where(parts => parts.Length == 2 && long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            .Select(parts => new LibraryChapter(TimeSpan.FromTicks(long.Parse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture)), parts[1]))];
}

/// <summary>What a scan of a folder found against what the library knew.</summary>
public readonly record struct ScanResult(int Added, int Changed, int Removed)
{
    public bool Any => Added + Changed + Removed > 0;
}

/// <summary>
/// One file of the library (LIB-05): where it is, what kind, its details once read (until then its
/// title is its file name and <see cref="Probed"/> is false), and how often and when it was played.
/// </summary>
public sealed record LibraryEntry
{
    public required string Path { get; init; }

    public required LibraryKind Kind { get; init; }

    public long Size { get; init; }

    /// <summary>When the file last changed, in UTC; a change means its details are read again.</summary>
    public DateTime Modified { get; init; }

    /// <summary>When the library first found it, in UTC.</summary>
    public DateTime Added { get; init; }

    /// <summary>Whether its details have been read (or found unreadable).</summary>
    public bool Probed { get; init; }

    /// <summary>Why its details could not be read (a damaged file, say), in plain words; null when they were, or are yet to be.</summary>
    public string? Problem { get; init; }

    public required string Title { get; init; }

    public string? Artist { get; init; }

    /// <summary>
    /// The tag's own list of the song's artists (ARTISTS in Vorbis comments, TXXX:ARTISTS in ID3 and
    /// the MP4 freeform item), separate values joined with "; "; null when the file has none.
    /// </summary>
    public string? Artists { get; init; }

    public string? Album { get; init; }

    public string? AlbumArtist { get; init; }

    /// <summary>Audiobook writing credit from AUTHOR/BOOKAUTHOR, kept apart from narrator and music artist.</summary>
    public string? Author { get; init; }

    /// <summary>The credited narrator/reader, never automatically treated as the writer.</summary>
    public string? Narrator { get; init; }

    public string? Genre { get; init; }

    public int? Track { get; init; }

    public int? Disc { get; init; }

    public int? Year { get; init; }

    public TimeSpan? Duration { get; init; }

    /// <summary>Picture pixel dimensions in display orientation, when a decoder reports them.</summary>
    public int? PictureWidth { get; init; }

    public int? PictureHeight { get; init; }

    public int Plays { get; init; }

    /// <summary>When it was last played, in UTC.</summary>
    public DateTime? LastPlayed { get; init; }

    /// <summary>Details the person changed that rexplayer keeps over the file's own tags.</summary>
    public DetailOverride? Override { get; init; }

    /// <summary>
    /// Whether the file is still being downloaded into its folder (<see cref="Downloads"/>): named or
    /// marked as partial by a downloader, short of a declared size, or growing between looks.
    /// </summary>
    public bool Downloading { get; init; }

    /// <summary>How many bytes it will be once whole, when a downloader's sidecar or its container says; null when nothing does, or it is whole.</summary>
    public long? FinalSize { get; init; }

    /// <summary>
    /// The version of rexplayer's tag reading that filled in the details (<see cref="MediaLibrary.TagReading"/>);
    /// music read by an older one is read again in the background, after every file never read at all.
    /// </summary>
    public int TagReading { get; init; }

    /// <summary>
    /// The chapters the file marks (an audiobook's, most often), empty when it marks none; null until
    /// it is read by a rexplayer that keeps them (<see cref="MediaLibrary.TagReading"/> 2).
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<LibraryChapter>? Chapters
    {
        get => ChapterMarks is null ? null : LibraryChapter.Decode(ChapterMarks);
        init => ChapterMarks = value is null ? null : LibraryChapter.Encode(value);
    }

    /// <summary>
    /// <see cref="Chapters"/> as the store keeps them: a line a chapter, its start in ticks and its
    /// title apart by a tab; empty for none, null when not read. Text, so two entries alike are equal.
    /// </summary>
    public string? ChapterMarks { get; init; }

    /// <summary>What the tags say the file is when it is not music ("podcast", "audiobook"); null when they say nothing.</summary>
    public string? MediaType { get; init; }

    /// <summary>For a sparse file still filling in, how many of its bytes are on the disk; null otherwise.</summary>
    public long? Allocated { get; init; }

    /// <summary>A weak sign of a download (growth, a rewrite, a writer) was seen at the last look and waits for a second.</summary>
    public bool Suspect { get; init; }

    /// <summary>The artist an album is filed under: its album artist, else its artist.</summary>
    [JsonIgnore]
    public string? FiledArtist => AlbumArtist ?? Artist;
}

/// <summary>
/// The media library (LIB-05): the folders the user chose to watch and the music and videos in
/// them, kept in a <see cref="RexStore"/> of its own. Scans compare a folder's files with what is
/// known, by size and change time, so only new and changed files are read again; reading their
/// details (<see cref="SetDetails(string, MediaInfo?)"/>) is left to whoever scans, at their own pace. Safe to use from
/// a scanning thread and the window at once.
/// </summary>
public sealed class MediaLibrary
{
    private const string FoldersKey = "folders";
    private const string HiddenKey = "hidden";
    private const string EntryPrefix = "entry/";

    /// <summary>
    /// The version of the tag reading that fills in a song's details. It goes up when a reader starts
    /// keeping something it used to drop (1: every artist value and the tag's list of artists; 2: the
    /// chapters and the podcast and audiobook marks), so music read before is read again in the
    /// background rather than waiting for a rescan.
    /// </summary>
    public const int TagReading = 4;

    private readonly object _gate = new();
    private readonly Dictionary<string, LibraryEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hidden = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _time;
    private List<string> _folders;

    public MediaLibrary(RexStore store, TimeProvider? time = null)
    {
        Store = store ?? throw new ArgumentNullException(nameof(store));
        _time = time ?? TimeProvider.System;
        _folders = [.. (Parse(Store.Get(FoldersKey), LibraryJson.Default.ListString) ?? []).Where(folder => !string.IsNullOrEmpty(folder))];
        _hidden.UnionWith((Parse(Store.Get(HiddenKey), LibraryJson.Default.ListString) ?? []).Where(path => !string.IsNullOrEmpty(path)));
        foreach (var (_, json) in Store.WithPrefix(EntryPrefix))
        {
            if (Parse(json, LibraryJson.Default.LibraryEntry) is { Path.Length: > 0, Title: not null } entry)
            {
                _entries[entry.Path] = entry;
            }
        }
    }

    /// <summary>Raised after the folders or the entries change, on whichever thread changed them.</summary>
    public event EventHandler? Changed;

    public RexStore Store { get; }

    /// <summary>The folders the library watches, in the order they were added.</summary>
    public IReadOnlyList<string> Folders
    {
        get
        {
            lock (_gate)
            {
                return [.. _folders];
            }
        }
    }

    /// <summary>Every file the library knows, in no particular order.</summary>
    public IReadOnlyList<LibraryEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries.Values];
            }
        }
    }

    /// <summary>The files the user removed from the library (<see cref="Hide"/>), which scans pass over while they stay on the disk.</summary>
    public IReadOnlyList<string> Hidden
    {
        get
        {
            lock (_gate)
            {
                return [.. _hidden.Order(StringComparer.OrdinalIgnoreCase)];
            }
        }
    }

    /// <summary>
    /// The files whose details are still to be read, those found first first, then the music an
    /// older tag reading read (<see cref="TagReading"/>), so a new file never waits behind them.
    /// </summary>
    public IReadOnlyList<LibraryEntry> Unprobed
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries.Values.Where(entry => !entry.Probed || ((entry.Kind is LibraryKind.Music or LibraryKind.Picture) && entry.TagReading < TagReading))
                    .OrderBy(entry => entry.Probed).ThenBy(entry => entry.Added).ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)];
            }
        }
    }

    public LibraryEntry? Entry(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        lock (_gate)
        {
            return _entries.GetValueOrDefault(path);
        }
    }

    /// <summary>
    /// Watches <paramref name="folder"/>; false when it, or a folder holding it, already is. A
    /// folder holding ones already watched takes their place.
    /// </summary>
    public bool AddFolder(string folder)
    {
        folder = Normal(folder);
        lock (_gate)
        {
            if (_folders.Any(known => Holds(known, folder)))
            {
                return false;
            }

            _folders = [.. _folders.Where(known => !Holds(folder, known)), folder];
            Store.Set(FoldersKey, JsonSerializer.Serialize(_folders, LibraryJson.Default.ListString));
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Stops watching <paramref name="folder"/> and forgets the files in it; false when it was not watched.</summary>
    public bool RemoveFolder(string folder)
    {
        folder = Normal(folder);
        lock (_gate)
        {
            var index = _folders.FindIndex(known => string.Equals(known, folder, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return false;
            }

            _folders.RemoveAt(index);

            Store.Set(FoldersKey, JsonSerializer.Serialize(_folders, LibraryJson.Default.ListString));
            Write([.. _entries.Values.Where(entry => Holds(folder, entry.Path) && !_folders.Any(other => Holds(other, entry.Path)))], remove: true);

            // Adding the folder again later starts afresh: nothing in it stays hidden.
            if (_hidden.RemoveWhere(path => Holds(folder, path) && !_folders.Any(other => Holds(other, path))) > 0)
            {
                WriteHidden();
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Brings what the library knows of <paramref name="folder"/> in line with <paramref name="files"/>,
    /// every media file in it and the folders inside it: new files are added, changed ones are to be
    /// read again, and ones no longer there are forgotten. <paramref name="kindOf"/> says what each
    /// file is (null for a file that is not media). A file still being downloaded is marked so
    /// (<see cref="Downloads"/>), judged by the markers listed beside it, by how it grew since the
    /// last scan, and by what <paramref name="readMarker"/> reads of a downloader's sidecar; a
    /// partial file whose whole name is listed too is left to that one.
    /// </summary>
    public ScanResult Scan(string folder, IEnumerable<LibraryFile> files, Func<string, LibraryKind?> kindOf, Func<string, byte[]?>? readMarker = null, Func<LibraryFile, FileLook>? inspect = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(kindOf);
        folder = Normal(folder);
        var now = _time.GetUtcNow().UtcDateTime;
        ScanResult result;
        var listed = files as IReadOnlyCollection<LibraryFile> ?? files.ToList();
        var paths = new HashSet<string>(listed.Select(file => file.Path), StringComparer.OrdinalIgnoreCase);
        lock (_gate)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var written = new List<LibraryEntry>();
            int added = 0, changed = 0;
            foreach (var file in listed)
            {
                if (kindOf(file.Path) is not { } kind || _hidden.Contains(file.Path) || !seen.Add(file.Path)
                    || (Downloads.WholeNameOf(file.Path) is { } whole && paths.Contains(whole)))
                {
                    continue;
                }

                var known = _entries.GetValueOrDefault(file.Path);
                if (known is not null && known.Size == file.Size && known.Modified == file.Modified && !known.Downloading && !known.Suspect)
                {
                    // Unchanged and not in question: nothing is opened or written.
                    continue;
                }

                // A download renamed into place ("film.mp4.crdownload" to "film.mp4") keeps its entry,
                // judged afresh: the rename itself says the download finished.
                var carried = known is null ? RenamedPartial(file.Path, paths) : null;
                var before = known;
                var start = known ?? (carried is null
                    ? new LibraryEntry { Path = file.Path, Kind = kind, Size = file.Size, Modified = file.Modified, Added = now, Title = TitleOf(file.Path) }
                    : carried with { Path = file.Path, Probed = false });
                var evidence = Downloads.Gather(file.Path, paths.Contains, readMarker ?? (_ => null));
                var looked = Looked(start, before, file, evidence, LookAt(file, evidence, inspect)) with { Kind = kind };
                if (known is null && carried is null)
                {
                    written.Add(looked);
                    added++;
                }
                else if (looked != known)
                {
                    written.Add(looked);
                    changed += known is null || known.Size != file.Size || known.Modified != file.Modified || known.Downloading != looked.Downloading ? 1 : 0;
                }
            }

            var gone = _entries.Values.Where(entry => Holds(folder, entry.Path) && !seen.Contains(entry.Path)).ToList();
            Write(written, remove: false);
            Write(gone, remove: true);
            result = new ScanResult(added, changed, gone.Count);
        }

        if (result.Any)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return result;
    }

    /// <summary>
    /// Looks again at every file marked as downloading, or showing a first weak sign of it:
    /// <paramref name="stat"/> gives it as the disk lists it now (null when it is gone), the markers
    /// beside it are read as a scan reads them, and <paramref name="inspect"/> looks at the file
    /// itself. One that stopped changing, with nothing marking it, nothing left to arrive and no
    /// writer, is whole; its details are read again. Only entries that changed are written. For the
    /// window, which looks while such files are shown or opened.
    /// </summary>
    public void RecheckDownloads(Func<string, LibraryFile?> stat, Func<string, bool> exists, Func<string, byte[]?> readMarker, Func<LibraryFile, FileLook>? inspect = null)
    {
        ArgumentNullException.ThrowIfNull(stat);
        ArgumentNullException.ThrowIfNull(exists);
        ArgumentNullException.ThrowIfNull(readMarker);
        List<LibraryEntry> inQuestion;
        lock (_gate)
        {
            inQuestion = [.. _entries.Values.Where(entry => entry.Downloading || entry.Suspect)];
        }

        var looked = new List<LibraryEntry>();
        foreach (var known in inQuestion)
        {
            // A file can disappear while its folder is open. Do not probe downloader sidecars for a
            // path the filesystem has already said is gone; leave its remembered state for the next scan.
            if (stat(known.Path) is not { } file)
            {
                continue;
            }

            var evidence = Downloads.Gather(known.Path, exists, readMarker);
            if (Looked(known, known, file, evidence, LookAt(file, evidence, inspect)) is var now && now != known)
            {
                looked.Add(now);
            }
        }

        Change(looked.Select(entry => entry.Path), known => looked.First(entry => string.Equals(entry.Path, known.Path, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>The entry of a partial file that <paramref name="path"/> was renamed from, when that file is no longer listed.</summary>
    private LibraryEntry? RenamedPartial(string path, HashSet<string> listed) =>
        Downloads.PartialExtensions.Select(extension => path + extension)
            .Where(partial => !listed.Contains(partial))
            .Select(partial => _entries.GetValueOrDefault(partial))
            .FirstOrDefault(entry => entry is not null);

    /// <summary>What the file itself says, asked only when no name or marker settles it already, and never of a cloud placeholder.</summary>
    private static FileLook LookAt(LibraryFile file, DownloadEvidence evidence, Func<LibraryFile, FileLook>? inspect) =>
        inspect is null || evidence.Marked || file.Placeholder ? default : inspect(file);

    /// <summary>
    /// <paramref name="start"/> as the disk shows it now, judged against <paramref name="before"/>
    /// (what was known at the last look; null for a file never seen). A file that changed is read
    /// again, except while it downloads: its details are read once, then left until it is whole.
    /// </summary>
    private static LibraryEntry Looked(LibraryEntry start, LibraryEntry? before, LibraryFile file, DownloadEvidence evidence, FileLook look)
    {
        var state = Downloads.Judge(evidence, look, before, file.Size, file.Modified);
        var changed = before is null || before.Size != file.Size || before.Modified != file.Modified;
        var wasDownloading = before is { Downloading: true };
        var reread = state.Downloading ? changed && !wasDownloading : changed || wasDownloading;
        return start with
        {
            Size = file.Size,
            Modified = file.Modified,
            Downloading = state.Downloading,
            Suspect = state.Suspect,
            FinalSize = state.FinalSize,
            Allocated = state.Allocated,
            Probed = start.Probed && !reread,
        };
    }

    /// <summary>
    /// Fills in the details of <paramref name="path"/> from <paramref name="info"/>; null marks it as
    /// unreadable, so it is not tried again until it changes. A file no longer known is passed over.
    /// </summary>
    public void SetDetails(string path, MediaInfo? info) => SetDetails([(path, info)]);

    /// <summary>Fills in the details of several files in one write, as <see cref="SetDetails(string, MediaInfo?)"/> does each.</summary>
    public void SetDetails(IEnumerable<(string Path, MediaInfo? Info)> details)
    {
        ArgumentNullException.ThrowIfNull(details);
        SetReadDetails(details.Select(detail => (detail.Path, detail.Info, (string?)null)));
    }

    /// <summary>
    /// Fills in the details of several files in one write; a file whose details could not be read
    /// keeps the <paramref name="details"/>' problem, which says why (shown with it, and cleared once it reads).
    /// </summary>
    public void SetReadDetails(IEnumerable<(string Path, MediaInfo? Info, string? Problem)> details)
    {
        ArgumentNullException.ThrowIfNull(details);
        lock (_gate)
        {
            var written = new List<LibraryEntry>();
            foreach (var (path, info, problem) in details)
            {
                if (_entries.TryGetValue(path, out var known))
                {
                    written.Add(info is null ? known with { Probed = true, Problem = problem, TagReading = TagReading } : WithDetails(known, info) with { Problem = null });
                }
            }

            if (written.Count == 0)
            {
                return;
            }

            Write(written, remove: false);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Keeps <paramref name="change"/> over the details of each of <paramref name="paths"/>, at once and after every rescan.</summary>
    public void SetOverride(IEnumerable<string> paths, DetailOverride change)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(change);
        Change(paths, known =>
        {
            var merged = (known.Override ?? new DetailOverride()).Then(change);
            return merged.ApplyTo(known) with { Override = merged };
        });
    }

    /// <summary>
    /// Drops the overrides of <paramref name="paths"/> (their files now hold the details) so their tags
    /// are read again; choices no file can hold, such as "Move to Podcasts" or "This is a movie", are kept.
    /// </summary>
    public void ClearOverride(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        Change(paths, known => known with { Override = known.Override?.Kept(), Probed = false });
    }

    /// <summary>Counts a play of <paramref name="path"/>, now; one the library does not know is passed over.</summary>
    public void Played(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        lock (_gate)
        {
            if (!_entries.TryGetValue(path, out var known))
            {
                return;
            }

            Write([known with { Plays = known.Plays + 1, LastPlayed = _time.GetUtcNow().UtcDateTime }], remove: false);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Marks each of <paramref name="paths"/> as played through: a play is counted for one never
    /// played, and the last play is now. Ones the library does not know are passed over.
    /// </summary>
    public void MarkPlayed(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var now = _time.GetUtcNow().UtcDateTime;
        Change(paths, known => known with { Plays = Math.Max(1, known.Plays), LastPlayed = now });
    }

    /// <summary>Forgets the plays of each of <paramref name="paths"/>, so it counts as never played.</summary>
    public void ResetPlays(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        Change(paths, known => known with { Plays = 0, LastPlayed = null });
    }

    /// <summary>Forgets <paramref name="paths"/> (files deleted from the disk); a scan finds them again if they come back.</summary>
    public int Forget(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        List<LibraryEntry> gone;
        lock (_gate)
        {
            gone = [.. paths.Select(path => _entries.GetValueOrDefault(path)).OfType<LibraryEntry>().DistinctBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)];
            Write(gone, remove: true);
        }

        if (gone.Count > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return gone.Count;
    }

    /// <summary>
    /// Removes <paramref name="paths"/> from the library while the files stay on the disk: they are
    /// forgotten and scans pass them over, until their folder is removed and added again.
    /// </summary>
    public int Hide(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        List<LibraryEntry> gone;
        lock (_gate)
        {
            var hidden = paths.Where(path => !string.IsNullOrWhiteSpace(path)).ToList();
            if (hidden.Count == 0)
            {
                return 0;
            }

            _hidden.UnionWith(hidden);
            WriteHidden();
            gone = [.. hidden.Select(path => _entries.GetValueOrDefault(path)).OfType<LibraryEntry>().DistinctBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)];
            Write(gone, remove: true);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return gone.Count;
    }

    /// <summary>Writes each known entry of <paramref name="paths"/> as <paramref name="change"/> makes it; unknown ones are passed over.</summary>
    private void Change(IEnumerable<string> paths, Func<LibraryEntry, LibraryEntry> change)
    {
        lock (_gate)
        {
            var written = paths.Select(path => _entries.GetValueOrDefault(path)).OfType<LibraryEntry>().DistinctBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase).Select(change).ToList();
            if (written.Count == 0)
            {
                return;
            }

            Write(written, remove: false);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Keeps the hidden paths in the store. Called holding the gate.</summary>
    private void WriteHidden() => Store.Set(HiddenKey, JsonSerializer.Serialize(_hidden.Order(StringComparer.OrdinalIgnoreCase).ToList(), LibraryJson.Default.ListString));

    /// <summary>
    /// Brings in entries from a backup (LIB-10): ones not known are added as they were, unless they
    /// were removed from the library (a scan would not bring them back either); for ones known, the
    /// higher play count and the later last play win. Gives how many were added.
    /// </summary>
    public int Restore(IEnumerable<LibraryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var added = 0;
        lock (_gate)
        {
            var written = new List<LibraryEntry>();
            foreach (var entry in entries.DistinctBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase))
            {
                if (_hidden.Contains(entry.Path))
                {
                    continue;
                }

                if (!_entries.TryGetValue(entry.Path, out var known))
                {
                    written.Add(entry);
                    added++;
                }
                else if (entry.Plays > known.Plays || entry.LastPlayed > (known.LastPlayed ?? DateTime.MinValue))
                {
                    written.Add(known with
                    {
                        Plays = Math.Max(known.Plays, entry.Plays),
                        LastPlayed = entry.LastPlayed > (known.LastPlayed ?? DateTime.MinValue) ? entry.LastPlayed : known.LastPlayed,
                    });
                }
            }

            if (written.Count == 0)
            {
                return 0;
            }

            Write(written, remove: false);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return added;
    }

    /// <summary>Forgets every play count and when each file was last played (the history, PRIV-03).</summary>
    public void ClearPlays()
    {
        lock (_gate)
        {
            var played = _entries.Values.Where(entry => entry.Plays > 0 || entry.LastPlayed is not null).Select(entry => entry with { Plays = 0, LastPlayed = null }).ToList();
            if (played.Count == 0)
            {
                return;
            }

            Write(played, remove: false);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The title a file has until its details are read: its name without the extension.</summary>
    public static string TitleOf(string path) => System.IO.Path.GetFileNameWithoutExtension(Downloads.WholeNameOf(path) ?? path) is { Length: > 0 } name ? name : path;

    /// <summary>Whether <paramref name="folder"/> is <paramref name="path"/> or holds it.</summary>
    public static bool Holds(string folder, string path)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(path);
        return path.Equals(folder, StringComparison.OrdinalIgnoreCase)
            || (path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && (folder.EndsWith('\\') || folder.EndsWith('/') || path[folder.Length] is '\\' or '/'));
    }

    private static string Normal(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var trimmed = folder.TrimEnd('\\', '/');

        // A drive's root keeps its separator ("D:\"); "/" stays itself.
        return trimmed.Length == 0 ? folder[..1] : trimmed.EndsWith(':') ? trimmed + folder[trimmed.Length] : trimmed;
    }

    private static LibraryEntry WithDetails(LibraryEntry entry, MediaInfo info)
    {
        string? Tag(string key) => info.Metadata.GetValueOrDefault(key) is { } value && value.Trim() is { Length: > 0 } trimmed ? trimmed : null;

        var image = entry.Kind == LibraryKind.Picture ? info.FirstTrack(MediaKind.Video)?.Video : null;
        var sideways = image?.Rotation is 90 or 270;

        // A container that declares more bytes than the file holds is still arriving.
        var (downloading, finalSize) = info.DeclaredLength is { } declared && declared > entry.Size
            ? (true, (long?)declared)
            : (entry.Downloading, entry.FinalSize);
        return (entry with
        {
            Probed = true,
            Downloading = downloading,
            FinalSize = finalSize,
            TagReading = TagReading,
            // A video file with only sound in it (an .mp4 of a song) is music.
            Kind = info.FirstTrack(MediaKind.Video) is null && info.FirstTrack(MediaKind.Audio) is not null ? LibraryKind.Music : entry.Kind,
            Title = Tag(MetadataKeys.Title) ?? TitleOf(entry.Path),
            Artist = Tag(MetadataKeys.Artist),
            Artists = Tag(MetadataKeys.Artists),
            Album = Tag(MetadataKeys.Album),
            AlbumArtist = Tag(MetadataKeys.AlbumArtist),
            Author = Tag(MetadataKeys.Author) ?? Tag("bookauthor") ?? Tag("book_author") ?? Tag("book author") ?? Tag("written by") ?? Tag("written_by"),
            Narrator = Tag(MetadataKeys.Narrator) ?? Tag("read by") ?? Tag("read_by") ?? Tag("reader"),
            Genre = Tag(MetadataKeys.Genre),
            Track = Number(Tag(MetadataKeys.Track)),
            Disc = Number(Tag(MetadataKeys.Disc)),
            Year = Number(Tag(MetadataKeys.Date)) is { } year && year is >= 1000 and <= 9999 ? year : null,
            Duration = info.Duration.IsKnown ? TimeSpan.FromTicks(info.Duration.Ticks) : null,
            PictureWidth = image is { Width: > 0, Height: > 0 } ? (sideways ? image.Height : image.Width) : null,
            PictureHeight = image is { Width: > 0, Height: > 0 } ? (sideways ? image.Width : image.Height) : null,
            Chapters = [.. info.Chapters.Select(chapter => new LibraryChapter(TimeSpan.FromTicks(Math.Max(0, chapter.Start.Ticks)), chapter.Title))],
            MediaType = Tag(MetadataKeys.MediaType),
        }) is var read && entry.Override is { } kept ? kept.ApplyTo(read) : read;
    }

    /// <summary>The number a tag starts with: "3/12" is 3, "2019-05-01" is 2019; null when there is none.</summary>
    private static int? Number(string? text)
    {
        var digits = text is null ? "" : new string([.. text.TakeWhile(char.IsAsciiDigit)]);
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0 ? number : null;
    }

    /// <summary>Keeps or forgets <paramref name="entries"/> in one write to the store. Called holding the gate.</summary>
    private void Write(IReadOnlyCollection<LibraryEntry> entries, bool remove)
    {
        if (entries.Count == 0)
        {
            return;
        }

        foreach (var entry in entries)
        {
            if (remove)
            {
                _entries.Remove(entry.Path);
            }
            else
            {
                _entries[entry.Path] = entry;
            }
        }

        Store.SetMany([.. entries.Select(entry => new KeyValuePair<string, string?>(EntryPrefix + KeyFor(entry.Path), remove ? null : JsonSerializer.Serialize(entry, LibraryJson.Default.LibraryEntry)))]);
    }

    private static string KeyFor(string path) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())))[..32];

    private static T? Parse<T>(string? json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
        where T : class
    {
        if (json is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, type);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

[JsonSerializable(typeof(List<string>), TypeInfoPropertyName = "ListString")]
[JsonSerializable(typeof(LibraryEntry))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, UseStringEnumConverter = true)]
internal sealed partial class LibraryJson : JsonSerializerContext;
