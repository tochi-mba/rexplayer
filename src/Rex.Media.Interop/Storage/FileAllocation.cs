using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Rex.Media.IO;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Ioctl;

namespace Rex.Media.Interop.Storage;

/// <summary>
/// Which bytes of a sparse file are on the disk, asked of the file system with
/// FSCTL_QUERY_ALLOCATED_RANGES: one call per question, no allocation. When the file system cannot
/// say (not NTFS, not sparse, an error), every byte counts as there, so nothing waits on a guess.
/// </summary>
[SupportedOSPlatform("windows5.1.2600")]
public sealed class FileAllocation : IFileAllocation
{
    private const int MoreData = 234;
    private const int Batch = 32;

    public static FileAllocation Instance { get; } = new();

    public unsafe long Allocated(SafeFileHandle file, long length)
    {
        ArgumentNullException.ThrowIfNull(file);
        var ranges = stackalloc FILE_ALLOCATED_RANGE_BUFFER[Batch];
        long total = 0;
        long from = 0;
        while (from < length)
        {
            if (Query(file, from, length - from, ranges, out var count, out var more) is false)
            {
                return length;
            }

            for (var i = 0; i < count; i++)
            {
                total += ranges[i].Length;
            }

            if (!more || count == 0)
            {
                break;
            }

            from = ranges[count - 1].FileOffset + ranges[count - 1].Length;
        }

        return Math.Clamp(total, 0, length);
    }

    public unsafe long AllocatedEnd(SafeFileHandle file, long position, long length)
    {
        ArgumentNullException.ThrowIfNull(file);
        var ranges = stackalloc FILE_ALLOCATED_RANGE_BUFFER[Batch];
        var end = position;
        var limit = position + length;
        while (end < limit)
        {
            if (Query(file, end, limit - end, ranges, out var count, out var more) is false)
            {
                return limit;
            }

            var reached = end;
            for (var i = 0; i < count && ranges[i].FileOffset <= reached; i++)
            {
                reached = Math.Max(reached, ranges[i].FileOffset + ranges[i].Length);
            }

            if (reached == end)
            {
                break;
            }

            end = reached;
            if (!more)
            {
                break;
            }
        }

        return Math.Min(end, limit);
    }

    /// <summary>The allocated ranges within [<paramref name="offset"/>, +<paramref name="length"/>); false when the file system cannot say.</summary>
    private static unsafe bool Query(SafeFileHandle file, long offset, long length, FILE_ALLOCATED_RANGE_BUFFER* ranges, out int count, out bool more)
    {
        var query = new FILE_ALLOCATED_RANGE_BUFFER { FileOffset = offset, Length = length };
        uint returned = 0;
        var added = false;
        bool ok;
        try
        {
            file.DangerousAddRef(ref added);
            ok = PInvoke.DeviceIoControl((HANDLE)file.DangerousGetHandle(), PInvoke.FSCTL_QUERY_ALLOCATED_RANGES, &query, (uint)sizeof(FILE_ALLOCATED_RANGE_BUFFER),
                ranges, (uint)(sizeof(FILE_ALLOCATED_RANGE_BUFFER) * Batch), &returned, null);
        }
        finally
        {
            if (added)
            {
                file.DangerousRelease();
            }
        }

        more = !ok && Marshal.GetLastPInvokeError() == MoreData;
        count = (int)(returned / (uint)sizeof(FILE_ALLOCATED_RANGE_BUFFER));
        return ok || more;
    }
}
