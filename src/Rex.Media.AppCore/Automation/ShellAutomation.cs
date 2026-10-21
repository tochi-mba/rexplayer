using Rex.Media.AppCore.Commands;

namespace Rex.Media.AppCore.Automation;

/// <summary>
/// The window-driving half of the automation pipe (protocol 2), shared by every shell: a request
/// whose command starts with <c>ui.</c> is turned into a call on the shell's
/// <see cref="IShellSurface"/> and the answer into a reply. The commands are <c>ui.tree</c> (every
/// control), <c>ui.menu</c> (the menu bar), <c>ui.press</c>, <c>ui.toggle</c>, <c>ui.set</c> (with
/// <see cref="AutomationRequest.Value"/>), <c>ui.key</c> (with <see cref="AutomationRequest.Chord"/>),
/// <c>ui.text</c> and <c>ui.snapshot</c> (with <see cref="AutomationRequest.Path"/>); the ones that
/// act on a control name it in <see cref="AutomationRequest.Target"/>.
/// </summary>
public static class ShellAutomation
{
    public const string Prefix = "ui.";

    /// <summary>The commands this class answers, for the pipe's own help text.</summary>
    public static IReadOnlyList<string> Commands { get; } = ["ui.tree", "ui.menu", "ui.press", "ui.toggle", "ui.set", "ui.key", "ui.text", "ui.snapshot"];

    /// <summary>Whether a request is one of the window-driving commands rather than open, run or status.</summary>
    public static bool IsUiRequest(string? command) => command is not null && command.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>Answers a window-driving request against <paramref name="surface"/>, on the window's thread.</summary>
    public static async Task<AutomationReply> AnswerAsync(AutomationRequest request, IShellSurface surface)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(surface);
        switch (request.Command)
        {
            case "ui.tree":
                return new AutomationReply { Ok = true, Controls = surface.Controls() };
            case "ui.menu":
                return new AutomationReply { Ok = true, Menus = surface.Menus() };
            case "ui.press":
                return Target(request) is { } pressed ? Acted(surface.Press(pressed), pressed) : NoTarget(request);
            case "ui.toggle":
                return Target(request) is { } toggled ? Acted(surface.Toggle(toggled), toggled) : NoTarget(request);
            case "ui.set":
                if (Target(request) is not { } set)
                {
                    return NoTarget(request);
                }

                return request.Value is { } value ? Acted(surface.SetValue(set, value), set) : AutomationReply.Failed("ui.set needs a value.");
            case "ui.key":
                if (KeyChord.TryParse(request.Chord) is not { } chord)
                {
                    return AutomationReply.Failed($"\"{request.Chord}\" is not a shortcut; write it such as Ctrl+Shift+M.");
                }

                return surface.SendChord(chord) ? new AutomationReply { Ok = true } : AutomationReply.Failed($"{chord} runs nothing here.");
            case "ui.text":
                if (Target(request) is not { } read)
                {
                    return NoTarget(request);
                }

                return surface.Text(read) is { } text ? new AutomationReply { Ok = true, Text = text } : AutomationReply.Failed(Nothing(read));
            case "ui.snapshot":
                if (string.IsNullOrWhiteSpace(request.Path))
                {
                    return AutomationReply.Failed("ui.snapshot needs a path to save the picture at.");
                }

                await surface.SnapshotAsync(request.Path).ConfigureAwait(true);
                return new AutomationReply { Ok = true, Text = request.Path };
            default:
                return AutomationReply.Failed($"\"{request.Command}\" is not a request; the window answers {string.Join(", ", Commands)}.");
        }
    }

    private static string? Target(AutomationRequest request) => string.IsNullOrWhiteSpace(request.Target) ? null : request.Target;

    private static AutomationReply NoTarget(AutomationRequest request) => AutomationReply.Failed($"{request.Command} needs a target: the automation id of a control.");

    private static AutomationReply Acted(bool done, string id) => done ? new AutomationReply { Ok = true } : AutomationReply.Failed(Nothing(id));

    private static string Nothing(string id) => $"Nothing in the window is called \"{id}\", or it cannot be used that way now.";
}
