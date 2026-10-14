using Rex.Media.Codecs;
using Rex.Media.Codecs.Software.Pcm;
using Rex.Media.Containers;
using Rex.Media.Containers.Riff;
using Rex.Media.Engine;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Engine;

/// <summary>
/// A damaged file plays as far as it can be read: the engine reads past a demuxer that refuses,
/// seeks past damage it cannot read through, fills short holes in the sound with silence and jumps
/// over long ones, passes over decoders that fail on damaged data, and tells the window once that
/// the file is damaged and then what it skipped. Nothing a reader throws ends the process.
/// </summary>
public sealed class DamagedPlaybackTests
{
    private const int Rate = 8000;

    private static byte[] Ramp(double seconds) => Pcm.RampWav(Rate, (int)(Rate * seconds));

    private static IByteSource Damaged(byte[] wav) => SessionHarness.Source(wav, "clip.dmg");

    private static async Task<SessionHarness> PlayAsync(DamagedWavFactory demuxer, byte[] wav, IDecoderFactory? decoder = null)
    {
        var harness = new SessionHarness(demuxer: demuxer, decoder: decoder);
        await harness.Session.OpenAsync(Damaged(wav));
        await harness.FinishAsync();
        return harness;
    }

    [Fact]
    public async Task ADemuxerThatRefusesAFewTimesIsReadAgainAndTheWholeFilePlays()
    {
        using var harness = await PlayAsync(new DamagedWavFactory { FailAt = 20, FailTimes = 2 }, Ramp(3));

        Assert.Equal(SessionState.Ended, harness.Session.State);
        Assert.Equal(Rate * 3, harness.Recording.SampleCount);
        Assert.Equal(MediaTime.Zero, Assert.Single(harness.Events.OfType<MediaDamagedEvent>()).Skipped);
    }

    [Fact]
    public async Task DamageThatCannotBeReadThroughIsSkippedAndTheClockJumpsOverIt()
    {
        var demuxer = new DamagedWavFactory { StuckFrom = MediaTime.FromSeconds(2), StuckTo = MediaTime.FromSeconds(3.5), SeekBack = MediaTime.FromMilliseconds(100) };
        using var harness = await PlayAsync(demuxer, Ramp(8));

        Assert.Equal(SessionState.Ended, harness.Session.State);
        var notices = harness.Events.OfType<MediaDamagedEvent>().ToList();
        Assert.Equal(MediaTime.Zero, notices[0].Skipped);
        var skipped = Assert.Single(notices.Skip(1)).Skipped;
        Assert.InRange(skipped.TotalSeconds, 1.5, 3);

        // Every sample before the damage and after the place reading resumed plays once, in order.
        var heard = harness.Recording.Channel(0);
        Assert.InRange(heard.Length, (int)(Rate * (8 - skipped.TotalSeconds)) - 400, (int)(Rate * (8 - skipped.TotalSeconds)) + 400);
        Assert.Equal((short)(Rate * 2 - 1) / 32768f, heard[(Rate * 2) - 1]);
    }

    [Fact]
    public async Task DamageNearTheEndOfAFileThatCannotSeekEndsTheStreamNotTheSession()
    {
        foreach (var demuxer in new[]
        {
            new DamagedWavFactory { StuckFrom = MediaTime.FromSeconds(1), StuckTo = MediaTime.FromSeconds(9), NotSeekable = true },
            new DamagedWavFactory { StuckFrom = MediaTime.FromSeconds(1), StuckTo = MediaTime.FromSeconds(9), SeekFails = true },
            new DamagedWavFactory { StuckFrom = MediaTime.FromSeconds(1), StuckTo = MediaTime.FromSeconds(9) },
        })
        {
            using var harness = await PlayAsync(demuxer, Ramp(2));

            Assert.Equal(SessionState.Ended, harness.Session.State);
            Assert.Equal(Rate, harness.Recording.SampleCount);
        }
    }

    [Fact]
    public async Task AReadersOwnFaultOnHostileBytesIsDamageToo()
    {
        using var harness = await PlayAsync(new DamagedWavFactory { FailAt = 5, FailTimes = 1, Failure = () => new KeyNotFoundException("hostile") }, Ramp(1));

        Assert.Equal(SessionState.Ended, harness.Session.State);
        Assert.Equal(Rate, harness.Recording.SampleCount);
    }

    [Fact]
    public async Task ADiskThatFailsStillFailsTheSession()
    {
        using var harness = await PlayAsync(new DamagedWavFactory { FailAt = 5, FailTimes = 1, Failure = () => new IOException("unplugged") }, Ramp(1));

        Assert.Equal(SessionState.Faulted, harness.Session.State);
        Assert.Contains("unplugged", harness.Session.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AShortHoleInTheSoundIsFilledWithSilence()
    {
        using var harness = await PlayAsync(new DamagedWavFactory { ShiftFrom = 10, Shift = MediaTime.FromMilliseconds(300) }, Ramp(2));

        var heard = harness.Recording.Channel(0);
        Assert.Equal((Rate * 2) + (Rate * 3 / 10), heard.Length);
        Assert.All(heard.AsSpan(Rate / 2, Rate * 3 / 10).ToArray(), sample => Assert.Equal(0f, sample));
        Assert.Empty(harness.Events.OfType<MediaDamagedEvent>());
    }

    [Fact]
    public async Task ALongHoleIsJumpedOverAndTold()
    {
        using var harness = await PlayAsync(new DamagedWavFactory { ShiftFrom = 10, Shift = MediaTime.FromSeconds(5) }, Ramp(10));

        Assert.Equal(Rate * 10, harness.Recording.SampleCount);
        Assert.Equal(MediaTime.FromSeconds(5), Assert.Single(harness.Events.OfType<MediaDamagedEvent>()).Skipped);
    }

    [Fact]
    public async Task ATimeNoMediaCanReachIsNotBelieved()
    {
        using var harness = await PlayAsync(new DamagedWavFactory { BogusAt = 10 }, Ramp(2));

        Assert.Equal(Rate * 2, harness.Recording.SampleCount);
        Assert.Empty(harness.Events.OfType<MediaDamagedEvent>());
    }

    [Theory]
    [InlineData("decode")]
    [InlineData("flush")]
    [InlineData("drain")]
    public async Task AnAudioDecoderThatFailsOnDamagedDataIsPassedOver(string failing)
    {
        var decoder = new FaultyDecoderFactory { Failing = failing };
        using var harness = new SessionHarness(autoPlay: false, decoder: decoder);
        await harness.Session.OpenAsync(SessionHarness.Source(Ramp(2)));
        if (failing == "flush")
        {
            await harness.Session.SeekAsync(MediaTime.FromSeconds(1));
        }

        await harness.Session.PlayAsync();
        await harness.FinishAsync();

        Assert.Equal(SessionState.Ended, harness.Session.State);
        Assert.Single(harness.Events.OfType<MediaDamagedEvent>());
        Assert.True((await harness.Session.GetStatsAsync()).CorruptPackets >= 1);
    }

    [Fact]
    public async Task AVideoFileWithAHoleInItsPicturesJumpsItsSilenceOverTheHole()
    {
        var presenter = new RecordingVideoPresenter();
        using var harness = new SessionHarness(demuxer: new Rex.Media.Containers.Matroska.MatroskaDemuxerFactory(), videoDecoder: new FakeVideoDecoderFactory(), presenter: presenter);
        await harness.Session.OpenAsync(SessionHarness.Source(SilentPictures([0, 40, 80, 3000, 3040]), "holes.mkv"));
        await harness.FinishAsync();

        Assert.Equal(SessionState.Ended, harness.Session.State);
        Assert.Equal(5, presenter.Shown.Count);
        Assert.InRange(harness.Recording.SampleCount, 48_000 * 0.15, 48_000 * 0.3);
        Assert.Single(harness.Events.OfType<MediaDamagedEvent>());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task AVideoDecoderThatFailsOnDamagedDataLeavesTheSoundPlaying(bool failDrain, bool failFlush)
    {
        var presenter = new RecordingVideoPresenter();
        var video = new FakeVideoDecoderFactory { BrokenByte = failDrain || failFlush ? null : 1, FailDrain = failDrain, FailFlush = failFlush, Throws = () => new InvalidCastException("hostile") };
        using var harness = new SessionHarness(autoPlay: false, demuxer: new Rex.Media.Containers.Matroska.MatroskaDemuxerFactory(), videoDecoder: video, presenter: presenter);
        await harness.Session.OpenAsync(SessionHarness.Source(SilentPictures([0, 40, 80, 120]), "pictures.mkv"));
        if (failFlush)
        {
            await harness.Session.SeekAsync(MediaTime.FromMilliseconds(40));
        }

        await harness.Session.PlayAsync();
        await harness.FinishAsync();

        Assert.Equal(SessionState.Ended, harness.Session.State);
        Assert.NotEmpty(presenter.Shown);
        Assert.Single(harness.Events.OfType<MediaDamagedEvent>());
    }

    [Fact]
    public async Task AReaderThatFaultsWhileOpeningFailsTheSessionWithoutLeavingItOpening()
    {
        using var harness = new SessionHarness(demuxer: new DamagedWavFactory { OpenFailure = () => new KeyNotFoundException("hostile header") });

        var failure = await Assert.ThrowsAsync<KeyNotFoundException>(() => harness.Session.OpenAsync(Damaged(Ramp(1))));
        await harness.FinishAsync();

        Assert.Equal("hostile header", failure.Message);
        Assert.Equal(SessionState.Faulted, harness.Session.State);
        Assert.Contains("hostile header", harness.Session.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AQueuedItemWhoseReaderFaultsIsPassedOver()
    {
        using var harness = new SessionHarness(demuxer: new DamagedWavFactory { OpenFailure = () => new KeyNotFoundException("hostile header") });
        await harness.Session.OpenAsync(SessionHarness.Source(Ramp(0.5)), [Damaged(Ramp(1)), SessionHarness.Source(Ramp(0.5))]);
        await harness.FinishAsync();

        var skipped = Assert.Single(harness.Events.OfType<ItemSkippedEvent>());
        Assert.Contains("KeyNotFoundException: hostile header", skipped.Reason, StringComparison.Ordinal);
        Assert.Equal(Rate, harness.Recording.SampleCount);
    }

    /// <summary>A Matroska file of pictures at these times (milliseconds) and no sound.</summary>
    private static byte[] SilentPictures(int[] times)
    {
        var video = EbmlWriter.Element(
            Rex.Media.Containers.Matroska.MatroskaId.TrackEntry,
            EbmlWriter.UInt(Rex.Media.Containers.Matroska.MatroskaId.TrackNumber, 2),
            EbmlWriter.UInt(Rex.Media.Containers.Matroska.MatroskaId.TrackType, 1),
            EbmlWriter.Text(Rex.Media.Containers.Matroska.MatroskaId.CodecId, "V_MPEG4/ISO/AVC"),
            EbmlWriter.UInt(Rex.Media.Containers.Matroska.MatroskaId.DefaultDuration, 40_000_000),
            EbmlWriter.Element(Rex.Media.Containers.Matroska.MatroskaId.Video, EbmlWriter.UInt(Rex.Media.Containers.Matroska.MatroskaId.PixelWidth, 16), EbmlWriter.UInt(Rex.Media.Containers.Matroska.MatroskaId.PixelHeight, 8)));
        var blocks = times.Select((time, i) => Containers.MatroskaCraftedTests.Simple(2, (short)time, true, (byte)(i + 1))).ToArray();
        return Containers.MatroskaCraftedTests.Mkv(Containers.MatroskaCraftedTests.Tracks(video), Containers.MatroskaCraftedTests.Cluster(0, blocks));
    }
}

/// <summary>
/// A WAV demuxer damaged to order: it refuses at a packet a number of times, gets stuck refusing
/// over a stretch until sought past it, seeks short of where it is asked, cannot seek, shifts or
/// garbles the times of later packets, or faults while opening.
/// </summary>
internal sealed class DamagedWavFactory : IDemuxerFactory
{
    public int FailAt { get; init; } = -1;

    public int FailTimes { get; init; }

    public Func<Exception> Failure { get; init; } = () => new MediaFormatException("damaged");

    public MediaTime StuckFrom { get; init; } = MediaTime.Unknown;

    public MediaTime StuckTo { get; init; } = MediaTime.Unknown;

    public MediaTime SeekBack { get; init; } = MediaTime.Zero;

    public bool NotSeekable { get; init; }

    public bool SeekFails { get; init; }

    public int ShiftFrom { get; init; } = int.MaxValue;

    public MediaTime Shift { get; init; } = MediaTime.Zero;

    public int BogusAt { get; init; } = -1;

    public Func<Exception>? OpenFailure { get; init; }

    public string Name => "damaged";

    public int Probe(ReadOnlySpan<byte> head, string? extension) => extension == ".dmg" ? 200 : 0;

    public IDemuxer Open(IByteSource source, CancellationToken cancellationToken) =>
        OpenFailure is { } failure ? throw failure() : new Demuxer(this, new WavDemuxer(source, cancellationToken));

    private sealed class Demuxer(DamagedWavFactory damage, WavDemuxer inner) : IDemuxer
    {
        private int _read;
        private int _failures;

        public MediaInfo Info { get; } = inner.Info with { IsSeekable = !damage.NotSeekable };

        public Packet? ReadPacket(CancellationToken cancellationToken)
        {
            if (_read == damage.FailAt && _failures++ < damage.FailTimes)
            {
                throw damage.Failure();
            }

            var packet = inner.ReadPacket(cancellationToken);
            if (packet is not null && damage.StuckFrom.IsKnown && packet.Timestamp >= damage.StuckFrom && packet.Timestamp < damage.StuckTo)
            {
                // Stuck: the damage is read again, and refused again, until something seeks past it.
                inner.Seek(packet.Timestamp, cancellationToken);
                packet.Dispose();
                throw damage.Failure();
            }

            if (packet is not null && _read >= damage.ShiftFrom)
            {
                packet.Pts += damage.Shift;
                packet.Dts += damage.Shift;
            }

            if (packet is not null && _read == damage.BogusAt)
            {
                packet.Pts = packet.Dts = MediaTime.FromSeconds(100_000);
            }

            _read++;
            return packet;
        }

        public void Seek(MediaTime target, CancellationToken cancellationToken)
        {
            if (damage.SeekFails)
            {
                throw new MediaFormatException("cannot seek");
            }

            inner.Seek(MediaTime.Max(MediaTime.Zero, target - damage.SeekBack), cancellationToken);
        }

        public void Dispose() => inner.Dispose();
    }
}

/// <summary>A PCM decoder that fails on damaged data in one place: decoding a packet, flushing, or draining.</summary>
internal sealed class FaultyDecoderFactory : IDecoderFactory
{
    public string Failing { get; init; } = "decode";

    public string Name => "faulty";

    public DecoderSource Source => DecoderSource.Own;

    public int Rank => 1000;

    public bool CanDecode(TrackInfo track) => true;

    public IAudioDecoder CreateAudio(TrackInfo track) => new Decoder(new PcmDecoder(track), Failing);

    private sealed class Decoder(PcmDecoder inner, string failing) : IAudioDecoder
    {
        private int _decoded;

        public string Name => "faulty";

        public DecoderSource Source => DecoderSource.Own;

        public void Decode(Packet packet, ICollection<AudioFrame> output)
        {
            if (failing == "decode" && ++_decoded == 3)
            {
                throw new ArgumentOutOfRangeException(nameof(packet), "hostile");
            }

            inner.Decode(packet, output);
        }

        public void Drain(ICollection<AudioFrame> output)
        {
            if (failing == "drain")
            {
                output.Add(AudioFrame.Rent(Rate, 1, 10));
                throw new MediaFormatException("cannot finish");
            }
        }

        public void Flush()
        {
            if (failing == "flush")
            {
                throw new MediaFormatException("cannot reset");
            }
        }

        public void Dispose() => inner.Dispose();

        private const int Rate = 8000;
    }
}
