using System.Buffers.Binary;
using System.Globalization;

namespace Rex.Media.Library;

/// <summary>What the disk says about a file that may still be arriving: whether a downloader has marked it, and how big it will be when whole.</summary>
/// <param name="Marked">The file has a download-in-progress name, or a downloader's marker file sits beside it.</param>
/// <param name="FinalSize">How many bytes it will be once whole, from the downloader's sidecar; null when nothing says.</param>
public readonly record struct DownloadEvidence(bool Marked, long? FinalSize);

/// <summary>What a look at the file itself says, asked only of files that changed or are already in question.</summary>
/// <param name="WriterOpen">Another program holds it open for writing (a downloader, a copy, a torrent client).</param>
/// <param name="Allocated">For a sparse file, how many of its bytes are on the disk; null for a file that is not sparse, or when the system cannot tell.</param>
public readonly record struct FileLook(bool WriterOpen, long? Allocated);

/// <summary>Whether a file is still downloading, after a look.</summary>
/// <param name="Downloading">It is still arriving.</param>
/// <param name="Suspect">A weak sign was seen once, not yet confirmed by a second look.</param>
/// <param name="FinalSize">How big it will be once whole, when something declares it and it has not got there.</param>
/// <param name="Allocated">How many of a sparse file's bytes have arrived, while some have not.</param>
public readonly record struct DownloadState(bool Downloading, bool Suspect, long? FinalSize, long? Allocated);

/// <summary>
/// Tells a file still being downloaded into a watched folder (LIB-05) from a whole one. Strong
/// signs settle it at one look: a download-in-progress name ("film.mp4.part") or a marker beside
/// it (aria2's control file, a browser's part file); a declared size it has not reached (the
/// downloader's sidecar or the container's own header); or, for a sparse file (how torrent clients
/// lay out a download at its full size), bytes not yet on the disk. Weak signs (it grew, it was
/// rewritten in place, another program holds it open for writing) count only on two looks in a
/// row, or while it is already known to be downloading, so a whole file that a virus scanner or
/// a tag editor touches once never flickers into "downloading". rexplayer's own reads never count:
/// they hold a file open for reading only.
/// </summary>
public static class Downloads
{
    /// <summary>The marker aria2 keeps beside a file until it is whole, which says how big it will be.</summary>
    public const string Aria2Marker = ".aria2";

    /// <summary>The extensions downloaders add to a file's name until it is whole.</summary>
    public static readonly IReadOnlyList<string> PartialExtensions = [".part", ".crdownload", ".crswap", ".!qb", ".!ut", ".download", ".tmp", ".partial"];

    /// <summary>The name a partial file will have once whole ("film.mp4" for "film.mp4.part"), or null when the name is not a downloader's.</summary>
    public static string? WholeNameOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var extension = Path.GetExtension(path);
        return extension.Length > 0 && PartialExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
            ? path[..^extension.Length]
            : null;
    }

    /// <summary>The files whose presence beside <paramref name="path"/> means it is still being downloaded.</summary>
    public static IEnumerable<string> MarkersOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        yield return path + Aria2Marker;
        foreach (var extension in PartialExtensions)
        {
            yield return path + extension;
        }
    }

    /// <summary>
    /// Looks beside <paramref name="path"/>: <paramref name="exists"/> says which marker files are
    /// there, and <paramref name="read"/> gives the head of one (null when it cannot be read).
    /// </summary>
    public static DownloadEvidence Gather(string path, Func<string, bool> exists, Func<string, byte[]?> read)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(exists);
        ArgumentNullException.ThrowIfNull(read);
        var marked = WholeNameOf(path) is not null;
        long? finalSize = null;
        foreach (var marker in MarkersOf(path))
        {
            if (!exists(marker))
            {
                continue;
            }

            marked = true;
            if (marker.EndsWith(Aria2Marker, StringComparison.Ordinal) && read(marker) is { } control)
            {
                finalSize = Aria2TotalLength(control);
            }
        }

        return new DownloadEvidence(marked, finalSize);
    }

    /// <summary>
    /// Whether a file of <paramref name="size"/> bytes, changed at <paramref name="modified"/>, is
    /// still downloading, given what the disk says (<paramref name="evidence"/>, <paramref name="look"/>)
    /// and what the library knew of it at the last look (<paramref name="before"/>, null for a file
    /// never seen). See <see cref="Downloads"/> for the rule.
    /// </summary>
    public static DownloadState Judge(DownloadEvidence evidence, FileLook look, LibraryEntry? before, long size, DateTime modified)
    {
        var declared = Math.Max(evidence.FinalSize ?? 0, before?.FinalSize ?? 0);
        long? final = declared > size ? declared : null;
        long? allocated = look.Allocated is { } onDisk && onDisk < size ? onDisk : null;
        var strong = evidence.Marked || final is not null || allocated is not null;
        // Growth and rewrites say nothing of a file that reached its declared size, or a sparse file
        // whose every byte is on the disk: the last piece arriving changes it once more, and that is
        // the download finishing.
        var changing = declared == 0 && look.Allocated is null && before is not null && (size > before.Size || (size == before.Size && modified != before.Modified));
        var weak = look.WriterOpen || changing;
        var downloading = strong || (weak && before is { Downloading: true } or { Suspect: true });
        return new DownloadState(downloading, weak && !downloading, final, allocated);
    }

    /// <summary>How far along a download is, 0 to 1: the share of a sparse file on the disk, else its size against its final size; null when nothing says.</summary>
    public static double? Fraction(long size, long? finalSize, long? allocated = null) =>
        allocated is { } onDisk && size > 0 ? Math.Clamp((double)onDisk / size, 0, 1)
        : finalSize is > 0 and var final ? Math.Clamp((double)size / final, 0, 1)
        : null;

    /// <summary>
    /// The detail line under a downloading file: "Downloading · 41%" for a sparse file filling in,
    /// "Downloading · 1.2 GB of 3.4 GB" when its final size is known, else "Downloading".
    /// </summary>
    public static string Detail(long size, long? finalSize, long? allocated = null) =>
        allocated is not null && Fraction(size, finalSize, allocated) is { } share
            ? string.Create(CultureInfo.CurrentCulture, $"Downloading \u00B7 {Math.Floor(share * 100):0}%")
            : finalSize is > 0 and var final ? $"Downloading \u00B7 {SizeText(size)} of {SizeText(final)}" : "Downloading";

    /// <summary>What Play says of a file too incomplete to open: "Still downloading: 20% so far", or "Still downloading".</summary>
    public static string NotYetNotice(long size, long? finalSize, long? allocated = null) =>
        Fraction(size, finalSize, allocated) is { } fraction
            ? string.Create(CultureInfo.CurrentCulture, $"Still downloading: {Math.Floor(fraction * 100):0}% so far")
            : "Still downloading";

    /// <summary>A size as a person reads it: "812 MB", "3.4 GB".</summary>
    public static string SizeText(long bytes)
    {
        const double Kilo = 1024;
        return bytes switch
        {
            < 1024 => string.Create(CultureInfo.CurrentCulture, $"{bytes} B"),
            < 1024 * 1024 => string.Create(CultureInfo.CurrentCulture, $"{bytes / Kilo:0} KB"),
            < 1024L * 1024 * 1024 => string.Create(CultureInfo.CurrentCulture, $"{bytes / Kilo / Kilo:0} MB"),
            _ => string.Create(CultureInfo.CurrentCulture, $"{bytes / Kilo / Kilo / Kilo:0.0} GB"),
        };
    }

    /// <summary>
    /// The total length an aria2 control file declares. Its layout (the aria2 manual, "Control File
    /// (*.aria2) Format"): a 2-byte version, a 4-byte extension field, the 4-byte length of the info
    /// hash, the info hash, the 4-byte piece length, then the 8-byte total length, all big-endian.
    /// Null for a file that is too short or of a version this does not read.
    /// </summary>
    public static long? Aria2TotalLength(ReadOnlySpan<byte> control)
    {
        if (control.Length < 10 || BinaryPrimitives.ReadUInt16BigEndian(control) != 1)
        {
            return null;
        }

        var hashLength = BinaryPrimitives.ReadUInt32BigEndian(control[6..]);
        var at = 10L + hashLength + 4;
        if (hashLength > 1024 || control.Length < at + 8)
        {
            return null;
        }

        var total = BinaryPrimitives.ReadUInt64BigEndian(control[(int)at..]);
        return total > long.MaxValue ? null : (long)total;
    }
}
