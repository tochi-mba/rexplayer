using Rex.Media.AppCore.Commands;

namespace Rex.Media.AppCore.Automation;

/// <summary>
/// One control of the window as a script sees it (protocol 2): the automation id every shell gives
/// it, what kind of thing it is in the shell's own words, its name for a screen reader, and what
/// it holds now.
/// </summary>
public sealed record UiControl
{
    public required string Id { get; init; }

    public required string Kind { get; init; }

    public string Name { get; init; } = "";

    public bool Enabled { get; init; } = true;

    public bool Visible { get; init; } = true;

    /// <summary>On or off, for a control that is one or the other; null otherwise.</summary>
    public bool? Checked { get; init; }

    /// <summary>The position of a slider or the like; null otherwise.</summary>
    public double? Value { get; init; }

    /// <summary>The text shown, for a control that shows some; null otherwise.</summary>
    public string? Text { get; init; }
}

/// <summary>
/// One entry of a menu as rendered: a menu, a command, a sub-menu, a toggle or a separator, with
/// the entries under it. Both shells report theirs through this, which is how a test proves the
/// menus match.
/// </summary>
public sealed record UiMenuItem
{
    public required string Id { get; init; }

    public string Title { get; init; } = "";

    /// <summary>menu, command, submenu, toggle or separator.</summary>
    public required string Kind { get; init; }

    /// <summary>The shortcut shown beside the entry, or null.</summary>
    public string? Shortcut { get; init; }

    public bool? Checked { get; init; }

    public bool Enabled { get; init; } = true;

    public IReadOnlyList<UiMenuItem> Items { get; init; } = [];
}

/// <summary>
/// What a shell lets a script do to its window (protocol 2). Each shell answers from its own
/// widgets; <see cref="ShellAutomation"/> turns pipe requests into these calls and the answers
/// into replies, so the two never differ in what they accept or how they fail. Every method runs
/// on the window's thread; a method given an id that names nothing returns false or null.
/// </summary>
public interface IShellSurface
{
    /// <summary>Every control that carries an automation id, in tree order.</summary>
    IReadOnlyList<UiControl> Controls();

    /// <summary>The menu bar as rendered now, menus first, each with its entries.</summary>
    IReadOnlyList<UiMenuItem> Menus();

    /// <summary>Presses a button or menu item; chooses a choice.</summary>
    bool Press(string id);

    /// <summary>Flips a control that is on or off.</summary>
    bool Toggle(string id);

    /// <summary>Moves a slider or the like.</summary>
    bool SetValue(string id, double value);

    /// <summary>Presses a key chord as the keyboard would, through the window's own shortcuts.</summary>
    bool SendChord(KeyChord chord);

    /// <summary>The text a control shows, or null when it names nothing.</summary>
    string? Text(string id);

    /// <summary>Saves what the window shows as a PNG at <paramref name="path"/>.</summary>
    Task SnapshotAsync(string path);
}
