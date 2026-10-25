using Rex.Media.Engine;
using Rex.Media.Primitives;

namespace Rex.Media.Tests.Engine;

public sealed class DamageGuardTests
{
    [Fact]
    public void WorkThatSucceedsReportsNothing()
    {
        var damaged = new List<Exception>();

        Assert.True(DamageGuard.Try(() => { }, damaged.Add));
        Assert.Empty(damaged);
    }

    [Fact]
    public void FormatErrorsAndAReadersOwnFaultsAreDamage()
    {
        foreach (var failure in new Exception[] { new MediaFormatException("bad bytes"), new KeyNotFoundException(), new OverflowException(), new FormatException() })
        {
            var damaged = new List<Exception>();

            Assert.False(DamageGuard.Try(() => throw failure, damaged.Add));
            Assert.Same(failure, Assert.Single(damaged));
        }
    }

    [Fact]
    public void TheDiskFailingAndTheSessionClosingAreNotDamage()
    {
        Assert.Throws<IOException>(() => DamageGuard.Try(() => throw new IOException("unplugged"), _ => { }));
        Assert.Throws<UnauthorizedAccessException>(() => DamageGuard.Try(() => throw new UnauthorizedAccessException(), _ => { }));
        Assert.Throws<OperationCanceledException>(() => DamageGuard.Try(() => throw new OperationCanceledException(), _ => { }));
        Assert.True(DamageGuard.Recoverable(new MediaFormatException("an IOException of the bytes")));
        Assert.False(DamageGuard.Recoverable(new FileNotFoundException()));
    }
}
