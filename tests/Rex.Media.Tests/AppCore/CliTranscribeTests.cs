using System.Net;
using System.Text.Json;
using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Cli;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>The transcribe and models commands, with a transcriber that hears a fixed line and a model folder of the test's own.</summary>
public sealed class CliTranscribeTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("rexplayer-transcribe-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class RefusedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private sealed class FixedTranscriber(string modelName) : ISpeechTranscriber
    {
        public int Heard { get; private set; }

        public string ModelName { get; } = modelName;

        public Task<Transcript> TranscribeAsync(string path, string? language, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            Heard++;
            progress?.Report(0.5);
            progress?.Report(1);
            return Task.FromResult(new Transcript(language ?? "en", ModelName, [
                new TranscriptWord("Lonely", TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.9)),
                new TranscriptWord("at", TimeSpan.FromSeconds(1.0), TimeSpan.FromSeconds(1.1)),
                new TranscriptWord("the", TimeSpan.FromSeconds(1.15), TimeSpan.FromSeconds(1.3)),
                new TranscriptWord("top.", TimeSpan.FromSeconds(1.4), TimeSpan.FromSeconds(1.8)),
            ]));
        }
    }

    private (int Exit, string Out, string Error) Run(FixedTranscriber? transcriber, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = new CliHost
        {
            Out = output,
            Error = error,
            Version = "9.9.9",
            DataRoot = Path.Combine(_directory, "data"),
            Transcriber = (modelPath, modelName) => transcriber is null ? null : new FixedTranscriber(modelName) { },
            ExtraDecoders = [new FakeVideoDecoderFactory()],
        };
        var exit = CliApplication.Run(args, host);
        return (exit, output.ToString(), error.ToString());
    }

    private string Film(string name)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, Pcm.SineWav(220, 8000, 1, 2.0));
        return path;
    }

    /// <summary>Pretends the model is downloaded: a file of the right size in the store.</summary>
    private void Install(string modelId)
    {
        var model = SpeechModels.Find(modelId)!;
        var store = new SpeechModelStore(SpeechModelStore.DefaultRoot(Path.Combine(_directory, "data")));
        Directory.CreateDirectory(store.Root);
        using var file = File.Create(store.PathOf(model));
        file.SetLength(model.Bytes);
    }

    [Fact]
    [Capability("SUB-19")]
    public void TranscribeHearsAFileKeepsTheTranscriptAndWritesItOut()
    {
        var film = Film("talk.wav");
        Install("base.en");
        var transcriber = new FixedTranscriber("base.en");
        var srt = Path.Combine(_directory, "talk.srt");

        var (exit, output, error) = Run(transcriber, "transcribe", film, "--out", srt, "--json");

        Assert.Equal((0, ""), (exit, error));
        var data = JsonDocument.Parse(output).RootElement.GetProperty("data");
        Assert.Equal(("base.en", "en", false, srt, 4), (data.GetProperty("model").GetString(), data.GetProperty("language").GetString(), data.GetProperty("cached").GetBoolean(), data.GetProperty("out").GetString(), data.GetProperty("words").GetArrayLength()));
        Assert.Contains("Lonely at the top.", File.ReadAllText(srt), StringComparison.Ordinal);
        var (withoutOutput, plain, plainError) = Run(transcriber, "transcribe", film);
        Assert.Equal((0, ""), (withoutOutput, plainError));
        Assert.Contains("4 words heard", plain, StringComparison.Ordinal);

        // Heard once: the second time comes from the cache, in words.
        var (again, human, progress) = Run(transcriber, "transcribe", film, "--out", Path.Combine(_directory, "talk.vtt"));
        Assert.Equal(0, again);
        Assert.Contains("4 words heard with Base (English) (from the cache), written to", human, StringComparison.Ordinal);
        Assert.Contains("0.50  Lonely at the top.", human, StringComparison.Ordinal);
        Assert.Equal("", progress);
        Assert.Contains("<00:00:01.000>at", File.ReadAllText(Path.Combine(_directory, "talk.vtt")), StringComparison.Ordinal);

        var (lrcExit, _, lrcError) = Run(transcriber, "transcribe", film, "--out", Path.Combine(_directory, "talk.lrc"));
        Assert.Equal((0, ""), (lrcExit, lrcError));
        Assert.Contains("[00:00.50]<00:00.50>Lonely", File.ReadAllText(Path.Combine(_directory, "talk.lrc")), StringComparison.Ordinal);

        // --again hears it afresh, with progress on the error stream; --language is passed on.
        var (fresh, _, heard) = Run(transcriber, "transcribe", film, "--again", "--language", "FR", "--out", Path.Combine(_directory, "talk.json"));
        Assert.Equal(0, fresh);
        Assert.Contains("Hearing talk.wav with Base (English)...", heard, StringComparison.Ordinal);
        Assert.Contains("100", heard, StringComparison.Ordinal);
        Assert.Equal("fr", Transcript.FromJson(File.ReadAllText(Path.Combine(_directory, "talk.json")))!.Language);

        // Kinetic captions from the transcript, and the subtitles timed by it.
        File.WriteAllText(Path.Combine(_directory, "talk.srt"), "1\n00:00:00,300 --> 00:00:02,000\nLonely at the TOP!\n");
        var (fromTranscript, captions, _) = Run(transcriber, "captions", film, "--words", "transcript", "--language", "fr", "--json");
        Assert.Equal(0, fromTranscript);
        var words = JsonDocument.Parse(captions).RootElement.GetProperty("data").GetProperty("words");
        Assert.Equal(["Lonely", "at", "the", "top."], words.EnumerateArray().Select(word => word.GetProperty("text").GetString()!.ToLowerInvariant() is var text && text == "lonely" ? "Lonely" : text));
        Assert.InRange(words[1].GetProperty("at").GetDouble(), 0.95, 1.05);
        var (timed, timedOut, _) = Run(transcriber, "captions", film, "--words", "timed", "--subtitles", Path.Combine(_directory, "talk.srt"), "--json");
        Assert.Equal(0, timed);
        var timedWords = JsonDocument.Parse(timedOut).RootElement.GetProperty("data").GetProperty("words");
        Assert.InRange(timedWords[3].GetProperty("at").GetDouble(), 1.35, 1.45);
    }

    [Fact]
    [Capability("SUB-19")]
    public void TranscribeSaysWhatIsMissingAndRefusesBadOptions()
    {
        var film = Film("talk.wav");
        var (noModel, _, missing) = Run(new FixedTranscriber("base.en"), "transcribe", film);
        Assert.Equal(1, noModel);
        Assert.Contains("is not downloaded; run 'rexplay models --get base.en'", missing, StringComparison.Ordinal);

        using var output = new StringWriter();
        using var downloadError = new StringWriter();
        var downloading = new CliHost
        {
            Out = output,
            Error = downloadError,
            Version = "9.9.9",
            DataRoot = Path.Combine(_directory, "download"),
            SpeechStore = root => new SpeechModelStore(root, () => new RefusedHandler()),
        };
        Assert.Equal(1, CliApplication.Run(["transcribe", film, "--download"], downloading));
        Assert.Contains("404", downloadError.ToString(), StringComparison.Ordinal);
        Assert.Null(downloading.Transcriber("model", "name"));

        downloadError.GetStringBuilder().Clear();
        Assert.Equal(1, CliApplication.Run(["models", "--get", "base.en"], downloading));
        Assert.Contains("404", downloadError.ToString(), StringComparison.Ordinal);

        var ordinary = new CliHost { Out = TextWriter.Null, Error = TextWriter.Null, Version = "test" };
        Assert.EndsWith("speech", ordinary.SpeechStore(SpeechModelStore.DefaultRoot(_directory)).Root, StringComparison.Ordinal);

        Install("tiny.en");
        var (cannot, _, deaf) = Run(null, "transcribe", film, "--model", "tiny.en");
        Assert.Equal(1, cannot);
        Assert.Contains("cannot hear speech", deaf, StringComparison.Ordinal);

        var (badModel, _, modelError) = Run(null, "transcribe", film, "--model", "huge");
        Assert.Equal(1, badModel);
        Assert.Contains("--model takes tiny.en, base.en, small.en", modelError, StringComparison.Ordinal);

        var (badOut, _, outError) = Run(new FixedTranscriber("tiny.en"), "transcribe", film, "--model", "tiny.en", "--out", Path.Combine(_directory, "t.txt"));
        Assert.Equal(1, badOut);
        Assert.Contains("--out takes a .srt, .vtt, .lrc or .json file", outError, StringComparison.Ordinal);

        var (noTranscript, _, captionsError) = Run(null, "captions", film, "--words", "transcript");
        Assert.Equal(1, noTranscript);
        Assert.Contains("There is no transcript of talk.wav by base.en", captionsError, StringComparison.Ordinal);
        var (badWords, _, wordsError) = Run(null, "captions", film, "--words", "guess");
        Assert.Equal(1, badWords);
        Assert.Contains("--words takes subtitles, transcript or timed", wordsError, StringComparison.Ordinal);
    }

    [Fact]
    [Capability("SUB-19")]
    public void ModelsListsWhatIsThereAndRemovesOne()
    {
        Install("tiny.en");
        var (exit, output, _) = Run(null, "models", "--json");
        Assert.Equal(0, exit);
        var models = JsonDocument.Parse(output).RootElement.GetProperty("data").GetProperty("models");
        Assert.Equal(3, models.GetArrayLength());
        // An already-installed model takes the successful download path without network access.
        var (fetched, fetchedJson, _) = Run(null, "models", "--get", "tiny.en", "--json");
        Assert.Equal(0, fetched);
        Assert.True(JsonDocument.Parse(fetchedJson).RootElement.GetProperty("data").GetProperty("models")[0].GetProperty("installed").GetBoolean());
        Assert.True(models[0].GetProperty("installed").GetBoolean());
        Assert.False(models[1].GetProperty("installed").GetBoolean());

        var (human, text, _) = Run(null, "models");
        Assert.Equal(0, human);
        Assert.Contains("tiny.en", text, StringComparison.Ordinal);
        Assert.Contains("installed", text, StringComparison.Ordinal);
        Assert.Contains("not downloaded", text, StringComparison.Ordinal);

        var (removed, after, _) = Run(null, "models", "--remove", "tiny.en", "--json");
        Assert.Equal(0, removed);
        Assert.False(JsonDocument.Parse(after).RootElement.GetProperty("data").GetProperty("models")[0].GetProperty("installed").GetBoolean());

        var (unknown, _, error) = Run(null, "models", "--get", "huge");
        Assert.Equal(1, unknown);
        Assert.Contains("There is no model 'huge'", error, StringComparison.Ordinal);
        var (unknownRemove, _, removeError) = Run(null, "models", "--remove", "huge");
        Assert.Equal(1, unknownRemove);
        Assert.Contains("There is no model 'huge'", removeError, StringComparison.Ordinal);
    }
}
