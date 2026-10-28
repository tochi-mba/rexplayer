using Rex.Media.AppCore.Commands;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>The picture's right-click menu (UI-03).</summary>
public sealed class ContextMenuLayoutTests
{
    [Fact]
    [Capability("UI-03")]
    public void TheRightClickMenuZoomsBothWaysAndBack()
    {
        var video = ContextMenuLayout.Groups.Single(group => group.Title == "Video").Commands;

        var zoomIn = Array.IndexOf(video, CommandCatalog.ZoomIn);
        Assert.True(zoomIn >= 0);
        Assert.Equal([CommandCatalog.ZoomIn, CommandCatalog.ZoomOut, CommandCatalog.ResetZoom], video[zoomIn..(zoomIn + 3)]);
    }

    [Fact]
    [Capability("UI-03")]
    public void ItsGroupsComeInTheMenuBarsOrderAndItFitsOverTheWindow()
    {
        var bar = MenuBarLayout.Menus.Select(menu => menu.Title).ToList();
        var groups = ContextMenuLayout.Groups.Select(group => group.Title).Where(title => title.Length > 0).ToList();

        Assert.All(groups, title => Assert.Contains(title, bar));
        Assert.Equal(groups.OrderBy(bar.IndexOf), groups);

        // Each group is one line at the top level; with the commands there it stays short enough
        // to open from the middle of a small window without running off its bottom.
        var lines = ContextMenuLayout.Groups.Sum(group => group.Title.Length > 0 ? 1 : group.Commands.Count(command => command is not null));
        Assert.InRange(lines, 1, 11);
        Assert.DoesNotContain(CommandCatalog.Quit, ContextMenuLayout.Groups.SelectMany(group => group.Commands));
        Assert.Contains(CommandCatalog.MinimalInterface, ContextMenuLayout.Groups[0].Commands);
    }

    [Fact]
    [Capability("UI-03")]
    public void EveryEntryIsACommandOrASeparator()
    {
        var ids = ContextMenuLayout.Groups.SelectMany(group => group.Commands).OfType<string>().ToList();

        Assert.All(ids, id => Assert.NotNull(CommandCatalog.Find(id)));
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }
}
