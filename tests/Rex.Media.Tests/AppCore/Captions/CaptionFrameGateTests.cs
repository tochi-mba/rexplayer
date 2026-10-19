using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Player;
using Rex.Media.Subtitles;

namespace Rex.Media.Tests.AppCore.Captions;

/// <summary>
/// Paused kinetic captions are drawn once and then left standing (OSD-11): every movement is worked
/// out from the position, so a frame at a position that does not move is the frame on screen.
/// </summary>
public sealed class CaptionFrameGateTests
{
    private static readonly Area Area = new(0, 0, 1280, 720);
    private static readonly TimeSpan Refresh = TimeSpan.FromSeconds(1 / 60.0);

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static SubtitleCue Cue(double start, double end, string text) => new(S(start), S(end), [new SubtitleLine([new SubtitleRun(text)])]);

    [Fact]
    public void A_paused_frame_is_the_frame_already_drawn()
    {
        // What the gate relies on: the director, asked again and again at a position that does not
        // move, lays the words out exactly as it did, refresh after refresh.
        var cue = Cue(1, 4, "Every word lands on its syllable");
        var director = new CaptionDirector(CaptionStyle.Default);
        for (var at = 0.0; at < 2.5; at += Refresh.TotalSeconds)
        {
            director.Frame(S(at), [], 0, Refresh, cue.IsShownAt(S(at)) ? [cue] : [], Area, 40);
        }

        var paused = director.Frame(S(2.5), [], 0, Refresh, [cue], Area, 40);
        Assert.NotEmpty(paused);
        var before = GC.GetAllocatedBytesForCurrentThread();
        IReadOnlyList<CaptionWord> again = paused;
        for (var i = 0; i < 60; i++)
        {
            again = director.Frame(S(2.5), [], 0, Refresh, [cue], Area, 40);
        }

        var perFrame = (GC.GetAllocatedBytesForCurrentThread() - before) / 60;
        Assert.Equal(paused, again);

        // Before the gate, each of those refreshes (sixty a second while paused) was worked out and
        // laid out again; it allocated this much on the window's thread each time, beside the XAML work.
        Assert.True(perFrame > 0, $"A paused frame allocated {perFrame} bytes");
    }

    [Fact]
    public void A_paused_minute_draws_one_frame()
    {
        var gate = new CaptionFrameGate();
        IReadOnlyList<SubtitleCue> cues = [Cue(1, 4, "held")];
        var drawn = 0;
        for (var i = 0; i < 3600; i++)
        {
            drawn += gate.Draws(playing: false, S(2.5), Area, 40, cues, 20) ? 1 : 0;
        }

        Assert.Equal(1, drawn);
    }

    [Fact]
    public void Playing_draws_every_frame()
    {
        var gate = new CaptionFrameGate();
        IReadOnlyList<SubtitleCue> cues = [];
        var drawn = 0;
        for (var i = 0; i < 600; i++)
        {
            drawn += gate.Draws(playing: true, S(2.5), Area, 40, cues, 0) ? 1 : 0;
        }

        Assert.Equal(600, drawn);
    }

    [Fact]
    public void Anything_a_paused_frame_is_drawn_from_draws_it_again()
    {
        var gate = new CaptionFrameGate();
        var cue = Cue(1, 4, "held");
        IReadOnlyList<SubtitleCue> cues = [cue];
        Assert.True(gate.Draws(false, S(2.5), Area, 40, cues, 20));
        Assert.False(gate.Draws(false, S(2.5), Area, 40, cues, 20));

        // A seek while paused, a resize, another font size, another cue, a cue more, the picture read further.
        Assert.True(gate.Draws(false, S(3), Area, 40, cues, 20));
        Assert.True(gate.Draws(false, S(3), Area with { Width = 640 }, 40, cues, 20));
        Assert.True(gate.Draws(false, S(3), Area with { Width = 640 }, 48, cues, 20));
        IReadOnlyList<SubtitleCue> other = [Cue(1, 4, "held")];
        Assert.True(gate.Draws(false, S(3), Area with { Width = 640 }, 48, other, 20));
        IReadOnlyList<SubtitleCue> more = [other[0], Cue(2, 5, "and more")];
        Assert.True(gate.Draws(false, S(3), Area with { Width = 640 }, 48, more, 20));
        IReadOnlyList<SubtitleCue> none = [];
        Assert.True(gate.Draws(false, S(3), Area with { Width = 640 }, 48, none, 20));
        Assert.True(gate.Draws(false, S(3), Area with { Width = 640 }, 48, none, 21));
        Assert.False(gate.Draws(false, S(3), Area with { Width = 640 }, 48, none, 21));

        // Cleared or restyled: drawn again.
        gate.Reset();
        Assert.True(gate.Draws(false, S(3), Area with { Width = 640 }, 48, none, 21));
        Assert.Throws<ArgumentNullException>(() => gate.Draws(false, S(3), Area, 40, null!, 0));
    }
}
