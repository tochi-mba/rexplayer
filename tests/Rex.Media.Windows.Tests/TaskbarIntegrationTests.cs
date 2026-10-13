using System.Runtime.Versioning;
using Rex.Media.AppCore.Shell;
using Rex.Media.Interop.Shell;

namespace Rex.Media.Windows.Tests;

/// <summary>Smoke checks for the real GDI icon adapter used by Windows taskbar controls.</summary>
[SupportedOSPlatform("windows8.0")]
public sealed class TaskbarIntegrationTests
{
    [Fact]
    public void TaskbarIconsAreRealDisposableWindowsHandles()
    {
        var pixels = TaskbarGlyphs.Render(TaskbarGlyph.Play, 16, TaskbarGlyphs.LightInk);
        using var icon = TaskbarIcon.FromPixels(pixels, 16);
        Assert.Equal(16, icon.Size);
        Assert.NotEqual(nint.Zero, icon.Handle);
        icon.Dispose();
        Assert.Equal(nint.Zero, icon.Handle);
        icon.Dispose();
    }

    [Fact]
    public void AnInvalidIconIsRefusedBeforeCallingGdi()
    {
        Assert.Throws<ArgumentException>(() => TaskbarIcon.FromPixels([], 16));
        Assert.Throws<ArgumentException>(() => TaskbarIcon.FromPixels(new byte[16], 0));
    }
}
