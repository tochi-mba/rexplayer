namespace Rex.Media.AppCore.Commands;

/// <summary>
/// Which command each key chord runs: the defaults of <see cref="CommandCatalog"/> with the user's
/// changes on top. A change replaces all of a command's default chords with one chord, or removes
/// them when it is empty; changes naming unknown commands or unreadable chords are ignored, so a
/// hand-edited settings file can never stop the keyboard working.
/// </summary>
public sealed class Keymap
{
    private readonly Dictionary<string, IReadOnlyList<KeyChord>> _byCommand = new(StringComparer.Ordinal);
    private readonly Dictionary<KeyChord, string> _byChord = [];
    private readonly List<(KeyChord Chord, string First, string Second)> _conflicts = [];

    public Keymap(IReadOnlyDictionary<string, string>? changes = null)
    {
        foreach (var command in CommandCatalog.All)
        {
            IReadOnlyList<KeyChord> chords = command.DefaultShortcuts;
            if (changes is not null && changes.TryGetValue(command.Id, out var text))
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    chords = [];
                }
                else if (KeyChord.TryParse(text) is { } chord)
                {
                    chords = [chord];
                }
            }

            _byCommand[command.Id] = chords;
            foreach (var chord in chords)
            {
                if (!_byChord.TryAdd(chord, command.Id))
                {
                    _conflicts.Add((chord, _byChord[chord], command.Id));
                }
            }
        }
    }

    /// <summary>
    /// Chords bound to two commands. The first in the catalog keeps the chord; the editor shows the
    /// conflict so the user can choose.
    /// </summary>
    public IReadOnlyList<(KeyChord Chord, string First, string Second)> Conflicts => _conflicts;

    /// <summary>The command a chord runs, or null when it runs none.</summary>
    public string? CommandFor(KeyChord chord) => _byChord.GetValueOrDefault(chord);

    /// <summary>A command's chords, in the order they are shown.</summary>
    public IReadOnlyList<KeyChord> ShortcutsFor(string commandId) => _byCommand.GetValueOrDefault(commandId) ?? [];

    /// <summary>The first chord's text for menus ("Ctrl+O"), or an empty string.</summary>
    public string Label(string commandId) => ShortcutsFor(commandId) is [var first, ..] ? first.ToString() : "";
}
