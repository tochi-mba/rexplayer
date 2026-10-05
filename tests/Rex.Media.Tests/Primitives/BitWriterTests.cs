using Rex.Media.Primitives;

namespace Rex.Media.Tests.Primitives;

public sealed class BitWriterTests
{
    [Fact]
    public void FieldsComeBackThroughTheReaderInOrder()
    {
        var random = new Random(7);
        var fields = Enumerable.Range(0, 2000).Select(_ =>
        {
            var count = random.Next(0, 33);
            var value = count == 0 ? 0u : (uint)random.NextInt64(0, 1L << count);
            return (value, count);
        }).ToList();
        var buffer = new byte[fields.Sum(f => f.count) / 8 + 1];
        var writer = new BitWriter(buffer);
        foreach (var (value, count) in fields)
        {
            writer.Write(value, count);
        }

        var reader = new BitReader(buffer);
        foreach (var (value, count) in fields)
        {
            Assert.Equal(value, reader.ReadBits(count));
        }

        Assert.Equal(fields.Sum(f => f.count), writer.BitPosition);
    }

    [Fact]
    public void BitsAboveTheCountAreIgnoredAndNeighboursAreKept()
    {
        var buffer = new byte[] { 0xFF, 0xFF };
        var writer = new BitWriter(buffer);

        writer.Write(0xFFFF_FF00, 4);
        writer.Write(0b101, 3);

        // 0000 from the first field, 101 from the second, and the untouched last bit still 1.
        Assert.Equal(0b0000_1011, buffer[0]);
        Assert.Equal(0xFF, buffer[1]);
    }

    [Fact]
    public void SixtyFourBitSignedUnaryAndBitFieldsRoundTrip()
    {
        var buffer = new byte[32];
        var writer = new BitWriter(buffer);
        writer.Write64(0x0F_1234_5678UL, 36);
        writer.Write64(5, 20);
        writer.WriteSigned(-3, 5);
        writer.WriteBit(true);
        writer.WriteBit(false);
        writer.WriteUnary(40);
        writer.WriteUnary(0);

        var reader = new BitReader(buffer);
        Assert.Equal(0x0F_1234_5678UL, reader.ReadBits64(36));
        Assert.Equal(5UL, reader.ReadBits64(20));
        Assert.Equal(-3, reader.ReadSignedBits(5));
        Assert.True(reader.ReadBit());
        Assert.False(reader.ReadBit());
        Assert.Equal(40u, reader.ReadUnary());
        Assert.Equal(0u, reader.ReadUnary());
    }

    [Fact]
    public void AligningPadsWithZerosAndCountsTouchedBytes()
    {
        var buffer = new byte[] { 0xFF, 0xFF };
        var writer = new BitWriter(buffer);
        Assert.True(writer.IsByteAligned);

        writer.Write(1, 3);
        Assert.False(writer.IsByteAligned);
        Assert.Equal(1, writer.BytesWritten);
        writer.AlignToByte();
        writer.AlignToByte();

        Assert.True(writer.IsByteAligned);
        Assert.Equal(8, writer.BitPosition);
        Assert.Equal(0b0010_0000, buffer[0]);
    }

    [Fact]
    public void WritingPastTheEndOrMoreThanTheWidthIsAProgrammingError()
    {
        var buffer = new byte[1];

        Assert.Throws<InvalidOperationException>(() =>
        {
            var writer = new BitWriter(buffer);
            writer.Write(0, 9);
        });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            var writer = new BitWriter(new byte[8]);
            writer.Write(0, 33);
        });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            var writer = new BitWriter(new byte[16]);
            writer.Write64(0, 65);
        });
    }

    [Fact]
    public void UnaryRunsLongerThanTheReadersWindowAreCounted()
    {
        var buffer = new byte[32];
        var writer = new BitWriter(buffer);
        writer.Write(0b101, 3);
        writer.WriteUnary(100);
        writer.WriteUnary(31);
        writer.WriteUnary(32);

        var reader = new BitReader(buffer);
        Assert.Equal(0b101u, reader.ReadBits(3));
        Assert.Equal(100u, reader.ReadUnary());
        Assert.Equal(31u, reader.ReadUnary());
        Assert.Equal(32u, reader.ReadUnary());
    }

    [Fact]
    public void AUnaryCodeThatRunsOffTheEndAfterAFullWindowIsBroken()
    {
        var zeros = new byte[5];

        Assert.Throws<MediaFormatException>(() => new BitReader(zeros).ReadUnary());
    }
}
