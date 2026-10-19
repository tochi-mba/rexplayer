namespace Rex.Media.AppCore.Library;

/// <summary>
/// The search a library view was last built for. Changing view clears the search box, and the box
/// reports that change after the new view is already built; the report finds nothing new and the
/// view is not built a second time.
/// </summary>
public sealed class LibrarySearchText
{
    /// <summary>The search, trimmed, that the shown view was built for; empty for none.</summary>
    public string Built { get; private set; } = "";

    /// <summary>Records that a view is being built for what the box holds now, and gives it trimmed.</summary>
    /// <param name="text">What the search box holds.</param>
    public string Build(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var trimmed = text.AsSpan().Trim();
        if (!trimmed.SequenceEqual(Built))
        {
            Built = trimmed.ToString();
        }

        return Built;
    }

    /// <summary>Whether what the box holds asks for a different view from the one built; nothing is allocated.</summary>
    /// <param name="text">What the search box holds.</param>
    public bool Changes(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return !text.AsSpan().Trim().SequenceEqual(Built);
    }
}
