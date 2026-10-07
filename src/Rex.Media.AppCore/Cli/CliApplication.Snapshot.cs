using System.Globalization;
using System.Text.Json.Nodes;
using Rex.Media.AppCore.Machine;
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.AppCore.Cli;

/// <summary>The snapshot command.</summary>
public static partial class CliApplication
{
    private static int TakeSnapshot(CliArguments arguments, bool machine, CliHost host)
    {
        var path = RequireFile(arguments);
        var at = ParseTime(arguments.Option("--at"), "--at") ?? MediaTime.Zero;
        var output = arguments.Option("--out") is { Length: > 0 } chosen
            ? Path.GetFullPath(chosen)
            : Path.GetFullPath(Path.ChangeExtension(path, ".png"));
        using var source = new FileByteSource(path);
        var saved = Snapshot.SaveAsPng(source, MediaRegistries.Decoders([.. host.ExtraDecoders]), at, output, host.Cancellation);
        if (machine)
        {
            host.Out.WriteLine(MachineEnvelope.Success("snapshot", new JsonObject
            {
                ["file"] = Path.GetFileName(path),
                ["out"] = output,
                ["at"] = saved.At.IsKnown ? saved.At.TotalSeconds : null,
                ["width"] = saved.Width,
                ["height"] = saved.Height,
                ["decoder"] = saved.Decoder,
            }));
        }
        else
        {
            var shown = saved.At.IsKnown ? saved.At.ToClock() : "an unknown time";
            host.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Saved the {saved.Width}x{saved.Height} picture at {shown} to {output}."));
        }

        return 0;
    }
}
