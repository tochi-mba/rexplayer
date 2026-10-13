using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Transcription;
using Rex.Media.Subtitles;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore.Transcription;

public sealed class ProgressiveTranscriptTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static TimeRange R(double start, double end) => new(S(start), S(end));

    private static TranscriptWord W(string text, double start, double end) => new(text, S(start), S(end), 0.9f);

    /// <summary>Speech for a file of <paramref name="seconds"/>: words, sentences and breaths, the same every run.</summary>
    internal static List<TranscriptWord> Speech(double seconds, int seed = 7)
    {
        var random = new Random(seed);
        var words = new List<TranscriptWord>();
        var at = 0.5;
        var n = 0;
        while (at < seconds - 1)
        {
            var length = 0.15 + (random.NextDouble() * 0.3);
            var text = "word" + n++ + (random.Next(9) == 0 ? "." : "");
            words.Add(W(text, Math.Round(at, 3), Math.Round(at + length, 3)));
            at += length + (random.Next(12) == 0 ? 1.2 : 0.05 + (random.NextDouble() * 0.1));
        }

        return words;
    }

    /// <summary>What a model would hear of <paramref name="speech"/> in a piece: the words in what it hears, timed from its start.</summary>
    internal static List<TranscriptWord> Hear(IReadOnlyList<TranscriptWord> speech, SpeechChunk chunk) =>
        [.. speech.Where(word => chunk.Heard.Contains(word.Start)).Select(word => word with { Start = word.Start - chunk.Heard.Start, End = word.End - chunk.Heard.Start })];

    internal static string Describe(SubtitleCue cue) =>
        $"{cue.Start.TotalMilliseconds}-{cue.End.TotalMilliseconds} {cue.Text} [{string.Join(",", cue.Lines[0].Runs.Select(run => run.At?.TotalMilliseconds))}]";

    [Fact]
    [Capability("SUB-21")]
    public void PiecesHeardInAnyOrderChangeOnlyTheCuesAroundThemAndEndAsTheWholeWould()
    {
        var speech = Speech(300);
        var transcript = new ProgressiveTranscript("base.en", "en", S(300));
        var track = new SubtitleTrack("Transcript");
        var playhead = S(140);
        var pieces = 0;
        while (ChunkPlanner.Next(transcript.Done, transcript.Duration, playhead) is { } chunk)
        {
            var change = transcript.Add(chunk, Hear(speech, chunk), "en");
            track.Replace(change.Replaced.Start, change.Replaced.End, change.Cues);

            // After every piece the track is exactly what building it whole would give.
            Assert.Equal(transcript.ToTranscript().ToCues().Select(Describe), track.All().Select(Describe));
            pieces++;
            if (pieces == 3)
            {
                // A seek back.
                playhead = S(37);
            }
        }

        Assert.True(transcript.IsComplete);
        Assert.Equal(1, transcript.Share);
        Assert.Equal(speech.Select(word => word.Text), transcript.ToTranscript().Words.Select(word => word.Text));
        Assert.Equal(new Transcript("en", "base.en", speech).ToCues().Select(Describe), track.All().Select(Describe));
        Assert.Equal(speech.Count, transcript.WordCount);
    }

    [Fact]
    [Capability("SUB-21")]
    public void APieceHeardAgainReplacesItsWordsAndKeepsOnlyWhatStartsInItsStretch()
    {
        var transcript = new ProgressiveTranscript("tiny.en");
        Assert.Equal(("tiny.en", "", null), (transcript.Model, transcript.Language, transcript.Duration));
        Assert.False(transcript.IsComplete);
        Assert.Equal(0, transcript.Share);
        var chunk = new SpeechChunk(R(10, 36), R(8, 38));

        // Times are from the start of what was heard; words outside what is kept, and blanks, go.
        var change = transcript.Add(chunk, [W("early", 1, 1.5), W("hello", 2.5, 3), W(" ", 4, 4.1), W("there.", 3.2, 3.6), W("late", 28.5, 29)], null);
        Assert.Equal(["hello", "there."], transcript.ToTranscript().Words.Select(word => word.Text));
        Assert.Equal(S(10.5), transcript.ToTranscript().Words[0].Start);
        Assert.Equal(TimeSpan.Zero, change.Replaced.Start);
        Assert.Equal(TimeSpan.MaxValue, change.Replaced.End);
        Assert.Equal(["hello there."], change.Cues.Select(cue => cue.Text));
        Assert.Equal("", transcript.Language);

        transcript.Add(chunk, [W("goodbye", 3, 3.5)], "en");
        Assert.Equal(["goodbye"], transcript.ToTranscript().Words.Select(word => word.Text));
        Assert.Equal("en", transcript.Language);

        // A file's length known later; complete once every stretch is heard.
        transcript.Duration = S(36);
        Assert.Equal(26.0 / 36, transcript.Share, 6);
        transcript.Add(new SpeechChunk(R(0, 10), R(0, 12)), [W("first.", 1, 1.4)], "");
        Assert.True(transcript.IsComplete);
        Assert.Equal("en", transcript.Language);
        Assert.Throws<ArgumentNullException>(() => transcript.Add(chunk, null!, null));
        Assert.Throws<ArgumentNullException>(() => new ProgressiveTranscript(null!));
    }

    [Fact]
    [Capability("SUB-21")]
    public void ThePartHeardIsKeptAsJsonAndReadBack()
    {
        var transcript = new ProgressiveTranscript("base.en", "en", S(90));
        transcript.Add(new SpeechChunk(R(30, 56), R(28, 58)), [W("middle", 3, 3.4), W("words.", 3.5, 3.9)], "en");
        transcript.Add(new SpeechChunk(R(0, 26), R(0, 28)), [W("start", 1, 1.2)], "en");

        var back = ProgressiveTranscript.FromJson(transcript.ToJson())!;
        Assert.Equal(("base.en", "en", (TimeSpan?)S(90)), (back.Model, back.Language, back.Duration));
        Assert.Equal(transcript.Done.Ranges, back.Done.Ranges);
        Assert.Equal(["start", "middle", "words."], back.ToTranscript().Words.Select(word => word.Text));
        Assert.Equal(S(31), back.ToTranscript().Words[1].Start);

        // A length never known, a word ending before it starts, blank words and odd pairs read sensibly.
        var odd = ProgressiveTranscript.FromJson("""{"Language":null,"Model":"m","Duration":null,"Done":[[1,2],[3]],"Words":[{"Text":"b","Start":5,"End":4,"Confidence":1},{"Text":" ","Start":1,"End":2,"Confidence":1},{"Text":null,"Start":1,"End":2,"Confidence":1},{"Text":"a","Start":2,"End":3,"Confidence":1}]}""")!;
        Assert.Null(odd.Duration);
        Assert.Equal("", odd.Language);
        Assert.Equal([R(1, 2)], odd.Done.Ranges);
        Assert.Equal(["a", "b"], odd.ToTranscript().Words.Select(word => word.Text));
        Assert.Equal(S(5), odd.ToTranscript().Words[1].End);

        // Anything else is not a transcript.
        Assert.Null(ProgressiveTranscript.FromJson("{broken"));
        Assert.Null(ProgressiveTranscript.FromJson("null"));
        Assert.Null(ProgressiveTranscript.FromJson("""{"Model":"m","Words":[]}"""));
        Assert.Null(ProgressiveTranscript.FromJson("""{"Model":"m","Done":[]}"""));
        Assert.Null(ProgressiveTranscript.FromJson("""{"Done":[],"Words":[]}"""));
        Assert.Throws<ArgumentNullException>(() => ProgressiveTranscript.FromJson(null!));
    }

    [Fact]
    [Capability("SUB-21")]
    public void ATracksCuesAreReplacedInAStretchFoundByBinarySearch()
    {
        SubtitleCue Cue(double start, string text) => new(S(start), S(start + 1), [new SubtitleLine([new SubtitleRun(text)])]);
        var track = new SubtitleTrack("t");
        track.AddRange([Cue(1, "a"), Cue(2, "b"), Cue(3, "c"), Cue(4, "d")]);
        track.Replace(S(2), S(4), [Cue(2.5, "x")]);
        Assert.Equal(["a", "x", "d"], track.All().Select(cue => cue.Text));

        // A replaced cue may come back with the same words and start.
        track.Replace(S(0), S(10), [Cue(2.5, "x"), Cue(1, "a")]);
        Assert.Equal(["a", "x"], track.All().Select(cue => cue.Text));
        track.Replace(S(5), S(1), []);
        Assert.Equal(2, track.Count);
        Assert.Throws<ArgumentNullException>(() => track.Replace(S(0), S(1), null!));
    }

    [Fact]
    [Capability("SUB-21")]
    public void ACueBeginsAfterASentencesEndOrABreathWhateverCameBefore()
    {
        List<TranscriptWord> words = [W("one", 0, 0.2), W("two.", 0.3, 0.5), W("three", 0.6, 0.8), W("four", 2, 2.2)];
        Assert.True(Transcript.StartsCue(words, 0));
        Assert.False(Transcript.StartsCue(words, 1));
        Assert.True(Transcript.StartsCue(words, 2));
        Assert.True(Transcript.StartsCue(words, 3));
        Assert.True(Transcript.StartsCue(words, 4));
        Assert.Equal(["three"], Transcript.CuesOf(words, 2, 3).Select(cue => cue.Text));

        // The last cue of a stretch ends by the next word's start.
        Assert.Equal(S(1.2), Transcript.CuesOf(words, 2, 3)[0].End);
        Assert.Equal(S(2.6), Transcript.CuesOf(words, 3, 4)[0].End);
        Assert.Throws<ArgumentNullException>(() => Transcript.CuesOf(null!, 0, 0));
        Assert.Throws<ArgumentNullException>(() => Transcript.StartsCue(null!, 0));
    }
}
