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
        new("snapshot", "rexplay snapshot <file> [--at <time>] [--out <png>]", "Saves the picture a video shows at a moment as a PNG.", MachineReadable: true),
        new("sections", "rexplay sections <file> [--season-folder <dir>] [--json]", "Finds repeat-based intro and credits suggestions without skipping media.", MachineReadable: true),
        new("pulse", "rexplay pulse <file> [--start <time>] [--stop <time>] [--fps <frames>] [--json]", "Hears a file as the visualisations do: its tempo, every beat, and how strongly it moves them.", MachineReadable: true),
        new("captions", "rexplay captions <file> [--subtitles <file>] [--words subtitles|transcript|timed] [--model <id>] [--start <time>] [--stop <time>] [--fps <frames>] [--style <look>] [--energy <percent>] [--layout scatter|stack|centre|left|confetti] [--no-avoid] [--json]", "Places the words of a subtitle file or a transcript as kinetic captions would show them, frame by frame: sized, coloured and moved by how they were said, clear of what matters in the picture.", MachineReadable: true),
        new("transcribe", "rexplay transcribe <file> [--model tiny.en|base.en|small.en] [--language <code>] [--download] [--again] [--out <file.srt|.vtt|.json>] [--json]", "Hears a file's speech with a downloaded model, word by word with the time each is said, and keeps the transcript for the player.", MachineReadable: true),
        new("models", "rexplay models [--get <id>] [--remove <id>] [--json]", "Lists the speech models, downloads one, or removes one.", MachineReadable: true),
        new("probe", "rexplay probe <file> | --system", "Describes a file's format, tracks and tags without playing it, or with --system what this PC can decode and show.", MachineReadable: true),
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
        new("--system", string.Empty, "With probe: describes this PC's decoders, graphics and audio output instead of a file."),
        new("--window", string.Empty, "Shows the pictures in a window while playing; closing it stops playback."),
        new("--at", "<time>", "The moment a snapshot shows (default 0)."),
        new("--out", "<path>", "Where a snapshot is saved (default: beside the video, with its name and .png)."),
        new("--season-folder", "<dir>", "Folder containing other episodes of this show and season."),
        new("--fps", "<frames>", "With pulse: how many frames a second the music is heard at, as a window drawing that fast hears it (default 30)."),
    ];

    public static CliCommand? Find(string name) =>
        Commands.FirstOrDefault(command => string.Equals(command.Name, name, StringComparison.Ordinal));
}
