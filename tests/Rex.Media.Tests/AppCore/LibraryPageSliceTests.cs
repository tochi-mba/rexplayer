using Rex.Media.AppCore.Library;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>Persistent Mac library paging as the index changes mid-browse.</summary>
public sealed class LibraryPageSliceTests
{
    [Theory]
    [Capability("LIB-05")]
    [InlineData(0, 0, 60, 0, 0, 0, false, false)]
    [InlineData(1, 0, 60, 0, 0, 1, false, false)]
    [InlineData(60, 0, 60, 0, 0, 60, false, false)]
    [InlineData(61, 0, 60, 0, 0, 60, false, true)]
    [InlineData(61, 1, 60, 1, 60, 61, true, false)]
    [InlineData(121, 1, 60, 1, 60, 120, true, true)]
    [InlineData(121, 2, 60, 2, 120, 121, true, false)]
    [InlineData(61, 7, 60, 1, 60, 61, true, false)]
    [InlineData(61, -10, 60, 0, 0, 60, false, true)]
    [InlineData(0, 7, 60, 0, 0, 0, false, false)]
    [InlineData(int.MaxValue, int.MaxValue, int.MaxValue, 0, 0, int.MaxValue, false, false)]
    [InlineData(int.MaxValue, int.MaxValue, 60, 35791394, 2147483640, int.MaxValue, true, false)]
    public void MissingOrNewEntriesCannotLeaveAnInvalidPage(int count, int requested, int size,
        int page, int start, int end, bool previous, bool next)
    {
        var result = LibraryPageSlice.For(count, requested, size);
        Assert.Equal(new LibraryPageSlice(page, start, end, previous, next), result);
    }

    [Fact]
    [Capability("LIB-05")]
    public void RejectsInvalidCountsAndSizes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LibraryPageSlice.For(-1, 0, 60));
        Assert.Throws<ArgumentOutOfRangeException>(() => LibraryPageSlice.For(0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => LibraryPageSlice.For(1, 0, -1));
    }
}
