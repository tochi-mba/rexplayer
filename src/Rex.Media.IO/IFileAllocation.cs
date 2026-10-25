using Microsoft.Win32.SafeHandles;

namespace Rex.Media.IO;

/// <summary>
/// Which bytes of a sparse file are on the disk. A torrent client lays a download out at its full
/// size and fills it in any order; the parts not yet written read as zeros, which are not media.
/// The system that can tell (Windows, through the file system) supplies this; elsewhere there is none.
/// </summary>
public interface IFileAllocation
{
    /// <summary>How many of the first <paramref name="length"/> bytes of <paramref name="file"/> are on the disk.</summary>
    long Allocated(SafeFileHandle file, long length);

    /// <summary>
    /// Where the run of bytes on the disk that starts at <paramref name="position"/> ends, looking no
    /// further than <paramref name="length"/> bytes on: <paramref name="position"/> itself when a part
    /// not yet written starts there, <paramref name="position"/> + <paramref name="length"/> when all are there.
    /// </summary>
    long AllocatedEnd(SafeFileHandle file, long position, long length);
}
