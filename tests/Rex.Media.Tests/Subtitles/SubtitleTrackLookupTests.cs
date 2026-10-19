using Rex.Media.Subtitles;

namespace Rex.Media.Tests.Subtitles;

public sealed class SubtitleTrackLookupTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static SubtitleCue Cue(double start, double end, string text) =>
        new(S(start), S(end), [new SubtitleLine([new SubtitleRun(text)])]);

    // The lookup as it was before the binary search: a scan from the start.
    private static SubtitleCue[] Scan(IReadOnlyList<SubtitleCue> cues, TimeSpan time) =>
        [.. cues.TakeWhile(cue => cue.Start <= time).Where(cue => cue.IsShownAt(time))];

    [Fact]
    public void FindsTheSameCuesAsAScanWithOverlapsSeeksBackAndChanges()
    {
        var random = new Random(7);
        var track = new SubtitleTrack("t");
        for (var i = 0; i < 300; i++)
        {
            var start = random.NextDouble() * 600;
            track.Add(Cue(start, start + (random.NextDouble() * (i % 10 == 0 ? 120 : 6)), $"c{i}"));
        }

        var position = 0.0;
        for (var step = 0; step < 4000; step++)
        {
            position = step % 97 == 0 ? random.NextDouble() * 650 : Math.Max(0, position + ((random.NextDouble() - 0.2) * 3));
            if (step % 500 == 0)
            {
                track.Replace(S(position), S(position + 20), [Cue(position + 1, position + 2, $"r{step}"), Cue(position, position + 40, $"long{step}")]);
            }

            var delay = S(step % 3 - 1);
            Assert.Equal(Scan(track.All(), S(position) - delay), track.At(S(position), delay));
        }
    }

    [Fact]
    public void ALongCueStaysShownBesideLaterShortOnes()
    {
        var track = new SubtitleTrack("t");
        track.AddRange([Cue(0, 100, "long"), Cue(10, 11, "a"), Cue(20, 21, "b")]);

        Assert.Equal(["long", "b"], track.At(S(20.5)).Select(c => c.Text));
        Assert.Equal(["long"], track.At(S(15)).Select(c => c.Text));
        Assert.Equal(["long", "a"], track.At(S(10)).Select(c => c.Text));
        Assert.Empty(track.At(S(100)));
        Assert.Empty(new SubtitleTrack("e").At(S(1)));
    }

    [Fact]
    public void PlayingThroughACueAllocatesNothingPerFrame()
    {
        var track = new SubtitleTrack("t");
        for (var i = 0; i < 2000; i++)
        {
            track.Add(Cue(i * 3, (i * 3) + 2.5, $"c{i}"));
        }

        var first = track.At(S(3000.1));
        var before = GC.GetAllocatedBytesForCurrentThread();
        IReadOnlyList<SubtitleCue> again = first;
        for (var i = 0; i < 60; i++)
        {
            again = track.At(S(3000.1 + (i / 60.0)));
            _ = track.At(S(3002.6 + (i / 600.0)));
        }

        var perFrame = (GC.GetAllocatedBytesForCurrentThread() - before) / 60;
        Assert.Same(first, again);

        // Before, a scan from the start allocated an enumerator chain and a list each call (441 bytes
        // a frame here, and it walked a thousand cues to get there).
        Assert.Equal(0, perFrame);
    }
}
