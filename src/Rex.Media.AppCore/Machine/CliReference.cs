namespace Rex.Media.AppCore.Machine;

/// <summary>One command the rexplay command line accepts.</summary>
public sealed record CliCommand(string Name, string Usage, string Summary, bool MachineReadable);

/// <summary>One option a command accepts.</summary>
public sealed record CliOption(string Name, string Argument, string Summary);

/// <summary>
/// Every command and option rexplay accepts. Help text, the capabilities document and the website's
/// command-line page are all generated from this list, and a test checks that the parser accepts
/// exactly these commands, so the three can never disagree.
/// </summary>
public static class CliReference
{
    public static IReadOnlyList<CliCommand> Commands { get; } =
    [
        new("play", "rexplay play <file> [more files] [options]", "Plays the files one after another without gaps, then exits.", MachineReadable: true),
        new("probe", "rexplay probe <file>", "Describes a file's format, tracks and tags without playing it.", MachineReadable: true),
        new("version", "rexplay version", "Prints the version.", MachineReadable: true),
        new("help", "rexplay help [command]", "Explains the commands.", MachineReadable: false),
        new("agent capabilities", "rexplay agent capabilities", "Describes this build for scripts and agents as one JSON line.", MachineReadable: true),
    ];

    public static IReadOnlyList<CliOption> Options { get; } =
    [
        new("--json", string.Empty, "Prints exactly one JSON document instead of text (machine mode)."),
        new("--aout", "<sink>", "Where audio goes: 'default' (the Windows default device), 'null' (nowhere, in real time) or 'wav:<path>' (a capture file, as fast as possible)."),
        new("--start", "<time>", "Starts playing at a time such as 90, 1:30 or 1:02:03.5."),
        new("--stop", "<time>", "Stops playing at a time."),
        new("--volume", "<percent>", "Sets the volume, from 0 to 200."),
    ];

    public static CliCommand? Find(string name) =>
        Commands.FirstOrDefault(command => string.Equals(command.Name, name, StringComparison.Ordinal));
}
