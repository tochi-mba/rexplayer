using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Player;
using Rex.Media.Settings;
using Rex.Media.Subtitles;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore.Captions;

public sealed class CaptionDirectorTests
{
    private const int Rate = 48000;
    private static readonly Area Area = new(0, 0, 1280, 720);
    private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1 / 30.0);

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static SubtitleCue Cue(double start, double end, string text) => new(S(start), S(end), [new SubtitleLine([new SubtitleRun(text)])]);

    /// <summary>Plays <paramref name="cues"/> from <paramref name="from"/> to <paramref name="to"/> seconds without sound, a frame at a time; the words shown at each frame.</summary>
    private static List<(double At, IReadOnlyList<CaptionWord> Words)> Play(CaptionDirector director, SubtitleCue[] cues, double from, double to, PictureTimeline? pictures = null)
    {
        var frames = new List<(double, IReadOnlyList<CaptionWord>)>();
        for (var at = from; at <= to; at += Frame.TotalSeconds)
        {
            var showing = cues.Where(cue => cue.IsShownAt(S(at))).ToList();
            frames.Add((at, director.Frame(S(at), [], 0, Frame, showing, Area, 40, pictures)));
        }

        return frames;
    }

    [Fact]
    [Capability("OSD-11")]
    public void WordsComeInOneAtATimeThroughTheCueAndClearAfterASentence()
    {
        var director = new CaptionDirector(CaptionStyle.Default);
        var cues = new[] { Cue(1, 3, "Why do many creators."), Cue(3.1, 5, "So adding text") };
        var frames = Play(director, cues, 0, 6);

        int Count(double at) => frames.First(frame => frame.At >= at).Words.Count;
        Assert.Equal(0, Count(0.5));
        // Without sound, each word comes in half way through its share of the cue.
        Assert.Equal(1, Count(1.3));
        Assert.Equal(3, Count(2.3));
        Assert.Equal(4, Count(2.95));
        // A cue that ends a sentence clears its words before the next begins.
        var nextStarted = frames.First(frame => frame.At >= 3.6).Words;
        Assert.DoesNotContain(nextStarted, word => word.Text == "creators.");
        Assert.Contains(nextStarted, word => word.Text == "So");
        // After the last cue, its words fade away.
        Assert.Empty(frames[^1].Words);
        Assert.Equal((3, 3), director.Progress);
    }

    [Fact]
    [Capability("OSD-11")]
    public void CuesThatRunOnKeepOneClusterAndASeekStartsAfresh()
    {
        var director = new CaptionDirector(CaptionStyle.Default);
        var cues = new[] { Cue(0, 1, "a lot of"), Cue(1.1, 2, "people watch") };
        var frames = Play(director, cues, 0, 1.95);
        Assert.Equal(["a", "lot", "of", "people", "watch"], frames[^1].Words.Select(word => word.Text));

        // Back to the start: the words shown are the first cue's again.
        var after = director.Frame(S(0.2), [], 0, Frame, [cues[0]], Area, 40);
        Assert.Single(after);
    }

    [Fact]
    [Capability("OSD-11")]
    public void TheVoiceBringsWordsInOnItsSyllablesAndLoudWordsShout()
    {
        var director = new CaptionDirector(CaptionStyle.Default);
        var cue = Cue(0, 4, "one two three four five six seven eight nine BIG");
        var mono = new float[SpeechPulse.Size];
        var revealedAt = new List<double>();
        var shown = 0;
        IReadOnlyList<CaptionWord> words = [];
        var frame = 0;
        for (var at = 0.0; at < 4; at += SpeechPulse.Size / (double)Rate, frame++)
        {
            // A syllable every seven frames, the last one far louder.
            var syllable = frame % 7 < 4;
            var amplitude = at > 3.3 ? 0.9 : 0.15;
            for (var i = 0; i < mono.Length; i++)
            {
                mono[i] = syllable ? (float)(amplitude * Math.Sin(2 * Math.PI * 120 * ((frame * mono.Length) + i) / Rate)) : 0;
            }

            words = director.Frame(S(at), mono, Rate, TimeSpan.FromSeconds(SpeechPulse.Size / (double)Rate), [cue], Area, 40);
            if (words.Count > shown)
            {
                revealedAt.Add(at);
                shown = words.Count;
            }
        }

        Assert.True(director.Voice.Onsets >= 8, $"{director.Voice.Onsets}");
        Assert.Equal(10, words.Count);
        var big = words.Single(word => word.Text == "BIG");
        Assert.True(big.Stress is Stress.Shout or Stress.Peak, $"{big.Stress}");
        Assert.True(big.Size > 1.5);
    }

    [Fact]
    [Capability("OSD-11")]
    public void ThePictureReadAheadKeepsTheWordsOffAFace()
    {
        var face = PictureSpace.Read(
            PictureSpaceTests.Picture((x, y) => y < 120 ? ((byte)224, (byte)172, (byte)140) : ((byte)30, (byte)30, (byte)30)),
            320,
            180,
            1280);
        var pictures = new PictureTimeline();
        pictures.Add(0, face);
        var director = new CaptionDirector(CaptionStyle.Default);
        var frames = Play(director, [Cue(0, 2, "keep clear of me")], 0, 1.9, pictures);
        Assert.All(frames[^1].Words, word => Assert.True(word.Y > Area.Height * 0.66, $"{word.Text} at {word.Y}"));
    }

    [Fact]
    public void RestylingStartsAfreshAndTheSettingsGiveTheStyle()
    {
        var director = new CaptionDirector(CaptionStyle.Default);
        Play(director, [Cue(0, 1, "hello there")], 0, 0.9);
        var settings = new PlayerSettings
        {
            SubtitleColor = 0x00FF00,
            KineticCaptions = new KineticCaptionSettings { Preset = CaptionPreset.Custom, Accent = 0x0000FF, Colors = CaptionColors.Neon, Energy = 150, Layout = CaptionLayoutChoice.Centre, Glyphs = false, ShoutsInCapitals = false, AvoidPicture = false },
        };
        var style = CaptionDirector.StyleFor(settings);
        Assert.Equal(new CaptionStyle(new Argb(255, 0, 255, 0), new Argb(255, 0, 0, 255), VisualPalette.Neon, 1.5, CaptionLayout.Centre, false, false, false), style);
        director.Restyle(style);
        Assert.Same(style, director.Style);
        Assert.Empty(director.Frame(S(0.95), [], 0, Frame, [], Area, 40));

        Assert.Equal(
            [VisualPalette.Warm, VisualPalette.Fire, VisualPalette.Neon, VisualPalette.Sunset, VisualPalette.Ocean, VisualPalette.Rainbow, VisualPalette.Accent],
            Enum.GetValues<CaptionColors>().Select(colors => CaptionDirector.StyleFor(new PlayerSettings { KineticCaptions = new KineticCaptionSettings { Preset = CaptionPreset.Custom, Colors = colors } }).Palette));
        Assert.Equal(CaptionLayout.Stack, CaptionDirector.StyleFor(new PlayerSettings { KineticCaptions = new KineticCaptionSettings { Preset = CaptionPreset.Custom, Layout = CaptionLayoutChoice.Stack } }).Layout);
        Assert.Throws<ArgumentNullException>(() => CaptionDirector.StyleFor(null!));
        Assert.Throws<ArgumentNullException>(() => director.Restyle(null!));
        Assert.Throws<ArgumentNullException>(() => new CaptionDirector(null!));
        Assert.Throws<ArgumentNullException>(() => director.Frame(S(1), [], 0, Frame, null!, Area, 40));
    }

    [Fact]
    [Capability("OSD-11")]
    public void AnUnfilledLineShowsWholeAndLightsWordByWord()
    {
        var director = new CaptionDirector(CaptionPresets.Style(CaptionPreset.Classic, CaptionStyle.Default));
        var frames = Play(director, [Cue(0, 2, "lonely at the top")], 0, 1.9);
        var start = frames.First(frame => frame.At >= 0.05).Words;
        Assert.Equal(4, start.Count);
        Assert.All(start, word => Assert.False(word.Lit));
        var half = frames.First(frame => frame.At >= 1.0).Words;
        Assert.Equal(4, half.Count);
        Assert.True(half.Count(word => word.Lit) is >= 1 and <= 3, $"{half.Count(word => word.Lit)}");
        Assert.All(frames[^1].Words, word => Assert.True(word.Lit));
    }

    [Fact]
    public void ACueGapOrAQuietCueAloneStillReads()
    {
        var director = new CaptionDirector(CaptionStyle.Default);
        // A cue after a long gap starts its own cluster; one with quoted closing punctuation ends a sentence.
        var cues = new[] { Cue(0, 1, "\"Hi!\""), Cue(3, 4, "there") };
        var frames = Play(director, cues, 0, 3.9);
        Assert.Equal(["there"], frames[^1].Words.Select(word => word.Text));
    }
}
