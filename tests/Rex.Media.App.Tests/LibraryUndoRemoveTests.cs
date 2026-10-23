using System.IO;
using System.Windows.Automation;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>A file can leave and return to the library without ever leaving the disk.</summary>
[Collection("desktop")]
public sealed class LibraryUndoRemoveTests : IDisposable
{
    private readonly LibraryScene _scene = new();

    public void Dispose() => _scene.Dispose();

    [Fact]
    [Capability("LIB-05")]
    public void LibraryRemovalCanBeCancelledAndUndoneWithoutInterruptingCurrentMedia()
    {
        var song = _scene.WithAsake().Ready().Media;
        using var app = _scene.Start();
        var transform = (TransformPattern)app.Window.GetCurrentPattern(TransformPattern.Pattern);
        transform.Resize(1300, 800);
        app.Visit("Songs");
        app.Pick("LibraryLook", "List");
        app.Line("Sungba");

        void OpenRemove()
        {
            Input.Focus(app);
            Input.RightClick(app.Line("Sungba"));
            Wait.For(() => app.IsShown("LibraryMenuRemove"), "the non-destructive remove action");
            app.Press("LibraryMenuRemove");
            Wait.For(() => app.IsShown("PrimaryButton"), "the remove confirmation");
        }

        OpenRemove();
        app.Press("CloseButton");
        Assert.True(app.Lines().Contains("Sungba"));
        Assert.False(app.IsShown("LibraryUndoRemove"));

        OpenRemove();
        app.Press("PrimaryButton");
        Wait.For(() => !app.Lines().Contains("Sungba"), "the item to leave library browsing");
        Wait.For(() => app.IsShown("LibraryUndoRemove"), "the safe Undo action");
        Assert.True(File.Exists(Path.Combine(song, "Sungba.wav")));
        app.Press("LibraryUndoRemove");
        Wait.For(() => app.Lines().Contains("Sungba"), "the hidden item to return with the same title");
        Assert.False(app.IsShown("LibraryUndoRemove"));
        Assert.True(File.Exists(Path.Combine(song, "Sungba.wav")));
        Assert.Equal(0, app.Close());
    }
}
