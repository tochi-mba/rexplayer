using Rex.Media.AppCore.Player;
using Rex.Media.Settings;
using Rex.Media.Subtitles;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>How subtitles look and where they sit, from the settings and the picture's size.</summary>
public sealed class SubtitleLookTests
{
    [Fact]
    [Capability("OSD-01")]
    public void TheDefaultsAreWhiteOutlinedTextSizedToThePicture()
    {
        var look = SubtitleLook.For(new PlayerSettings(), 1000);

        Assert.Equal("Segoe UI", look.Font);
        Assert.Equal(55, look.FontSize, 6);
        Assert.False(look.Bold);
        Assert.Equal(new Argb(255, 255, 255, 255), look.Text);
        Assert.Equal(55 * 0.06, look.Outline, 6);
        Assert.Equal(new Argb(255, 0, 0, 0), look.OutlineColor);
        Assert.Equal(2, look.ShadowOffset);
        Assert.Equal(new Argb(153, 0, 0, 0), look.Shadow);
        Assert.Equal(0, look.Box.A);
        Assert.Equal(50, look.Margin, 6);
        Assert.False(look.AllAtBottom);
        Assert.True(look.RespectStyles);
    }

    [Fact]
    [Capability("OSD-01")]
    public void EveryPartOfTheLookFollowsItsSetting()
    {
        var settings = new PlayerSettings
        {
            SubtitleFont = "  Cascadia Mono ",
            SubtitleSize = 200,
            SubtitleColor = 0x12FFFF00 /* the high byte is not a colour */,
            SubtitleOpacity = 50,
            SubtitleBold = true,
            SubtitleOutlineColor = 0x0000FF,
            SubtitleShadowOpacity = 0,
            SubtitleBoxColor = 0x102030,
            SubtitleBoxOpacity = 100,
            SubtitleMargin = 10,
            SubtitlesAtBottom = true,
            SubtitleStyles = SubtitleStyleChoice.Override,
        };

        var look = SubtitleLook.For(settings, 500);

        Assert.Equal("Cascadia Mono", look.Font);
        Assert.Equal(55, look.FontSize, 6);
        Assert.True(look.Bold);
        Assert.Equal(new Argb(128, 255, 255, 0), look.Text);
        Assert.Equal(new Argb(128, 0, 0, 255), look.OutlineColor);
        Assert.Equal(0, look.ShadowOffset);
        Assert.Equal(new Argb(255, 0x10, 0x20, 0x30), look.Box);
        Assert.Equal(50, look.Margin, 6);
        Assert.True(look.AllAtBottom);
        Assert.False(look.RespectStyles);
    }

    [Theory]
    [Capability("OSD-01")]
    [InlineData(OutlineChoice.None, 0)]
    [InlineData(OutlineChoice.Thin, 0.03)]
    [InlineData(OutlineChoice.Normal, 0.06)]
    [InlineData(OutlineChoice.Thick, 0.1)]
    public void TheOutlineGrowsWithTheText(OutlineChoice outline, double share)
    {
        var look = SubtitleLook.For(new PlayerSettings { SubtitleOutline = outline }, 2000);

        Assert.Equal(110 * share, look.Outline, 6);
    }

    [Fact]
    public void TinyPicturesStillGetReadableTextAndAVisibleOutline()
    {
        var look = SubtitleLook.For(new PlayerSettings { SubtitleOutline = OutlineChoice.Thin }, 0);
        Assert.Equal(10, look.FontSize);
        Assert.Equal(1, look.Outline);
        Assert.Equal(0, look.Margin);

        Assert.Equal(10, SubtitleLook.For(new PlayerSettings(), double.NaN).FontSize);
        Assert.Throws<ArgumentNullException>(() => SubtitleLook.For(null!, 100));
    }

    [Fact]
    public void ValuesFromADamagedFileAreBroughtBackIntoRange()
    {
        var settings = new PlayerSettings
        {
            SubtitleFont = " ",
            SubtitleSize = 9000,
            SubtitleOpacity = -5,
            SubtitleOutline = (OutlineChoice)42,
            SubtitleShadowOpacity = 400,
            SubtitleShadowOffset = 99,
            SubtitleBoxOpacity = 101,
            SubtitleMargin = -1,
            SubtitleStyles = (SubtitleStyleChoice)7,
        }.Normalize();

        Assert.Equal("Segoe UI", settings.SubtitleFont);
        Assert.Equal(400, settings.SubtitleSize);
        Assert.Equal(0, settings.SubtitleOpacity);
        Assert.Equal(OutlineChoice.Normal, settings.SubtitleOutline);
        Assert.Equal(100, settings.SubtitleShadowOpacity);
        Assert.Equal(10, settings.SubtitleShadowOffset);
        Assert.Equal(100, settings.SubtitleBoxOpacity);
        Assert.Equal(0, settings.SubtitleMargin);
        Assert.Equal(SubtitleStyleChoice.Respect, settings.SubtitleStyles);
    }

    [Fact]
    [Capability("OSD-02")]
    public void TheSizeStepsThroughThePresets()
    {
        Assert.Equal([50, 75, 100, 125, 150, 200, 300, 400], SubtitleLook.Sizes);
        Assert.Equal(125, SubtitleLook.StepSize(100, 1));
        Assert.Equal(75, SubtitleLook.StepSize(100, -1));
        Assert.Equal(150, SubtitleLook.StepSize(130, 1));
        Assert.Equal(125, SubtitleLook.StepSize(130, -1));
        Assert.Equal(400, SubtitleLook.StepSize(400, 1));
        Assert.Equal(50, SubtitleLook.StepSize(50, -1));
        Assert.Equal(130, SubtitleLook.StepSize(130, 0));
    }

    [Fact]
    [Capability("OSD-03")]
    public void APictureIsFittedWithBarsAndSubtitlesMayUseThem()
    {
        // A wide film in a squarer window: bars above and below.
        var wide = SubtitleLook.Picture(1000, 1000, 2, 1);
        Assert.Equal(new Area(0, 250, 1000, 500), wide);
        Assert.Equal(wide, SubtitleLook.SubtitleArea(wide, 1000, inBars: false));
        Assert.Equal(new Area(0, 0, 1000, 1000), SubtitleLook.SubtitleArea(wide, 1000, inBars: true));

        // A tall picture in a wide window: bars at the sides, which subtitles never use.
        var tall = SubtitleLook.Picture(1000, 500, 1, 1);
        Assert.Equal(new Area(250, 0, 500, 500), tall);
        Assert.Equal(tall, SubtitleLook.SubtitleArea(tall, 500, inBars: true));

        // Nothing known about the picture: the whole stage.
        Assert.Equal(new Area(0, 0, 640, 360), SubtitleLook.Picture(640, 360, 0, 0));
        Assert.Equal(new Area(0, 0, 640, 360), SubtitleLook.Picture(640, 360, 16, -1));
        Assert.Equal(new Area(0, 0, 0, 0), SubtitleLook.Picture(-1, 0, 16, 9));
        Assert.Equal(new Area(0, 0, 640, 0), SubtitleLook.Picture(640, 0, 16, 9));
    }

    [Fact]
    [Capability("OSD-03")]
    public void CuesCanAllBeKeptAtTheBottom()
    {
        var top = new SubtitleCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), [], SubtitlePlacement.Top);

        Assert.Equal(SubtitlePlacement.Top, SubtitleLook.For(new PlayerSettings(), 100).PlacementOf(top));
        Assert.Equal(SubtitlePlacement.Bottom, SubtitleLook.For(new PlayerSettings { SubtitlesAtBottom = true }, 100).PlacementOf(top));
        Assert.Throws<ArgumentNullException>(() => SubtitleLook.For(new PlayerSettings(), 100).PlacementOf(null!));
    }

    [Fact]
    [Capability("OSD-05")]
    public void TheFilesColoursAndWeightsAreUsedUnlessTheUserOverridesThem()
    {
        var red = new SubtitleRun("Red", Bold: true, Color: 0xFF0000);
        var plain = new SubtitleRun("Plain");
        var respect = SubtitleLook.For(new PlayerSettings { SubtitleOpacity = 60 }, 100);
        var overridden = SubtitleLook.For(new PlayerSettings { SubtitleStyles = SubtitleStyleChoice.Override }, 100);

        Assert.Equal(new Argb(153, 255, 0, 0), respect.ColorOf(red));
        Assert.Equal(respect.Text, respect.ColorOf(plain));
        Assert.True(respect.IsBold(red));
        Assert.False(respect.IsBold(plain));
        Assert.Equal(overridden.Text, overridden.ColorOf(red));
        Assert.False(overridden.IsBold(red));
        Assert.True(SubtitleLook.For(new PlayerSettings { SubtitleBold = true }, 100).IsBold(plain));
        Assert.Throws<ArgumentNullException>(() => respect.ColorOf(null!));
        Assert.Throws<ArgumentNullException>(() => respect.IsBold(null!));
    }
}
