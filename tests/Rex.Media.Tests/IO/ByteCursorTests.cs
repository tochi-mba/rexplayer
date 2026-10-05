using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.Tests.IO;

public sealed class ByteCursorTests
{
    private static ByteCursor Cursor(params byte[] data) => new(new MemoryByteSource(data));

    [Fact]
    public void NumbersAreReadInBothByteOrders()
    {
        var cursor = Cursor(0x01, 0x02, 0x01, 0x02, 0x01, 0x02, 0x03, 0x01, 0x02, 0x03, 0x04, 0x01, 0x02, 0x03, 0x04,
            1, 2, 3, 4, 5, 6, 7, 8, 1, 2, 3, 4, 5, 6, 7, 8);

        Assert.Equal(0x0102, cursor.ReadUInt16BigEndian());
        Assert.Equal(0x0201, cursor.ReadUInt16LittleEndian());
        Assert.Equal(0x010203u, cursor.ReadUInt24BigEndian());
        Assert.Equal(0x01020304u, cursor.ReadUInt32BigEndian());
        Assert.Equal(0x04030201u, cursor.ReadUInt32LittleEndian());
        Assert.Equal(0x0102030405060708UL, cursor.ReadUInt64BigEndian());
        Assert.Equal(0x0807060504030201UL, cursor.ReadUInt64LittleEndian());
        Assert.True(cursor.EndOfStream);
    }

    [Fact]
    public void AFourCharacterCodeIsReadAsText()
    {
        var cursor = Cursor((byte)'R', (byte)'I', (byte)'F', (byte)'F');

        Assert.Equal("RIFF", cursor.ReadFourCC());
    }

    [Fact]
    public void ReadsCrossTheInternalBufferBoundary()
    {
        var data = Enumerable.Range(0, 200_000).Select(i => (byte)i).ToArray();
        var cursor = Cursor(data);
        cursor.Seek(65_530);
        var block = new byte[20];

        cursor.ReadExactly(block);

        Assert.Equal(data.Skip(65_530).Take(20), block);
        Assert.Equal(65_550, cursor.Position);
        Assert.Equal(200_000 - 65_550, cursor.Remaining);
        Assert.Equal(200_000, cursor.Length);
    }

    [Fact]
    public void SeekingInsideTheBufferDoesNotRereadAndOutsideItDoes()
    {
        var source = new CountingSource(Enumerable.Range(0, 100_000).Select(i => (byte)i).ToArray());
        var cursor = new ByteCursor(source);

        cursor.ReadByte();
        cursor.Seek(10);
        Assert.Equal(10, cursor.ReadByte());
        Assert.Equal(1, source.Reads);

        cursor.Seek(90_000);
        Assert.Equal((byte)(90_000 & 0xFF), cursor.ReadByte());
        Assert.Equal(2, source.Reads);
        Assert.Same(source, cursor.Source);
    }

    [Fact]
    public void ReadReturnsWhatIsLeftAndExactReadsFailAtTheEnd()
    {
        var cursor = Cursor(1, 2, 3);
        var buffer = new byte[5];

        Assert.Equal(3, cursor.Read(buffer));
        Assert.Throws<MediaFormatException>(() => Cursor(1, 2).ReadExactly(new byte[3]));
        Assert.Throws<MediaFormatException>(() => Cursor().ReadByte());
    }

    [Fact]
    public void ABufferHoldsTheNextBytesAndIsReturnedWhenTheReadFails()
    {
        var cursor = Cursor(1, 2, 3, 4);

        using (var buffer = cursor.ReadBuffer(3))
        {
            Assert.Equal(new byte[] { 1, 2, 3 }, buffer.Span.ToArray());
        }

        Assert.Throws<MediaFormatException>(() => cursor.ReadBuffer(5));
    }

    [Fact]
    public void PeekLeavesThePositionAlone()
    {
        var cursor = Cursor(5, 6, 7);
        var buffer = new byte[2];

        Assert.Equal(2, cursor.Peek(buffer));
        Assert.Equal(0, cursor.Position);
        Assert.Equal(5, cursor.ReadByte());
    }

    [Fact]
    public void SkipMovesForwardAndRefusesToSkipPastTheEnd()
    {
        var cursor = Cursor(1, 2, 3, 4);

        cursor.Skip(3);
        Assert.Equal(4, cursor.ReadByte());
        Assert.Throws<MediaFormatException>(() => Cursor(1, 2).Skip(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => cursor.Skip(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => cursor.Seek(-1));
    }

    [Fact]
    public void ASourceWithoutALengthIsReadUntilItRunsDry()
    {
        var cursor = new ByteCursor(new UnknownLengthSource([1, 2, 3]));

        Assert.Null(cursor.Remaining);
        cursor.Skip(10);
        Assert.True(cursor.EndOfStream);
    }

    [Fact]
    public void EndOfStreamPeeksWhenAFieldStraddlesTheBuffer()
    {
        var data = new byte[70_000];
        var cursor = Cursor(data);
        cursor.Seek(65_535);
        cursor.ReadByte();

        Assert.False(cursor.EndOfStream);
        cursor.Seek(69_999);
        Assert.False(cursor.EndOfStream);
        cursor.ReadByte();
        Assert.True(cursor.EndOfStream);
    }

    [Fact]
    public void TheCursorNeedsASource()
    {
        Assert.Throws<ArgumentNullException>(() => new ByteCursor(null!));
    }

    [Fact]
    public void TheCancellationTokenReachesTheSource()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var cursor = new ByteCursor(new MemoryByteSource(new byte[] { 1 }), cancelled.Token);

        Assert.Throws<OperationCanceledException>(() => cursor.ReadByte());
        cursor.CancellationToken = CancellationToken.None;
        Assert.Equal(1, cursor.ReadByte());
    }

    private sealed class CountingSource(byte[] data) : IByteSource
    {
        private readonly MemoryByteSource _inner = new(data);

        public int Reads { get; private set; }

        public string Name => "counting";

        public long? Length => _inner.Length;

        public bool CanSeek => true;

        public int Read(long position, Span<byte> destination, CancellationToken cancellationToken)
        {
            Reads++;
            return _inner.Read(position, destination, cancellationToken);
        }

        public void Dispose() => _inner.Dispose();
    }

    private sealed class UnknownLengthSource(byte[] data) : IByteSource
    {
        private readonly MemoryByteSource _inner = new(data);

        public string Name => "live";

        public long? Length => null;

        public bool CanSeek => false;

        public int Read(long position, Span<byte> destination, CancellationToken cancellationToken) => _inner.Read(position, destination, cancellationToken);

        public void Dispose() => _inner.Dispose();
    }
}
