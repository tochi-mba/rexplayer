using System.Xml.Linq;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Repository;

/// <summary>
/// The project graph of ADR-001. Each project may reference only the projects listed for it, and
/// each targets exactly the framework listed: the portable engine projects stay free of Windows,
/// and only the adapter, interop, command-line and app projects may know about it.
/// </summary>
public sealed class ArchitectureTests
{
    private const string Portable = "net10.0";
    private const string Windows = "net10.0-windows";
    private const string WindowsApp = "net10.0-windows10.0.22621.0";

    private static readonly string[] Everything =
    [
        "Primitives", "Diagnostics", "IO", "Containers", "Codecs", "Codecs.Software", "Codecs.MediaFoundation",
        "Audio", "Audio.Wasapi", "Video", "Video.D3D11", "Subtitles", "Subtitles.DirectWrite", "Engine",
        "Net", "Library", "Settings", "StreamOut", "Cast", "Discs", "Discs.Device", "Extensibility",
        "AppCore", "Updater", "Interop",
    ];

    private static readonly Dictionary<string, (string Framework, string[] Allowed)> Graph = new(StringComparer.Ordinal)
    {
        ["Primitives"] = (Portable, []),
        ["Diagnostics"] = (Portable, ["Primitives"]),
        ["IO"] = (Portable, ["Primitives", "Diagnostics"]),
        ["Containers"] = (Portable, ["Primitives", "Diagnostics", "IO"]),
        ["Codecs"] = (Portable, ["Primitives", "Diagnostics"]),
        ["Codecs.Software"] = (Portable, ["Primitives", "Diagnostics", "Codecs"]),
        ["Codecs.MediaFoundation"] = (Windows, ["Primitives", "Diagnostics", "IO", "Containers", "Codecs", "Video", "Interop"]),
        ["Audio"] = (Portable, ["Primitives", "Diagnostics", "IO"]),
        ["Audio.Wasapi"] = (Windows, ["Primitives", "Diagnostics", "Audio", "Interop"]),
        ["Video"] = (Portable, ["Primitives", "Diagnostics"]),
        ["Video.D3D11"] = (Windows, ["Primitives", "Diagnostics", "Video", "Interop"]),
        ["Subtitles"] = (Portable, ["Primitives", "Diagnostics", "IO"]),
        ["Subtitles.DirectWrite"] = (Windows, ["Primitives", "Subtitles", "Interop"]),
        ["Engine"] = (Portable, ["Primitives", "Diagnostics", "IO", "Containers", "Codecs", "Codecs.Software", "Audio", "Video", "Subtitles"]),
        ["Net"] = (Portable, ["Primitives", "Diagnostics", "IO"]),
        ["Library"] = (Portable, ["Primitives", "Diagnostics", "IO", "Containers"]),
        ["Settings"] = (Portable, ["Primitives", "Diagnostics", "IO"]),
        ["StreamOut"] = (Portable, ["Primitives", "Diagnostics", "IO", "Containers", "Codecs", "Audio", "Video", "Engine", "Net"]),
        ["Cast"] = (Portable, ["Primitives", "Diagnostics", "IO", "Net", "StreamOut"]),
        ["Discs"] = (Portable, ["Primitives", "Diagnostics", "IO", "Containers"]),
        ["Discs.Device"] = (Windows, ["Primitives", "Discs", "Interop"]),
        ["Extensibility"] = (Portable, ["Primitives", "Diagnostics"]),
        ["AppCore"] = (Portable, ["Primitives", "Diagnostics", "IO", "Containers", "Codecs", "Codecs.Software", "Audio", "Video", "Subtitles", "Engine", "Net", "Library", "Settings", "StreamOut", "Cast", "Discs", "Extensibility"]),
        ["Updater"] = (Windows, ["Primitives", "Diagnostics", "IO", "Settings"]),
        ["Interop"] = (Windows, []),
        ["Cli"] = (Windows, Everything),
        ["App"] = (WindowsApp, Everything),
    };

    [Fact]
    public void EveryProjectIsInTheGraphWithItsFrameworkAndAllowedReferences()
    {
        var problems = new List<string>();
        foreach (var path in ProductProjects())
        {
            var name = ShortName(path);
            if (!Graph.TryGetValue(name, out var rule))
            {
                problems.Add($"{name} is not in the project graph; add it to ADR-001 and to this test.");
                continue;
            }

            var project = XDocument.Load(RepoPaths.Combine(path));
            var framework = project.Descendants("TargetFramework").SingleOrDefault()?.Value ?? Portable;
            if (framework != rule.Framework)
            {
                problems.Add($"{name} targets {framework}; the graph says {rule.Framework}.");
            }

            foreach (var reference in project.Descendants("ProjectReference").Select(r => ShortName((string)r.Attribute("Include")!)))
            {
                if (!rule.Allowed.Contains(reference, StringComparer.Ordinal))
                {
                    problems.Add($"{name} references {reference}, which the graph does not allow.");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void TheGraphHasNoCycles()
    {
        foreach (var start in Graph.Keys)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>(Graph[start].Allowed);
            while (pending.Count > 0)
            {
                var next = pending.Pop();
                Assert.NotEqual(start, next);
                if (seen.Add(next) && Graph.TryGetValue(next, out var rule))
                {
                    foreach (var reference in rule.Allowed)
                    {
                        pending.Push(reference);
                    }
                }
            }
        }
    }

    private static IEnumerable<string> ProductProjects() =>
        RepoPaths.SourceFiles().Where(path => path.StartsWith("src/", StringComparison.Ordinal) && path.EndsWith(".csproj", StringComparison.Ordinal));

    private static string ShortName(string path)
    {
        var file = Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));
        const string prefix = "Rex.Media.";
        return file.StartsWith(prefix, StringComparison.Ordinal) ? file[prefix.Length..] : file;
    }
}
