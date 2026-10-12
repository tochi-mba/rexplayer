using Rex.Media.AppCore.Player;

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
    [InlineData(false, true, 10, 10, "Removal works while the subject is being tracked.")]
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

    [Fact]
    public void TheWordsAreShortPlainAndExplainTheControls()
    {
        string[] words = [RemovalPanel.Description, RemovalPanel.Note, RemovalPanel.CoverageLabel, RemovalPanel.CoverageHelp,
            RemovalPanel.SoftnessLabel, RemovalPanel.SoftnessHelp];
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
