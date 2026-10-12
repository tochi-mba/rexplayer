using System.Text.Json.Nodes;
using Rex.Media.AppCore.Machine;
using Rex.Media.AppCore.Player;
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.AppCore.Cli;

/// <summary>Machine-readable intro/credits proposals; no media playback or user confirmation.</summary>
public static partial class CliApplication
{
    private static int Sections(CliArguments arguments, bool machine, CliHost host)
    {
        var path = Path.GetFullPath(RequireFile(arguments));
        var folder = arguments.Option("--season-folder") is { Length: > 0 } supplied
            ? Path.GetFullPath(supplied) : Path.GetDirectoryName(path)!;
        if (!Directory.Exists(folder))
        {
            throw new CliException("The season folder does not exist: " + folder);
        }

        var identity = EpisodeGrouping.Identify(path);
        IReadOnlyList<string> siblingPaths = identity is null ? [] : EpisodeGrouping.Siblings(path, Directory.EnumerateFiles(folder));
        var decoders = MediaRegistries.Decoders([.. host.ExtraDecoders]);
        var own = SectionAnalysis.Analyze(path, decoders, host.Cancellation);
        var companions = new List<EpisodeAnalysis>();
        foreach (var sibling in siblingPaths.Take(12))
        {
            host.Cancellation.ThrowIfCancellationRequested();
            try
            {
                companions.Add(SectionAnalysis.Analyze(sibling, decoders, host.Cancellation));
            }
            catch (Exception ex) when (ex is IOException or MediaFormatException or NotSupportedException)
            {
                // One bad sibling must not prevent the remaining episodes from being compared.
                host.Log.Warning("sections", "Could not analyse sibling " + sibling + ": " + ex.Message);
            }
        }

        var result = SectionDetector.Detect(own, companions);
        using var source = new FileByteSource(path);
        using var demuxer = MediaRegistries.Demuxers().Open(source, host.Cancellation);
        var chapters = EpisodeSectionDetection.Suggest(demuxer.Info.Chapters, own.Duration);
        var sections = new JsonArray();
        foreach (var proposal in result.Candidates)
        {
            sections.Add(new JsonObject
            {
                ["kind"] = proposal.Kind.ToString().ToLowerInvariant(),
                ["start"] = proposal.Start.TotalSeconds,
                ["end"] = proposal.End.TotalSeconds,
                ["confidence"] = proposal.Confidence,
                ["reason"] = proposal.Reason,
                ["source"] = proposal.Source,
            });
        }

        var data = new JsonObject
        {
            ["file"] = path,
            ["signature"] = own.Signature,
            ["episodesCompared"] = companions.Count,
            ["proposals"] = sections,
            ["chapterHints"] = chapters is null ? null : new JsonObject
            {
                ["introStart"] = chapters.IntroStart?.TotalSeconds,
                ["introEnd"] = chapters.IntroEnd?.TotalSeconds,
                ["creditsStart"] = chapters.CreditsStart?.TotalSeconds,
                ["creditsEnd"] = chapters.CreditsEnd?.TotalSeconds,
            },
            ["autoSkipEnabled"] = false,
        };
        if (machine)
        {
            host.Out.WriteLine(MachineEnvelope.Success("sections", data));
        }
        else
        {
            host.Out.WriteLine($"{Path.GetFileName(path)}: compared with {companions.Count} episodes.");
            foreach (var proposal in result.Candidates)
            {
                host.Out.WriteLine($"{proposal.Kind}: {proposal.Start:c} – {proposal.End:c} ({proposal.Confidence:P0}; {proposal.Reason})");
            }

            if (result.Candidates.Count == 0)
            {
                host.Out.WriteLine("No reliable repeated section detected; nothing will be skipped.");
            }
        }

        return 0;
    }
}
