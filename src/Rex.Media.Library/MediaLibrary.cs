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
public readonly record struct LibraryFile(string Path, long Size, DateTime Modified);

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

    public string? Album { get; init; }

    public string? AlbumArtist { get; init; }

    public string? Genre { get; init; }

    public int? Track { get; init; }

    public int? Disc { get; init; }

    public int? Year { get; init; }

    public TimeSpan? Duration { get; init; }

    public int Plays { get; init; }

    /// <summary>When it was last played, in UTC.</summary>
    public DateTime? LastPlayed { get; init; }

    /// <summary>Details the person changed that rexplayer keeps over the file's own tags.</summary>
    public DetailOverride? Override { get; init; }

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

    private readonly object _gate = new();
    private readonly Dictionary<string, LibraryEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hidden = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _time;
    private List<string> _folders;

    public MediaLibrary(RexStore store, TimeProvider? time = null)
    {
        Store = store ?? throw new ArgumentNullException(nameof(store));
        _time = time ?? TimeProvider.System;
        _folders = Parse(Store.Get(FoldersKey), LibraryJson.Default.ListString) ?? [];
        _hidden.UnionWith(Parse(Store.Get(HiddenKey), LibraryJson.Default.ListString) ?? []);
        foreach (var (_, json) in Store.WithPrefix(EntryPrefix))
        {
            if (Parse(json, LibraryJson.Default.LibraryEntry) is { } entry)
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

    /// <summary>The files whose details are still to be read, those found first first.</summary>
    public IReadOnlyList<LibraryEntry> Unprobed
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries.Values.Where(entry => !entry.Probed).OrderBy(entry => entry.Added).ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)];
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
    /// file is (null for a file that is not media).
    /// </summary>
    public ScanResult Scan(string folder, IEnumerable<LibraryFile> files, Func<string, LibraryKind?> kindOf)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(kindOf);
        folder = Normal(folder);
        var now = _time.GetUtcNow().UtcDateTime;
        ScanResult result;
        lock (_gate)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var written = new List<LibraryEntry>();
            int added = 0, changed = 0;
            foreach (var file in files)
            {
                if (kindOf(file.Path) is not { } kind || _hidden.Contains(file.Path) || !seen.Add(file.Path))
                {
                    continue;
                }

                if (!_entries.TryGetValue(file.Path, out var known))
                {
                    written.Add(new LibraryEntry { Path = file.Path, Kind = kind, Size = file.Size, Modified = file.Modified, Added = now, Title = TitleOf(file.Path) });
                    added++;
                }
                else if (known.Size != file.Size || known.Modified != file.Modified)
                {
                    written.Add(known with { Kind = kind, Size = file.Size, Modified = file.Modified, Probed = false });
                    changed++;
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
                    written.Add(info is null ? known with { Probed = true, Problem = problem } : WithDetails(known, info) with { Problem = null });
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

    /// <summary>Drops the overrides of <paramref name="paths"/> (their files now hold the details) so their tags are read again.</summary>
    public void ClearOverride(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        Change(paths, known => known with { Override = null, Probed = false });
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
    public static string TitleOf(string path) => System.IO.Path.GetFileNameWithoutExtension(path) is { Length: > 0 } name ? name : path;

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

        return (entry with
        {
            Probed = true,
            // A video file with only sound in it (an .mp4 of a song) is music.
            Kind = info.FirstTrack(MediaKind.Video) is null && info.FirstTrack(MediaKind.Audio) is not null ? LibraryKind.Music : entry.Kind,
            Title = Tag(MetadataKeys.Title) ?? TitleOf(entry.Path),
            Artist = Tag(MetadataKeys.Artist),
            Album = Tag(MetadataKeys.Album),
            AlbumArtist = Tag(MetadataKeys.AlbumArtist),
            Genre = Tag(MetadataKeys.Genre),
            Track = Number(Tag(MetadataKeys.Track)),
            Disc = Number(Tag(MetadataKeys.Disc)),
            Year = Number(Tag(MetadataKeys.Date)) is { } year && year is >= 1000 and <= 9999 ? year : null,
            Duration = info.Duration.IsKnown ? TimeSpan.FromTicks(info.Duration.Ticks) : null,
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
