using System.Text.Json.Nodes;
using Rex.Media.AppCore.Machine;
using Rex.Media.AppCore.Player;
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.AppCore.Cli;

/// <summary>
/// The intro and credits detected in an episode, compared with its nearest siblings in the season
/// folder, exactly as the player detects them. Each episode is analysed once and kept in the
/// player's cache, so running it over a whole season decodes every file once.
/// </summary>
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

        var siblingPaths = EpisodeGrouping.Siblings(path, Directory.EnumerateFiles(folder), SectionDetector.SiblingsCompared);
        var decoders = MediaRegistries.Decoders([.. host.ExtraDecoders]);
        EpisodeAnalysis Analysis(string file)
        {
            var signature = SectionAnalysis.SignatureOf(file);
            if (host.SectionCache.Get(file, signature) is { } kept)
            {
                return kept;
            }

            var made = SectionAnalysis.Analyze(file, decoders, host.Cancellation);
            host.SectionCache.Set(made);
            return made;
        }

        var own = Analysis(path);
        var companions = new List<EpisodeAnalysis>();
        foreach (var sibling in siblingPaths)
        {
            host.Cancellation.ThrowIfCancellationRequested();
            try
            {
                companions.Add(Analysis(sibling));
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
                ["offered"] = proposal.Confidence >= SectionProposals.OfferThreshold,
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
                host.Out.WriteLine($"{proposal.Kind}: {proposal.Start:c} to {proposal.End:c} ({proposal.Confidence:P0}; {proposal.Reason})" +
                    (proposal.Confidence >= SectionProposals.OfferThreshold ? "" : "; too unsure to offer"));
            }

            if (result.Candidates.Count == 0)
            {
                host.Out.WriteLine("No reliable repeated section detected; nothing will be skipped.");
            }
        }

        return 0;
    }
}
