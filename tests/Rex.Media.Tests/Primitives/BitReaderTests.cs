using Rex.Media.Primitives;

namespace Rex.Media.Tests.Primitives;

public sealed class BitReaderTests
{
    [Fact]
    public void FieldsAreReadMostSignificantBitFirstAcrossByteBoundaries()
    {
        var reader = new BitReader([0b1011_0010, 0b1100_1111, 0xFF, 0x00]);

        Assert.Equal(0b101u, reader.ReadBits(3));
        Assert.Equal(0b1_0010_1100u, reader.ReadBits(9));
        Assert.Equal(12, reader.BitPosition);
        Assert.Equal(1, reader.BytePosition);
        Assert.Equal(20, reader.BitsRemaining);
        Assert.False(reader.IsByteAligned);
        Assert.Equal(0b1111_1111_1111_0000_0000u, reader.ReadBits(20));
        Assert.Equal(0, reader.BitsRemaining);
    }

    [Fact]
    public void ThirtyTwoBitFieldsSurviveAnUnalignedStart()
    {
        var reader = new BitReader([0x0F, 0xFF, 0xFF, 0xFF, 0xF0]);
        reader.SkipBits(4);

        Assert.Equal(uint.MaxValue, reader.ReadBits(32));
    }

    [Fact]
    public void ZeroBitsReadAsZeroWithoutMoving()
    {
        var reader = new BitReader([0xFF]);

        Assert.Equal(0u, reader.ReadBits(0));
        Assert.Equal(0, reader.BitPosition);
        Assert.Equal(8, reader.BitLength);
    }

    [Fact]
    public void PeekDoesNotMove()
    {
        var reader = new BitReader([0xA5]);

        Assert.Equal(0xAu, reader.PeekBits(4));
        Assert.Equal(0xAu, reader.ReadBits(4));
        Assert.Equal(0x5u, reader.ReadBits(4));
    }

    [Fact]
    public void ReadingPastTheEndThrowsAFormatError()
    {
        Assert.Throws<MediaFormatException>(() => new BitReader([0xFF]).ReadBits(9));
        Assert.Throws<MediaFormatException>(() => new BitReader([0xFF]).SkipBits(9));
        Assert.Throws<MediaFormatException>(() => new BitReader([0x00]).ReadUnary());
    }

    [Fact]
    public void OutOfRangeCountsAreProgrammingErrors()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BitReader([0xFF, 0xFF, 0xFF, 0xFF, 0xFF]).ReadBits(33));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BitReader([0xFF]).ReadBits(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BitReader([0xFF]).SkipBits(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BitReader([0xFF]).ReadSignedBits(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BitReader(new byte[9]).ReadBits64(65));
    }

    [Fact]
    public void SixtyFourBitReadsSplitAtThirtyTwo()
    {
        var reader = new BitReader([0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0, 0x11]);

        Assert.Equal(0x1u, reader.ReadBits64(4));
        Assert.Equal(0x2_3456_789AUL, reader.ReadBits64(36));
        Assert.Equal(0xBCUL, reader.ReadBits64(8));
    }

    [Fact]
    public void SignedFieldsAreSignExtended()
    {
        var reader = new BitReader([0b1110_0111, 0b1000_0000]);

        Assert.Equal(-1, reader.ReadSignedBits(3));
        Assert.Equal(0, reader.ReadSignedBits(2));
        Assert.Equal(-1, reader.ReadSignedBits(3));
        Assert.Equal(-2, reader.ReadSignedBits(2));
        Assert.Equal(0, reader.ReadSignedBits(6));
    }

    [Fact]
    public void ABitAndAlignmentBehave()
    {
        var reader = new BitReader([0b1000_0000, 0x7F]);

        Assert.True(reader.ReadBit());
        Assert.False(reader.ReadBit());
        reader.AlignToByte();
        Assert.True(reader.IsByteAligned);
        Assert.Equal(1, reader.BytePosition);
        reader.AlignToByte();
        Assert.Equal(1, reader.BytePosition);
        Assert.Equal(0x7Fu, reader.ReadBits(8));
    }

    [Fact]
    public void UnaryCountsZerosBeforeTheOne()
    {
        var reader = new BitReader([0b0001_1000]);

        Assert.Equal(3u, reader.ReadUnary());
        Assert.Equal(0u, reader.ReadUnary());
    }

    [Theory]
    [InlineData(new byte[] { 0b1000_0000 }, 0u)]
    [InlineData(new byte[] { 0b0100_0000 }, 1u)]
    [InlineData(new byte[] { 0b0110_0000 }, 2u)]
    [InlineData(new byte[] { 0b0010_0000 }, 3u)]
    [InlineData(new byte[] { 0b0001_0110 }, 10u)]
    public void UnsignedExpGolombMatchesTheSpecificationTable(byte[] data, uint expected)
    {
        var reader = new BitReader(data);

        Assert.Equal(expected, reader.ReadUnsignedExpGolomb());
    }

    [Theory]
    [InlineData(new byte[] { 0b1000_0000 }, 0)]
    [InlineData(new byte[] { 0b0100_0000 }, 1)]
    [InlineData(new byte[] { 0b0110_0000 }, -1)]
    [InlineData(new byte[] { 0b0010_0000 }, 2)]
    [InlineData(new byte[] { 0b0010_1000 }, -2)]
    public void SignedExpGolombAlternatesSigns(byte[] data, int expected)
    {
        var reader = new BitReader(data);

        Assert.Equal(expected, reader.ReadSignedExpGolomb());
    }

    [Fact]
    public void SeekingMovesBothWaysWithinTheData()
    {
        var reader = new BitReader([0b1010_0000, 0xFF]);
        reader.SkipBits(12);

        reader.Seek(2);
        Assert.Equal(0b10u, reader.ReadBits(2));
        reader.Seek(16);
        Assert.Equal(0, reader.BitsRemaining);
        Assert.Throws<MediaFormatException>(() => new BitReader([1]).Seek(9));
        Assert.Throws<MediaFormatException>(() => new BitReader([1]).Seek(-1));
    }

    [Fact]
    public void AnExpGolombCodeLongerThanThirtyTwoBitsIsRejected()
    {
        var data = new byte[9];
        data[4] = 0x01;

        Assert.Throws<MediaFormatException>(() => new BitReader(data).ReadUnsignedExpGolomb());
    }

    [Fact]
    public void ABoundedExpGolombValueIsReadUpToItsLimitAndRefusedAbove()
    {
        var reader = new BitReader([0b0001_0110, 0b0000_0000]);
        Assert.Equal(10, reader.ReadUnsignedExpGolomb(10, "count"));

        // The largest code there is, 2^32 - 2, cast to an int would wrap negative and slip past a check.
        var huge = new byte[9];
        huge[3] = 0x01;
        Array.Fill(huge, (byte)0xFF, 4, 5);
        var error = Assert.Throws<MediaFormatException>(() => new BitReader(huge).ReadUnsignedExpGolomb(64, "num_short_term_ref_pic_sets"));
        Assert.Equal("num_short_term_ref_pic_sets is 4294967294, above the 64 the specification allows.", error.Message);
        Assert.Throws<MediaFormatException>(() => new BitReader([0b0001_0110]).ReadUnsignedExpGolomb(9, "count"));
    }
}
