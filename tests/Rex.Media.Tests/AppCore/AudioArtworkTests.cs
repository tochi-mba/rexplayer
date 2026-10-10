using Rex.Media.AppCore.Library;
using Rex.Media.Primitives;
using Rex.Media.Settings;

namespace Rex.Media.Tests.AppCore;

public sealed class AudioArtworkTests
{
    private static float[] Sound(double frequency, int count = 8192) =>
        [.. Enumerable.Range(0, count).Select(i => (float)(Math.Sin(2 * Math.PI * frequency * i / 48_000) * (0.25 + (0.75 * i / count))))];

    [Fact]
    public void TheSoundAndIdentityMakeStableDistinctArtwork()
    {
        using var first = AudioArtwork.Render(Sound(220), 48_000, "first", 96);
        using var again = AudioArtwork.Render(Sound(220), 48_000, "first", 96);
        using var otherSound = AudioArtwork.Render(Sound(1760), 48_000, "first", 96);
        using var otherName = AudioArtwork.Render(Sound(220), 48_000, "second", 96);
        using var otherRate = AudioArtwork.Render(Sound(220), 44_100, "first", 96);

        Assert.Equal(PixelFormat.Bgra32, first.Format);
        Assert.Equal((96, 96), (first.Width, first.Height));
        Assert.Equal(first.Plane(0).ToArray(), again.Plane(0).ToArray());
        Assert.NotEqual(first.Plane(0).ToArray(), otherSound.Plane(0).ToArray());
        Assert.NotEqual(first.Plane(0).ToArray(), otherName.Plane(0).ToArray());
        Assert.NotEqual(first.Plane(0).ToArray(), otherRate.Plane(0).ToArray());
        Assert.True(first.Plane(0).ToArray().Distinct().Count() > 32);
    }

    [Fact]
    public void SilenceAndUnusualSamplesStillMakeArtworkAndBadArgumentsAreRefused()
    {
        var unusual = new float[2049];
        unusual[0] = float.NaN;
        unusual[1] = float.PositiveInfinity;
        using var picture = AudioArtwork.Render(unusual, 44_100, "silence", 64);
        Assert.Equal((64, 64), (picture.Width, picture.Height));

        Assert.Throws<ArgumentNullException>(() => AudioArtwork.Render(null!, 1, "x"));
        Assert.Throws<ArgumentNullException>(() => AudioArtwork.Render([0], 1, null!));
        Assert.Throws<ArgumentNullException>(() => AudioArtwork.Render([0], 1, "x", null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioArtwork.Render([0], 0, "x"));
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioArtwork.Render([0], 1, "x", 63));
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioArtwork.Render([0], 1, "x", 1025));
        Assert.Throws<ArgumentException>(() => AudioArtwork.Render([], 1, "x"));
    }

    [Fact]
    public void EveryControlChangesTheArtworkAndUnsafeValuesAreNormalized()
    {
        var samples = Sound(440);
        using var baseline = AudioArtwork.Render(samples, 48_000, "one", new(), 64);
        using var orbit = AudioArtwork.Render(samples, 48_000, "one", new(ArtworkStyle.Orbit), 64);
        using var wave = AudioArtwork.Render(samples, 48_000, "one", new(ArtworkStyle.Wave, 160, 180, 140), 64);
        using var minimal = AudioArtwork.Render(samples, 48_000, "one", new(ArtworkStyle.Minimal, 70, 30, 60), 64);
        using var noName = AudioArtwork.Render(samples, 48_000, "one", new(UseIdentity: false), 64);
        using var otherNoName = AudioArtwork.Render(samples, 48_000, "two", new(UseIdentity: false), 64);
        using var normalized = AudioArtwork.Render(samples, 48_000, "one", new((ArtworkStyle)99, -20, 900, -5), 64);

        var pictures = new[] { baseline, orbit, wave, minimal }.Select(frame => frame.Plane(0).ToArray()).ToArray();
        Assert.Equal(pictures.Length, pictures.DistinctBy(Convert.ToBase64String).Count());
        Assert.Equal(noName.Plane(0).ToArray(), otherNoName.Plane(0).ToArray());
        Assert.Equal((64, 64), (normalized.Width, normalized.Height));
    }
}
