using Rex.Media.AppCore.Shell;

namespace Rex.Media.Tests.AppCore.Shell;

public sealed class TaskbarGlyphTests
{
    [Fact]
    public void EveryButtonAndBadgeRendersPixelsAtWindowsIconSizes()
    {
        Assert.Equal(16, TaskbarGlyphs.SizeFor(96));
        Assert.Equal(32, TaskbarGlyphs.SizeFor(192));
        Assert.Equal(16, TaskbarGlyphs.SizeFor(0));
        Assert.Equal(TaskbarGlyphs.DarkInk, TaskbarGlyphs.InkFor(true));
        Assert.Equal(TaskbarGlyphs.LightInk, TaskbarGlyphs.InkFor(false));

        foreach (var glyph in Enum.GetValues<TaskbarGlyph>())
        {
            foreach (var size in new[] { 8, 16, 24, 32 })
            {
                var first = TaskbarGlyphs.Render(glyph, size, TaskbarGlyphs.DarkInk);
                var second = TaskbarGlyphs.Render(glyph, size, TaskbarGlyphs.DarkInk);
                Assert.Equal(size * size * 4, first.Length);
                Assert.Equal(first, second);
                Assert.Contains(first.Chunk(4), pixel => pixel[3] > 0);
            }
        }
    }

    [Fact]
    public void InvalidIconRequestsFailWithoutAllocatingAPicture()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TaskbarGlyphs.Render(TaskbarGlyph.Play, 7, TaskbarGlyphs.LightInk));
        Assert.Throws<ArgumentOutOfRangeException>(() => TaskbarGlyphs.Render(TaskbarGlyph.Play, 257, TaskbarGlyphs.LightInk));
        Assert.Throws<ArgumentOutOfRangeException>(() => TaskbarGlyphs.Render((TaskbarGlyph)999, 16, TaskbarGlyphs.LightInk));
    }
}
