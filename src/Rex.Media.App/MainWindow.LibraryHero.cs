using Rex.Media.Library;

namespace Rex.Media.App;

/// <summary>
/// The library banner's art and text. Local artist portraits win over cover art when available,
/// with requests guarded by the current page so old artwork cannot paint over navigation.
/// </summary>
public sealed partial class MainWindow
{
    private void ShowHero(string kicker, string title, string subtitle, string glyph, LibraryEntry? art, PictureArt shape = PictureArt.Natural, string? artistPhoto = null)
    {
        LibraryKicker.Text = kicker;
        LibraryTitle.Text = title;
        LibrarySubtitle.Text = subtitle;
        LibraryHeroGlyph.Glyph = glyph;
        var key = title + "\n" + art?.Path + "\n" + shape + "\n" + artistPhoto;
        if (key == _shownHero)
        {
            return;
        }

        _shownHero = key;
        var request = ++_heroRequest;
        // The banner's artwork fades in when decoded instead of popping over the glyph.
        LibraryHeroArt.Source = null;
        LibraryHeroBackdrop.Source = null;
        LibraryHeroArt.Opacity = 0;
        LibraryHeroBackdrop.Opacity = 0;
        void ShowPicture(Microsoft.UI.Xaml.Media.ImageSource picture)
        {
            if (_shownHero == key && request == _heroRequest)
            {
                (LibraryHeroArt.Source, LibraryHeroBackdrop.Source) = (picture, picture);
                LibraryHeroArt.Opacity = 1;
                LibraryHeroBackdrop.Opacity = 0.6;
            }
        }

        if (artistPhoto is not null)
        {
            Forget(ShowArtistPictureAsync());
        }
        else if (art is not null)
        {
            Forget(ShowPictureAsync(art.Path, art.Kind, art.Duration, ShowPicture,
                CancellationToken.None, art: shape, side: Math.Max(1600, _budget.LibraryPictureSide)));
        }

        async Task ShowArtistPictureAsync()
        {
            if (await LocalPictureAsync(artistPhoto!, Math.Max(1600, _budget.LibraryPictureSide), CancellationToken.None) is { } portrait)
            {
                ShowPicture(portrait);
            }
            else if (art is not null && _shownHero == key)
            {
                await ShowPictureAsync(art.Path, art.Kind, art.Duration, ShowPicture, CancellationToken.None,
                    art: shape, side: Math.Max(1600, _budget.LibraryPictureSide));
            }
        }
    }
}
