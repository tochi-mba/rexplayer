using Rex.Media.AppCore.Player;
using Rex.Media.Codecs;
using Rex.Media.Primitives;
using Rex.Media.Codecs.Software.Pcm;
using Rex.Media.Containers.Matroska;
using Rex.Media.TestKit;
using Rex.Media.Tests.Containers;

namespace Rex.Media.Tests.AppCore;

/// <summary>Bounded feature decoding with the production demuxer and injectable decoder ladder.</summary>
public sealed class SectionAnalysisTests
{
    private static byte[] Recording(bool video, bool audio)
    {
        var entries = new List<byte[]>();
        if (audio)
        {
            entries.Add(MatroskaCraftedTests.PcmTrack(1));
        }

        if (video)
        {
            entries.Add(EbmlWriter.Element(MatroskaId.TrackEntry,
                EbmlWriter.UInt(MatroskaId.TrackNumber, 2),
                EbmlWriter.UInt(MatroskaId.TrackType, 1),
                EbmlWriter.Text(MatroskaId.CodecId, "V_MPEG4/ISO/AVC"),
                EbmlWriter.Element(MatroskaId.Video,
                    EbmlWriter.UInt(MatroskaId.PixelWidth, 16),
                    EbmlWriter.UInt(MatroskaId.PixelHeight, 8))));
        }

        var info = EbmlWriter.Element(MatroskaId.Info,
            EbmlWriter.UInt(MatroskaId.TimestampScale, 1_000_000),
            EbmlWriter.Float(MatroskaId.Duration, 600_000));
        var beginning = new List<byte[]>();
        var ending = new List<byte[]>();
        if (audio)
        {
            beginning.Add(MatroskaCraftedTests.Simple(1, 0, true, [.. Enumerable.Repeat((byte)16, 4096)]));
            ending.Add(MatroskaCraftedTests.Simple(1, 0, true, [.. Enumerable.Repeat((byte)10, 4096)]));
        }

        if (video)
        {
            beginning.Add(MatroskaCraftedTests.Simple(2, 0, true, 30));
            ending.Add(MatroskaCraftedTests.Simple(2, 0, true, 40));
        }

        return MatroskaCraftedTests.Mkv(info, MatroskaCraftedTests.Tracks([.. entries]),
            MatroskaCraftedTests.Cluster(0, [.. beginning]),
            MatroskaCraftedTests.Cluster(590_000, [.. ending]));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void OwnDemuxerCanSampleShortWindowsWithoutReadingPlaybackSession(bool video, bool audio)
    {
        var folder = Directory.CreateTempSubdirectory("rexplayer-section-analysis-").FullName;
        try
        {
            var path = Path.Combine(folder, "Series.S01E03.mkv");
            File.WriteAllBytes(path, Recording(video, audio));
            var decoders = new DecoderRegistry().Add(new PcmDecoderFactory()).Add(new FakeVideoDecoderFactory());
            var analyzed = SectionAnalysis.Analyze(path, decoders, CancellationToken.None);
            Assert.Equal(path, analyzed.Path);
            Assert.Equal(TimeSpan.FromMinutes(10), analyzed.Duration);
            Assert.Equal(SectionAnalysis.SignatureOf(path), analyzed.Signature);
            if (video)
            {
                Assert.NotEmpty(analyzed.Frames);
                Assert.All(analyzed.Frames, frame => Assert.True(frame.At >= TimeSpan.FromMinutes(4)));
            }
            else
            {
                Assert.Empty(analyzed.Frames);
            }

            if (audio)
            {
                Assert.NotNull(analyzed.Opening);
                Assert.NotNull(analyzed.Closing);
            }
            else
            {
                Assert.Empty(analyzed.Opening);
                Assert.Empty(analyzed.Closing);
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }


    /// <summary>
    /// Ten minutes of video with audio interleaved: keyframes at 0, 450 and 600 s, and pictures
    /// that are not keyframes at 451, 585, 587 and 589 s.
    /// </summary>
    private static byte[] SparseKeyframes()
    {
        var video = EbmlWriter.Element(MatroskaId.TrackEntry,
            EbmlWriter.UInt(MatroskaId.TrackNumber, 2),
            EbmlWriter.UInt(MatroskaId.TrackType, 1),
            EbmlWriter.Text(MatroskaId.CodecId, "V_MPEG4/ISO/AVC"),
            EbmlWriter.Element(MatroskaId.Video,
                EbmlWriter.UInt(MatroskaId.PixelWidth, 16),
                EbmlWriter.UInt(MatroskaId.PixelHeight, 8)));
        var info = EbmlWriter.Element(MatroskaId.Info,
            EbmlWriter.UInt(MatroskaId.TimestampScale, 1_000_000),
            EbmlWriter.Float(MatroskaId.Duration, 600_000));
        return MatroskaCraftedTests.Mkv(info, MatroskaCraftedTests.Tracks(MatroskaCraftedTests.PcmTrack(1), video),
            MatroskaCraftedTests.Cluster(0, MatroskaCraftedTests.Simple(2, 0, true, 30)),
            MatroskaCraftedTests.Cluster(450_000,
                MatroskaCraftedTests.Simple(1, 0, true, [.. Enumerable.Repeat((byte)16, 64)]),
                MatroskaCraftedTests.Simple(2, 0, true, 20),
                MatroskaCraftedTests.Simple(2, 1000, false, 21)),
            MatroskaCraftedTests.Cluster(585_000,
                MatroskaCraftedTests.Simple(2, 0, false, 22),
                MatroskaCraftedTests.Simple(2, 2000, false, 23),
                MatroskaCraftedTests.Simple(2, 4000, false, 24)),
            MatroskaCraftedTests.Cluster(600_000, MatroskaCraftedTests.Simple(2, 0, true, 25)));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void KeyframesAreDecodedAloneAndOnlyTheLastSecondsDensely(bool hold, bool silent, bool untimed)
    {
        var folder = Directory.CreateTempSubdirectory("rexplayer-section-keyframes-").FullName;
        try
        {
            var path = Path.Combine(folder, "Series.S01E04.mkv");
            File.WriteAllBytes(path, SparseKeyframes());
            var pictures = new FakeVideoDecoderFactory { Hold = hold, DrainOneAtATime = hold, Silent = silent, Untimed = untimed };
            var decoders = new DecoderRegistry().Add(new PcmDecoderFactory()).Add(pictures);
            var analyzed = SectionAnalysis.Analyze(path, decoders, CancellationToken.None);
            Assert.StartsWith("v" + SectionAnalysis.FeatureVersion + ":", analyzed.Signature, StringComparison.Ordinal);
            var times = analyzed.Frames.Select(frame => frame.At.TotalSeconds).ToArray();
            if (silent)
            {
                Assert.Empty(times);
            }
            else if (untimed)
            {
                // The keyframe keeps its packet's time; untimed pictures in the dense pass are skipped.
                Assert.Equal([450.0], times);
            }
            else if (hold)
            {
                // A holding decoder gives its pictures only when drained: keyframes still come out.
                Assert.Contains(450.0, times);
            }
            else
            {
                // The keyframe pass sees 450 s; the window over the last 17 s decodes 585 to 589 s
                // a second apart and stops at the picture at the end.
                Assert.Equal([450.0, 585.0, 587.0, 589.0], times);
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void SearchWindowsGrowWithTheEpisodeUpToTheirLimits()
    {
        Assert.Equal(TimeSpan.FromMinutes(12), SectionAnalysis.OpeningSpan(TimeSpan.FromMinutes(60)));
        Assert.Equal(TimeSpan.FromMinutes(7), SectionAnalysis.OpeningSpan(TimeSpan.FromMinutes(20)));
        Assert.Equal(TimeSpan.FromMinutes(6), SectionAnalysis.ClosingSpan(TimeSpan.FromMinutes(60)));
        Assert.Equal(TimeSpan.FromMinutes(4), SectionAnalysis.ClosingSpan(TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void MissingCodecsLeaveSectionFeaturesEmptyInsteadOfFailingTheEpisode()
    {
        var folder = Directory.CreateTempSubdirectory("rexplayer-section-no-decoder-").FullName;
        try
        {
            var path = Path.Combine(folder, "Show.S01E02.mkv");
            File.WriteAllBytes(path, Recording(video: true, audio: true));
            var result = SectionAnalysis.Analyze(path, new DecoderRegistry(), CancellationToken.None);
            Assert.Empty(result.Opening);
            Assert.Empty(result.Closing);
            Assert.Empty(result.Frames);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private sealed class DeliberateDecoderFactory(bool fail, bool nearlyConsumed) : IDecoderFactory
    {
        public string Name => "Deliberate section test decoder";
        public DecoderSource Source => DecoderSource.Own;
        public int Rank => 1;
        public bool CanDecode(TrackInfo track) => true;
        public IAudioDecoder CreateAudio(TrackInfo track) => new DeliberateAudioDecoder(fail, nearlyConsumed);
        public IVideoDecoder CreateVideo(TrackInfo track) => new DeliberateVideoDecoder(fail);
    }

    private sealed class DeliberateAudioDecoder(bool fail, bool nearlyConsumed) : IAudioDecoder
    {
        public string Name => "Synthetic audio";
        public DecoderSource Source => DecoderSource.Own;
        public void Decode(Packet packet, ICollection<AudioFrame> output)
        {
            var frame = AudioFrame.Rent(8000, 1, 100);
            if (nearlyConsumed)
            {
                // The closing window starts at 360 s: only half a sample of this frame overlaps it.
                // Rounding the cut up consumes all 100 samples, without crossing the
                // earlier strictly-before-the-window check.
                frame.Pts = MediaTime.FromSeconds(359.9875625);
            }
            output.Add(frame);
            if (fail)
            {
                throw new InvalidOperationException("A decoder failed after delivering its frame.");
            }
        }
        public void Drain(ICollection<AudioFrame> output) { }
        public void Flush() { }
        public void Dispose() { }
    }

    private sealed class DeliberateVideoDecoder(bool fail) : IVideoDecoder
    {
        public string Name => "Synthetic video";
        public DecoderSource Source => DecoderSource.Own;
        public void Decode(Packet packet, ICollection<VideoFrame> output)
        {
            output.Add(VideoFrame.Rent(PixelFormat.Gray8, 16, 8));
            if (fail)
            {
                throw new InvalidOperationException("A video decoder failed after delivering its frame.");
            }
        }
        public bool Drain(ICollection<VideoFrame> output) => true;
        public void Flush() { }
        public void Dispose() { }
    }

    [Fact]
    public void FramesSurviveExceptionsLongEnoughToBeDisposedByBothAnalysisWindows()
    {
        var folder = Directory.CreateTempSubdirectory("rexplayer-section-decoder-fault-").FullName;
        try
        {
            var path = Path.Combine(folder, "Show.S01E02.mkv");
            var registry = new DecoderRegistry().Add(new DeliberateDecoderFactory(
                fail: true, nearlyConsumed: false));

            File.WriteAllBytes(path, Recording(video: false, audio: true));
            Assert.Throws<InvalidOperationException>(() =>
                SectionAnalysis.Analyze(path, registry, CancellationToken.None));

            File.WriteAllBytes(path, Recording(video: true, audio: false));
            Assert.Throws<InvalidOperationException>(() =>
                SectionAnalysis.Analyze(path, registry, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void APartiallyOverlappingFrameCanHaveAllItsSamplesDiscarded()
    {
        var folder = Directory.CreateTempSubdirectory("rexplayer-section-audio-cut-").FullName;
        try
        {
            var path = Path.Combine(folder, "Show.S01E02.mkv");
            File.WriteAllBytes(path, Recording(video: false, audio: true));
            var registry = new DecoderRegistry().Add(new DeliberateDecoderFactory(
                fail: false, nearlyConsumed: true));
            var analyzed = SectionAnalysis.Analyze(path, registry, CancellationToken.None);
            Assert.Empty(analyzed.Closing);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AnalysisHonorsCancellationBeforeOpeningAndRejectsNullDecoders()
    {
        var folder = Directory.CreateTempSubdirectory("rexplayer-section-cancel-").FullName;
        try
        {
            var path = Path.Combine(folder, "Series.S01E02.mkv");
            File.WriteAllBytes(path, Recording(video: false, audio: true));
            using var stop = new CancellationTokenSource();
            stop.Cancel();
            Assert.Throws<OperationCanceledException>(() =>
                SectionAnalysis.Analyze(path, new DecoderRegistry(), stop.Token));
            Assert.Throws<ArgumentNullException>(() =>
                SectionAnalysis.Analyze(path, null!, CancellationToken.None));
            Assert.Throws<ArgumentException>(() =>
                SectionAnalysis.Analyze("  ", new DecoderRegistry(), CancellationToken.None));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
