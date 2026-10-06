using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rex.Media.TestKit;

namespace Rex.Media.Windows.Tests.Perf;

/// <summary>
/// The committed performance baselines, one set per lane: a machine (or CI's runners) measures
/// differently, so each compares only with itself. The lane is REXPLAYER_PERF_LANE (CI's is
/// "ci-windows"), or "local-" and the machine's name. A lane without a baseline only reports: a
/// developer's machine runs other work too, so it is too noisy to gate on unless its owner commits
/// a lane for it. Every run writes what it measured to artifacts/perf/&lt;lane&gt;.json (CI keeps it as
/// an artefact); with REXPLAYER_WRITE_PERF=1 the measurements also become the lane's baselines, and
/// a regression accepted that way is explained in the commit.
/// </summary>
internal static class PerfBaselines
{
    /// <summary>Throughput may fall to this share of its baseline before the gate fails.</summary>
    public const double ThroughputFloor = 0.7;

    private static readonly object Gate = new();

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static string Path => RepoPaths.Combine("tests", "perf-baselines.json");

    private static string Measured => RepoPaths.Combine("artifacts", "perf", Lane + ".json");

    public static string Lane => Environment.GetEnvironmentVariable("REXPLAYER_PERF_LANE") is { Length: > 0 } lane
        ? lane
        : "local-" + Environment.MachineName.ToLowerInvariant();

    private static bool Recording => Environment.GetEnvironmentVariable("REXPLAYER_WRITE_PERF") == "1";

    /// <summary>A throughput: higher is better.</summary>
    public static void AtLeast(string measure, double value)
    {
        if (Check(measure, value) is { } baseline)
        {
            Assert.True(value >= baseline * ThroughputFloor, $"{measure} is {value:F1}, below {ThroughputFloor:P0} of the {Lane} baseline {baseline:F1}.");
        }
    }

    /// <summary>The lane's baseline for a measure, or null when there is none or this run records them.</summary>
    private static double? Check(string measure, double value)
    {
        TestContext.Current.SendDiagnosticMessage($"{Lane} {measure}: {value.ToString("F1", CultureInfo.InvariantCulture)}");
        lock (Gate)
        {
            Write(Measured, document => document[measure] = Math.Round(value, 1));
            if (Recording)
            {
                Write(Path, document =>
                {
                    var lanes = document["lanes"]?.AsObject() ?? [];
                    var lane = lanes[Lane]?.AsObject() ?? [];
                    lane[measure] = Math.Round(value, 1);
                    lanes[Lane] = lane;
                    document["lanes"] = lanes;
                });
                return null;
            }

            var baselines = File.Exists(Path) ? JsonNode.Parse(File.ReadAllText(Path)) : null;
            return baselines?["lanes"]?[Lane]?[measure]?.GetValue<double>();
        }
    }

    /// <summary>Changes a JSON document on disk, creating it (and its folder) when it is missing.</summary>
    private static void Write(string path, Action<JsonObject> change)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var document = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))!.AsObject() : [];
        change(document);
        File.WriteAllText(path, document.ToJsonString(Indented) + "\n");
    }
}
