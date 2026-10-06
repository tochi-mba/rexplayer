using Rex.Media.AppCore;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Robustness;

/// <summary>
/// Damaged files must never crash or hang the player. Every committed fixture is mutated hundreds
/// of ways from fixed seeds (bytes flipped, the end cut off, blocks copied over or zeroed) and then
/// probed, opened, read to the end, decoded and sought through. The only acceptable failure is a
/// <see cref="MediaFormatException"/>; anything else names the seed that reproduces it.
/// </summary>
public sealed class HostileInputTests
{
    private const int Mutations = 60;
    private const int PacketLimit = 20_000;

    public static TheoryData<string> Fixtures =>
    [
        "tests/fixtures/smoke/tone.wav",
        "tests/fixtures/flac/stereo-16bit-44k-lpc.flac",
        "tests/fixtures/flac/mono-24bit-96k-sweep.flac",
        "tests/fixtures/flac/surround-51-16bit-48k.flac",
        "tests/fixtures/mp3/stereo-44k-128k-cbr.mp3",
        "tests/fixtures/mp3/stereo-44k-vbr-bursts.mp3",
        "tests/fixtures/mp3/stereo-22k-64k-mpeg2.mp3",
        "tests/fixtures/mp3/mono-8k-16k-mpeg25.mp3",
    ];

    [Theory]
    [MemberData(nameof(Fixtures))]
    [Capability("SEC-01")]
    public async Task MutatedFilesPlayOrFailCleanly(string fixture)
    {
        var original = File.ReadAllBytes(RepoPaths.Combine(fixture));
        for (var seed = 0; seed < Mutations; seed++)
        {
            var mutated = Mutate(original, new Random(seed));
            var run = Task.Run(() => Exercise(mutated, new Random(seed)), TestContext.Current.CancellationToken);
            try
            {
                await run.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            }
            catch (MediaFormatException)
            {
                // A clean refusal is a correct answer for a damaged file.
            }
            catch (TimeoutException)
            {
                Assert.Fail($"{fixture} mutated with seed {seed} did not finish within 10 seconds.");
            }
            catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
            {
                Assert.Fail($"{fixture} mutated with seed {seed} threw {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }

    [Fact]
    public void TheMutationsAreReproducibleAndAllChangeTheFile()
    {
        var original = Enumerable.Range(0, 4096).Select(i => (byte)i).ToArray();

        for (var seed = 0; seed < 50; seed++)
        {
            Assert.Equal(Mutate(original, new Random(seed)), Mutate(original, new Random(seed)));
            Assert.NotEqual(original, Mutate(original, new Random(seed)));
        }
    }

    private static byte[] Mutate(byte[] original, Random random)
    {
        var bytes = (byte[])original.Clone();
        switch (random.Next(4))
        {
            case 0:
                // A handful of flipped bytes, weighted towards the headers at the front.
                var flips = random.Next(1, 20);
                for (var i = 0; i < flips; i++)
                {
                    var at = random.Next(2) == 0 ? random.Next(Math.Min(bytes.Length, 512)) : random.Next(bytes.Length);
                    bytes[at] ^= (byte)random.Next(1, 256);
                }

                return bytes;
            case 1:
                return bytes[..random.Next(1, bytes.Length)];
            case 2:
                var length = random.Next(1, Math.Min(2048, bytes.Length));
                Array.Copy(original, random.Next(original.Length - length), bytes, random.Next(bytes.Length - length), length);
                return bytes.AsSpan().SequenceEqual(original) ? [.. bytes, 0] : bytes;
            default:
                var zeros = random.Next(1, Math.Min(4096, bytes.Length));
                Array.Clear(bytes, random.Next(bytes.Length - zeros), zeros);
                return bytes.AsSpan().SequenceEqual(original) ? bytes[..^1] : bytes;
        }
    }

    private static void Exercise(byte[] file, Random random)
    {
        var demuxers = MediaRegistries.Demuxers();
        using var demuxer = demuxers.Open(new MemoryByteSource(file, "mutated"), CancellationToken.None);
        var track = demuxer.Info.Tracks.FirstOrDefault(t => t.Audio is not null);
        if (track is null)
        {
            return;
        }

        var decoded = MediaRegistries.Decoders().CreateAudio(track);
        using var decoder = decoded.Decoder;
        var frames = new List<AudioFrame>();
        for (var pass = 0; pass < 2; pass++)
        {
            for (var read = 0; read < PacketLimit && demuxer.ReadPacket(CancellationToken.None) is { } packet; read++)
            {
                using (packet)
                {
                    decoder?.Decode(packet, frames);
                }

                foreach (var frame in frames)
                {
                    frame.Dispose();
                }

                frames.Clear();
            }

            decoder?.Flush();
            var duration = demuxer.Info.Duration.IsKnown ? demuxer.Info.Duration.TotalSeconds : 1;
            demuxer.Seek(MediaTime.FromSeconds(random.NextDouble() * duration * 1.2), CancellationToken.None);
        }
    }

}
