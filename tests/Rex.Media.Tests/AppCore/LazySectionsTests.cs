using Rex.Media.AppCore;

namespace Rex.Media.Tests.AppCore;

public sealed class LazySectionsTests
{
    private static LazySections<object, int> Sections(List<string> built)
    {
        var sections = new LazySections<object, int>();
        sections.Add("Add", () =>
        {
            built.Add("Add");
            return (new object(), n => n + 10);
        });
        sections.Add("Double", () =>
        {
            built.Add("Double");
            return (new object(), n => n * 2);
        });
        return sections;
    }

    [Fact]
    public void OnlyTheSectionShownIsBuiltAndOnlyOnce()
    {
        var built = new List<string>();
        var shown = new List<object>();
        var sections = Sections(built);
        sections.Built = shown.Add;
        Assert.Equal(["Add", "Double"], sections.Names);
        Assert.Equal((2, 0), (sections.Count, sections.BuiltCount));
        var page = sections.Page(1);
        Assert.Same(page, sections.Page(1));
        Assert.Equal(["Double"], built);
        Assert.Equal([page], shown);
        Assert.True(sections.IsBuilt(1));
        Assert.False(sections.IsBuilt(0));
        Assert.Equal(1, sections.IndexOf("Double"));
        Assert.Equal(-1, sections.IndexOf("Missing"));
    }

    [Fact]
    public void SaveReadsOnlyBuiltSectionsInSectionOrder()
    {
        var built = new List<string>();
        var sections = Sections(built);
        Assert.Equal(1, sections.Apply(1));
        sections.Page(1);
        Assert.Equal(2, sections.Apply(1));
        sections.Page(0);
        // Add runs before Double whatever order they were shown in.
        Assert.Equal(22, sections.Apply(1));
    }

    [Fact]
    public void SearchingBuildsEveryPage()
    {
        var built = new List<string>();
        var sections = Sections(built);
        sections.Page(0);
        Assert.Equal(["Add", "Double"], sections.All().Select(item => item.Name));
        Assert.Equal(["Add", "Double"], built);
        Assert.Equal(2, sections.BuiltCount);
    }
}
