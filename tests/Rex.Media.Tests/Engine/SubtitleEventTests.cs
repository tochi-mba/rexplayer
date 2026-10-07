using System.Text;
using Rex.Media.Containers.Matroska;
using Rex.Media.Engine;
using Rex.Media.TestKit;
using Rex.Media.Tests.Containers;
using static Rex.Media.TestKit.EbmlWriter;
using Id = Rex.Media.Containers.Matroska.MatroskaId;

namespace Rex.Media.Tests.Engine;

/// <summary>Subtitles carried in the media: read with everything else and reported as cues.</summary>
public sealed class SubtitleEventTests
{
    private const int Audio = 1;
    private const int Text = 3;

    [Fact]
    public async Task CuesInTheMediaArriveAsEventsForTheirItemAndTrack()
    {
        var subtitles = Element(Id.TrackEntry, UInt(Id.TrackNumber, Text), UInt(Id.TrackType, 17), Text(Id.CodecId, "S_TEXT/UTF8"), Text(Id.Language, "eng"));
        var clip = MatroskaCraftedTests.Mkv(
            MatroskaCraftedTests.Tracks(MatroskaCraftedTests.PcmTrack(Audio), subtitles),
            MatroskaCraftedTests.Cluster(
                0,
                MatroskaCraftedTests.Simple(Audio, 0, true, Pcm.Int16(new float[800])),
                MatroskaCraftedTests.Simple(Text, 10, true, Encoding.UTF8.GetBytes("<i>First</i>")),
                MatroskaCraftedTests.Simple(Text, 60, true, Encoding.UTF8.GetBytes("Second"))));
        using var harness = new SessionHarness(demuxer: new MatroskaDemuxerFactory());

        await harness.Session.OpenAsync(SessionHarness.Source(clip, "clip.mkv"));
        await harness.FinishAsync();
        harness.WaitFor<EndedEvent>();

        var opened = harness.Events.OfType<MediaOpenedEvent>().Single().Info;
        var cues = harness.Events.OfType<SubtitleCueEvent>().ToList();
        Assert.Equal(["First", "Second"], cues.Select(e => e.Cue.Text));
        Assert.All(cues, e => Assert.Same(opened, e.Item));
        Assert.All(cues, e => Assert.Equal(Text, e.TrackId));
        Assert.Equal(TimeSpan.FromMilliseconds(10), cues[0].Cue.Start);
        Assert.True(cues[0].Cue.Lines[0].Runs[0].Italic);
    }
}
