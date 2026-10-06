namespace Rex.Media.AppCore.Cli;

/// <summary>
/// The command line split into a command, positional arguments and options. Options take the form
/// <c>--name value</c> or <c>--name=value</c>; flags are options without a value. <c>--json</c> may
/// appear anywhere, so <c>rexplay --json probe x</c> and <c>rexplay probe x --json</c> mean the same.
/// </summary>
public sealed class CliArguments
{
    private static readonly HashSet<string> Flags = new(StringComparer.Ordinal) { "--json", "--window" };

    private readonly Dictionary<string, string> _options = new(StringComparer.Ordinal);
    private readonly List<string> _positional = [];

    private CliArguments()
    {
    }

    /// <summary>The command, including "agent" sub-commands ("agent capabilities"), or empty.</summary>
    public string Command { get; private set; } = string.Empty;

    public IReadOnlyList<string> Positional => _positional;

    public bool Json => _options.ContainsKey("--json");

    public static CliArguments Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var result = new CliArguments();
        var words = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                words.Add(arg);
                continue;
            }

            var equals = arg.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0)
            {
                result._options[arg[..equals]] = arg[(equals + 1)..];
            }
            else if (Flags.Contains(arg) || i + 1 >= args.Count)
            {
                result._options[arg] = string.Empty;
            }
            else
            {
                result._options[arg] = args[++i];
            }
        }

        if (words.Count > 0)
        {
            var first = words[0];
            words.RemoveAt(0);
            if (first == "agent" && words.Count > 0)
            {
                first = "agent " + words[0];
                words.RemoveAt(0);
            }

            result.Command = first;
        }

        result._positional.AddRange(words);
        return result;
    }

    public string? Option(string name) => _options.TryGetValue(name, out var value) ? value : null;

    public IEnumerable<string> OptionNames => _options.Keys;
}
