using Rex.Media.Codecs.Pictures;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Codecs;

/// <summary>Which way up a photo was taken (FMT-C19), from its Exif orientation.</summary>
public sealed class ExifOrientationTests
{
    /// <summary>A TIFF structure whose first directory holds <paramref name="entries"/> (tag, type, value).</summary>
    public static byte[] Tiff(bool little, params (int Tag, int Type, int Value)[] entries)
    {
        var bytes = new List<byte>(little ? "II*\0"u8.ToArray() : "MM\0*"u8.ToArray());
        bytes.AddRange(Number(8, 4, little));
        bytes.AddRange(Number(entries.Length, 2, little));
        foreach (var (tag, type, value) in entries)
        {
            bytes.AddRange(Number(tag, 2, little));
            bytes.AddRange(Number(type, 2, little));
            bytes.AddRange(Number(1, 4, little));
            bytes.AddRange(type == 3 ? [.. Number(value, 2, little), 0, 0] : Number(value, 4, little));
        }

        return [.. bytes];
    }

    /// <summary>A JPEG's first segments with an Exif segment holding <paramref name="tiff"/>.</summary>
    public static byte[] Jpeg(byte[] tiff) =>
        [0xFF, 0xD8, 0xFF, 0xE0, 0, 4, 0, 0, 0xFF, 0xE1, .. Number(2 + 6 + tiff.Length, 2, little: false), .. "Exif\0\0"u8, .. tiff, 0xFF, 0xDA, 0, 2];

    private static byte[] Number(int value, int size, bool little)
    {
        var bytes = Enumerable.Range(0, size).Select(i => (byte)(value >> (8 * i))).ToArray();
        return little ? bytes : [.. bytes.Reverse()];
    }

    [Theory]
    [Capability("FMT-C19")]
    [InlineData(true, 6)]
    [InlineData(false, 8)]
    [InlineData(true, 3)]
    public void APhotoSaysWhichWayUpItWasTaken(bool little, int orientation)
    {
        var tiff = Tiff(little, (0x0100, 3, 64), (0x0112, 3, orientation));

        Assert.Equal(orientation, ExifOrientation.Of(Jpeg(tiff)));
        Assert.Equal(orientation, ExifOrientation.Of(tiff));
    }

    [Fact]
    public void AnythingElseIsAsStored()
    {
        Assert.Equal(1, ExifOrientation.Of(File.ReadAllBytes(RepoPaths.Combine("tests", "fixtures", "picture", "still.jpg"))));
        Assert.Equal(1, ExifOrientation.Of(File.ReadAllBytes(RepoPaths.Combine("tests", "fixtures", "picture", "still.png"))));
        Assert.Equal(1, ExifOrientation.Of(Jpeg(Tiff(true, (0x0112, 3, 9)))));
        Assert.Equal(1, ExifOrientation.Of(Jpeg(Tiff(true, (0x0112, 4, 6)))));
        Assert.Equal(1, ExifOrientation.Of(Jpeg([.. "II*\0"u8, 99, 0, 0, 0])));
        Assert.Equal(1, ExifOrientation.Of(Jpeg([.. "II*\0"u8])));
        Assert.Equal(1, ExifOrientation.Of(Jpeg([.. "II*\0"u8, 8, 0, 0, 0, 5, 0, 1, 1])));
        Assert.Equal(1, ExifOrientation.Of([0xFF, 0xD8, 0xFF, 0xE0, 0, 1]));
        Assert.Equal(1, ExifOrientation.Of([0xFF, 0xD8, 0xFF, 0xD9, 0, 2]));
        Assert.Equal(1, ExifOrientation.Of([0xFF, 0xD8]));
        Assert.Equal(1, ExifOrientation.Of([1, 2, 3, 4]));
    }
}
