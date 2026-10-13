using Rex.Media.AppCore.Player;
using Rex.Media.Primitives;
using Rex.Media.Video;

namespace Rex.Media.Tests.AppCore;

public sealed class RemovalPanelTests
{
    [Theory]
    [InlineData(0, "0%, tight")]
    [InlineData(35, "35%, tight")]
    [InlineData(50, "50%, balanced")]
    [InlineData(65, "65%, wide")]
    [InlineData(100, "100%, whole box")]
    [InlineData(140, "100%, whole box")]
    [InlineData(-5, "0%, tight")]
    public void TheCoverageReadoutSaysTheValueAndWhatItMeans(int coverage, string expected) =>
        Assert.Equal(expected, RemovalPanel.CoverageText(coverage));

    [Theory]
    [InlineData(0, "Hard edge")]
    [InlineData(-2, "Hard edge")]
    [InlineData(3, "3 px")]
    [InlineData(12, "12 px")]
    public void TheSoftnessReadoutIsInPixels(int feather, string expected) =>
        Assert.Equal(expected, RemovalPanel.SoftnessText(feather));

    [Theory]
    [InlineData(false, false, 10, 10, "Removal works while the subject is being tracked.")]
    [InlineData(false, true, 10, 10, "Removal paused: the subject is lost. It resumes once the subject is found again.")]
    [InlineData(true, false, 10, 10, "Turn on the preview to see the subject removed.")]
    [InlineData(true, true, 0, 0, "Nothing removed: the subject blends into its surroundings.")]
    [InlineData(true, true, -4, 0, "Nothing removed: the subject blends into its surroundings.")]
    [InlineData(true, true, 500, 0, "Fill: all of it seen in other frames.")]
    [InlineData(true, true, 0, 500, "Fill: all of it estimated; this background has not been visible yet.")]
    [InlineData(true, true, 820, 180, "Fill: 82% seen in other frames, 18% estimated.")]
    [InlineData(true, true, 1, 999, "Fill: 1% seen in other frames, 99% estimated.")]
    [InlineData(true, true, 999, 1, "Fill: 99% seen in other frames, 1% estimated.")]
    public void TheFillLineSaysTruthfullyWhereTheFillCameFrom(bool tracking, bool erase, int observed, int estimated, string expected) =>
        Assert.Equal(expected, RemovalPanel.FillText(tracking, erase, observed, estimated));

    [Theory]
    [InlineData(RemovalFillSource.Model, "Fill: 82% seen in other frames, 18% estimated. The estimate was painted by the inpainting model.")]
    [InlineData(RemovalFillSource.ModelWorking, "Fill: 82% seen in other frames, 18% estimated. Filling with the inpainting model...")]
    [InlineData(RemovalFillSource.Synthesised, "Fill: 82% seen in other frames, 18% estimated.")]
    public void TheFillLineSaysWhenTheModelPaintedTheEstimate(RemovalFillSource source, string expected)
    {
        Assert.Equal(expected, RemovalPanel.FillText(true, true, 820, 180, source));
        // With nothing estimated, or no preview, the model has no part to mention.
        Assert.Equal("Fill: all of it seen in other frames.", RemovalPanel.FillText(true, true, 500, 0, source));
        Assert.Equal("Turn on the preview to see the subject removed.", RemovalPanel.FillText(true, false, 10, 10, source));
        Assert.Equal("Removal works while the subject is being tracked.", RemovalPanel.FillText(false, false, 10, 10, source));
    }

    [Fact]
    public void TheTwoModesAreNamedExplainedAndTheBlendSaysWhereItsCoverCameFrom()
    {
        Assert.Equal("Remove", RemovalPanel.ModeName(RemovalMode.Remove));
        Assert.Equal("Blend", RemovalPanel.ModeName(RemovalMode.Blend));
        Assert.Equal(RemovalPanel.Description, RemovalPanel.Explain(RemovalMode.Remove));
        Assert.Equal(RemovalPanel.BlendDescription, RemovalPanel.Explain(RemovalMode.Blend));
        Assert.Contains("Blend", RemovalPanel.ModeHelp, StringComparison.Ordinal);
        Assert.Equal("Blend: covered with the surface around the selection.", RemovalPanel.FillText(true, true, 0, 900, RemovalFillSource.Synthesised, RemovalMode.Blend));
        Assert.Equal("Nothing removed: the subject blends into its surroundings.", RemovalPanel.FillText(true, true, 0, 0, RemovalFillSource.Synthesised, RemovalMode.Blend));
        Assert.Equal("Turn on the preview to see the subject removed.", RemovalPanel.FillText(true, false, 0, 900, RemovalFillSource.Synthesised, RemovalMode.Blend));
    }

    [Fact]
    public void TheWordsAreShortPlainAndExplainTheControls()
    {
        string[] words = [RemovalPanel.Description, RemovalPanel.Note, RemovalPanel.CoverageLabel, RemovalPanel.CoverageHelp,
            RemovalPanel.SoftnessLabel, RemovalPanel.SoftnessHelp, RemovalPanel.BlendDescription, RemovalPanel.ModeLabel, RemovalPanel.ModeHelp];
        foreach (var text in words)
        {
            Assert.DoesNotContain((char)0x2014, text);
            Assert.DoesNotContain((char)0x00B7, text);
            Assert.InRange(text.Length, 1, 200);
        }

        Assert.Contains("never changed", RemovalPanel.Note, StringComparison.Ordinal);
        Assert.Contains("never visible", RemovalPanel.Description, StringComparison.Ordinal);
        Assert.Contains("Raise it", RemovalPanel.CoverageHelp, StringComparison.Ordinal);
    }
}
