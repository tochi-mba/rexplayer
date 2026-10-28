using Rex.Media.AppCore.Player;

namespace Rex.Media.Tests.AppCore;

/// <summary>The transport's line of buttons at every width: nothing cut off, nothing scrolled away.</summary>
public sealed class TransportFitTests
{
    // Previous, Play and Next, 44 wide with 4 after each.
    private const double Fixed = 144;

    // Shuffle, Repeat, Playlist, Library, Full screen (with the separator before the group) and Stop.
    private static readonly double[] Movable = [48, 48, 48, 48, 56, 48];

    // Speed (52), mute (44) and volume (120), with 4 after each.
    private const double Group = 228;
    private const double Speed = 56;

    private static TransportRoom Room(double width, bool isShort = false, double gap = 12, double group = Group) =>
        new(width, gap, Fixed, Movable, 48, group, Speed, isShort);

    [Theory]
    [InlineData(952)]
    [InlineData(788)]
    public void EverythingStaysOnOneLineWhenTheTitleKeepsReadableRoom(double width)
    {
        var layout = TransportFit.Decide(Room(width));

        Assert.Equal(new TransportLayout(0, false, false), layout);
        Assert.False(layout.MoreShown);
    }

    [Theory]
    [InlineData(787)]
    [InlineData(560)]
    public void TheSpeedAndVolumeGroupTakesALineOfItsOwnBeforeAnyButtonMoves(double width)
    {
        Assert.Equal(new TransportLayout(0, false, true), TransportFit.Decide(Room(width)));
    }

    [Fact]
    public void ButtonsThatStillDoNotFitMoveIntoMoreInTheirOrder()
    {
        // One button out gains nothing, since More takes its place: three must go.
        var layout = TransportFit.Decide(Room(500));

        Assert.Equal(new TransportLayout(3, false, true), layout);
        Assert.True(layout.MoreShown);
    }

    [Fact]
    public void TheNarrowestLineKeepsPreviousPlayNextAndMore()
    {
        Assert.Equal(new TransportLayout(Movable.Length, false, true), TransportFit.Decide(Room(200)));
    }

    [Fact]
    public void AShortWindowMovesButtonsBeforeItGivesTheGroupALine()
    {
        Assert.Equal(new TransportLayout(2, false, false), TransportFit.Decide(Room(650, isShort: true)));
    }

    [Fact]
    public void AShortWindowMovesTheSpeedButtonLastToKeepOneLine()
    {
        // The smallest window: 420 wide, its group narrowed to a 88-wide volume slider.
        var layout = TransportFit.Decide(Room(380, isShort: true, gap: 8, group: Group - 32));

        Assert.Equal(new TransportLayout(Movable.Length, true, false), layout);
        Assert.True(layout.MoreShown);
    }

    [Fact]
    public void AShortWindowTooNarrowForOneLineStillWrapsTheGroup()
    {
        Assert.Equal(new TransportLayout(5, false, true), TransportFit.Decide(Room(300, isShort: true)));
    }

    [Fact]
    public void NothingMovableWrapsTheGroupAlone()
    {
        var room = new TransportRoom(300, 12, 96, [], 48, 228, 56, Short: false);

        Assert.Equal(new TransportLayout(0, false, true), TransportFit.Decide(room));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(301, true)]
    [InlineData(479.5, true)]
    [InlineData(480, false)]
    [InlineData(700, false)]
    public void AWindowIsShortBelowItsThreshold(double height, bool expected)
    {
        Assert.Equal(expected, TransportFit.IsShort(height));
    }

    [Fact]
    public void TheRoomIsRequired()
    {
        Assert.Throws<ArgumentNullException>(() => TransportFit.Decide(null!));
    }
}
