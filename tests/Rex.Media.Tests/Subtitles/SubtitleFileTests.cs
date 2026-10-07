using Rex.Media.Subtitles;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Subtitles;

public sealed class SubtitleFileTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    [Capability("SUB-01")]
    public void SubRipBlocksBecomeCuesWithTheirStyling()
    {
        var document = SubtitleFile.Parse("""
            1
            00:00:01,000 --> 00:00:03,500
            Hello <i>there</i>,
            <b>friend</b>

            2
            00:00:04,000 --> 00:00:05,000 X1:10 X2:20
            <font color="#FF8000">Orange</font> and plain

            not a block

            3
            00:00:09,000 --> 00:00:08,000
            ends before it starts
            """);

        Assert.Equal(SubtitleFormat.SubRip, document.Format);
        Assert.Equal(2, document.Cues.Count);
        var first = document.Cues[0];
        Assert.Equal((S(1), S(3.5)), (first.Start, first.End));
        Assert.Equal("Hello there,\nfriend", first.Text);
        Assert.Equal([new SubtitleRun("Hello "), new SubtitleRun("there", Italic: true), new SubtitleRun(",")], first.Lines[0].Runs);
        Assert.Equal(new SubtitleRun("friend", Bold: true), Assert.Single(first.Lines[1].Runs));
        Assert.Equal([new SubtitleRun("Orange", Color: 0xFF8000), new SubtitleRun(" and plain")], document.Cues[1].Lines[0].Runs);
    }

    [Fact]
    [Capability("SUB-02")]
    public void WebVttCuesKeepTheirPlaceAndSkipNotesAndStyles()
    {
        var document = SubtitleFile.Parse("""
            WEBVTT - a test

            NOTE this is not shown

            STYLE
            ::cue { color: yellow }

            intro
            00:01.000 --> 00:02.000 line:0
            <v Asake>At the <c.loud>top</c></v>

            00:00:03.000 --> 00:00:04.000 line:50%
            Middle <00:00:03.500>timed

            00:05.000 --> 00:06.000 align:start line:90%
            Bottom

            00:07.000 --> 00:08.000 line:auto
            Unplaced
            """);

        Assert.Equal(SubtitleFormat.WebVtt, document.Format);
        Assert.Equal(["At the top", "Middle timed", "Bottom", "Unplaced"], document.Cues.Select(cue => cue.Text));
        Assert.Equal([SubtitlePlacement.Top, SubtitlePlacement.Middle, SubtitlePlacement.Bottom, SubtitlePlacement.Bottom], document.Cues.Select(cue => cue.Placement));
    }

    [Fact]
    [Capability("SUB-03")]
    public void AssDialoguesTakeTheirStylesAndOverrides()
    {
        var document = SubtitleFile.Parse("""
            [Script Info]
            Title: test

            [V4+ Styles]
            Format: Name, Fontname, Fontsize, PrimaryColour, Bold, Italic, Alignment
            Style: Default,Arial,20,&H00FFFFFF,0,0,2
            Style: Sign,Arial,20,&H000000FF,-1,0,8
            Style: Centre,Arial,20,&H00FFFFFF,0,0,5

            [Events]
            Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
            Dialogue: 0,0:00:01.00,0:00:02.50,Default,,0,0,0,,Line one\NLine two, with a comma
            Dialogue: 0,0:00:03.00,0:00:04.00,Sign,,0,0,0,,{\i1}Shop{\i0} sign
            Dialogue: 0,0:00:05.00,0:00:06.00,Default,,0,0,0,,{\an8\pos(10,10)}Moved{\b1} up
            Dialogue: 0,0:00:07.00,0:00:08.00,Default,,0,0,0,,{\p1}m 0 0 l 10 10{\p0}
            Dialogue: 0,0:00:08.00,0:00:09.00,Centre,,0,0,0,,In the middle
            Comment: 0,0:00:09.00,0:00:10.00,Default,,0,0,0,,not shown
            Dialogue: 0,bad,0:00:10.00,Default,,0,0,0,,no start
            """);

        Assert.Equal(SubtitleFormat.Ass, document.Format);
        Assert.Equal(["Line one\nLine two, with a comma", "Shop sign", "Moved up", "In the middle"], document.Cues.Select(cue => cue.Text));
        Assert.Equal(SubtitlePlacement.Middle, document.Cues[3].Placement);
        Assert.Equal([new SubtitleRun("Shop", Bold: true, Italic: true, Color: 0xFF0000), new SubtitleRun(" sign", Bold: true, Color: 0xFF0000)], document.Cues[1].Lines[0].Runs);
        Assert.Equal(SubtitlePlacement.Top, document.Cues[1].Placement);
        Assert.Equal(SubtitlePlacement.Top, document.Cues[2].Placement);
        Assert.Equal([new SubtitleRun("Moved"), new SubtitleRun(" up", Bold: true)], document.Cues[2].Lines[0].Runs);
    }

    [Fact]
    public void SsaAndScriptsWithoutFormatsStillRead()
    {
        var document = SubtitleFile.Parse("""
            [Script Info]
            ScriptType: v4.00

            [Events]
            Dialogue: Marked=0,0:00:01.00,0:00:02.00,Missing,,0,0,0,,Hello\hthere
            """);

        Assert.Equal("Hello\u00A0there", Assert.Single(document.Cues).Text);
        Assert.Equal(SubtitlePlacement.Bottom, document.Cues[0].Placement);
    }

    [Fact]
    [Capability("SUB-05")]
    public void MicroDvdFramesBecomeTimesAtTheStatedRate()
    {
        var document = SubtitleFile.Parse("""
            {1}{1}25
            {25}{50}{y:i}First|second
            {75}{}Open ended
            {100}{125}{Y:b,u}Strong
            """);

        Assert.Equal(SubtitleFormat.MicroDvd, document.Format);
        Assert.Equal((S(1), S(2)), (document.Cues[0].Start, document.Cues[0].End));
        Assert.Equal("First\nsecond", document.Cues[0].Text);
        Assert.True(document.Cues[0].Lines[0].Runs[0].Italic);
        Assert.Equal(S(6), document.Cues[1].End);
        Assert.Equal(new SubtitleRun("Strong", Bold: true, Underline: true), document.Cues[2].Lines[0].Runs[0]);
        Assert.Equal(S(1), SubtitleFile.Parse("{24}{48}No stated rate", 24).Cues[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(24 / 23.976), SubtitleFile.Parse("{24}{48}No rate at all", 0).Cues[0].Start);
    }

    [Fact]
    public void Mpl2SubViewerAndSamiRead()
    {
        var mpl2 = SubtitleFile.Parse("[10][25]/Slanted|plain\n[30][]Open ended");
        Assert.Equal(SubtitleFormat.Mpl2, mpl2.Format);
        Assert.Equal((S(1), S(2.5)), (mpl2.Cues[0].Start, mpl2.Cues[0].End));
        Assert.True(mpl2.Cues[0].Lines[0].Runs[0].Italic);
        Assert.Equal(S(6), mpl2.Cues[1].End);

        var subViewer = SubtitleFile.Parse("[INFORMATION]\n[TITLE]x\n\n00:00:01.00,00:00:02.50\nOne[br]Two\n\n00:00:03.00,00:00:04.00\n");
        Assert.Equal(SubtitleFormat.SubViewer, subViewer.Format);
        Assert.Equal("One\nTwo", Assert.Single(subViewer.Cues).Text);
        Assert.Equal(SubtitleFormat.SubViewer, SubtitleFile.Recognise("00:00:01.00,00:00:02.00\nhi"));

        var sami = SubtitleFile.Parse("""
            <SAMI><BODY>
            <SYNC Start=1000><P Class=ENCC>Hello<br>world &amp; all
            <SYNC Start=2500><P Class=ENCC>&nbsp;
            <SYNC Start="4000"><P Class=ENCC>Last
            </BODY></SAMI>
            """);
        Assert.Equal(SubtitleFormat.Sami, sami.Format);
        Assert.Equal(["Hello\nworld & all", "Last"], sami.Cues.Select(cue => cue.Text));
        Assert.Equal((S(1), S(2.5)), (sami.Cues[0].Start, sami.Cues[0].End));
        Assert.Equal(S(7), sami.Cues[1].End);
    }

    [Fact]
    public void TextThatIsNoSubtitleFileIsRefused()
    {
        Assert.Null(SubtitleFile.Recognise("just words"));
        Assert.Throws<FormatException>(() => SubtitleFile.Parse("just words"));
        Assert.Throws<ArgumentNullException>(() => SubtitleFile.Parse(null!));
        Assert.Throws<ArgumentNullException>(() => SubtitleFile.Recognise(null!));
        Assert.Equal(SubtitleFormat.SubRip, SubtitleFile.Parse("\uFEFF\n1\n00:00:01,000 --> 00:00:02,000\nx").Format);
    }

    [Theory]
    [InlineData("01:02:03,450", 3723.45)]
    [InlineData("2:03.4", 123.4)]
    [InlineData("00:01.000", 1.0)]
    public void TimesReadWithEitherSeparator(string text, double seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), SubtitleFile.Time(text));
    }

    [Theory]
    [InlineData("12")]
    [InlineData("a:b")]
    [InlineData("1:2:3:4")]
    [InlineData("1:xx")]
    public void TextThatIsNoTimeIsNone(string text)
    {
        Assert.Null(SubtitleFile.Time(text));
    }
}
