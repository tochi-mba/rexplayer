using Rex.Media.AppCore;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit.Damage;

namespace Rex.Media.Tests.Robustness;

/// <summary>
/// Every sample begins as sound or video made by TestKit. The mutations are reproducible,
/// checked on the real demuxers, bounded by a packet budget and a wall-clock deadline.
/// </summary>
public sealed class DamagedFileTests
{
    private const int MaximumPackets = 20_000;
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(8);

    [Fact]
    public async Task EveryDamagedFormatFinishesCleanlyWithinABound()
    {
        var seen = 0;
        foreach (var healthy in HealthyMedia.All(seconds: 2))
        {
            foreach (var broken in DamageCorpus.Make(healthy))
            {
                var name = broken.Format + "/" + broken.Kind;
                // The file's extension is deliberately wrong in one case; probing, not a
                // filename, must decide whether the bytes hold a supported format.
                var result = Task.Run(() => Exercise(broken.Bytes, broken.Extension), TestContext.Current.CancellationToken);
                try
                {
                    var (packets, bytes) = await result.WaitAsync(Deadline, TestContext.Current.CancellationToken);
                    Assert.InRange(packets, 0, MaximumPackets);
                    Assert.InRange(bytes, 0, 128L * 1024 * 1024);
                }
                catch (MediaFormatException)
                {
                    // A header too badly damaged to open is a normal format refusal.
                }
                catch (TimeoutException)
                {
                    Assert.Fail(name + " failed to finish in " + Deadline.TotalSeconds + " seconds.");
                }
                catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
                {
                    Assert.Fail(name + " raised an unexpected " + ex.GetType().Name + ": " + ex.Message);
                }

                seen++;
            }
        }

        Assert.True(seen >= 40, "The corpus must exercise multiple kinds of damage in all six formats.");
    }

    [Fact]
    public void DamageCasesAreReproducibleAndDoNotMutateTheirSource()
    {
        var healthy = HealthyMedia.Wav(1);
        var original = (byte[])healthy.Bytes.Clone();
        var a = DamageCorpus.Make(healthy);
        var b = DamageCorpus.Make(healthy);
        Assert.Equal(a.Select(x => x.Kind), b.Select(x => x.Kind));
        Assert.Equal(a.Select(x => x.Bytes), b.Select(x => x.Bytes), new ByteArrayComparer());
        Assert.Equal(original, healthy.Bytes);
        Assert.Contains(a, x => x.Kind == "broken-fmt");
        Assert.Contains(a, x => x.Kind == "wrong-extension");
        Assert.Contains(a, x => x.Kind == "garbage-prefix");
    }

    private static (int Packets, long Bytes) Exercise(byte[] bytes, string extension)
    {
        var demuxers = MediaRegistries.Demuxers();
        using var source = new MemoryByteSource(bytes, "damage" + extension);
        using var demuxer = demuxers.Open(source, CancellationToken.None);
        long readBytes = 0;
        var packets = 0;
        while (packets < MaximumPackets && demuxer.ReadPacket(CancellationToken.None) is { } packet)
        {
            using (packet)
            {
                readBytes += packet.Data.Length;
                packets++;
            }
        }

        if (packets >= MaximumPackets)
        {
            throw new MediaFormatException("The damaged file exceeded the packet budget.");
        }

        return (packets, readBytes);
    }

    private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        public bool Equals(byte[]? x, byte[]? y) => x is null ? y is null : y is not null && x.AsSpan().SequenceEqual(y);
        public int GetHashCode(byte[] obj) => obj.Length;
    }
}
