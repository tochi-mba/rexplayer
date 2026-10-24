using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Metadata;

namespace Rex.Media.App;

/// <summary>
/// The pieces the details screens share: the banner's facts, synopsis, credit, trailer and wide
/// backdrop; the show page's facts, cast and season overview; the cast row; and pictures from the
/// web (backdrops, photos), fetched once into the metadata cache when a screen shows them and
/// decoded at the size they are shown. With metadata turned off nothing is fetched: a picture
/// already cached still shows, and everything else simply is not there.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The metadata cache's kind for pictures fetched for display: backdrops and cast photos.</summary>
    private const string RemoteArtKind = "remote";

    /// <summary>A cast photo's side on screen, in pixels before scaling.</summary>
    private const int CastPhotoSide = 88;

    private LibraryDetailsTemplates? _detailsTemplates;
    private HttpClient? _pictureHttp;
    private CancellationTokenSource? _wideWork;
    private string _shownWide = "";
    private string _shownShowHeader = "";

    /// <summary>
    /// The banner's details under the title, each hidden when empty: <paramref name="facts"/>,
    /// the synopsis, its credit (Wikipedia's whenever the synopsis is Wikipedia's, else
    /// <paramref name="credit"/>), Watch trailer, a poster-shaped frame and a wide backdrop.
    /// </summary>
    private void ShowTitleExtras(string facts, Description? synopsis, (string Text, Uri Link)? credit, Uri? trailer, bool poster, Uri? backdrop)
    {
        ShowText(LibraryFacts, facts);
        ShowText(LibraryOverview, synopsis?.Text ?? "");
        if (synopsis is { Source: DescriptionSource.Wikipedia } article)
        {
            credit = (Wikipedia.DescriptionCredit, article.Link);
        }
        else if (synopsis is { Source: DescriptionSource.OpenLibrary } book)
        {
            credit = ("Description from Open Library", book.Link);
        }

        LibraryCredit.Visibility = credit is null ? Visibility.Collapsed : Visibility.Visible;
        LibraryCredit.Content = credit?.Text;
        LibraryCredit.NavigateUri = credit?.Link;
        AutomationProperties.SetName(LibraryCredit, credit?.Text ?? "");
        _shownTrailer = trailer;
        LibraryTrailer.Visibility = trailer is null ? Visibility.Collapsed : Visibility.Visible;
        LibraryHeroArtBox.Height = poster ? 225 : 150;

        // A metadata refresh can arrive while the page is scrolled. Keep the compact header's
        // deliberate visibility choices instead of letting newly populated text expand it.
        if (_libraryHeaderCompact)
        {
            ApplyLibraryHeaderState();
        }

        var wide = backdrop?.AbsoluteUri ?? "";
        if (wide == _shownWide)
        {
            return;
        }

        _shownWide = wide;
        _wideWork?.Cancel();
        _wideWork?.Dispose();
        _wideWork = null;
        LibraryHeroWide.Source = null;
        LibraryHeroWide.Opacity = 0;
        if (backdrop is not null)
        {
            _wideWork = new CancellationTokenSource();
            ShowRemotePicture(backdrop, ShownWidth(LibraryHero), picture =>
            {
                if (_shownWide == wide)
                {
                    LibraryHeroWide.Source = picture;
                    LibraryHeroWide.Opacity = 0.6;
                }
            }, _wideWork.Token);
        }
    }

    /// <summary>
    /// The banner's pictures take its words' height (and never less than its least), so a title's
    /// synopsis can grow the banner while a picture, however tall, never stretches it.
    /// </summary>
    private void OnLibraryHeroSized(object sender, SizeChangedEventArgs e) =>
        LibraryHeroBackdrop.Height = LibraryHeroWide.Height = Math.Max(LibraryHero.MinHeight, e.NewSize.Height);

    private static void ShowText(TextBlock block, string text)
    {
        block.Text = text;
        block.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>A cast member's photo in the banner's frame, when their card had one.</summary>
    private void ShowHeroPhoto(string? photo)
    {
        if (TitleSections.AbsoluteUri(photo) is not { } uri)
        {
            return;
        }

        var hero = _shownHero;
        ShowRemotePicture(uri, (int)(150 * Scale), picture =>
        {
            if (_shownHero == hero)
            {
                (LibraryHeroArt.Source, LibraryHeroBackdrop.Source) = (picture, picture);
                LibraryHeroArt.Opacity = 1;
                LibraryHeroBackdrop.Opacity = 0.6;
            }
        }, _libraryPictures.Token);
    }

    /// <summary>
    /// A show's page: its facts and synopsis under the title with their credit, and over the
    /// episodes the season's overview and the cast row. Rebuilt only when what it shows changed.
    /// </summary>
    private void ShowShowDetails(LibraryGroup show, LibraryGroup? season)
    {
        var facts = ShowFacts(show.Name);
        if (_details.Count == 0)
        {
            // Under a page opened over the show, the page owns the banner.
            (string, Uri)? credit = TitleSections.FromTvMaze(facts) ? (TitleSections.TvMazeCredit, TitleSections.TvMazePage(facts!.CatalogueId)) : null;
            ShowTitleExtras(TitleFacts.Show(facts), TitleSections.ShowSynopsis(facts), credit, trailer: null, poster: false, backdrop: null);
        }

        var number = season?.Entries.Select(LibraryViews.EpisodeOf).FirstOrDefault(episode => episode is not null)?.Season;
        var overview = TitleSections.SeasonOverview(facts, number);
        var cast = TitleSections.CastRow(facts?.Cast ?? []);
        // A completed season must not suggest the whole series was watched.
        // Aggregate from the locally saved per-episode history, never from online metadata.
        var seasonProgress = season is null ? null : ShowPage.WatchSummary(season.Entries, _player.LeftAt).Describe("Season");
        var seriesProgress = ShowPage.WatchSummary(show.Entries, _player.LeftAt).Describe("Series");
        var viewing = string.Join(" · ", new[] { seasonProgress, seriesProgress }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var key = show.Name + "\n" + overview + "\n" + viewing + "\n" + string.Join("|", cast.Select(member => member.Name + member.Character + member.PhotoUrl));
        if (key == _shownShowHeader)
        {
            return;
        }

        _shownShowHeader = key;
        var header = new StackPanel { Spacing = 14, Padding = new Thickness(6, 4, 6, 14) };
        var progress = new TextBlock
        {
            Text = viewing,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 900,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetAutomationId(progress, "LibraryViewingProgress");
        AutomationProperties.SetName(progress, viewing);
        header.Children.Add(progress);
        if (overview is not null)
        {
            var text = new TextBlock { Text = overview, TextWrapping = TextWrapping.Wrap, MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetAutomationId(text, "LibrarySeasonOverview");
            header.Children.Add(text);
        }

        if (cast.Count > 0)
        {
            header.Children.Add(CastRow(cast));
        }

        LibraryList.Header = header;
    }

    /// <summary>Off a show's page and its details: the banner's extras and the list's header go; a page shown instead sets its own.</summary>
    private void ClearShowDetails()
    {
        if (_details.Count > 0)
        {
            return;
        }

        ShowTitleExtras("", null, credit: null, trailer: null, poster: false, backdrop: null);
        if (_shownShowHeader.Length > 0)
        {
            _shownShowHeader = "";
            LibraryList.Header = null;
        }
    }

    /// <summary>A horizontal, virtualised row of cast cards; a click opens everything that person appears in.</summary>
    private StackPanel CastRow(IReadOnlyList<CastMember> cast)
    {
        _detailsTemplates ??= new LibraryDetailsTemplates();
        var row = new StackPanel { Spacing = 6 };
        row.Children.Add(new TextBlock { Text = "Cast", Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] });
        var cards = new ListView
        {
            ItemsSource = cast.Select(member => new CastCard(member)).ToList(),
            ItemTemplate = (DataTemplate)_detailsTemplates["LibraryCastTemplate"],
            ItemsPanel = (ItemsPanelTemplate)XamlReader.Load("""<ItemsPanelTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><ItemsStackPanel Orientation="Horizontal" /></ItemsPanelTemplate>"""),
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = true,
            Height = 184,
            Padding = new Thickness(0),
            ItemContainerStyle = CastCardStyle(),
        };
        ScrollViewer.SetHorizontalScrollMode(cards, ScrollMode.Enabled);
        ScrollViewer.SetHorizontalScrollBarVisibility(cards, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollMode(cards, ScrollMode.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(cards, ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(cards, "Cast");
        AutomationProperties.SetAutomationId(cards, "LibraryCastRow");
        cards.ContainerContentChanging += OnCastCardShown;
        cards.ItemClick += (_, e) => OpenPerson(((CastCard)e.ClickedItem).Member);
        row.Children.Add(cards);
        return row;
    }

    /// <summary>Cards sit at the top of the row, side by side, with no list padding around them.</summary>
    private static Style CastCardStyle()
    {
        var style = new Style(typeof(ListViewItem));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0)));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 0d));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Top));
        style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top));
        return style;
    }

    /// <summary>A card coming on screen asks for its photo; one the row recycles lets it go.</summary>
    private void OnCastCardShown(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is not CastCard card)
        {
            return;
        }

        if (args.InRecycleQueue)
        {
            card.Release();
            return;
        }

        if (card.Photo is not null || card.PhotoWork is not null || TitleSections.AbsoluteUri(card.Member.PhotoUrl) is not { } photo)
        {
            return;
        }

        card.PhotoWork = CancellationTokenSource.CreateLinkedTokenSource(_libraryPictures.Token);
        ShowRemotePicture(photo, (int)(CastPhotoSide * Scale), picture => card.Photo = picture, card.PhotoWork.Token);
    }

    private double Scale => Content?.XamlRoot?.RasterizationScale ?? 1;

    /// <summary>How many pixels wide a picture filling <paramref name="frame"/> is decoded at.</summary>
    private int ShownWidth(FrameworkElement frame) => (int)(Math.Max(frame.ActualWidth, 960) * Scale);

    /// <summary>Shows a picture from the web once it is cached and decoded; a failure only leaves it out.</summary>
    private void ShowRemotePicture(Uri uri, int decodeWidth, Action<ImageSource> show, CancellationToken token)
    {
        Forget(ShowAsync());

        async Task ShowAsync()
        {
            try
            {
                if (await RemotePictureAsync(uri, decodeWidth, token) is { } picture && !token.IsCancellationRequested)
                {
                    show(picture);
                }
            }
            catch (OperationCanceledException)
            {
                // The screen moved on before the picture came.
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or COMException or ArgumentException or InvalidOperationException or NotSupportedException)
            {
                App.Log.Debug(MetadataLogSource, $"A picture from {uri.Host} could not be shown: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// A picture from the web decoded at <paramref name="decodeWidth"/>: from the metadata cache,
    /// or fetched into it first while a metadata switch is on; null when it cannot be had.
    /// </summary>
    private async Task<ImageSource?> RemotePictureAsync(Uri uri, int decodeWidth, CancellationToken token)
    {
        var cache = MetadataCacheNow;
        var key = uri.AbsoluteUri;
        var bytes = await Task.Run(() => cache.ReadBytes(RemoteArtKind, key, MetadataFetcher.ArtExtension), token);
        if (bytes is null && _settings.Metadata is { TvShows: true } or { Movies: true })
        {
            if (_pictureHttp is null)
            {
                _pictureHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                _pictureHttp.DefaultRequestHeaders.UserAgent.ParseAdd($"rexplayer/{Version} (https://github.com/tochi-mba/rexplayer)");
            }

            using var response = await _pictureHttp.GetAsync(uri, token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var fetched = await response.Content.ReadAsByteArrayAsync(token);
            await Task.Run(() => cache.Write(RemoteArtKind, key, MetadataFetcher.ArtExtension, fetched), token);
            bytes = fetched;
        }

        return bytes is { Length: > 0 } ? await DecodePictureAsync(bytes, decodeWidth, token) : null;
    }
}
