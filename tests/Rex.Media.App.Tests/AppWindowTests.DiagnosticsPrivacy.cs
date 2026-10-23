using Rex.Media.AppCore.Commands;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

public sealed partial class AppWindowTests
{
    [Fact]
    [Capability("TOOL-10")]
    public void DiagnosticsExportExplainsWhatLeavesTheComputerAndDoesNothingOnCancel()
    {
        using var app = AppProcess.Start([Song(15)]);
        app.Run(CommandCatalog.SaveDiagnostics);
        Wait.For(() => app.IsShown("DiagnosticsPrivacyNotice"),
            "the privacy explanation before choosing an export destination");
        var notice = app.Text("DiagnosticsPrivacyNotice");
        Assert.Contains("never uploads", notice, StringComparison.Ordinal);
        Assert.Contains("settings", notice, StringComparison.Ordinal);
        Assert.Contains("filenames", notice, StringComparison.Ordinal);
        Assert.Contains("Review it before sharing", notice, StringComparison.Ordinal);

        app.Press("CloseButton");
        Wait.For(() => !app.IsShown("DiagnosticsPrivacyNotice"),
            "canceling diagnostic export without prompting for a destination");
        Assert.Empty(Directory.GetFiles(app.Root, "*.zip", SearchOption.AllDirectories));
        Assert.Equal(0, app.Close());
    }
}
