using System.IO;
using Rex.Media.Containers.Mp4;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.Settings;
using Rex.Media.TestKit;
using static Rex.Media.TestKit.Mp4Writer;

namespace Rex.Media.App.Tests;

/// <summary>
/// An episode's intro: marked from the menu, drawn on the timeline, skipped with the button,
/// skipped by itself when Preferences say so, and left alone when the countdown is cancelled.
/// </summary>
public sealed partial class AppWindowTests
{
    /// <summary>
    /// A film of <paramref name="seconds"/> seconds, named <paramref name="name"/>: the test pattern's
    /// pictures from tests/fixtures, played again and again. Every repeat starts at a key picture, so
    /// it decodes exactly and seeks anywhere.
    /// </summary>
    private string LongFilm(string name, int seconds)
    {
        using var demuxer = new Mp4Demuxer(new FileByteSource(RepoPaths.Combine("tests", "fixtures", "mp4", "h264-aac.mp4")), CancellationToken.None);
        var video = demuxer.Info.FirstTrack(MediaKind.Video)!;
        var pictures = new List<(byte[] Data, double Dts, double Pts, bool Key)>();
        while (demuxer.ReadPacket(CancellationToken.None) is { } packet)
        {
            using (packet)
            {
                if (packet.TrackId == video.Id)
                {
                    pictures.Add((packet.Data.Span.ToArray(), packet.Dts.TotalSeconds, packet.Pts.TotalSeconds, packet.IsKeyframe));
                }
            }
        }

        const int Rate = 25;
        var repeats = (int)Math.Ceiling(seconds * (double)Rate / pictures.Count);
        var all = Enumerable.Repeat(pictures, repeats).SelectMany(run => run).ToList();
        var film = Mp4Writer.File([new Mp4TrackSpec
        {
            Handler = "vide",
            Timescale = Rate,
            Duration = 1,
            SampleEntry = VideoEntry("avc1", video.Video!.Width, video.Video.Height, Box("avcC", video.CodecPrivate)),
            Samples = [.. all.Select(picture => picture.Data)],
            CompositionOffsets = [.. all.Select(picture => (int)Math.Round((picture.Pts - picture.Dts) * Rate))],
            SignedCompositionOffsets = true,
            SyncSamples = [.. all.Select((picture, index) => (picture, index)).Where(p => p.picture.Key).Select(p => p.index + 1)],
            SamplesPerChunk = Rate,
        }]);
        var path = Path.Combine(_media, name + ".mp4");
        System.IO.File.WriteAllBytes(path, film);
        return path;
    }

    private static int Count(string text, string part)
    {
        var count = 0;
        for (var at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    [Fact]
    public void AnIntroMarkedFromTheMenuIsSkippedByHandThenByItselfUnlessCancelled()
    {
        var film = LongFilm("Asake Live S01E01", 60);
        var root = RootWith(new PlayerSettings());
        using (var app = AppProcess.Start([film], root))
        {
            Wait.For(() => app.Status().Duration > 50, "the episode to open");
            app.Press("PlayPauseButton");
            Wait.For(() => app.Status().State == "Paused", "the pause");

            // Marked from the Video menu, start and end at the paused position.
            foreach (var (at, mark, said) in new[] { (2, "VideoIntroStart", "Intro start marked at 0:02"), (20, "VideoIntroEnd", "Intro end marked at 0:20") })
            {
                app.SetValue("SeekBar", at);
                WaitForPosition(app, at, "the position for " + mark);
                app.OpenMenu(mark, "VideoMenu", "VideoEpisodeSections");
                app.Press(mark);
                Wait.For(() => app.LogText.Contains(said, StringComparison.Ordinal), said);
            }

            Wait.For(() => app.IsShown("SectionMarker-Intro"), "the intro on the timeline");

            // Inside the intro the button offers the skip, and takes it.
            app.SetValue("SeekBar", 5);
            Wait.For(() => app.IsShown("EpisodeSkipButton"), "the skip button inside the intro");
            Assert.False(app.IsShown("EpisodeSkipCancel"), "Without automatic skipping there is no countdown to cancel.");
            app.Press("EpisodeSkipButton");
            Wait.For(() => app.Position >= 19.5 && app.LogText.Contains("Intro skipped.", StringComparison.Ordinal), "the intro to be skipped");
            Wait.For(() => !app.IsShown("EpisodeSkipButton"), "the button to go after the intro");
            Assert.Equal(0, app.Close());
        }

        SettingsStore.Save(Path.Combine(root, "settings.json"), SettingsStore.Load(Path.Combine(root, "settings.json")) with { SkipIntrosAutomatically = true });
        using var again = AppProcess.Start([film], root);
        Wait.For(() => again.Status().Duration > 50, "the episode to open again");

        // With automatic skipping, the intro goes by itself after its countdown.
        var skipped = Count(again.LogText, "Intro skipped.");
        again.SetValue("SeekBar", 3);
        Wait.For(() => again.IsShown("EpisodeSkipCancel"), "the countdown to show");
        Wait.For(() => again.Position >= 19.5 && Count(again.LogText, "Intro skipped.") == skipped + 1, "the intro to skip by itself");
        Assert.Equal(0, again.Close());

        // An intro skipped once is not counted down again in that session; a fresh one shows that
        // a cancelled countdown leaves the intro playing.
        using var third = AppProcess.Start([film], root);
        Wait.For(() => third.Status().Duration > 50, "the episode to open a third time");
        skipped = Count(third.LogText, "Intro skipped.");
        third.SetValue("SeekBar", 3);
        Wait.For(() => third.IsShown("EpisodeSkipCancel"), "the countdown to show again");
        third.Press("EpisodeSkipCancel");
        Wait.For(() => !third.IsShown("EpisodeSkipCancel"), "the countdown to stop");
        PlayOn(third, 8, "the intro to play on past where the countdown would have skipped");
        Assert.InRange(third.Position, 10, 19.4);
        Assert.Equal(skipped, Count(third.LogText, "Intro skipped."));
        Assert.Equal(0, third.Close());
    }
}
