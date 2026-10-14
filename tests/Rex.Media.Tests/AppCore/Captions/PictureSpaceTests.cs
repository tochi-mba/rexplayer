using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Player;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore.Captions;

public sealed class PictureSpaceTests
{
    private const int Width = 320;
    private const int Height = 180;

    /// <summary>A picture painted by <paramref name="paint"/>, which gives each pixel's red, green and blue.</summary>
    internal static byte[] Picture(Func<int, int, (byte R, byte G, byte B)> paint, int width = Width, int height = Height)
    {
        var bgra = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b) = paint(x, y);
                var at = ((y * width) + x) * 4;
                (bgra[at], bgra[at + 1], bgra[at + 2], bgra[at + 3]) = (b, g, r, 255);
            }
        }

        return bgra;
    }

    private static readonly (byte, byte, byte) Grey = (40, 40, 48);
    private static readonly (byte, byte, byte) Skin = (224, 172, 140);

    private static bool InFace(int x, int y) => Math.Pow((x - 80) / 30.0, 2) + Math.Pow((y - 60) / 40.0, 2) <= 1;

    [Fact]
    [Capability("OSD-11")]
    public void AFaceMattersAndACaptionIsPlacedClearOfIt()
    {
        var bgra = Picture((x, y) => InFace(x, y) ? Skin : Grey);
        var space = PictureSpace.Read(bgra, Width, Height, Width * 4);

        Assert.Equal(32, space.ColumnCount);
        Assert.Equal(18, space.RowCount);
        // The face (around a quarter across, a third down) matters; a far corner does not.
        Assert.Equal(1, space.Importance[(6 * 32) + 8]);
        Assert.Equal(0, space.Importance[(15 * 32) + 28]);

        // Asked for the top left, where the face is, the caption goes where it covers nothing.
        var (x, y) = space.Place(0.4, 0.2, (0.05, 0.1));
        Assert.Equal(0, space.Covered(x, y, 0.4, 0.2).Most);
        // Of the clear places, the nearest to where it was asked for.
        Assert.True(x < 0.5 || y < 0.5, $"{x}, {y}");
    }

    [Fact]
    [Capability("OSD-11")]
    public void TextInThePictureMattersAndMakesTheCaptionMove()
    {
        // White strokes on black, a stripe a few pixels apart, across the bottom: burned-in text.
        var bgra = Picture((x, y) => y > 140 && y < 170 && x > 40 && x < 280 && (x / 3) % 2 == 0 ? ((byte)250, (byte)250, (byte)250) : ((byte)10, (byte)10, (byte)10));
        var space = PictureSpace.Read(bgra, Width, Height, Width * 4);

        Assert.True(space.Importance[(16 * 32) + 16] >= 0.9f);
        Assert.True(space.CoversProtected(0.2, 0.78, 0.6, 0.15));
        Assert.False(space.CoversAFace(0.2, 0.78, 0.6, 0.15));
        var (_, y) = space.Place(0.6, 0.15, (0.2, 0.78));
        Assert.True(y < 0.7, $"{y}");
    }

    [Fact]
    [Capability("OSD-11")]
    public void MovementAndTheFollowedSubjectMatter()
    {
        var still = Picture((x, y) => Grey);
        var lit = Picture((x, y) => x > 200 && y > 100 ? ((byte)200, (byte)200, (byte)200) : Grey);
        var before = PictureSpace.Read(still, Width, Height, Width * 4);
        var moved = PictureSpace.Read(lit, Width, Height, Width * 4, before, subject: new Area(0, 0, 0.25, 0.25));

        Assert.True(moved.Importance[(14 * 32) + 28] >= 0.6f, $"{moved.Importance[(14 * 32) + 28]}");
        Assert.Equal(1, moved.Importance[(1 * 32) + 1]);
        Assert.Equal(0, moved.Importance[(14 * 32) + 12]);

        // A reading of a picture another shape gives no motion.
        var other = PictureSpace.Read(Picture((x, y) => Grey, 100, 100), 100, 100, 400, moved);
        Assert.Equal(32, other.RowCount);
        Assert.All(other.Importance, value => Assert.Equal(0, value));
    }

    [Fact]
    [Capability("OSD-11")]
    public void WhiteTextGetsAPlateWhereThePictureIsBrightOrBusyAndNoneWhereItIsDark()
    {
        var bgra = Picture((x, y) => x < 160 ? ((byte)15, (byte)15, (byte)20) : x < 240 ? ((byte)235, (byte)235, (byte)225) : (x + y) % 2 == 0 ? ((byte)255, (byte)255, (byte)255) : ((byte)0, (byte)0, (byte)0));
        var space = PictureSpace.Read(bgra, Width, Height, Width * 4);

        var dark = space.Behind(0.1, 0.4, 0.3, 0.1);
        var bright = space.Behind(0.55, 0.4, 0.15, 0.1);
        var busy = space.Behind(0.8, 0.4, 0.15, 0.1);
        Assert.Equal(new Backing(0.4, 0), dark);
        Assert.True(bright.Plate >= 0.6, $"{bright}");
        Assert.True(busy.Plate > 0.3 && busy.Outline == 1, $"{busy}");
        Assert.InRange(space.Luma[0], 0.05, 0.07);
        Assert.Equal(0, space.Busy[0]);
    }

    [Fact]
    [Capability("OSD-11")]
    public void FacesADetectorFoundStandInForTheSkinGuessAndAreNeverCovered()
    {
        // Skin colour over the left, but the detector says the face is on the right: the detector wins.
        var bgra = Picture((x, y) => x < 100 ? Skin : Grey);
        var space = PictureSpace.Read(bgra, Width, Height, Width * 4, faces: [new Area(0.7, 0.3, 0.2, 0.3)]);

        Assert.Equal(0, space.Importance[(9 * 32) + 2]);
        Assert.Equal(1, space.Importance[(8 * 32) + 25]);
        // Widened by a third of the face all round.
        Assert.Equal(1, space.Importance[(4 * 32) + 20]);
        Assert.True(space.CoversAFace(0.65, 0.25, 0.2, 0.2));
        Assert.False(space.CoversAFace(0.05, 0.05, 0.3, 0.2));

        // Asked for the very spot, the caption still goes anywhere but the face.
        var (x, y) = space.Place(0.25, 0.3, (0.7, 0.3));
        Assert.False(space.CoversAFace(x, y, 0.25, 0.3));
    }

    [Fact]
    public void AnEmptyDetectorResultFallsBackToTheSkinGuess()
    {
        var bgra = Picture((x, y) => InFace(x, y) ? Skin : Grey);
        var space = PictureSpace.Read(bgra, Width, Height, Width * 4, faces: []);

        Assert.Equal(1, space.Importance[(6 * 32) + 8]);
        Assert.True(space.CoversAFace(0.15, 0.1, 0.25, 0.45));
    }

    [Fact]
    public void MostTakesTheGreatestOfEveryReadingOfTheSameShape()
    {
        var left = PictureSpace.Read(Picture((x, y) => x < 40 && y < 40 ? Skin : Grey), Width, Height, Width * 4);
        var right = PictureSpace.Read(Picture((x, y) => x > 280 && y > 140 ? Skin : Grey), Width, Height, Width * 4);
        var square = PictureSpace.Read(Picture((x, y) => Skin, 64, 64), 64, 64, 256);

        var most = PictureSpace.Most([left, right, square]);
        Assert.Equal(1, most.Importance[0]);
        Assert.Equal(1, most.Importance[^1]);
        Assert.Equal(0, most.Importance[(9 * 32) + 16]);
        Assert.Equal(18, PictureSpace.Most([]).RowCount);
        Assert.All(PictureSpace.Most([]).Importance, value => Assert.Equal(0, value));
    }

    [Fact]
    public void PlacingKeepsInsideTheMarginsWhateverIsAsked()
    {
        var space = PictureSpace.Empty(9);
        Assert.Equal(9, space.RowCount);
        Assert.Equal((0.04, 0.04), space.Place(0.3, 0.2, (-1, -1)));
        var (x, y) = space.Place(double.NaN, 5, (2, 2));
        Assert.Equal((0.96, 0.04), (Math.Round(x, 3), Math.Round(y, 3)));
        Assert.Equal((0, 0), space.Covered(0, 0, 1, 1));
    }

    [Fact]
    public void WrongPicturesAreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PictureSpace.Read(new byte[16], 0, 1, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => PictureSpace.Read(new byte[16], 1, 0, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => PictureSpace.Read(new byte[16], 2, 2, 4));
        Assert.Throws<ArgumentException>(() => PictureSpace.Read(new byte[15], 2, 2, 8));
        Assert.Throws<ArgumentNullException>(() => PictureSpace.Most(null!));
    }
}
