using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;

namespace Rex.Media.App;

/// <summary>
/// A heading in a virtualised grouped library view. The full rows and their count survive while
/// collapsed; only the observable rows presented to the ListView are removed. Reopening sends one
/// collection reset rather than thousands of per-item changes for large libraries.
/// </summary>
public sealed partial class LibraryRowGroup : ObservableCollection<LibraryRow>
{
    private readonly LibraryRow[] _allRows;
    private bool _expanded = true;

    public LibraryRowGroup(string header, IEnumerable<LibraryRow> rows) : base(rows)
    {
        Header = header;
        _allRows = [.. this];
    }

    public string Header { get; }

    /// <summary>The original contents, even when the group is folded away.</summary>
    public IReadOnlyList<LibraryRow> AllRows => _allRows;

    public bool IsExpanded => _expanded;

    public string CountText => _allRows.Length.ToString(CultureInfo.CurrentCulture);

    public string ChevronGlyph => _expanded ? "\uE70D" : "\uE76C";

    public string ToggleName => (_expanded ? "Collapse " : "Expand ") + Header + " (" + CountText + " items)";

    public string ToggleId => "LibraryGroupToggle-" + Header;

    /// <summary>Hide or restore the group's items without discarding them or its original count.</summary>
    public void SetExpanded(bool expanded)
    {
        if (_expanded == expanded)
        {
            return;
        }

        _expanded = expanded;
        Items.Clear();
        if (expanded)
        {
            foreach (var row in _allRows)
            {
                Items.Add(row);
            }
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsExpanded)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(ChevronGlyph)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(ToggleName)));
    }

    public override string ToString() => Header;
}

/// <summary>One of the library's views in its sidebar: its icon and its name.</summary>
public sealed record LibrarySourceItem(string Glyph, string Name)
{
    public override string ToString() => Name;
}
