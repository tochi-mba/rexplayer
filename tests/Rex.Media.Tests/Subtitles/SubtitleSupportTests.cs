using System.Text;
using Rex.Media.Primitives;
using Rex.Media.Subtitles;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Subtitles;

public sealed class SubtitleSupportTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void MarkupKeepsStylesNestsColoursAndShowsWhatIsNoTag()
    {
        var (lines, placement) = SubtitleMarkup.Parse("<b>a <font color=red>b <font color=\"#00ff00\">c</font> d</font> e</b> 1 < 2 <x> {\\c&H0000FF&}f{\\r}g{\\fad(1,2)}h\r\n\n<i></i>");

        Assert.Null(placement);
        var line = Assert.Single(lines);
        Assert.Equal("a b c d e 1 < 2 <x> fgh", line.Text);
        Assert.Equal(
        [
            new SubtitleRun("a ", Bold: true),
            new SubtitleRun("b ", Bold: true, Color: 0xFF0000),
            new SubtitleRun("c", Bold: true, Color: 0x00FF00),
            new SubtitleRun(" d", Bold: true, Color: 0xFF0000),
            new SubtitleRun(" e", Bold: true),
            new SubtitleRun(" 1 < 2 <x> "),
            new SubtitleRun("f", Color: 0xFF0000),
            new SubtitleRun("gh"),
        ],
            line.Runs);
    }

    [Theory]
    [InlineData("{\\an1}x", SubtitlePlacement.Bottom)]
    [InlineData("{\\an5}x", SubtitlePlacement.Middle)]
    [InlineData("{\\an9}x", SubtitlePlacement.Top)]
    [InlineData("{\\anx}x", null)]
    public void AssAlignmentTagsPlaceTheCue(string text, SubtitlePlacement? placement)
    {
        Assert.Equal(placement, SubtitleMarkup.Parse(text).Placement);
    }

    [Fact]
    public void ColourNamesAndOddTagsAreUnderstoodOrIgnored()
    {
        foreach (var (name, rgb) in new[] { ("white", 0xFFFFFF), ("yellow", 0xFFFF00), ("green", 0x00FF00), ("blue", 0x0000FF), ("cyan", 0x00FFFF), ("magenta", 0xFF00FF), ("black", 0) })
        {
            Assert.Equal(rgb, SubtitleMarkup.Parse($"<font color={name}>x</font>").Lines[0].Runs[0].Color);
        }

        Assert.Null(SubtitleMarkup.Parse("<font color=mauve>x").Lines[0].Runs[0].Color);
        Assert.Null(SubtitleMarkup.Parse("<font face=Arial>x").Lines[0].Runs[0].Color);
        Assert.Equal("x", SubtitleMarkup.Parse("</font><u>x</u>").Lines[0].Text);
        Assert.Null(SubtitleMarkup.AssColor("&Hzz&"));
        Assert.Equal(0x40_80_FF, SubtitleMarkup.AssColor("&H00FF8040&"));
        Assert.Equal("x", SubtitleMarkup.Parse("{\\i1\\b1\\u1}x{\\i0\\b0\\u0}").Lines[0].Text);
        Assert.Equal(new SubtitleRun("x", true, true, true), SubtitleMarkup.Parse("{\\i\\b\\u}x").Lines[0].Runs[0]);
        Assert.Throws<ArgumentNullException>(() => SubtitleMarkup.Parse(null!));
    }

    [Fact]
    [Capability("SUB-15")]
    public void TextIsDecodedByItsMarkThenAsUtf8ThenByTheFallback()
    {
        Assert.Equal(("caf\u00E9", "utf-8"), Name(SubtitleText.Decode([0xEF, 0xBB, 0xBF, (byte)'c', (byte)'a', (byte)'f', 0xC3, 0xA9])));
        Assert.Equal(("hi", "utf-16"), Name(SubtitleText.Decode([0xFF, 0xFE, (byte)'h', 0, (byte)'i', 0])));
        Assert.Equal(("hi", "utf-16BE"), Name(SubtitleText.Decode([0xFE, 0xFF, 0, (byte)'h', 0, (byte)'i'])));
        Assert.Equal(("caf\u00E9", "utf-8"), Name(SubtitleText.Decode([(byte)'c', (byte)'a', (byte)'f', 0xC3, 0xA9])));
        Assert.Equal(("caf\u00E9", "Windows-1252"), Name(SubtitleText.Decode([(byte)'c', (byte)'a', (byte)'f', 0xE9])));
        Assert.Equal(("\u0434", "windows-1251"), Name(SubtitleText.Decode([0xE4], 1251)));
        Assert.Equal("Windows-1252", SubtitleText.Decode([0xE9], 12345).Encoding.WebName, ignoreCase: true);
        Assert.Equal(15, SubtitleText.Fallbacks.Count);
    }

    private static (string, string) Name((string Text, Encoding Encoding) decoded) => (decoded.Text, decoded.Encoding.WebName switch
    {
        "utf-16" => "utf-16",
        "utf-16BE" or "unicodeFFFE" => "utf-16BE",
        "windows-1252" => "Windows-1252",
        var other => other,
    });

    [Fact]
    [Capability("SUB-14")]
    public void SidecarsAreFoundBesideTheMediaBestMatchFirst()
    {
        var files = new Dictionary<string, string[]>
        {
            [Path.Combine("Films")] = [Path.Combine("Films", "Film.mkv"), Path.Combine("Films", "Film.srt"), Path.Combine("Films", "Film.fr.forced.ass"), Path.Combine("Films", "Other.srt"), Path.Combine("Films", "notes.txt.bak")],
            [Path.Combine("Films", "Subs")] = [Path.Combine("Films", "Subs", "Film.eng.sdh.srt"), Path.Combine("Films", "Subs", "The Film director cut.vtt")],
        };

        var found = SubtitleSidecars.Find(Path.Combine("Films", "Film.mkv"), folder => files.TryGetValue(folder, out var list) ? list : throw new DirectoryNotFoundException());

        Assert.Equal(
        [
            (Path.Combine("Films", "Film.srt"), 3, (string?)null, false),
            (Path.Combine("Films", "Film.fr.forced.ass"), 2, "fr", true),
            (Path.Combine("Films", "Subs", "Film.eng.sdh.srt"), 2, "eng", false),
            (Path.Combine("Films", "Subs", "The Film director cut.vtt"), 1, null, false),
        ],
            found.Select(s => (s.Path, s.Match, s.Language, s.Forced)));
        Assert.True(SubtitleSidecars.IsSubtitle("a.SRT"));
        Assert.False(SubtitleSidecars.IsSubtitle("a.mkv"));
    }

    [Fact]
    public void AnUnrelatedSubtitleFileCountsOnlyWhenItIsTheOnlyOne()
    {
        string[] lone = [Path.Combine("x", "whatever.srt")];
        Assert.Single(SubtitleSidecars.Find(Path.Combine("x", "Film.mkv"), folder => folder == "x" ? lone : []));

        string[] two = [Path.Combine("x", "a.srt"), Path.Combine("x", "b.srt")];
        Assert.Empty(SubtitleSidecars.Find(Path.Combine("x", "Film.mkv"), folder => folder == "x" ? two : []));
        Assert.Empty(SubtitleSidecars.Find(Path.Combine(Path.GetTempPath(), "rexplayer-none-" + Guid.NewGuid().ToString("N"), "Film.mkv")));
        Assert.Throws<ArgumentNullException>(() => SubtitleSidecars.Find(null!));
    }

    [Fact]
    public void ACueSeenAgainAfterASeekIsKeptOnceAndTheDelayShiftsWhenCuesShow()
    {
        var track = new SubtitleTrack("English");
        var hello = SubtitleFile.Cue(S(1), S(3), "Hello")!;
        track.AddRange([SubtitleFile.Cue(S(5), S(6), "Later")!, hello, SubtitleFile.Cue(S(2), S(4), "Overlap")!, hello]);

        Assert.Equal(3, track.Count);
        Assert.Equal("English", track.Name);
        Assert.Equal(["Hello", "Overlap"], track.At(S(2.5)).Select(cue => cue.Text));
        Assert.Empty(track.At(S(4.5)));
        Assert.Equal(["Later"], track.At(S(6.5), delay: S(1)).Select(cue => cue.Text));
        Assert.Throws<ArgumentNullException>(() => track.Add(null!));
        Assert.Throws<ArgumentNullException>(() => track.AddRange(null!));
        Assert.True(hello.IsShownAt(S(1)));
        Assert.False(hello.IsShownAt(S(3)));
    }

    [Fact]
    [Capability("SUB-04")]
    public void PacketsFromContainersBecomeCues()
    {
        static Packet Packet(byte[] data, double start, double duration) =>
            Rex.Media.Primitives.Packet.Create(3, MediaBuffer.CopyOf(data), MediaTime.FromSeconds(start), MediaTime.FromSeconds(start), MediaTime.FromSeconds(duration), true);

        var srt = new SubtitlePackets(new TrackInfo { Id = 3, Codec = CodecId.SubRip });
        using (var packet = Packet(Encoding.UTF8.GetBytes("<i>Hi</i>"), 1, 2))
        {
            var cue = srt.Read(packet)!;
            Assert.Equal((S(1), S(3), "Hi"), (cue.Start, cue.End, cue.Text));
        }

        var header = "[V4+ Styles]\nFormat: Name, Alignment\nStyle: Top,8\n\n[Events]\nFormat: Layer, Start, End, Style, Text";
        var ass = new SubtitlePackets(new TrackInfo { Id = 3, Codec = CodecId.Ass, CodecPrivate = Encoding.UTF8.GetBytes(header) });
        using (var packet = Packet(Encoding.UTF8.GetBytes("7,0,Top,,0,0,0,,Up, here"), 4, 1))
        {
            var cue = ass.Read(packet)!;
            Assert.Equal(("Up, here", SubtitlePlacement.Top), (cue.Text, cue.Placement));
        }

        using (var packet = Packet(Encoding.UTF8.GetBytes("too,few"), 4, 1))
        {
            Assert.Null(ass.Read(packet));
        }

        var movText = new SubtitlePackets(new TrackInfo { Id = 3, Codec = CodecId.MovText });
        using (var packet = Packet([0, 6, (byte)'a', (byte)'<', (byte)'b', (byte)'>', (byte)'{', (byte)'\\', 0, 0], 2, 0))
        {
            var cue = movText.Read(packet)!;
            Assert.Equal(("a\u2039b>{", S(5)), (cue.Text, cue.End));
        }

        using (var empty = Packet([0, 0], 3, 1))
        {
            Assert.Null(movText.Read(empty));
        }

        using (var broken = Packet([0, 9, 1], 3, 1))
        {
            Assert.Null(movText.Read(broken));
        }

        using (var unknownTime = Rex.Media.Primitives.Packet.Create(3, MediaBuffer.CopyOf("x"u8), MediaTime.Unknown, MediaTime.Unknown, MediaTime.Zero, true))
        {
            Assert.Equal(S(0), srt.Read(unknownTime)!.Start);
        }

        Assert.True(SubtitlePackets.CanRead(CodecId.WebVtt));
        Assert.False(SubtitlePackets.CanRead(CodecId.DvbSubtitle));
        Assert.Throws<ArgumentNullException>(() => new SubtitlePackets(null!));
        Assert.Throws<ArgumentNullException>(() => srt.Read(null!));
    }
}
