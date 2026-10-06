using System.Globalization;
using System.Text.Json.Nodes;
using Rex.Media.AppCore.Machine;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.Video;

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
        using var demuxer = MediaRegistries.Demuxers().Open(source, host.Cancellation);
        var (picture, decoder) = Snapshot.Take(demuxer, MediaRegistries.Decoders([.. host.ExtraDecoders]), at, host.Cancellation);
        using (picture)
        {
            using var file = File.Create(output);
            PngWriter.Write(file, picture);
            if (machine)
            {
                host.Out.WriteLine(MachineEnvelope.Success("snapshot", new JsonObject
                {
                    ["file"] = Path.GetFileName(path),
                    ["out"] = output,
                    ["at"] = picture.Pts.IsKnown ? picture.Pts.TotalSeconds : null,
                    ["width"] = picture.Width,
                    ["height"] = picture.Height,
                    ["decoder"] = decoder,
                }));
            }
            else
            {
                var shown = picture.Pts.IsKnown ? picture.Pts.ToClock() : "an unknown time";
                host.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Saved the {picture.Width}x{picture.Height} picture at {shown} to {output}."));
            }
        }

        return 0;
    }
}
