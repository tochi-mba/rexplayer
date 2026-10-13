using System.Globalization;
using System.Text.Json.Nodes;
using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Machine;
using Rex.Media.Settings;

namespace Rex.Media.AppCore.Cli;

/// <summary>
/// Speech heard without a window (SUB-19): <c>rexplay transcribe</c> hears a file with a downloaded
/// model (downloading it first when asked), keeps the transcript in the cache and writes it out as
/// SRT, WebVTT or JSON; <c>rexplay models</c> lists, fetches and removes the models.
/// </summary>
public static partial class CliApplication
{
    private static int Transcribe(CliArguments arguments, bool machine, CliHost host)
    {
        var path = Path.GetFullPath(RequireFile(arguments));
        var model = ParseModel(arguments.Option("--model"));
        var language = arguments.Option("--language") is { } code ? new KineticCaptionSettings { TranscriptLanguage = code }.Normalize().TranscriptLanguage : null;
        var store = new SpeechModelStore(SpeechModelStore.DefaultRoot(DataRoot(host)));
        var cache = new TranscriptCache(TranscriptCache.DefaultRoot(DataRoot(host)));
        var fresh = arguments.Option("--again") is not null;
        var transcript = fresh ? null : cache.Load(path, model.Id);
        var cached = transcript is not null;
        if (transcript is null)
        {
            if (!store.IsInstalled(model))
            {
                if (arguments.Option("--download") is null)
                {
                    throw new CliException($"The {model.Name} model ({model.Size}) is not downloaded; run 'rexplay models --get {model.Id}', or add --download to fetch it now.");
                }

                Progress(host, machine, $"Downloading the {model.Name} model ({model.Size})");
                store.DownloadAsync(model, Reporter(host, machine), host.Cancellation).GetAwaiter().GetResult();
            }

            var transcriber = host.Transcriber(store.PathOf(model), model.Id) ?? throw new CliException("This build cannot hear speech.");
            Progress(host, machine, $"Hearing {Path.GetFileName(path)} with {model.Name}");
            transcript = transcriber.TranscribeAsync(path, string.IsNullOrEmpty(language) ? null : language, Reporter(host, machine), host.Cancellation).GetAwaiter().GetResult();
            cache.Save(path, transcript);
        }

        var written = WriteTranscript(transcript, arguments.Option("--out"));
        if (machine)
        {
            host.Out.WriteLine(MachineEnvelope.Success("transcribe", new JsonObject
            {
                ["file"] = path,
                ["model"] = model.Id,
                ["language"] = transcript.Language,
                ["cached"] = cached,
                ["out"] = written,
                ["words"] = new JsonArray([.. transcript.Words.Select(word => new JsonObject { ["text"] = word.Text, ["start"] = Round(word.Start.TotalSeconds), ["end"] = Round(word.End.TotalSeconds), ["confidence"] = Round(word.Confidence) })]),
            }));
        }
        else
        {
            host.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileName(path)}: {transcript.Words.Count} words heard with {model.Name}{(cached ? " (from the cache)" : "")}{(written is null ? "" : ", written to " + written)}."));
            foreach (var cue in transcript.ToCues())
            {
                host.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{cue.Start.TotalSeconds,8:0.00}  {cue.Text}"));
            }
        }

        return 0;
    }

    private static int Models(CliArguments arguments, bool machine, CliHost host)
    {
        var store = new SpeechModelStore(SpeechModelStore.DefaultRoot(DataRoot(host)));
        if (arguments.Option("--get") is { } wanted)
        {
            var model = SpeechModels.Find(wanted) ?? throw new CliException($"There is no model '{wanted}'; the models are {string.Join(", ", SpeechModels.All.Select(m => m.Id))}.");
            Progress(host, machine, $"Downloading the {model.Name} model ({model.Size})");
            store.DownloadAsync(model, Reporter(host, machine), host.Cancellation).GetAwaiter().GetResult();
        }

        if (arguments.Option("--remove") is { } unwanted)
        {
            var model = SpeechModels.Find(unwanted) ?? throw new CliException($"There is no model '{unwanted}'.");
            store.Remove(model);
        }

        if (machine)
        {
            host.Out.WriteLine(MachineEnvelope.Success("models", new JsonObject
            {
                ["folder"] = store.Root,
                ["models"] = new JsonArray([.. SpeechModels.All.Select(model => new JsonObject
                {
                    ["id"] = model.Id,
                    ["name"] = model.Name,
                    ["bytes"] = model.Bytes,
                    ["licence"] = model.Licence,
                    ["speed"] = model.Speed,
                    ["installed"] = store.IsInstalled(model),
                    ["partialBytes"] = store.PartialBytes(model),
                })]),
            }));
        }
        else
        {
            host.Out.WriteLine("Speech models in " + store.Root);
            foreach (var model in SpeechModels.All)
            {
                host.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {model.Id,-10} {model.Size,8}  {(store.IsInstalled(model) ? "installed" : store.PartialBytes(model) > 0 ? "part downloaded" : "not downloaded"),-16} {model.Speed}"));
            }
        }

        return 0;
    }

    /// <summary>The transcript written as the file's kind says (.srt, .vtt or .json), or nothing without --out.</summary>
    private static string? WriteTranscript(Transcript transcript, string? output)
    {
        if (output is null)
        {
            return null;
        }

        var path = Path.GetFullPath(output);
        var text = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".srt" => transcript.ToSrt(),
            ".vtt" => transcript.ToWebVtt(),
            ".json" => transcript.ToJson(),
            var other => throw new CliException($"--out takes a .srt, .vtt or .json file, not '{other}'."),
        };
        File.WriteAllText(path, text);
        return path;
    }

    private static SpeechModel ParseModel(string? text) => text is null ? SpeechModels.BaseEnglish
        : SpeechModels.Find(text) ?? throw new CliException($"--model takes {string.Join(", ", SpeechModels.All.Select(model => model.Id))}, not '{text}'.");

    private static string DataRoot(CliHost host) => host.DataRoot ?? Path.Combine(Path.GetTempPath(), "rexplayer");

    /// <summary>A line of progress on the error stream, so it never joins a machine-readable answer.</summary>
    private static void Progress(CliHost host, bool machine, string text)
    {
        if (!machine)
        {
            host.Error.WriteLine(text + "...");
        }
    }

    /// <summary>Progress on the error stream, written as it comes (not posted, as Progress would, to a thread that may have moved on).</summary>
    private static IProgress<double>? Reporter(CliHost host, bool machine) => machine ? null : new LineProgress(host.Error);

    private sealed class LineProgress(TextWriter error) : IProgress<double>
    {
        public void Report(double share) => error.Write(string.Create(CultureInfo.InvariantCulture, $"\r{share:P0}   ");
    }
}
