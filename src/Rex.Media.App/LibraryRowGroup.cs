using System.Globalization;

namespace Rex.Media.App;

/// <summary>A heading in a grouped library view, and the lines under it.</summary>
public sealed partial class LibraryRowGroup(string header, IEnumerable<LibraryRow> rows) : List<LibraryRow>(rows)
{
    public string Header { get; } = header;

    public string CountText => Count.ToString(CultureInfo.CurrentCulture);

    public override string ToString() => Header;
}

/// <summary>One of the library's views in its sidebar: its icon and its name.</summary>
public sealed record LibrarySourceItem(string Glyph, string Name)
{
    public override string ToString() => Name;
}
