using Rex.Media.AppCore.Commands;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// The queue as it plays: repeat in each of its modes, shuffle, the end of the queue, and a change
/// made after the next item was already lined up to follow without a gap.
/// </summary>
public sealed partial class AppWindowTests
{
    /// <summary>Every title the player shows from now until <paramref name="done"/>, in order, once each time it changes.</summary>
    private static List<string> TitlesUntil(AppProcess app, Func<List<string>, bool> done, string what)
    {
        var seen = new List<string>();
        Wait.For(() =>
        {
            var status = app.Status();
            if (status.Title is { Length: > 0 } title && (seen.Count == 0 || seen[^1] != title))
            {
                seen.Add(title);
            }

            return done(seen);
        }, what, () => "Seen: " + string.Join(", ", seen));
        return seen;
    }

    [Fact]
    [Capability("PB-12")]
    public void RepeatCyclesThroughItsModesAndTheQueueStopsAtItsEndWhenOff()
    {
        using var app = AppProcess.Start([Named("Terminator", 2), Named("Sungba", 2)]);
        Wait.For(() => app.Text("NowPlaying") == "Terminator", "the first song");

        // Off, the queue plays once and ends on its last song.
        var once = TitlesUntil(app, seen => seen.Contains("Sungba") && app.Status().State == "Ended", "the queue to end");
        Assert.Equal(["Terminator", "Sungba"], once);

        // All: the queue goes round again.
        app.Press("RepeatButton");
        Wait.For(() => app.Find("RepeatButton").Current.Name == "Repeat all", "Repeat all");
        app.Press("NextButton");
        TitlesUntil(app, seen => seen.Count >= 3 && seen[^1] == "Sungba" && seen.Contains("Terminator"), "the queue to go round");

        // One: the same song again and again.
        app.Press("RepeatButton");
        Wait.For(() => app.Find("RepeatButton").Current.Name == "Repeat one", "Repeat one");
        // A song already lined up when Repeat one was chosen may still begin: from its first
        // restart on, the same song plays again and again.
        string? repeated = null;
        var wrapped = 0;
        var last = app.Position;
        Wait.For(() =>
        {
            var status = app.Status();
            var position = status.Position ?? 0;
            if (position + 0.5 < last && ++wrapped == 1)
            {
                repeated = status.Title;
            }

            last = position;
            Assert.True(repeated is null || status.Title == repeated, $"Repeat one moved from {repeated} to {status.Title}.");
            return wrapped >= 3;
        }, "the song to start over again and again");

        app.Press("RepeatButton");
        Wait.For(() => app.Find("RepeatButton").Current.Name == "Repeat off", "Repeat off");
        Wait.For(() => app.SavedSettings.Repeat == Rex.Media.Settings.RepeatMode.Off, "Repeat off to be kept");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("PB-12")]
    public void ShufflePlaysEverySongOnceBeforeTheQueueEnds()
    {
        string[] names = ["Lonely At The Top", "Terminator", "Sungba", "Joha", "Peace Be Unto You"];
        using var app = AppProcess.Start([.. names.Select(name => Named(name, 1))]);
        Wait.For(() => app.Status().Title is { Length: > 0 }, "the first song");
        app.Press("ShuffleButton");
        Wait.For(() => app.LogText.Contains("Shuffle on", StringComparison.Ordinal), "shuffle to be on");

        var played = TitlesUntil(app, seen => seen.Count >= names.Length && app.Status().State == "Ended", "the shuffled queue to end");

        // Whatever the order, nothing is skipped and nothing plays twice.
        Assert.Equal(played.Distinct().Count(), played.Count);
        Assert.Subset(names.ToHashSet(), played.ToHashSet());
        app.Press("ShuffleButton");
        Wait.For(() => app.LogText.Contains("Shuffle off", StringComparison.Ordinal), "shuffle to be off");
        Assert.Equal(0, app.Close());
    }

    [Fact]
    [Capability("PB-14")]
    public void RepeatOneChosenAfterTheNextSongWasLinedUpKeepsTheTitleTrueToWhatPlays()
    {
        // Shorter than the queueing distance: the second song is lined up as soon as the first opens.
        using var app = AppProcess.Start([Named("Terminator", 3), Named("Sungba", 20)]);
        Wait.For(() => app.Text("NowPlaying") == "Terminator", "the first song");
        app.Run(CommandCatalog.CycleRepeat);
        app.Run(CommandCatalog.CycleRepeat);
        Wait.For(() => app.Find("RepeatButton").Current.Name == "Repeat one", "Repeat one");

        // Past the first song's end, whichever plays, the window names it and shows its length.
        Wait.For(() => app.LogText is var log && (log.Contains("Carried on without a gap", StringComparison.Ordinal) || log.Contains("Reached the end of", StringComparison.Ordinal)),
            "the first song to finish");
        Wait.For(() => app.Position > 1, "the next one to play a little");
        var after = app.Status();
        Assert.True(after.Title == "Terminator" ? after.Duration < 4 : after.Duration > 19,
            $"The window says {after.Title} of {after.Duration:0.0} s is playing.");
        Assert.Equal(0, app.Close());
    }
}
