namespace Rex.Media.AppCore;

/// <summary>
/// A dialog's sections, named up front and built only when first shown. A built section stays
/// for the dialog's lifetime, so what was typed into it survives switching away and back, and
/// <see cref="Apply"/> folds in only the sections that were built: one never shown has no edits.
/// </summary>
public sealed class LazySections<TPage, TState> where TPage : class
{
    private readonly List<(string Name, Func<(TPage Page, Func<TState, TState> Apply)> Build)> _sections = [];
    private readonly Dictionary<int, (TPage Page, Func<TState, TState> Apply)> _built = [];

    /// <summary>Every section's name, built or not, in order.</summary>
    public IReadOnlyList<string> Names => [.. _sections.Select(section => section.Name)];

    public int Count => _sections.Count;

    /// <summary>How many sections have been built so far.</summary>
    public int BuiltCount => _built.Count;

    /// <summary>Called once for each page as it is built, before it is shown.</summary>
    public Action<TPage>? Built { get; set; }

    public void Add(string name, Func<(TPage Page, Func<TState, TState> Apply)> build) => _sections.Add((name, build));

    public int IndexOf(string name) => _sections.FindIndex(section => section.Name == name);

    public bool IsBuilt(int index) => _built.ContainsKey(index);

    /// <summary>The page of section <paramref name="index"/>, built the first time it is asked for.</summary>
    public TPage Page(int index)
    {
        if (!_built.TryGetValue(index, out var built))
        {
            built = _sections[index].Build();
            _built[index] = built;
            Built?.Invoke(built.Page);
        }

        return built.Page;
    }

    /// <summary>Every page, building those not yet built (a search reads them all).</summary>
    public IEnumerable<(string Name, TPage Page)> All() =>
        _sections.Select((section, index) => (section.Name, Page(index)));

    /// <summary><paramref name="state"/> with the edits of every built section, in section order.</summary>
    public TState Apply(TState state)
    {
        foreach (var index in _built.Keys.Order())
        {
            state = _built[index].Apply(state);
        }

        return state;
    }
}
