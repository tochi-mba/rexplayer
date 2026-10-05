using Rex.Media.Codecs;
using Rex.Media.Primitives;

namespace Rex.Media.Tests.Codecs;

public sealed class DecoderRegistryTests
{
    private static readonly TrackInfo Mp3 = new() { Id = 1, Codec = CodecId.Mp3, Audio = new AudioTrackInfo { SampleRate = 44_100, Channels = 2 } };

    [Fact]
    public void TheLadderRunsHighestRankFirstAndSkipsFactoriesThatCannotDecode()
    {
        var low = new FakeFactory("os", 50, CodecId.Mp3);
        var high = new FakeFactory("own", 100, CodecId.Mp3);
        var other = new FakeFactory("flac", 200, CodecId.Flac);
        var registry = new DecoderRegistry().Add(low).Add(other).Add(high);

        Assert.Equal([high, low], registry.Ladder(Mp3));
        Assert.Equal(3, registry.Factories.Count);
    }

    [Fact]
    public void TheFirstRungThatOpensWins()
    {
        var registry = new DecoderRegistry().Add(new FakeFactory("own", 100, CodecId.Mp3)).Add(new FakeFactory("os", 50, CodecId.Mp3));

        var result = registry.CreateAudio(Mp3);

        Assert.Equal("own", result.Decoder!.Name);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void AFailingRungFallsThroughToTheNextAndIsRemembered()
    {
        var registry = new DecoderRegistry()
            .Add(new FakeFactory("broken", 100, CodecId.Mp3) { Failure = new NotSupportedException("no luck") })
            .Add(new FakeFactory("os", 50, CodecId.Mp3));

        Assert.Equal("os", registry.CreateAudio(Mp3).Decoder!.Name);
    }

    [Fact]
    public void WhenEveryRungFailsTheReasonsAreKept()
    {
        var registry = new DecoderRegistry()
            .Add(new FakeFactory("a", 100, CodecId.Mp3) { Failure = new MediaFormatException("bad header") })
            .Add(new FakeFactory("b", 50, CodecId.Mp3) { Failure = new InvalidOperationException("device lost") });

        var result = registry.CreateAudio(Mp3);

        Assert.Null(result.Decoder);
        Assert.Equal("No decoder for MP3 could open the track. a: bad header b: device lost", result.Reason);
    }

    [Fact]
    public void WithNoFactoryTheReasonSaysSo()
    {
        var result = new DecoderRegistry().CreateAudio(Mp3);

        Assert.Equal("No decoder for MP3 is available.", result.Reason);
    }

    [Fact]
    public void ArgumentsAreChecked()
    {
        var registry = new DecoderRegistry();

        Assert.Throws<ArgumentNullException>(() => registry.Add(null!));
        Assert.Throws<ArgumentNullException>(() => registry.Ladder(null!));
        Assert.Throws<ArgumentNullException>(() => registry.CreateAudio(null!));
    }

    private sealed class FakeFactory(string name, int rank, CodecId codec) : IDecoderFactory
    {
        public Exception? Failure { get; init; }

        public string Name => name;

        public DecoderSource Source => DecoderSource.OsSoftware;

        public int Rank => rank;

        public bool CanDecode(TrackInfo track) => track.Codec == codec;

        public IAudioDecoder CreateAudio(TrackInfo track) => Failure is null ? new FakeDecoder(name) : throw Failure;
    }

    private sealed class FakeDecoder(string name) : IAudioDecoder
    {
        public string Name => name;

        public DecoderSource Source => DecoderSource.OsSoftware;

        public void Decode(Packet packet, ICollection<AudioFrame> output)
        {
        }

        public void Drain(ICollection<AudioFrame> output)
        {
        }

        public void Flush()
        {
        }

        public void Dispose()
        {
        }
    }
}
