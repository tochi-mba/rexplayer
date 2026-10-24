namespace Rex.Media.AppCore.Library;

/// <summary>
/// A bounded slice of a counted library view, including empty views and a page
/// that disappeared after a scan, folder removal or a narrower search.
/// Pure arithmetic: the UI never renders an invalid or one-past-the-end page.
/// </summary>
public readonly record struct LibraryPageSlice(
    int Page, int Start, int End, bool HasPrevious, bool HasNext)
{
    /// <summary>Creates a page over a nonnegative count without reading media files.</summary>
    public static LibraryPageSlice For(int count, int requestedPage, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);

        // Division is evaluated before increment so a maximal count cannot overflow.
        var lastPage = count == 0 ? 0 : (count - 1) / pageSize;
        var page = Math.Clamp(requestedPage, 0, lastPage);
        var start = page * pageSize; // page <= (count - 1) / pageSize
        // Subtraction avoids overflowing when pageSize is int.MaxValue.
        var end = start + Math.Min(pageSize, count - start);
        return new LibraryPageSlice(page, start, end, page > 0, end < count);
    }
}
