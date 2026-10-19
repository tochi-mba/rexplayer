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
    public void EveryEntryIsACommandOrASeparator()
    {
        var ids = ContextMenuLayout.Groups.SelectMany(group => group.Commands).OfType<string>().ToList();

        Assert.All(ids, id => Assert.NotNull(CommandCatalog.Find(id)));
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }
}
