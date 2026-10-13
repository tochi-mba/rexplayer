using System.Text.Json;
using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Cli;
using Rex.Media.AppCore.Transcription;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary><c>rexplay transcribe --from</c>: a file heard piece by piece from a time, as the player hears what plays.</summary>
public sealed class CliTranscribeAheadTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("rexplayer-transcribe-ahead-").FullName;

    [Fact]
    public void HearingRemainsUnavailableUnlessTheHostProvidesAnAdapter()
    {
        Assert.Null(new CliHost { Out = TextWriter.Null, Error = TextWriter.Null, Version = "0.0.0" }.Hearer("any-model-file"));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>Hears one word at the start of every piece.</summary>
    private sealed class OneWordHearer : ISpeechHearer
    {
        public List<int> Threads { get; } = [];

        public (IReadOnlyList<TranscriptWord> Words, string? Language) Hear(float[] samples, string? language, int threads, CancellationToken cancellationToken)
        {
            Threads.Add(threads);
            return ([new TranscriptWord("word.", TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(2.8))], language ?? "en");
        }

        public void Dispose()
        {
        }
    }

    private (int Exit, string Out, string Error) Run(ISpeechHearer? hearer, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = new CliHost
        {
            Out = output,
            Error = error,
            Version = "9.9.9",
            DataRoot = Path.Combine(_directory, "data"),
            Hearer = _ => hearer,
        };
        var exit = CliApplication.Run(args, host);
        return (exit, output.ToString(), error.ToString());
    }

    private string Talk()
    {
        var path = Path.Combine(_directory, "talk.wav");
        File.WriteAllBytes(path, Pcm.SineWav(220, 16000, 1, 70));
        return path;
    }

    private void Install(string modelId)
    {
        var model = SpeechModels.Find(modelId)!;
        var store = new SpeechModelStore(SpeechModelStore.DefaultRoot(Path.Combine(_directory, "data")));
        Directory.CreateDirectory(store.Root);
        using var file = File.Create(store.PathOf(model));
        file.SetLength(model.Bytes);
    }

    [Fact]
    [Capability("SUB-21")]
    public void ItHearsFromTheTimeGivenAndSaysHowSoonSubtitlesCameAndHowFast()
    {
        var talk = Talk();
        Install("base.en");
        var hearer = new OneWordHearer();

        var (exit, output, error) = Run(hearer, "transcribe", talk, "--from", "30", "--threads", "3", "--json");
        Assert.Equal((0, ""), (exit, error));
        var data = JsonDocument.Parse(output).RootElement.GetProperty("data");
        Assert.Equal(30, data.GetProperty("from").GetDouble());
        Assert.Equal(3, data.GetProperty("threads").GetInt32());
        Assert.Equal(3, data.GetProperty("pieces").GetInt32());
        Assert.Equal(3, data.GetProperty("words").GetInt32());
        Assert.Equal(70, data.GetProperty("soundSeconds").GetDouble());
        Assert.True(data.GetProperty("firstSubtitlesSeconds").GetDouble() >= 0);
        Assert.All(hearer.Threads, threads => Assert.Equal(3, threads));

        // Heard again with --again, in words, on the threads playback would leave it, with progress on the error stream.
        var (again, text, progress) = Run(new OneWordHearer(), "transcribe", talk, "--from", "0", "--again", "--language", "en");
        Assert.Equal(0, again);
        Assert.Contains("talk.wav: 3 words in 3 pieces", text, StringComparison.Ordinal);
        Assert.Contains("s heard", progress, StringComparison.Ordinal);
    }

    [Fact]
    [Capability("SUB-21")]
    public void ItRefusesWithoutAModelAHearerOrASensibleThreadCount()
    {
        var talk = Talk();
        var (noModel, _, missing) = Run(new OneWordHearer(), "transcribe", talk, "--from", "0");
        Assert.Equal(1, noModel);
        Assert.Contains("is not downloaded", missing, StringComparison.Ordinal);

        Install("base.en");
        var (deaf, _, cannot) = Run(null, "transcribe", talk, "--from", "0", "--again");
        Assert.Equal(1, deaf);
        Assert.Contains("cannot hear speech", cannot, StringComparison.Ordinal);

        var (threads, _, bad) = Run(new OneWordHearer(), "transcribe", talk, "--from", "0", "--threads", "lots");
        Assert.Equal(1, threads);
        Assert.Contains("--threads takes a number", bad, StringComparison.Ordinal);
        Assert.Equal(1, Run(new OneWordHearer(), "transcribe", talk, "--from", "0", "--threads", "0").Exit);
        Assert.Equal(1, Run(new OneWordHearer(), "transcribe", talk, "--from", "soon").Exit);

        var notes = Path.Combine(_directory, "notes.txt");
        File.WriteAllText(notes, "not a film at all, only words in a file");
        var (unreadable, _, why) = Run(new OneWordHearer(), "transcribe", notes, "--from", "0");
        Assert.Equal(1, unreadable);
        Assert.Contains("notes.txt could not be heard", why, StringComparison.Ordinal);
    }
}
