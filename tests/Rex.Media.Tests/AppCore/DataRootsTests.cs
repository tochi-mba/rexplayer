using Rex.Media.AppCore.Platform;

namespace Rex.Media.Tests.AppCore;

/// <summary>Every shell keeps rexplayer's files in the same places, chosen once per platform.</summary>
public sealed class DataRootsTests
{
    private static readonly string Local = Path.Combine("C:", "Users", "ada", "AppData", "Local");
    private static readonly string Home = Path.Combine("/", "Users", "ada");

    private static Func<string, string?> Environment(params (string Name, string Value)[] variables) =>
        name => variables.FirstOrDefault(variable => variable.Name == name).Value;

    [Fact]
    public void WindowsKeepsItsDataUnderLocalAppData()
    {
        var roots = DataRoots.Resolve(Environment(), PlatformKind.Windows, Local, Home);

        Assert.Equal(Path.Combine(Local, "REX", "rexplayer"), roots.Root);
    }

    [Fact]
    public void MacOSKeepsItsDataUnderApplicationSupport()
    {
        var roots = DataRoots.Resolve(Environment(), PlatformKind.MacOS, Local, Home);

        Assert.Equal(Path.Combine(Home, "Library", "Application Support", "REX", "rexplayer"), roots.Root);
    }

    [Fact]
    public void OtherPlatformsFollowTheXdgDataHomeOrItsDefault()
    {
        Assert.Equal(Path.Combine(Home, ".local", "share", "REX", "rexplayer"), DataRoots.Resolve(Environment(), PlatformKind.Other, Local, Home).Root);
        Assert.Equal(Path.Combine("/", "data", "REX", "rexplayer"), DataRoots.Resolve(Environment(("XDG_DATA_HOME", Path.Combine("/", "data"))), PlatformKind.Other, Local, Home).Root);
        Assert.Equal(Path.Combine(Home, ".local", "share", "REX", "rexplayer"), DataRoots.Resolve(Environment(("XDG_DATA_HOME", "")), PlatformKind.Other, Local, Home).Root);
    }

    [Fact]
    public void TheOverrideReplacesTheRootOnEveryPlatform()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "rexplayer-scratch");
        foreach (var platform in Enum.GetValues<PlatformKind>())
        {
            Assert.Equal(scratch, DataRoots.Resolve(Environment((DataRoots.RootVariable, scratch)), platform, Local, Home).Root);
        }

        Assert.NotEqual(scratch, DataRoots.Resolve(Environment((DataRoots.RootVariable, "")), PlatformKind.Windows, Local, Home).Root);
    }

    [Fact]
    public void EveryFileHasOnePlaceUnderTheRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "rexplayer-root");
        var roots = new DataRoots(root);

        Assert.Equal(Path.Combine(root, "settings.json"), roots.SettingsPath);
        Assert.Equal(Path.Combine(root, "logs"), roots.LogFolder);
        Assert.Equal(Path.Combine(root, "logs", "update.log"), roots.UpdateLog);
        Assert.Equal(Path.Combine(root, "updates"), roots.UpdateFolder);
        Assert.Equal(Path.Combine(root, "library.log"), roots.LibraryStore);
        Assert.Equal(Path.Combine(root, "store.log"), roots.MemoryStore);
        Assert.Equal(Path.Combine(root, "resume.json"), roots.ResumePath);
        Assert.Equal(Path.Combine(root, "cache", "shaders"), roots.ShaderCache);
        Assert.Equal(Path.Combine(root, "cache", "sections"), roots.SectionCache);
        Assert.Equal(Path.Combine(root, "models"), roots.Models);
        Assert.Equal(Path.Combine(root, "metadata"), roots.Metadata);
        Assert.Equal(Path.Combine(root, "cache", "library-pictures", "abc.png"), roots.LibraryPicture("abc"));
        Assert.Throws<ArgumentException>(() => roots.LibraryPicture(" "));
        Assert.Throws<ArgumentException>(() => new DataRoots(""));
        Assert.Throws<ArgumentNullException>(() => DataRoots.Resolve(null!, PlatformKind.Windows, Local, Home));
    }

    [Fact]
    public void TheCurrentProcessGetsTheOverrideWhenItIsSet()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "rexplayer-process-" + Guid.NewGuid().ToString("N"));
        var before = System.Environment.GetEnvironmentVariable(DataRoots.RootVariable);
        try
        {
            System.Environment.SetEnvironmentVariable(DataRoots.RootVariable, scratch);
            Assert.Equal(scratch, DataRoots.ForCurrentProcess().Root);
            System.Environment.SetEnvironmentVariable(DataRoots.RootVariable, null);
            Assert.EndsWith(Path.Combine("REX", "rexplayer"), DataRoots.ForCurrentProcess().Root);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(DataRoots.RootVariable, before);
        }
    }
}
