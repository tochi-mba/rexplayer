using System.Globalization;
using System.Text.Json.Nodes;
using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Machine;
using Rex.Media.AppCore.Transcription;
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
        var store = host.SpeechStore(SpeechModelStore.DefaultRoot(DataRoot(host)));
        var cache = new TranscriptCache(TranscriptCache.DefaultRoot(DataRoot(host)));
        if (ParseTime(arguments.Option("--from"), "--from") is { } from)
        {
            return TranscribeAhead(path, model, language, from.ToTimeSpan(), arguments, machine, host);
        }

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
                DownloadModel(store, model, Reporter(host, machine), host.Cancellation);
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

    /// <summary>
    /// Hears a file piece by piece from <paramref name="from"/> as the player does for what plays
    /// (SUB-21): ahead to the end, then from the top, and says how soon the first subtitles came,
    /// how fast the speech was heard and how much of the processor it took.
    /// </summary>
    private static int TranscribeAhead(string path, SpeechModel model, string? language, TimeSpan from, CliArguments arguments, bool machine, CliHost host)
    {
        var store = host.SpeechStore(SpeechModelStore.DefaultRoot(DataRoot(host)));
        if (!store.IsInstalled(model))
        {
            throw new CliException($"The {model.Name} model ({model.Size}) is not downloaded; run 'rexplay models --get {model.Id}' first.");
        }

        var threads = TranscriptionConditions.Plan(new TranscriptionSettings(), new TranscriptionMoment(true, false, false, true), Environment.ProcessorCount).PlayingThreads;
        if (arguments.Option("--threads") is { } text)
        {
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count is <= 0 or > 64)
            {
                throw new CliException($"--threads takes a number from 1 to 64, not '{text}'.");
            }

            threads = count;
        }
        var cache = new TranscriptCache(TranscriptCache.DefaultRoot(DataRoot(host)));
        if (arguments.Option("--again") is not null)
        {
            cache.Forget(path, model.Id);
        }

        // The model is loaded before anything is heard, so a build that cannot hear says so at once.
        var loaded = host.Hearer(store.PathOf(model)) ?? throw new CliException("This build cannot hear speech.");
        var decoders = MediaRegistries.Decoders([.. host.ExtraDecoders]);
        using var worker = new TranscriptionWorker(
            cache,
            new TranscriptionLedger(TranscriptionLedger.DefaultPath(DataRoot(host))),
            file => new SpeechAudioReader(file, decoders, host.Cancellation),
            () => Interlocked.Exchange(ref loaded, null) ?? host.Hearer(store.PathOf(model))!,
            model.Id,
            host.Time)
        {
            Language = string.IsNullOrEmpty(language) ? null : language,
        };
        worker.SetPlan(new TranscriptionPlan(true, false, threads, threads, null));
        worker.SetPlaying(path, wanted: true, from);
        var clock = host.Time.GetTimestamp();
        var processor = Environment.CpuUsage.TotalTime;
        TimeSpan? first = null;
        var sound = TimeSpan.Zero;
        var pieces = 0;
        string? failure = null;
        worker.Changed += (_, _) => failure ??= worker.Status.Error;
        worker.Heard += (_, heard) =>
        {
            first ??= host.Time.GetElapsedTime(clock);
            pieces += heard.Complete ? 0 : 1;
            sound = heard.Done.Total;
            Progress(host, machine, string.Create(CultureInfo.InvariantCulture, $"{sound.TotalSeconds:0} s heard"));
        };
        while (worker.Step(host.Cancellation))
        {
        }

        if (failure is not null)
        {
            throw new CliException($"{Path.GetFileName(path)} could not be heard: {failure}");
        }

        var took = host.Time.GetElapsedTime(clock);
        var cpu = Environment.CpuUsage.TotalTime - processor;
        var words = cache.Load(path, model.Id)?.Words.Count ?? 0;
        var data = new JsonObject
        {
            ["file"] = path,
            ["model"] = model.Id,
            ["from"] = Round(from.TotalSeconds),
            ["threads"] = threads,
            ["pieces"] = pieces,
            ["words"] = words,
            ["firstSubtitlesSeconds"] = first is { } soonest ? Round(soonest.TotalSeconds) : null,
            ["seconds"] = Round(took.TotalSeconds),
            ["soundSeconds"] = Round(sound.TotalSeconds),
            ["timesRealTime"] = took > TimeSpan.Zero ? Round(sound / took) : null,
            ["processorSeconds"] = Round(cpu.TotalSeconds),
            ["processorShare"] = took > TimeSpan.Zero ? Round(cpu / took / Environment.ProcessorCount) : null,
        };
        host.Out.WriteLine(machine
            ? MachineEnvelope.Success("transcribe", data)
            : string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileName(path)}: {words} words in {pieces} pieces on {threads} threads; first subtitles after {data["firstSubtitlesSeconds"]} s; {data["timesRealTime"]} times real time; {data["processorShare"]} of the processor."));
        return 0;
    }

    private static int Models(CliArguments arguments, bool machine, CliHost host)
    {
        var store = host.SpeechStore(SpeechModelStore.DefaultRoot(DataRoot(host)));
        if (arguments.Option("--get") is { } wanted)
        {
            var model = SpeechModels.Find(wanted) ?? throw new CliException($"There is no model '{wanted}'; the models are {string.Join(", ", SpeechModels.All.Select(m => m.Id))}.");
            Progress(host, machine, $"Downloading the {model.Name} model ({model.Size})");
            DownloadModel(store, model, Reporter(host, machine), host.Cancellation);
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

    /// <summary>The transcript written as the file's kind says (.srt, .vtt, .lrc or .json), or nothing without --out.</summary>
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
            ".lrc" => transcript.ToLrc(),
            ".json" => transcript.ToJson(),
            var other => throw new CliException($"--out takes a .srt, .vtt, .lrc or .json file, not '{other}'."),
        };
        File.WriteAllText(path, text);
        return path;
    }

    private static SpeechModel ParseModel(string? text) => text is null ? SpeechModels.BaseEnglish
        : SpeechModels.Find(text) ?? throw new CliException($"--model takes {string.Join(", ", SpeechModels.All.Select(model => model.Id))}, not '{text}'.");

    private static void DownloadModel(SpeechModelStore store, SpeechModel model, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        try
        {
            store.DownloadAsync(model, progress, cancellationToken).GetAwaiter().GetResult();
        }
        catch (Exception error) when (error is HttpRequestException or InvalidDataException or IOException)
        {
            throw new CliException(error.Message);
        }
    }

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
        public void Report(double share) => error.Write(string.Create(CultureInfo.InvariantCulture, $"\r{share:P0}   "));
    }
}
