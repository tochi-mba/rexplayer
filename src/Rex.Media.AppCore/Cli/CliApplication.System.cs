using System.Globalization;
using System.Text.Json.Nodes;
using Rex.Media.AppCore.Machine;
using Rex.Media.Primitives;

namespace Rex.Media.AppCore.Cli;

/// <summary>rexplay probe --system.</summary>
public static partial class CliApplication
{
    private static int ProbeSystem(bool machine, CliHost host)
    {
        var report = host.SystemReport() ?? throw new NotSupportedException("This build of rexplay cannot describe the system.");
        var own = SystemReport.OwnCodecs();
        var codecs = own.Concat(report.Decoders.Select(d => d.Codec)).Distinct().Order().ToList();
        if (machine)
        {
            var list = new JsonArray();
            foreach (var codec in codecs)
            {
                var windows = report.Decoders.FirstOrDefault(d => d.Codec == codec);
                list.Add(new JsonObject
                {
                    ["codec"] = codec.ToString(),
                    ["codecName"] = codec.DisplayName(),
                    ["own"] = own.Contains(codec),
                    ["windows"] = new JsonArray([.. (windows?.Software ?? []).Select(name => (JsonNode)name)]),
                    ["windowsHardware"] = new JsonArray([.. (windows?.Hardware ?? []).Select(name => (JsonNode)name)]),
                });
            }

            host.Out.WriteLine(MachineEnvelope.Success("probe", new JsonObject
            {
                ["windows"] = report.Windows,
                ["graphics"] = report.Graphics,
                ["graphicsInSoftware"] = report.GraphicsInSoftware,
                ["audioOutput"] = report.AudioOutput is { } format ? new JsonObject { ["sampleRate"] = format.SampleRate, ["channels"] = format.Channels } : null,
                ["codecs"] = list,
            }));
            return 0;
        }

        host.Out.WriteLine($"Windows {report.Windows}");
        host.Out.WriteLine(report.Graphics is null ? "Graphics: none would start" : $"Graphics: {report.Graphics}{(report.GraphicsInSoftware ? " (software)" : string.Empty)}");
        host.Out.WriteLine(report.AudioOutput is { } output
            ? string.Create(CultureInfo.InvariantCulture, $"Audio output: {output.SampleRate} Hz, {output.Channels} channels")
            : "Audio output: none plugged in");
        foreach (var codec in codecs)
        {
            var windows = report.Decoders.FirstOrDefault(d => d.Codec == codec);
            var sources = new List<string>();
            if (own.Contains(codec))
            {
                sources.Add("rexplayer");
            }

            if (windows is { Software.Count: > 0 })
            {
                sources.Add("Windows");
            }

            if (windows is { Hardware.Count: > 0 })
            {
                sources.Add("graphics card");
            }

            host.Out.WriteLine($"  {codec.DisplayName()}: {(sources.Count > 0 ? string.Join(", ", sources) : "not available")}");
        }

        return 0;
    }
}
