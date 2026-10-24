using Rex.Media.Containers.Tags;
using Rex.Media.Library;

namespace Rex.Media.AppCore.Library;

/// <summary>An edit waiting to go into one file: the details, and the cover to put in or take out.</summary>
public sealed record DetailWrite(string Path, DetailOverride Change, PictureBlock? Cover = null, bool RemoveCover = false)
{
    /// <summary>The edit as the tag writers take it.</summary>
    public TagEdit ToEdit() => new()
    {
        Title = Change.Title,
        Artist = Change.Artist,
        Album = Change.Album,
        AlbumArtist = Change.AlbumArtist,
        Track = Change.Track,
        Disc = Change.Disc,
        Date = Change.Year,
        Genre = Change.Genre,
        Cover = Cover,
        RemoveCover = Cover is null && RemoveCover,
    };

    /// <summary>A front cover from an image file's bytes, or null when the file is not a JPEG or PNG image.</summary>
    public const int MaxCoverBytes = 10 * 1024 * 1024;

    public static PictureBlock? CoverFrom(string path, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length is 0 or > MaxCoverBytes)
        {
            return null;
        }

        // A misleading extension cannot make arbitrary bytes safe to embed in
        // the user's music file. The native image decoder performs full validation
        // before showing the preview; these headers reject obviously wrong files.
        var extension = System.IO.Path.GetExtension(path);
        ReadOnlySpan<byte> pngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
        ReadOnlySpan<byte> jpegSignature = [0xFF, 0xD8, 0xFF];
        var png = extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            && data.AsSpan().StartsWith(pngSignature);
        var jpeg = (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            && data.AsSpan().StartsWith(jpegSignature);
        return png ? new PictureBlock(PictureBlock.FrontCover, "image/png", data)
            : jpeg ? new PictureBlock(PictureBlock.FrontCover, "image/jpeg", data)
            : null;
    }

    /// <summary>This write followed by <paramref name="later"/> for the same file, as one write.</summary>
    public DetailWrite Then(DetailWrite later)
    {
        ArgumentNullException.ThrowIfNull(later);
        return later.Cover is not null || later.RemoveCover
            ? later with { Change = Change.Then(later.Change) }
            : this with { Change = Change.Then(later.Change) };
    }
}

/// <summary>
/// Edits waiting to be written into their files. A file that is playing, or that another program
/// holds, keeps its edit here until it is free, so writing never interrupts playback; later edits
/// to the same file join the waiting one. Safe to use from several threads.
/// </summary>
public sealed class PendingDetailWrites
{
    private readonly object _gate = new();
    private readonly Dictionary<string, DetailWrite> _pending = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many files have an edit waiting.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count;
            }
        }
    }

    public void Add(DetailWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);
        lock (_gate)
        {
            _pending[write.Path] = _pending.TryGetValue(write.Path, out var waiting) ? waiting.Then(write) : write;
        }
    }

    /// <summary>
    /// Writes every waiting edit whose file is not <paramref name="inUse"/>; a busy file keeps its
    /// edit for the next time. Gives each write tried and how it went.
    /// </summary>
    public IReadOnlyList<(DetailWrite Write, TagWriteResult Result)> Flush(Func<string, bool> inUse, Func<string, TagEdit, TagWriteResult> write)
    {
        ArgumentNullException.ThrowIfNull(inUse);
        ArgumentNullException.ThrowIfNull(write);
        List<DetailWrite> ready;
        lock (_gate)
        {
            ready = [.. _pending.Values.Where(item => !inUse(item.Path))];
            foreach (var item in ready)
            {
                _pending.Remove(item.Path);
            }
        }

        var results = new List<(DetailWrite, TagWriteResult)>();
        foreach (var item in ready)
        {
            var result = write(item.Path, item.ToEdit());
            if (result.Status == TagWriteStatus.Busy)
            {
                lock (_gate)
                {
                    // An edit made while this one was being tried came later, so it goes on top.
                    _pending[item.Path] = _pending.TryGetValue(item.Path, out var newer) ? item.Then(newer) : item;
                }
            }

            results.Add((item, result));
        }

        return results;
    }
}
