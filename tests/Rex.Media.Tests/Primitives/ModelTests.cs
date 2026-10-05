using Rex.Media.Primitives;

namespace Rex.Media.Tests.Primitives;

public sealed class ModelTests
{
    [Theory]
    [InlineData(720, 480, ColorMatrix.Bt601, ColorPrimaries.Bt601Ntsc)]
    [InlineData(720, 576, ColorMatrix.Bt601, ColorPrimaries.Bt601Pal)]
    [InlineData(1920, 1080, ColorMatrix.Bt709, ColorPrimaries.Bt709)]
    public void UnspecifiedColourIsResolvedFromThePictureSize(int width, int height, ColorMatrix matrix, ColorPrimaries primaries)
    {
        var resolved = ColorInfo.Unspecified.Resolve(width, height);

        Assert.Equal(matrix, resolved.Matrix);
        Assert.Equal(primaries, resolved.Primaries);
        Assert.Equal(ColorTransfer.Bt709, resolved.Transfer);
        Assert.False(resolved.FullRange);
        Assert.False(resolved.IsHdr);
    }

    [Fact]
    public void SpecifiedColourIsKeptAndBt2020ImpliesItsPrimaries()
    {
        var hdr = new ColorInfo(ColorMatrix.Bt2020NonConstant, ColorTransfer.Pq, ColorPrimaries.Unspecified, true).Resolve(3840, 2160);

        Assert.Equal(ColorPrimaries.Bt2020, hdr.Primaries);
        Assert.Equal(ColorTransfer.Pq, hdr.Transfer);
        Assert.True(hdr.IsHdr);
        Assert.True(hdr.FullRange);
        Assert.True(new ColorInfo(ColorMatrix.Rgb, ColorTransfer.Hlg, ColorPrimaries.DisplayP3, false).Resolve(10, 10) is { Primaries: ColorPrimaries.DisplayP3, IsHdr: true });
    }

    [Fact]
    public void TrackInfoHasSensibleDefaults()
    {
        var track = new TrackInfo { Id = 2, Codec = CodecId.H264, Video = new VideoTrackInfo { Width = 1920, Height = 1080 } };

        Assert.Equal(MediaKind.Video, track.Kind);
        Assert.Empty(track.CodecPrivate);
        Assert.False(track.Duration.IsKnown);
        Assert.Equal(new Rational(1, 1), track.Video!.PixelAspect);
        Assert.Equal(ColorInfo.Unspecified, track.Video.Color);
        Assert.Null(track.Video.FrameRate);
        Assert.Equal(0, track.Video.Rotation);
        Assert.Null(track.Language);
        Assert.Null(track.Title);
        Assert.False(track.IsDefault);
        Assert.False(track.IsForced);
    }

    [Fact]
    public void MediaInfoFindsTheFirstTrackOfAKind()
    {
        var info = new MediaInfo
        {
            FormatName = "test",
            Tracks = [new TrackInfo { Id = 1, Codec = CodecId.H264 }, new TrackInfo { Id = 2, Codec = CodecId.Aac }, new TrackInfo { Id = 3, Codec = CodecId.Opus }],
            Chapters = [new Chapter(MediaTime.Zero, "Intro")],
        };

        Assert.Equal(2, info.FirstTrack(MediaKind.Audio)!.Id);
        Assert.Null(info.FirstTrack(MediaKind.Subtitle));
        Assert.Empty(info.Metadata);
        Assert.Null(info.CoverArt);
        Assert.True(info.IsSeekable);
        Assert.Equal("Intro", info.Chapters[0].Title);
    }

    [Fact]
    public void APacketOwnsItsDataAndReturnsToThePool()
    {
        var packet = Packet.Create(3, MediaBuffer.CopyOf([1, 2]), MediaTime.Unknown, MediaTime.FromSeconds(1), MediaTime.FromSeconds(0.5), isKeyframe: false);
        packet.IsDiscontinuity = true;

        Assert.Equal(3, packet.TrackId);
        Assert.Equal(MediaTime.FromSeconds(1), packet.Timestamp);
        Assert.False(packet.IsKeyframe);
        Assert.True(packet.IsDiscontinuity);
        Assert.Equal(2, packet.Data.Length);

        packet.Dispose();

        // Unpooled: a pooled packet may already be someone else's by the time the asserts run.
        var loose = Packet.Create(pool: null, 3, MediaBuffer.Rent(shells: null, 1), MediaTime.Zero, MediaTime.Zero, MediaTime.Zero, isKeyframe: true);
        loose.Dispose();
        loose.Dispose();

        Assert.True(loose.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => loose.Data);
        Assert.Throws<ArgumentNullException>(() => Packet.Create(0, null!, MediaTime.Zero, MediaTime.Zero, MediaTime.Zero, true));
    }

    [Fact]
    public void ARecycledPacketStartsClean()
    {
        for (var i = 0; i < 10; i++)
        {
            Packet.Create(0, MediaBuffer.Rent(1), MediaTime.Zero, MediaTime.Zero, MediaTime.Zero, true).Dispose();
        }

        using var packet = Packet.Create(1, MediaBuffer.Rent(1), MediaTime.FromSeconds(2), MediaTime.Zero, MediaTime.Zero, true);

        Assert.False(packet.IsDiscontinuity);
        Assert.Equal(0, packet.Generation);
        Assert.Equal(MediaTime.FromSeconds(2), packet.Timestamp);
    }

    [Fact]
    public void AnAudioFrameHoldsPlanesAndTrimsFromTheStart()
    {
        using var frame = AudioFrame.Rent(1000, 2, 4, ChannelLayout.Stereo);
        new[] { 1f, 2f, 3f, 4f }.CopyTo(frame.Channel(0));
        new[] { 5f, 6f, 7f, 8f }.CopyTo(frame.Channel(1));
        frame.Pts = MediaTime.FromSeconds(1);

        frame.TrimStart(1);

        Assert.Equal([2f, 3f, 4f], frame.Channel(0).ToArray());
        Assert.Equal([6f, 7f, 8f], frame.Channel(1).ToArray());
        Assert.Equal(MediaTime.FromMilliseconds(1001), frame.Pts);
        Assert.Equal(MediaTime.FromMilliseconds(3), frame.Duration);
        Assert.Equal(4, frame.Capacity);
        frame.TrimStart(10);
        Assert.Equal(0, frame.SampleCount);
    }

    [Fact]
    public void TrimmingAFrameWithoutATimestampLeavesItUnknown()
    {
        using var frame = AudioFrame.Rent(1000, 1, 4);

        frame.TrimStart(2);

        Assert.False(frame.Pts.IsKnown);
        Assert.Throws<ArgumentOutOfRangeException>(() => frame.TrimStart(-1));
    }

    [Fact]
    public void AnAudioFrameChecksItsArguments()
    {
        using var frame = AudioFrame.Rent(1000, 1, 4, ChannelLayout.Stereo);

        Assert.Equal(ChannelLayout.Mono, frame.Layout);
        frame.SetSampleCount(2);
        Assert.Equal(2, frame.SampleCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => frame.SetSampleCount(5));
        Assert.Throws<ArgumentOutOfRangeException>(() => frame.SetSampleCount(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => frame.Channel(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => frame.Channel(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioFrame.Rent(0, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioFrame.Rent(1, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioFrame.Rent(1, 33, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioFrame.Rent(1, 1, -1));
    }

    [Fact]
    public void ADisposedAudioFrameRefusesUse()
    {
        // Unpooled: a pooled frame may already be someone else's by the time the asserts run.
        var frame = AudioFrame.Rent(pool: null, 1000, 1, 4);
        frame.Dispose();
        frame.Dispose();

        Assert.True(frame.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => frame.Channel(0).Length);
        Assert.Throws<ObjectDisposedException>(() => frame.SetSampleCount(0));
    }
}
