using Rex.Media.AppCore.Shell;

namespace Rex.Media.Tests.AppCore.Shell;

/// <summary>A launch's odd arguments never stop it before its window shows.</summary>
public sealed class LaunchArgumentTests
{
    [Fact]
    public void BlankArgumentsNameNoFile()
    {
        Assert.Equal(["C:/Music/Sungba.mp3"], JumpListPlan.ParseLaunch(["", "C:/Music/Sungba.mp3", "   "]).Files);
        Assert.Empty(JumpListPlan.ParseLaunch([""]).Files);

        var withTask = JumpListPlan.ParseLaunch(["\t", JumpListPlan.CommandSwitch, "unknown"]);
        Assert.Empty(withTask.Files);
        Assert.Null(withTask.Command);
    }
}
