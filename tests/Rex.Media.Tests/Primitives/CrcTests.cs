using System.Text;
using Rex.Media.Primitives;

namespace Rex.Media.Tests.Primitives;

public sealed class CrcTests
{
    private static readonly byte[] Check = Encoding.ASCII.GetBytes("123456789");

    [Fact]
    public void EveryNamedCrcProducesItsCatalogueCheckValue()
    {
        Assert.Equal(0xF4u, Crc.Crc8Flac.Compute(Check));
        Assert.Equal(0xFEE8u, Crc.Crc16Flac.Compute(Check));
        Assert.Equal(0x89A1897Fu, Crc.Crc32Ogg.Compute(Check));
        Assert.Equal(0xCBF43926u, Crc.Crc32.Compute(Check));
    }

    [Fact]
    public void AChecksumComputedInPiecesMatchesOneComputedWhole()
    {
        foreach (var crc in new[] { Crc.Crc8Flac, Crc.Crc16Flac, Crc.Crc32 })
        {
            var register = crc.Append(crc.Start(), Check.AsSpan(0, 4));
            register = crc.Append(register, Check.AsSpan(4));

            Assert.Equal(crc.Compute(Check), crc.Finish(register));
        }
    }

    [Fact]
    public void TheParametersAreExposed()
    {
        var crc = Crc.Crc32;

        Assert.Equal(32, crc.Width);
        Assert.Equal(0x04C11DB7u, crc.Polynomial);
        Assert.Equal(0xFFFFFFFFu, crc.Initial);
        Assert.True(crc.Reflected);
        Assert.Equal(0xFFFFFFFFu, crc.FinalXor);
    }

    [Fact]
    public void WidthsOutsideEightToThirtyTwoAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Crc(7, 1, 0, false, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Crc(33, 1, 0, false, 0));
    }

    [Fact]
    public void AnEmptyInputGivesTheInitialValueXoredWithTheFinalXor()
    {
        Assert.Equal(0u, Crc.Crc32.Compute([]));
        Assert.Equal(0xFFFFFFFFu, new Crc(32, 0x04C11DB7, 0xFFFFFFFF, reflected: false, 0).Compute([]));
    }
}
