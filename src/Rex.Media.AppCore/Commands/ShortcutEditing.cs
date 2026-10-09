using System.Text.Json;
using System.Text.Json.Serialization;
using Rex.Media.Settings;

namespace Rex.Media.AppCore.Commands;

/// <summary>What the keyboard and mouse are set to do, as one file to keep or share (UI-11).</summary>
public sealed record InputProfile
{
    public IReadOnlyDictionary<string, string> Shortcuts { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public IReadOnlyList<string> GlobalShortcuts { get; init; } = [];

    public WheelChoice Wheel { get; init; } = WheelChoice.Volume;

    public WheelChoice SidewaysWheel { get; init; } = WheelChoice.Seek;

    public MiddleButtonChoice MiddleButton { get; init; } = MiddleButtonChoice.PlayPause;

    public SideButtonChoice SideButtons { get; init; } = SideButtonChoice.PreviousNext;
}

/// <summary>A binding made: the changes after it, and the command whose shortcut it took, if any.</summary>
public sealed record ShortcutChange(IReadOnlyDictionary<string, string> Changes, string? TakenFrom);

/// <summary>
/// The hotkey editor's rules (UI-11), kept apart from the window: changes are what
/// <see cref="Keymap"/> lays over the defaults, by command id (a chord's text, or empty for none).
/// </summary>
public static class ShortcutEditing
{
    /// <summary>
    /// Gives <paramref name="commandId"/> the one shortcut <paramref name="chord"/>. A command that
    /// had it loses it (keeping any other shortcut of its own), and is named so the editor can say so.
    /// </summary>
    public static ShortcutChange Bind(IReadOnlyDictionary<string, string> changes, string commandId, KeyChord chord)
    {
        ArgumentNullException.ThrowIfNull(changes);
        Known(commandId);
        var keymap = new Keymap(changes);
        var edited = new Dictionary<string, string>(changes, StringComparer.Ordinal) { [commandId] = chord.ToString() };
        var holder = keymap.CommandFor(chord);
        if (holder is null || holder == commandId)
        {
            return new ShortcutChange(edited, null);
        }

        edited[holder] = keymap.ShortcutsFor(holder).Where(other => other != chord).Select(other => other.ToString()).FirstOrDefault() ?? "";
        return new ShortcutChange(edited, holder);
    }

    /// <summary>Takes every shortcut from <paramref name="commandId"/>.</summary>
    public static IReadOnlyDictionary<string, string> Clear(IReadOnlyDictionary<string, string> changes, string commandId)
    {
        ArgumentNullException.ThrowIfNull(changes);
        Known(commandId);
        return new Dictionary<string, string>(changes, StringComparer.Ordinal) { [commandId] = "" };
    }

    /// <summary>Gives <paramref name="commandId"/> its own shortcuts back; another command using one of them shows as a conflict.</summary>
    public static IReadOnlyDictionary<string, string> Reset(IReadOnlyDictionary<string, string> changes, string commandId)
    {
        ArgumentNullException.ThrowIfNull(changes);
        Known(commandId);
        var edited = new Dictionary<string, string>(changes, StringComparer.Ordinal);
        edited.Remove(commandId);
        return edited;
    }

    /// <summary>Whether <paramref name="commandId"/>'s shortcuts are not its own.</summary>
    public static bool IsChanged(IReadOnlyDictionary<string, string> changes, string commandId)
    {
        ArgumentNullException.ThrowIfNull(changes);
        return changes.ContainsKey(commandId);
    }

    /// <summary>The settings' keyboard and mouse choices as a file's text.</summary>
    public static string Export(PlayerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var profile = new InputProfile
        {
            Shortcuts = new SortedDictionary<string, string>(settings.Shortcuts.ToDictionary(), StringComparer.Ordinal),
            GlobalShortcuts = settings.GlobalShortcuts,
            Wheel = settings.Wheel,
            SidewaysWheel = settings.SidewaysWheel,
            MiddleButton = settings.MiddleButton,
            SideButtons = settings.SideButtons,
        };
        return JsonSerializer.Serialize(profile, ShortcutJson.Default.InputProfile);
    }

    /// <summary>
    /// <paramref name="settings"/> with the keyboard and mouse choices of an exported file. Commands
    /// this version does not know are left out; text that is no such file throws FormatException.
    /// </summary>
    public static PlayerSettings Import(PlayerSettings settings, string text)
    {
        ArgumentNullException.ThrowIfNull(settings);
        InputProfile? profile;
        try
        {
            profile = JsonSerializer.Deserialize(text ?? "", ShortcutJson.Default.InputProfile);
        }
        catch (JsonException ex)
        {
            throw new FormatException("That is not a keyboard and mouse file from rexplayer.", ex);
        }

        if (profile is null)
        {
            throw new FormatException("That is not a keyboard and mouse file from rexplayer.");
        }

        return (settings with
        {
            Shortcuts = (profile.Shortcuts ?? new Dictionary<string, string>()).Where(pair => CommandCatalog.Find(pair.Key) is not null).ToDictionary(StringComparer.Ordinal),
            GlobalShortcuts = [.. (profile.GlobalShortcuts ?? []).Where(id => CommandCatalog.Find(id) is not null)],
            Wheel = profile.Wheel,
            SidewaysWheel = profile.SidewaysWheel,
            MiddleButton = profile.MiddleButton,
            SideButtons = profile.SideButtons,
        }).Normalize();
    }

    /// <summary>The command a turn of the wheel runs: up or right is <paramref name="forward"/>; null for nothing.</summary>
    public static string? WheelCommand(WheelChoice choice, bool forward) => choice switch
    {
        WheelChoice.Volume => forward ? CommandCatalog.VolumeUp : CommandCatalog.VolumeDown,
        WheelChoice.Seek => forward ? CommandCatalog.JumpForwardVeryShort : CommandCatalog.JumpBackVeryShort,
        _ => null,
    };

    /// <summary>The command the middle button runs; null for nothing.</summary>
    public static string? MiddleButtonCommand(MiddleButtonChoice choice) => choice switch
    {
        MiddleButtonChoice.PlayPause => CommandCatalog.PlayPause,
        MiddleButtonChoice.FullScreen => CommandCatalog.ToggleFullScreen,
        MiddleButtonChoice.Mute => CommandCatalog.Mute,
        _ => null,
    };

    /// <summary>The command the back or (with <paramref name="forward"/>) forward button runs; null for nothing.</summary>
    public static string? SideButtonCommand(SideButtonChoice choice, bool forward) => choice switch
    {
        SideButtonChoice.PreviousNext => forward ? CommandCatalog.Next : CommandCatalog.Previous,
        SideButtonChoice.JumpBackForward => forward ? CommandCatalog.JumpForwardShort : CommandCatalog.JumpBackShort,
        _ => null,
    };

    private static void Known(string commandId)
    {
        ArgumentNullException.ThrowIfNull(commandId);
        if (CommandCatalog.Find(commandId) is null)
        {
            throw new ArgumentException($"There is no command {commandId}.", nameof(commandId));
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true, WriteIndented = true)]
[JsonSerializable(typeof(InputProfile))]
internal sealed partial class ShortcutJson : JsonSerializerContext;
