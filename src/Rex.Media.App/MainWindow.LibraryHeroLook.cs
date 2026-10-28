using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Rex.Media.AppCore.Design;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Visuals;

namespace Rex.Media.App;

/// <summary>
/// What the library banner's words sit on (LIB-05). A banner with artwork shows it decoded so small
/// that, stretched, it is a blur, under a plate of REX ink just opaque enough for light words over
/// any picture; an album's plate is its cover's own colour, deepened only as far as its words need.
/// The plate stops above a short band where it fades into the page, with nothing but space over the
/// band, so there is no step where the banner ends. <see cref="Legibility"/> says which words read on
/// the plate, and the banner's words, links and buttons take that theme. A banner without artwork is
/// the page itself, in the window's own theme; in high contrast there is neither picture nor plate.
/// All of it is worked out when the banner changes, never per frame.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The side of square banner artwork, and of a poster's frame.</summary>
    private const double HeroCoverSide = 150;
    private const double HeroPosterWidth = 140;
    private const double HeroPosterHeight = 210;

    /// <summary>How tall the band is where a plate fades into the page: under the expanded banner, then the compact one.</summary>
    private const double HeroFadeBand = 28;
    private const double CompactHeroFadeBand = 16;

    /// <summary>How opaque the ink plate is: just enough for secondary light words over a white picture.</summary>
    private static readonly byte HeroPlateAlpha = Legibility.ScrimAlpha(DesignTokens.DarkInk[0], Legibility.LightWords, Legibility.BodyContrast);

    /// <summary>Whether the banner shows a picture (artwork or a photo), and so has a plate under its words.</summary>
    private bool _heroPictured;

    /// <summary>An album's colour, its plate instead of the ink; null on every other page.</summary>
    private Tint? _heroTint;

    private uint _shownHeroPlate;
    private Windows.UI.Color _shownHeroPage;

    /// <summary>Lays the plate and its fade under the banner's words and gives the words the theme that reads on it.</summary>
    private void ApplyHeroLook()
    {
        var plated = (_heroPictured || _heroTint is not null)
            && BrowseHeroPresentation.For(_libraryContrastSettings.HighContrast, _budget.Animations).DrawsBackdrop;
        var band = _libraryHeaderCompact ? CompactHeroFadeBand : HeroFadeBand;
        LibraryHeroPlate.Visibility = LibraryHeroFade.Visibility = LibraryHeroBackdrop.Visibility = LibraryHeroWide.Visibility =
            plated ? Visibility.Visible : Visibility.Collapsed;
        LibraryHeroContent.Padding = new Thickness(24, _libraryHeaderCompact ? 12 : 20, 24, plated ? band : 8);
        if (!plated)
        {
            LibraryHeroContent.RequestedTheme = ElementTheme.Default;
            return;
        }

        var plate = _heroTint is { } tint
            ? Legibility.Toward(Argb(tint), DesignTokens.DarkInk[0], Legibility.LightWords, Legibility.BodyContrast)
            : DesignTokens.WithAlpha(DesignTokens.DarkInk[0], HeroPlateAlpha);
        LibraryHeroContent.RequestedTheme = Legibility.LightWordsRead(_heroTint is null ? Legibility.OverAnyPicture(plate) : [plate])
            ? ElementTheme.Dark
            : ElementTheme.Light;
        LibraryHeroPlate.Margin = new Thickness(0, 0, 0, band);
        LibraryHeroFade.Height = band;
        var page = RexTheme.Resource("LibrarySurfaceBrush") is SolidColorBrush surface ? surface.Color : RexTheme.Color(DesignTokens.LightInk[1]);
        if (plate == _shownHeroPlate && page == _shownHeroPage)
        {
            return;
        }

        (_shownHeroPlate, _shownHeroPage) = (plate, page);
        LibraryHeroPlate.Background = new SolidColorBrush(RexTheme.Color(plate));
        var fade = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(0, 1) };
        fade.GradientStops.Add(new GradientStop { Color = RexTheme.Color(plate), Offset = 0 });
        fade.GradientStops.Add(new GradientStop { Color = page, Offset = 1 });
        LibraryHeroFade.Fill = fade;
    }

    /// <summary>The banner's artwork, decoded at <see cref="BlurSide"/>, as the blur behind its plate.</summary>
    private void ShowHeroBackdrop(ImageSource picture)
    {
        LibraryHeroBackdropBrush.ImageSource = picture;
        LibraryHeroBackdrop.Opacity = 1;
    }

    private static uint Argb(Tint tint) => 0xFF000000 | ((uint)tint.R << 16) | ((uint)tint.G << 8) | tint.B;
}
