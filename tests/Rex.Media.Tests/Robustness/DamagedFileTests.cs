using System.Globalization;
using System.Text;
using Rex.Media.TestKit;
using Rex.Media.TestKit.Damage;

namespace Rex.Media.Tests.Robustness;

/// <summary>
/// Damaged files never crash or hang the player, never cost unbounded memory or time, and play as
/// much as can be read. Every file begins as sound or video made by TestKit; the damage is
/// reproducible (<see cref="DamageCorpus"/>). Each is read on one thread with exact allocation counts
/// (the demuxers and decoders alone), then played through a whole session (what a viewer gets),
/// which must keep at least the share of the sound each kind of damage allows.
/// REXPLAYER_DAMAGE_LONG=1 runs longer files with more seeds; REXPLAYER_DAMAGE_REPORT names a file
/// the results table is written to.
/// </summary>
public sealed class DamagedFileTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    private static bool Long => Environment.GetEnvironmentVariable("REXPLAYER_DAMAGE_LONG") == "1";

    private static readonly string[] FormatNames = ["wav", "flac", "mp3", "mp4", "mkv", "ogg"];

    public static TheoryData<string> Formats => [.. FormatNames];

    private static HealthyFile Healthy(string format, double seconds) => format switch
    {
        "wav" => HealthyMedia.Wav(seconds),
        "flac" => HealthyMedia.Flac(seconds),
        "mp3" => HealthyMedia.Mp3(seconds),
        "mp4" => HealthyMedia.Mp4(seconds),
        "mkv" => HealthyMedia.Matroska(seconds),
        _ => HealthyMedia.Ogg(seconds),
    };

    [Theory]
    [MemberData(nameof(Formats))]
    [Capability("SEC-01")]
    public async Task DamagedFilesPlayAsMuchAsCanBeRead(string format)
    {
        var healthy = Healthy(format, Long ? 60 : 8);
        var whole = DamageRunner.Demux(healthy.Bytes, healthy.Extension, Deadline);
        var played = await DamagedSession.PlayAsync(healthy.Bytes, healthy.Extension, Deadline);
        Assert.Equal(DamageResult.Played, whole.Result);
        Assert.Equal(DamageResult.Played, played.Result);
        Assert.Equal(0, played.Notices);
        Assert.InRange(played.AudioSeconds, healthy.Seconds * 0.98, healthy.Seconds * 1.02);

        var failures = new List<string>();
        var report = new StringBuilder();
        report.AppendLine(Row(new DamagedMedia(format, "healthy", healthy.Bytes, healthy.Extension), whole, played, 1, 1, played.Pictures > 0 ? 1 : null));
        foreach (var seed in Long ? Enumerable.Range(1, 12) : [7])
        {
            foreach (var damaged in DamageCorpus.Make(healthy, seed))
            {
                var read = DamageRunner.Demux(damaged.Bytes, damaged.Extension, Deadline);
                var session = await DamagedSession.PlayAsync(damaged.Bytes, damaged.Extension, Deadline);
                var audioShare = session.AudioSeconds / played.AudioSeconds;
                var pictureShare = played.Pictures > 0 ? session.Pictures / (double)played.Pictures : 0;
                var share = Math.Max(audioShare, pictureShare);
                report.AppendLine(Row(damaged, read, session, read.AudioSeconds / whole.AudioSeconds, audioShare, played.Pictures > 0 ? pictureShare : null));
                foreach (var (what, measure) in new[] { ("read", read), ("played", session) })
                {
                    if (measure.Result is DamageResult.Crashed or DamageResult.Hung)
                    {
                        failures.Add($"{damaged.Name} (seed {seed}) {measure.Result} when {what}: {measure.Error}");
                    }
                }

                // Exact on the reading thread: a damaged size field must never become a huge allocation.
                if (read.AllocatedBytes > Math.Max(64L << 20, 16L * damaged.Bytes.Length))
                {
                    failures.Add($"{damaged.Name} (seed {seed}) allocated {read.AllocatedBytes >> 20} MB reading a {damaged.Bytes.Length >> 10} KB file.");
                }

                if (share < damaged.MinimumShare)
                {
                    failures.Add($"{damaged.Name} (seed {seed}) played {share:P0} of its sound or pictures; at least {damaged.MinimumShare:P0} can be read. {session.Error}");
                }
            }
        }

        Write(format, report.ToString());
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Theory]
    [InlineData("tests/fixtures/corpus/demux/mkv-vorbis-codebook-runs-never-advance.bin", ".webm")]
    [Capability("SEC-01")]
    public void FilesTheFuzzerFoundHangingAreReadOrRefusedInTime(string path, string extension)
    {
        var read = DamageRunner.Demux(File.ReadAllBytes(RepoPaths.Combine(path)), extension, Deadline);

        Assert.True(read.Result is not (DamageResult.Crashed or DamageResult.Hung), $"{path} {read.Result}: {read.Error}");
        Assert.True(read.Elapsed < TimeSpan.FromSeconds(2), $"{path} took {read.Elapsed.TotalSeconds:0.0} s.");
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
        Assert.Contains(a, x => x.Kind == "data-size-huge");
    }

    [Fact]
    public void EveryFormatHasDamageAimedAtItsStructure()
    {
        foreach (var format in FormatNames)
        {
            var healthy = Healthy(format, 2);
            Assert.True(StructuralDamage.For(healthy, new Random(1)).Count >= 3, format);
        }
    }

    /// <summary>
    /// A row of the results table: format, damage, how reading on one thread and playing in a session
    /// ended, the share of the sound read and played, the share of the pictures shown, the notices
    /// given, megabytes allocated reading, and seconds per megabyte reading.
    /// </summary>
    private static string Row(DamagedMedia damaged, DamageMeasure read, DamageMeasure session, double readShare, double audioShare, double? pictureShare) =>
        string.Create(CultureInfo.InvariantCulture,
            $"| {damaged.Format} | {damaged.Kind} | {read.Result} | {session.Result} | {readShare * 100:0}% | {audioShare * 100:0}% | {(pictureShare is { } shown ? $"{shown * 100:0}%" : "-")} | {session.Notices} | {read.AllocatedBytes / 1e6:0.0} | {read.SecondsPerMegabyte:0.00} |");

    private static void Write(string format, string rows)
    {
        TestContext.Current.SendDiagnosticMessage(format + Environment.NewLine + rows);
        if (Environment.GetEnvironmentVariable("REXPLAYER_DAMAGE_REPORT") is { Length: > 0 } path)
        {
            lock (typeof(DamagedFileTests))
            {
                File.AppendAllText(path, rows);
            }
        }
    }

    private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        public bool Equals(byte[]? x, byte[]? y) => x is null ? y is null : y is not null && x.AsSpan().SequenceEqual(y);

        public int GetHashCode(byte[] obj) => obj.Length;
    }
}
