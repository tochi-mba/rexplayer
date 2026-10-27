using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Rex.PerfTour;

/// <summary>
/// The player's own log, watched across the tour for its timing marks and for trouble. Only what
/// the code itself writes is quoted: milestone names and log sources. Messages are never copied
/// out, because they can carry what the library holds.
/// </summary>
public sealed partial class LogWatch
{
    private readonly string _path;
    private long _from;

    public LogWatch(string? root = null)
    {
        root ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "REX", "rexplayer");
        _path = Path.Combine(root, "logs", "rexplayer.log");
        _from = Length();
    }

    private long Length()
    {
        try
        {
            return File.Exists(_path) ? new FileInfo(_path).Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    /// <summary>What the log said during the tour, as a Markdown section; empty when it said nothing.</summary>
    public string Summary()
    {
        string tail;
        try
        {
            if (!File.Exists(_path))
            {
                return "";
            }

            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length < _from)
            {
                _from = 0; // The log rotated under the tour.
            }

            stream.Position = _from;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            tail = reader.ReadToEnd();
        }
        catch (IOException)
        {
            return "";
        }

        var timings = new List<string>();
        var trouble = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in tail.Split('\n'))
        {
            if (TimingLine().Match(line) is { Success: true } timing)
            {
                timings.Add(FormattableString.Invariant($"{timing.Groups["name"].Value} at {timing.Groups["ms"].Value} ms"));
            }
            else if (TroubleLine().Match(line) is { Success: true } warning)
            {
                var key = warning.Groups["level"].Value + " from " + warning.Groups["source"].Value;
                trouble[key] = trouble.GetValueOrDefault(key) + 1;
            }
        }

        if (timings.Count == 0 && trouble.Count == 0)
        {
            return "";
        }

        var text = new StringBuilder("## The player's log during the tour\n\n");
        foreach (var timing in timings)
        {
            text.Append("- timing: ").Append(timing).Append('\n');
        }

        foreach (var (key, count) in trouble.OrderByDescending(pair => pair.Value))
        {
            text.Append(CultureInfo.InvariantCulture, $"- {count} {key} line(s) (messages withheld)\n");
        }

        text.Append('\n');
        return text.ToString();
    }

    [GeneratedRegex(@"\[INFO\] timing: (?<name>[A-Za-z0-9_.-]+) at (?<ms>[0-9.]+) ms")]
    private static partial Regex TimingLine();

    [GeneratedRegex(@"\[(?<level>WARNING|ERROR|CRITICAL)\] (?<source>[A-Za-z0-9_.-]+):")]
    private static partial Regex TroubleLine();
}
