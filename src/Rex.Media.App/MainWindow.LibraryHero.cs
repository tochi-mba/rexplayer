using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Rex.Media.Library;

namespace Rex.Media.App;

/// <summary>
/// The library banner's art and text. Local artist portraits win over cover art when available,
/// with requests guarded by the current page so old artwork cannot paint over navigation. The
/// artwork shows sharp in its frame and, decoded tiny once it is in, as the blur behind the words.
/// </summary>
public sealed partial class MainWindow
{
    private void ShowHero(string kicker, string title, string subtitle, string glyph, LibraryEntry? art, PictureArt shape = PictureArt.Natural, string? artistPhoto = null)
    {
        LibraryKicker.Text = kicker;
        LibraryTitle.Text = title;
        LibrarySubtitle.Text = subtitle;
        LibraryKicker.Visibility = !_libraryHeaderCompact && kicker.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        LibrarySubtitle.Visibility = !_libraryHeaderCompact && subtitle.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        LibraryHeroGlyph.Glyph = glyph;
        // The plate is decided by whether a picture is coming, never by its arrival, so the words never change colour under the reader.
        _heroPictured = art is not null || artistPhoto is not null;
        ApplyHeroLook();
        var key = title + "\n" + art?.Path + "\n" + shape + "\n" + artistPhoto;
        if (key == _shownHero)
        {
            return;
        }

        _shownHero = key;
        var request = ++_heroRequest;
        // The banner's artwork fades in when decoded instead of popping over the glyph.
        LibraryHeroArt.Source = null;
        AutomationProperties.SetName(LibraryHeroArt, "");
        LibraryHeroBackdropBrush.ImageSource = null;
        LibraryHeroArt.Opacity = 0;
        LibraryHeroBackdrop.Opacity = 0;
        bool Current() => _shownHero == key && request == _heroRequest;
        void ShowPicture(ImageSource picture)
        {
            if (Current())
            {
                LibraryHeroArt.Source = picture;
                LibraryHeroArt.Opacity = 1;
                AutomationProperties.SetName(LibraryHeroArt, "Artwork for " + title);
            }
        }

        void ShowBlur(ImageSource picture)
        {
            if (Current())
            {
                ShowHeroBackdrop(picture);
            }
        }

        if (artistPhoto is not null)
        {
            Forget(ShowArtistPictureAsync());
        }
        else if (art is not null)
        {
            Forget(ShowArtAsync());
        }

        // The blur is decoded after the artwork, from the picture just found or made, never beside it;
        // the request stays counted as pending throughout, so a capture waits for both.
        async Task ShowArtAsync()
        {
            using var pending = PictureStarted();
            await ShowPictureAsync(art!.Path, art.Kind, art.Duration, ShowPicture,
                CancellationToken.None, art: shape, side: _budget.HeroPictureSide);
            if (Current())
            {
                await ShowPictureAsync(art.Path, art.Kind, art.Duration, ShowBlur, CancellationToken.None, art: shape, side: BlurSide);
            }
        }

        async Task ShowArtistPictureAsync()
        {
            using var pending = PictureStarted();
            if (await LocalPictureAsync(artistPhoto!, _budget.HeroPictureSide, CancellationToken.None) is { } portrait)
            {
                ShowPicture(portrait);
                if (Current())
                {
                    AutomationProperties.SetName(LibraryHeroArt, "Portrait of " + title);
                    if (await LocalPictureAsync(artistPhoto!, BlurSide, CancellationToken.None) is { } blur)
                    {
                        ShowBlur(blur);
                    }
                }
            }
            else if (art is not null && _shownHero == key)
            {
                await ShowArtAsync();
            }
        }
    }
}
