using Rex.Media.AppCore.Player;

namespace Rex.Media.Tests.AppCore;

public sealed class MediaPrefetchTests
{
    private const int End = MediaPrefetch.EndBytes;

    [Fact]
    public void BothEndsOfALargeFileAreReadAndASmallFileWhole()
    {
        Assert.Empty(MediaPrefetch.Ranges(0));
        Assert.Equal([(0L, 1000)], MediaPrefetch.Ranges(1000));
        Assert.Equal([(0L, 2 * End)], MediaPrefetch.Ranges(2L * End));
        Assert.Equal([(0L, End), (700L * End - End, End)], MediaPrefetch.Ranges(700L * End));
    }

    [Fact]
    public void EachLocalFileIsChosenOnce()
    {
        var prefetch = new MediaPrefetch(_ => throw new IOException());

        Assert.Equal(["C:\\Shows\\E02.mkv", "C:\\Shows\\E03.mkv"], prefetch.Choose(["C:\\Shows\\E02.mkv", null, "", "https://example.com/a.mp4", "C:\\Shows\\E03.mkv", "c:\\shows\\e02.MKV"]));
        Assert.Empty(prefetch.Choose(["C:\\Shows\\E02.mkv", "C:\\Shows\\E03.mkv"]));
        Assert.Throws<ArgumentNullException>(() => prefetch.Choose(null!));
    }

    [Fact]
    public void ReadingTouchesOnlyTheEnds()
    {
        var file = new TrackingStream(3L * End);
        var prefetch = new MediaPrefetch(_ => file);

        prefetch.Read("E02.mkv");

        Assert.Equal(2L * End, file.BytesRead);
        Assert.Equal([0L, 2L * End], file.Positions);
        Assert.True(file.Disposed);
    }

    [Fact]
    public void AFileThatCannotBeReadIsLeftForTheRealOpen()
    {
        new MediaPrefetch(_ => throw new FileNotFoundException()).Read("gone.mkv");
        new MediaPrefetch(_ => throw new UnauthorizedAccessException()).Read("locked.mkv");
        new MediaPrefetch(_ => new TrackingStream(10, failAfter: 0)).Read("broken.mkv");
    }

    [Fact]
    public void AFileThatEndsEarlyStopsTheRead()
    {
        var file = new TrackingStream(3L * End, shortBy: 100);
        new MediaPrefetch(_ => file).Read("short.mkv");

        Assert.Equal(2L * End - 100, file.BytesRead);
    }

    [Fact]
    public void ByDefaultTheDiskIsRead()
    {
        var path = Path.Combine(Path.GetTempPath(), "rexplayer-prefetch-" + Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllBytes(path, new byte[4096]);
        try
        {
            new MediaPrefetch().Read(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A stream of zeros that says where it was read from.</summary>
    private sealed class TrackingStream(long length, int failAfter = -1, int shortBy = 0) : Stream
    {
        private long _position;

        public long BytesRead { get; private set; }

        public List<long> Positions { get; } = [];

        public bool Disposed { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position
        {
            get => _position;
            set
            {
                _position = value;
                Positions.Add(value);
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (failAfter >= 0 && BytesRead >= failAfter)
            {
                throw new IOException("The disk went away.");
            }

            var available = (int)Math.Max(0, Math.Min(count, length - shortBy - _position));
            _position += available;
            BytesRead += available;
            return available;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
