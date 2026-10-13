using System.Globalization;
using System.Runtime.Versioning;
using Rex.Media.AppCore;
using Rex.Media.Codecs.MediaFoundation;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.Video;
using Rex.Media.Video.D3D11;

namespace Rex.Media.Windows.Tests;

/// <summary>
/// Subject Lock's cut detection replayed on real episodes, against the cuts found in them. The
/// episodes are not in the repository: set REXPLAYER_FOOTAGE to the folder that holds them and
/// the test finds each by its file name; without it the test is skipped. The expected lists are
/// every cut in each stretch: the pictures either side of each were saved and the borderline ones
/// checked by eye, including cuts between two angles of one room and an arm sweeping close past
/// the lens, which is not a cut.
/// </summary>
[SupportedOSPlatform("windows8.0")]
public sealed class SubjectShotChangeFootageTests
{
    private const string Rehearsal = "The.Rehearsal.S01E01.1080p.WEBRip.x265-RARBG[eztv.re].mp4";
    private const string Utopia = "Utopia (2013) - S01E01 - Episode 1 (1080p BluRay x265 Silence).mkv";

    // Seconds of the first picture of each new shot between 300 and 420 seconds.
    private static readonly double[] RehearsalCuts =
    [
        302.719, 311.061, 315.690, 318.109, 321.905, 323.907, 327.410, 331.873, 333.333, 335.418,
        342.801, 343.844, 345.095, 346.638, 349.182, 355.146, 358.441, 361.319, 363.363, 365.282,
        367.576, 369.536, 377.586, 381.256, 383.884, 385.385, 393.143, 396.563, 402.193, 405.030,
        413.872, 416.875,
    ];

    private static readonly double[] UtopiaCuts =
    [
        302.200, 304.760, 306.720, 359.240, 361.840, 364.920, 368.320, 375.160, 378.720, 383.400,
        384.760, 386.960, 389.240, 393.240, 395.640, 401.000, 403.400, 406.600, 409.040, 419.400,
    ];

    public static TheoryData<string, double[]> Stretches => new()
    {
        { Rehearsal, RehearsalCuts },
        { Utopia, UtopiaCuts },
    };

    [Theory]
    [MemberData(nameof(Stretches))]
    public void EveryCutIsFoundAndNothingElseIs(string name, double[] expected)
    {
        var folder = Environment.GetEnvironmentVariable("REXPLAYER_FOOTAGE");
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            Assert.Skip("Set REXPLAYER_FOOTAGE to the folder holding the episodes to replay real cuts.");
        }

        var path = Directory.EnumerateFiles(folder, name, SearchOption.AllDirectories).FirstOrDefault();
        if (path is null)
        {
            Assert.Skip($"{name} is not under REXPLAYER_FOOTAGE.");
        }

        var found = Cuts(path, 300, 420);
        var missed = expected.Where(cut => !found.Any(at => Math.Abs(at - cut) < 0.05)).ToArray();
        var extra = found.Where(at => !expected.Any(cut => Math.Abs(at - cut) < 0.05)).ToArray();
        Assert.True(missed.Length == 0 && extra.Length == 0,
            $"Missed: {string.Join(", ", missed.Select(Seconds))}. Not cuts: {string.Join(", ", extra.Select(Seconds))}.");
    }

    private static string Seconds(double value) => value.ToString("F3", CultureInfo.InvariantCulture);

    /// <summary>The times of the pictures the session marks as a new shot, decoding from shortly before <paramref name="from"/>.</summary>
    private static List<double> Cuts(string path, double from, double to)
    {
        using var source = new FileByteSource(path);
        using var demuxer = MediaRegistries.Demuxers().Probe(source, CancellationToken.None)!.Open(source, CancellationToken.None);
        var track = demuxer.Info.FirstTrack(MediaKind.Video)!;
        using var decoder = MediaRegistries.Decoders([new MfDecoderFactory()]).CreateVideo(track).Decoder!;
        demuxer.Seek(MediaTime.FromSeconds(from - 1), CancellationToken.None);
        using var session = new SubjectEditSession();
        var decoded = new List<VideoFrame>();
        var cuts = new List<double>();
        var started = false;
        var finished = false;
        while (!finished && demuxer.ReadPacket(CancellationToken.None) is { } packet)
        {
            using (packet)
            {
                if (packet.TrackId == track.Id)
                {
                    decoder.Decode(packet, decoded);
                }
            }

            foreach (var picture in decoded)
            {
                using (picture)
                {
                    var at = picture.Pts.TotalSeconds;
                    finished |= at > to;
                    if (at < from || at > to)
                    {
                        continue;
                    }

                    using var frame = ColorConverter.ToBgra(picture);
                    if (!started)
                    {
                        // A large central box: the measure looks at the picture around it.
                        session.Select(0.3f, 0.3f, 0.4f, 0.4f);
                        started = true;
                    }

                    session.Process(frame)?.Dispose();
                    if (session.IsCut)
                    {
                        cuts.Add(at);
                    }
                }
            }

            decoded.Clear();
        }

        return cuts;
    }
}
