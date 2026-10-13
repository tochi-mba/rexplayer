using System.Net;
using System.Security.Cryptography;
using Rex.Media.AppCore.Captions;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore.Captions;

public sealed class SpeechModelsTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("rexplayer-speech-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>A server holding one model's bytes, honouring byte ranges, failing when told to.</summary>
    private sealed class Server(byte[] bytes, HttpStatusCode status = HttpStatusCode.OK, bool ranges = true) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (status != HttpStatusCode.OK)
            {
                return Task.FromResult(new HttpResponseMessage(status));
            }

            if (request.Headers.Range?.Ranges.FirstOrDefault() is { From: { } from } && ranges)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(bytes[(int)from..]) });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }

    private static SpeechModel Model(byte[] bytes, string? sha = null) =>
        new("test", "Test", "ggml-test.bin", new Uri("https://example.test/ggml-test.bin"), bytes.Length, sha ?? Convert.ToHexStringLower(SHA256.HashData(bytes)), "MIT", "fast");

    [Fact]
    [Capability("SUB-19")]
    public void TheCatalogueNamesThreeEnglishModelsPinnedAndPermissive()
    {
        Assert.Equal(["tiny.en", "base.en", "small.en"], SpeechModels.All.Select(model => model.Id));
        Assert.All(SpeechModels.All, model =>
        {
            Assert.Equal("MIT", model.Licence);
            Assert.Equal(64, model.Sha256.Length);
            Assert.StartsWith("https://huggingface.co/ggerganov/whisper.cpp/", model.Url.ToString(), StringComparison.Ordinal);
            Assert.True(model.Bytes > 50_000_000);
            Assert.EndsWith(" MB", model.Size, StringComparison.Ordinal);
        });
        Assert.Same(SpeechModels.BaseEnglish, SpeechModels.For(TranscriptModelChoice.Base));
        Assert.Same(SpeechModels.TinyEnglish, SpeechModels.For(TranscriptModelChoice.Tiny));
        Assert.Same(SpeechModels.SmallEnglish, SpeechModels.For(TranscriptModelChoice.Small));
        Assert.Same(SpeechModels.BaseEnglish, SpeechModels.For((TranscriptModelChoice)9));
        Assert.Same(SpeechModels.SmallEnglish, SpeechModels.Find("SMALL.EN"));
        Assert.Null(SpeechModels.Find("huge"));
        Assert.Equal("148 MB", SpeechModels.BaseEnglish.Size);
    }

    [Fact]
    [Capability("SUB-19")]
    public async Task ADownloadLandsWholeOnlyWhenItsHashMatchesAndResumesAPart()
    {
        var bytes = Enumerable.Range(0, 200_000).Select(i => (byte)(i * 7)).ToArray();
        var model = Model(bytes);
        var server = new Server(bytes);
        var store = new SpeechModelStore(Path.Combine(_directory, "speech"), () => server);
        var reports = new List<double>();
        Assert.False(store.IsInstalled(model));

        // A part left from before is taken up where it stopped.
        Directory.CreateDirectory(store.Root);
        File.WriteAllBytes(store.PathOf(model) + SpeechModelStore.PartialSuffix, bytes[..50_000]);
        Assert.Equal(50_000, store.PartialBytes(model));
        await store.DownloadAsync(model, new Progress<double>(reports.Add), TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.True(store.IsInstalled(model));
        Assert.Equal(bytes, File.ReadAllBytes(store.PathOf(model)));
        Assert.Equal(0, store.PartialBytes(model));
        Assert.Equal(1, server.Requests);
        Assert.Contains(1, reports);

        // Installed already: nothing is asked for.
        await store.DownloadAsync(model, null, TestContext.Current.CancellationToken);
        Assert.Equal(1, server.Requests);
        store.Remove(model);
        store.Remove(model);
        Assert.False(store.IsInstalled(model));
        Assert.Equal(Path.Combine("data", "models", "speech"), SpeechModelStore.DefaultRoot("data"));
    }

    [Fact]
    [Capability("SUB-19")]
    public async Task AWrongDownloadIsDeletedAndSaidAndAServerThatRefusesIsReported()
    {
        var bytes = new byte[1000];
        var wrong = Model(bytes, new string('0', 64));
        var store = new SpeechModelStore(Path.Combine(_directory, "speech"), () => new Server(bytes));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => store.DownloadAsync(wrong, null, TestContext.Current.CancellationToken));
        Assert.Contains("was not the one expected", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(store.PathOf(wrong) + SpeechModelStore.PartialSuffix));

        var refused = new SpeechModelStore(Path.Combine(_directory, "refused"), () => new Server(bytes, HttpStatusCode.NotFound));
        var http = await Assert.ThrowsAsync<HttpRequestException>(() => refused.DownloadAsync(Model(bytes), null, TestContext.Current.CancellationToken));
        Assert.Contains("404", http.Message, StringComparison.Ordinal);

        // A part the server will not resume, or one too long, is started again.
        var plain = new Server(bytes, ranges: false);
        var again = new SpeechModelStore(Path.Combine(_directory, "again"), () => plain);
        Directory.CreateDirectory(again.Root);
        File.WriteAllBytes(again.PathOf(Model(bytes)) + SpeechModelStore.PartialSuffix, new byte[2000]);
        await again.DownloadAsync(Model(bytes), null, TestContext.Current.CancellationToken);
        Assert.True(again.IsInstalled(Model(bytes)));
        File.WriteAllBytes(again.PathOf(Model(bytes)) + SpeechModelStore.PartialSuffix, new byte[10]);
        again.Remove(Model(bytes));
        await again.DownloadAsync(Model(bytes), null, TestContext.Current.CancellationToken);
        Assert.True(again.IsInstalled(Model(bytes)));

        Assert.Throws<ArgumentException>(() => new SpeechModelStore(""));
        Assert.Throws<ArgumentNullException>(() => store.PathOf(null!));
        Assert.Throws<ArgumentNullException>(() => SpeechModelStore.DefaultRoot(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.DownloadAsync(null!, null, TestContext.Current.CancellationToken));
        Assert.Equal(0, new SpeechModelStore(Path.Combine(_directory, "none")).PartialBytes(Model(bytes)));
    }

    [Fact]
    [Capability("SUB-19")]
    public void ResamplingAveragesDownAndReadsBetweenGoingUp()
    {
        var tone = Signals.Sine(440, 48000, 48000);
        var down = SpeechAudio.Resample(tone, 48000);
        Assert.Equal(16000, down.Length);
        Assert.InRange(Signals.Amplitude(down, 16000, 440), 0.4, 0.5);

        var up = SpeechAudio.Resample(Signals.Sine(100, 8000, 800), 8000, 16000);
        Assert.Equal(1600, up.Length);
        Assert.InRange(Signals.Amplitude(up, 16000, 100), 0.45, 0.5);

        Assert.Equal([1f, 2f], SpeechAudio.Resample([1f, 2f], 16000));
        Assert.Empty(SpeechAudio.Resample([], 8000));
        Assert.Throws<ArgumentOutOfRangeException>(() => SpeechAudio.Resample([1f], 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => SpeechAudio.Resample([1f], 8000, 0));
    }

    [Fact]
    public void DecodingRefusesNullsAndGivesNothingForAFileWithoutSound()
    {
        Assert.Throws<ArgumentNullException>(() => SpeechAudio.DecodeMono(null!, new Rex.Media.Codecs.DecoderRegistry(), TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentNullException>(() => SpeechAudio.DecodeMono("x", null!, TestContext.Current.CancellationToken));
        var silent = Path.Combine(_directory, "picture.png");
        File.WriteAllBytes(silent, PngWriterTestBytes());
        var decoded = SpeechAudio.DecodeMono(silent, new Rex.Media.Codecs.DecoderRegistry(), TestContext.Current.CancellationToken);
        Assert.Empty(decoded.Mono);
        Assert.Equal(0, decoded.Rate);
    }

    private static byte[] PngWriterTestBytes() => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
}
