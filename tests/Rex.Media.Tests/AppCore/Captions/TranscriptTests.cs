using Rex.Media.AppCore.Captions;
using Rex.Media.Subtitles;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore.Captions;

public sealed class TranscriptTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("rexplayer-transcript-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static TranscriptWord W(string text, double start, double end, float confidence = 0.9f) => new(text, S(start), S(end), confidence);

    private static readonly Transcript Speech = new("en", "base.en", [
        W("Lonely", 1, 1.4), W("at", 1.5, 1.6), W("the", 1.65, 1.8), W("top.", 1.9, 2.3),
        W("Why", 3.5, 3.7), W("do", 3.75, 3.9), W("creators", 3.95, 4.5), W("add", 4.6, 4.8), W("text", 4.85, 5.1),
        W("to", 5.15, 5.3), W("their", 5.35, 5.5), W("videos", 5.55, 6.0), W("every", 6.1, 6.4), W("day", 6.5, 6.9),
        W("and", 7.0, 7.1), W("night", 7.15, 7.5), W(" ", 7.6, 7.7),
    ]);

    [Fact]
    [Capability("SUB-19")]
    public void WordsBecomeCuesAtSentencesBreathsAndLimitsEachRunTimed()
    {
        var cues = Speech.ToCues();

        Assert.Equal(["Lonely at the top.", "Why do creators add text to their videos every day", "and night"], cues.Select(cue => cue.Text));
        // A sentence's end; the breath after it; the tenth word; and the last cue lingering a little.
        Assert.Equal((S(1), S(2.7)), (cues[0].Start, cues[0].End));
        Assert.Equal((S(3.5), S(7.0)), (cues[1].Start, cues[1].End));
        Assert.Equal((S(7.0), S(7.9)), (cues[2].Start, cues[2].End));
        Assert.Equal([S(0), S(0.5), S(0.65), S(0.9)], cues[0].Lines[0].Runs.Select(run => run.At!.Value));
        Assert.True(SubtitleWords.IsTimed(cues[0]));
        var track = Speech.ToTrack("Transcript");
        Assert.Equal(("Transcript", "en", 3), (track.Name, track.Language, track.Count));
        Assert.Empty(Transcript.Empty.ToCues());
        Assert.True(Transcript.Empty.IsEmpty);
        Assert.Null(new Transcript("", "", []).ToTrack("x").Language);
    }

    [Fact]
    [Capability("SUB-19")]
    public void ALongCueIsCutAtFourSecondsAndACueNeverEndsBeforeItStarts()
    {
        var slow = new Transcript("en", "m", [W("one", 0, 3), W("two", 3.1, 4.5), W("three", 4.6, 5), W("four", 5, 5)]);
        var cues = slow.ToCues();
        Assert.Equal(["one two", "three four"], cues.Select(cue => cue.Text));
        Assert.Equal(S(4.6), cues[0].End);
        var instant = new Transcript("en", "m", [W("blink", 2, 2)]);
        Assert.Equal(S(2.4), Assert.Single(instant.ToCues()).End);

        var overlapping = new Transcript("en", "m", [W("first.", 2, 2), W("second", 1, 1.2)]);
        Assert.Equal(S(2.1), overlapping.ToCues()[0].End);
    }

    [Fact]
    [Capability("SUB-19")]
    public void TheTranscriptWritesSubRipAndWebVttWithWordTimes()
    {
        var srt = Speech.ToSrt();
        Assert.StartsWith("1\n00:00:01,000 --> 00:00:02,700\nLonely at the top.\n\n2\n00:00:03,500 --> 00:00:07,000\n", srt, StringComparison.Ordinal);
        Assert.Equal(3, SubtitleFile.Parse(srt).Cues.Count);

        var vtt = Speech.ToWebVtt();
        Assert.StartsWith("WEBVTT\n\n00:00:01.000 --> 00:00:02.700\n<00:00:01.000>Lonely <00:00:01.500>at <00:00:01.650>the <00:00:01.900>top.\n\n", vtt, StringComparison.Ordinal);
        var read = SubtitleFile.Parse(vtt).Cues;
        Assert.Equal(3, read.Count);
        Assert.Equal([S(0), S(0.5), S(0.65), S(0.9)], read[0].Lines[0].Runs.Select(run => run.At!.Value));
    }

    [Fact]
    [Capability("SUB-19")]
    public void JsonRoundTripsAndBadJsonIsNull()
    {
        var json = Speech.ToJson();
        var back = Transcript.FromJson(json)!;
        Assert.Equal((Speech.Language, Speech.Model, Speech.Words.Count), (back.Language, back.Model, back.Words.Count));
        Assert.Equal(Speech.Words[6], back.Words[6]);
        Assert.Null(Transcript.FromJson("not json"));
        Assert.Null(Transcript.FromJson("{}"));
        Assert.Null(Transcript.FromJson("[]"));
        // Missing names are empty; an end before its start is its start.
        var sparse = Transcript.FromJson("""{"Words":[{"Start":2,"End":1}]}""")!;
        Assert.Equal(("", "", "", S(2), S(2)), (sparse.Language, sparse.Model, sparse.Words[0].Text, sparse.Words[0].Start, sparse.Words[0].End));
        Assert.Throws<ArgumentNullException>(() => Transcript.FromJson(null!));
    }

    [Fact]
    [Capability("SUB-19")]
    public void TheCacheKeepsATranscriptByFileAndModelAndForgetsIt()
    {
        var cache = new TranscriptCache(Path.Combine(_directory, "cache"));
        var media = Path.Combine(_directory, "film.mkv");
        File.WriteAllBytes(media, [1, 2, 3]);

        Assert.Null(cache.Load(media, "base.en"));

        cache.Save(media, Speech);
        Assert.Equal(Speech.Words.Count, cache.Load(media, "base.en")!.Words.Count);
        using (new FileStream(cache.PathFor(media, "base.en"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Null(cache.Load(media, "base.en"));
        }
        Assert.Null(cache.Load(media, "tiny.en"));
        Assert.EndsWith(".json", cache.PathFor(media, "base.en"), StringComparison.Ordinal);
        Assert.NotEqual(cache.PathFor(media, "base.en"), cache.PathFor(media, "tiny.en"));

        // The file changing makes its transcript stale.
        var before = cache.PathFor(media, "base.en");
        File.WriteAllBytes(media, [1, 2, 3, 4]);
        Assert.NotEqual(before, cache.PathFor(media, "base.en"));
        Assert.Null(cache.Load(media, "base.en"));

        // Damage is read as nothing; forgetting is quiet whether or not there was anything.
        File.WriteAllText(cache.PathFor(media, "base.en"), "{broken");
        Assert.Null(cache.Load(media, "base.en"));
        cache.Forget(media, "base.en");
        cache.Forget(media, "base.en");
        Assert.False(File.Exists(cache.PathFor(media, "base.en")));
        Assert.Equal(Path.Combine("data", "cache", "transcripts"), TranscriptCache.DefaultRoot("data"));
        Assert.Throws<ArgumentException>(() => new TranscriptCache(" "));
        Assert.Throws<ArgumentNullException>(() => cache.Save(media, null!));
        Assert.Throws<ArgumentNullException>(() => cache.PathFor(null!, "m"));
        Assert.Throws<ArgumentNullException>(() => cache.PathFor(media, null!));
        Assert.Throws<ArgumentNullException>(() => TranscriptCache.DefaultRoot(null!));
        // A path that is no file yet still has a place.
        Assert.EndsWith(".json", cache.PathFor(Path.Combine(_directory, "nowhere.mkv"), "m"), StringComparison.Ordinal);
    }

    [Fact]
    [Capability("SUB-19")]
    public void SubtitlesTakeTheTimesTheTranscriptHeardWhereTheirWordsMatch()
    {
        var subtitles = new SubtitleTrack("film.srt") { Language = "en" };
        subtitles.Add(new SubtitleCue(S(0.8), S(2.5), [new SubtitleLine([new SubtitleRun("Lonely at the "), new SubtitleRun("TOP!", Bold: true)])]));
        subtitles.Add(new SubtitleCue(S(3.4), S(7.2), [new SubtitleLine([new SubtitleRun("Why do creators add text to their")]), new SubtitleLine([new SubtitleRun("videos, every single day")])]));
        subtitles.Add(new SubtitleCue(S(20), S(22), [new SubtitleLine([new SubtitleRun("Nothing heard here")])]));

        var timed = TranscriptAlignment.Time(subtitles, Speech);

        Assert.Equal(("film.srt", "en", 3), (timed.Name, timed.Language, timed.Count));
        var first = timed.At(S(1))[0];
        Assert.Equal(["Lonely ", "at ", "the ", "TOP!"], first.Lines[0].Runs.Select(run => run.Text));
        Assert.Equal([S(0.2), S(0.7), S(0.85), S(1.1)], first.Lines[0].Runs.Select(run => run.At!.Value));
        Assert.True(first.Lines[0].Runs[3].Bold);
        var second = timed.At(S(4))[0];
        // "single" was never heard: it is left for the voice; "videos," matches "videos".
        var runs = second.Lines[0].Runs.Concat(second.Lines[1].Runs).ToList();
        Assert.Null(runs.Single(run => run.Text == "single ").At);
        Assert.Equal(2.15, runs.Single(run => run.Text == "videos, ").At!.Value.TotalSeconds, 3);
        Assert.Equal(3.1, runs.Single(run => run.Text == "day").At!.Value.TotalSeconds, 3);
        // A cue the transcript heard nothing near is untouched.
        var third = timed.At(S(21))[0];
        Assert.Equal(subtitles.At(S(21))[0], third);
        Assert.Throws<ArgumentNullException>(() => TranscriptAlignment.Time(null!, Speech));
        Assert.Throws<ArgumentNullException>(() => TranscriptAlignment.Time(subtitles, null!));
        Assert.Throws<ArgumentNullException>(() => TranscriptAlignment.Time(null!, Speech.Words));
        Assert.Throws<ArgumentNullException>(() => TranscriptAlignment.Time(first, null!));
        Assert.Throws<ArgumentNullException>(() => TranscriptAlignment.Key(null!));
        Assert.Equal("dont", TranscriptAlignment.Key("Don't!"));
    }

    [Fact]
    public void ARunThatIsOnlySpacesOrAlreadyTimedKeepsItsTime()
    {
        var cue = new SubtitleCue(S(1), S(2), [new SubtitleLine([new SubtitleRun("  "), new SubtitleRun("word", At: S(0.3))])]);
        var timed = TranscriptAlignment.Time(cue, [W("other", 1.2, 1.4)]);
        Assert.Equal(["  ", "word"], timed.Lines[0].Runs.Select(run => run.Text));
        Assert.Equal([null, S(0.3)], timed.Lines[0].Runs.Select(run => run.At));
    }
}
