using Rex.Media.IO;

namespace Rex.Media.Tests.IO;

public sealed class AtomicFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rexplayer-atomic-" + Guid.NewGuid().ToString("N"));

    private string Target => Path.Combine(_folder, "nested", "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void AFirstWriteCreatesTheFolderAndTheFile()
    {
        AtomicFile.Write(Target, [1, 2, 3]);

        Assert.Equal([1, 2, 3], File.ReadAllBytes(Target));
        Assert.False(File.Exists(Target + AtomicFile.BackupSuffix));
    }

    [Fact]
    public void ALaterWriteKeepsThePreviousContentsAsTheBackupAndLeavesNoTemporaryFiles()
    {
        AtomicFile.Write(Target, [1]);
        AtomicFile.Write(Target, [2]);

        Assert.Equal([2], File.ReadAllBytes(Target));
        Assert.Equal([1], File.ReadAllBytes(Target + AtomicFile.BackupSuffix));
        Assert.Equal(2, Directory.GetFiles(Path.GetDirectoryName(Target)!).Length);
    }

    [Fact]
    public void AReplacementThatIsBlockedForAMomentIsRetried()
    {
        // A folder where the file belongs blocks the rename until the wait between attempts clears it.
        Directory.CreateDirectory(Target);
        var waits = new List<TimeSpan>();

        AtomicFile.Write(Target, [7], wait =>
        {
            waits.Add(wait);
            if (waits.Count == 2)
            {
                Directory.Delete(Target);
            }
        });

        Assert.Equal([7], File.ReadAllBytes(Target));
        Assert.Equal([TimeSpan.FromMilliseconds(25), TimeSpan.FromMilliseconds(50)], waits);
    }

    [Fact]
    public void AReplacementThatStaysBlockedFailsAndCleansUpItsTemporaryFile()
    {
        Directory.CreateDirectory(Target);
        var waits = 0;

        Assert.ThrowsAny<IOException>(() => AtomicFile.Write(Target, [7], _ => waits++));

        Assert.Equal(5, waits);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(Target)!));
    }

    [Fact]
    public void ReadingFallsBackToTheBackupWhenTheFileIsMissingOrRejected()
    {
        Assert.Null(AtomicFile.Read(Target));

        AtomicFile.Write(Target, [1]);
        AtomicFile.Write(Target, [2]);
        Assert.Equal([2], AtomicFile.Read(Target));
        Assert.Equal([1], AtomicFile.Read(Target, bytes => bytes[0] == 1));
        Assert.Null(AtomicFile.Read(Target, _ => false));

        File.Delete(Target);
        Assert.Equal([1], AtomicFile.Read(Target));
    }
}
