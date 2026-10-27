using System.Globalization;
using System.IO;
using Rex.PerfTour;

// perf-tour: attach to a RUNNING rexplayer and walk it through a scripted tour, timing every step
// as action-to-settled latency and sampling the process between steps. It never launches, closes
// or reconfigures the player, and its report never names what the library holds.
//
//   perf-tour [--pid N] [--pipe NAME] [--label NAME] [--out PATH]
//
// Without --pid it attaches to the rexplayer window that is running. Exit codes: 0 toured,
// 2 nothing to attach to, 3 bad arguments.
int? pid = null;
string? pipe = null;
var label = "tour";
string? output = null;
for (var at = 0; at < args.Length - 1; at += 2)
{
    switch (args[at])
    {
        case "--pid" when int.TryParse(args[at + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed):
            pid = parsed;
            break;
        case "--pipe":
            pipe = args[at + 1];
            break;
        case "--label":
            label = args[at + 1];
            break;
        case "--out":
            output = args[at + 1];
            break;
        default:
            Console.Error.WriteLine("perf-tour [--pid N] [--pipe NAME] [--label NAME] [--out PATH]");
            return 3;
    }
}

if (args.Length % 2 != 0)
{
    Console.Error.WriteLine("Every option takes a value: perf-tour [--pid N] [--pipe NAME] [--label NAME] [--out PATH]");
    return 3;
}

var app = AttachedApp.Attach(pid, pipe);
if (app is null)
{
    Console.Error.WriteLine(pid is { } wanted
        ? FormattableString.Invariant($"No rexplayer with PID {wanted} is showing a player window.")
        : "No running rexplayer is showing a player window.");
    return 2;
}

Console.WriteLine(FormattableString.Invariant(
    $"Attached to rexplayer PID {app.Process.Id}; pipe {(app.PipeAnswers ? "answers" : "silent")}."));
var log = new LogWatch();
var result = new Tour(app).Run();
var report = Report.Write(result, label, app.Process.Id) + log.Summary();
if (output is null)
{
    Console.WriteLine(report);
}
else
{
    File.WriteAllText(output, report);
    Console.WriteLine("Report written to " + output);
}

var failed = result.Steps.Count(step => !step.Ok);
Console.WriteLine(FormattableString.Invariant(
    $"{result.Steps.Count} steps, {failed} did not settle, slowest {result.Steps.Where(s => s.Ok).Select(s => s.Ms).DefaultIfEmpty(0).Max():0} ms."));
return 0;
