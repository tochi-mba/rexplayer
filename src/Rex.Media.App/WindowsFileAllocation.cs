using Microsoft.Win32.SafeHandles;
using Rex.Media.Interop.Storage;
using Rex.Media.IO;

namespace Rex.Media.App;

/// <summary>Which bytes of a sparse file are on the disk, as the file system says (<see cref="FileAllocation"/>), for the byte sources and the library's scanner.</summary>
internal sealed class WindowsFileAllocation : IFileAllocation
{
    public static WindowsFileAllocation Instance { get; } = new();

    public long Allocated(SafeFileHandle file, long length) => FileAllocation.Allocated(file, length);

    public long AllocatedEnd(SafeFileHandle file, long position, long length) => FileAllocation.AllocatedEnd(file, position, length);
}
