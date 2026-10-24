using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;
using static Rex.Media.AppCore.Library.LibraryActions;

namespace Rex.Media.App;

/// <summary>
/// The streaming-style music Home (LIB-05): a hero for the album or artist the user is into lately,
/// tinted from its artwork, over rows of square album, mix and playlist cards and round artist
/// portraits. <see cref="MusicHome"/> decides what goes where, locally; this file draws it on
/// horizontally virtualised rows whose artwork loads per visible card and is let go when a card
/// scrolls away. Cards lift on hover and focus, and their play button plays without opening.
/// </summary>
public sealed partial class MainWindow
{
    private const double MusicCardWidth = 176;
    private const double MusicCardHeight = 232;

    private LibraryMusicTemplates? _musicTemplates;
    private long _musicHeroRequest;

    /// <summary>The music pages' templates, created the first time one is laid out.</summary>
    private DataTemplate MusicTemplate(string key)
    {
        if (_musicTemplates is null)
        {
            _musicTemplates = new LibraryMusicTemplates(this);
            // The playing mark's equaliser holds still while playback is paused.
            PlayingBars.SetPaused(!_player.IsPlaying);
            _player.Changed += (_, _) =>
            {
                var paused = !_player.IsPlaying;
                if (paused != _barsPaused)
                {
                    _barsPaused = paused;
                    OnWindowThread(() => PlayingBars.SetPaused(paused));
                }
            };
        }

        return (DataTemplate)_musicTemplates[key];
    }

    private volatile bool _barsPaused;

    /// <summary>Draws the music Home; rebuilt only when what it shows has changed.</summary>
    private void ShowMusicHome(IReadOnlyList<LibraryEntry> entries, bool sameRoute)
    {
        ShowViewOptions(false, ViewChoice("Music"), false, LibraryKind.Music);
        LibraryBack.Visibility = LibraryFolderActions.Visibility = Visibility.Collapsed;
        LibraryPlay.Visibility = LibraryShuffle.Visibility = LibraryEnqueue.Visibility = Visibility.Collapsed;
        _libraryRows = [];

        var home = MusicHome.Rows(entries, _player.NamedPlaylists, DateTime.UtcNow,
            name => MetadataArtist(name)?.Genres ?? [], _settings.PinnedAlbumPaths);
        var shelves = new List<(string Title, string Id, IReadOnlyList<MusicRow> Rows)>
        {
            ("Pinned albums", "Pinned", [.. home.Pinned.Select(AlbumCard)]),
            ("Jump back in", "JumpBackIn", [.. home.JumpBackIn.Select(place => place.Album is { } album ? AlbumCard(album) : PlaylistCard(place.Playlist!))]),
            ("On repeat", "OnRepeat", [.. home.OnRepeat.Select(song => SongCard(song, home.OnRepeat))]),
            ("Recently added", "RecentlyAdded", [.. home.RecentlyAdded.Select(AlbumCard)]),
            ("Made for you", "Mixes", [.. home.Mixes.Select(MixCard)]),
            ("Albums you haven't played in a while", "NotLately", [.. home.NotLately.Select(AlbumCard)]),
            ("New to your library", "NewArtists", [.. home.NewArtists.Select(ArtistCard)]),
        };
        shelves.RemoveAll(shelf => shelf.Rows.Count == 0);
        ShowLibraryEmpty(home.Hero is null && shelves.Count == 0, false, LibrarySource.Music);

        // The route can stay identical while a background tag scan corrects
        // titles, credits, artwork or current-playback badges. Include the
        // actually displayed facts, not just paths, in this idempotence key.
        var shown = "Music\n" + home.Hero?.Group.Name + "|" + home.Hero?.IsArtist
            + "|" + home.Hero?.Group.Detail + "|" + home.Hero?.Reason + "\n"
            + string.Join("\n", shelves.Select(shelf => shelf.Title + ":" + string.Join(",", shelf.Rows.Select(row =>
                RowIdentity(row) + "|" + row.Name + "|" + row.Detail + "|" + row.PicturePath + "|" + row.IsPlaying))));
        if (shown == _shownLibrary)
        {
            return;
        }

        CancelLibraryPictures();
        _homeShelfRows = [.. shelves.SelectMany(shelf => shelf.Rows)];
        _shownLibrary = shown;
        var place = sameRoute ? CapturePlace() : null;
        LibraryShelves.Children.Clear();
        if (home.Hero is { } hero)
        {
            LibraryShelves.Children.Add(MusicHero(hero));
        }

        foreach (var (title, id, rows) in shelves)
        {
            LibraryShelves.Children.Add(MusicShelf(title, id, rows));
        }

        if (place is not null)
        {
            ApplyPlaceAfterLayout(place);
        }
    }

    /// <summary>
    /// The hero: the featured album's cover (or the artist's portrait) beside its name and facts,
    /// over a backdrop tinted from that picture, with Play, Shuffle and a way to open its page.
    /// </summary>
    private Grid MusicHero(MusicPick pick)
    {
        var group = pick.Group;
        var frame = new Grid { MinHeight = 268, CornerRadius = new CornerRadius(14), Padding = new Thickness(28), ColumnSpacing = 28, Background = TintBrush(AppCore.Visuals.DominantColour.Fallback) };
        frame.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        frame.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AutomationProperties.SetAutomationId(frame, "MusicHero");
        AutomationProperties.SetName(frame, group.Name);

        var photo = pick.IsArtist ? ArtistPhotoPath(group.Name) : null;
        var art = new Border
        {
            Width = 212,
            Height = 212,
            CornerRadius = new CornerRadius(pick.IsArtist ? 106 : 10),
            Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        var picture = new Image { Stretch = Stretch.UniformToFill, Opacity = 0, OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(200) } };
        art.Child = new Grid { Children = { new TextBlock { Text = "♪", FontSize = 64, Opacity = 0.3, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, picture } };
        frame.Children.Add(art);
        ShowCardPicture(group.Entries.FirstOrDefault(), photo, image => (picture.Source, picture.Opacity) = (image, 1), _libraryPictures.Token);

        var request = ++_musicHeroRequest;
        var token = _libraryPictures.Token;
        Forget(TintHeroAsync());
        async Task TintHeroAsync()
        {
            var tint = await HeaderTintAsync(group.Entries.FirstOrDefault(), photo, token);
            if (request == _musicHeroRequest && !token.IsCancellationRequested)
            {
                frame.Background = TintBrush(tint);
            }
        }

        var words = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(words, 1);
        words.Children.Add(new TextBlock
        {
            Text = pick.Reason.ToUpper(CultureInfo.CurrentCulture),
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            CharacterSpacing = 80,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xDC, 0xE3, 0xED)),
        });
        var name = new TextBlock { Text = group.Name, Style = (Style)Application.Current.Resources["TitleLargeTextBlockStyle"], TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 2, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) };
        AutomationProperties.SetAutomationId(name, "MusicHeroTitle");
        words.Children.Add(name);
        var line = pick.IsArtist
            ? string.Join(" · ", Count(LibraryViews.Albums(group.Entries).Count, "album"), Count(group.Entries.Count, "song"))
            : string.Join(" · ", new[] { ArtistNames.Display(group.Detail), AlbumPage.Line(group, AlbumPage.Kind(group, MetadataAlbum(group.Detail, group.Name)), AlbumPage.Year(group, MetadataAlbum(group.Detail, group.Name))) }.Where(part => !string.IsNullOrEmpty(part)));
        var facts = new TextBlock { Text = line, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xDC, 0xE3, 0xED)), TextTrimming = TextTrimming.CharacterEllipsis };
        AutomationProperties.SetAutomationId(facts, "MusicHeroLine");
        words.Children.Add(facts);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
        actions.Children.Add(HeroButton("Play", "", "MusicHeroPlay", accent: true, () => PlayGroup(group, shuffle: false)));
        actions.Children.Add(HeroButton("Shuffle", "", "MusicHeroShuffle", accent: false, () => PlayGroup(group, shuffle: true)));
        actions.Children.Add(HeroButton(pick.IsArtist ? "Go to artist" : "Open album", "", "MusicHeroOpen", accent: false, () => OpenMusicGroup(group, pick.IsArtist)));
        if (!pick.IsArtist)
        {
            actions.Children.Add(HeroButton(AlbumIsPinned(group) ? "Unpin album" : "Pin album", "",
                "MusicHeroPin", accent: false, () => ToggleAlbumPin(group)));
        }
        words.Children.Add(actions);
        frame.Children.Add(words);
        return frame;
    }

    private static Button HeroButton(string text, string glyph, string id, bool accent, Action click)
    {
        var button = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new FontIcon { Glyph = glyph, FontSize = 14 }, new TextBlock { Text = text } } },
        };
        if (accent)
        {
            button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        }

        AutomationProperties.SetAutomationId(button, id);
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>One row: its title over cards that scroll sideways, virtualised, each card's art asked for when it is realised and let go when it is recycled.</summary>
    private StackPanel MusicShelf(string title, string id, IReadOnlyList<MusicRow> rows)
    {
        var shelf = new StackPanel { Spacing = 6 };
        shelf.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] });
        var style = CardStyle(MusicCardWidth, MusicCardHeight);
        var cards = new ListView
        {
            ItemsSource = rows,
            ItemTemplate = MusicTemplate("MusicCardTemplate"),
            ItemsPanel = (ItemsPanelTemplate)XamlReader.Load("""<ItemsPanelTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><ItemsStackPanel Orientation="Horizontal" /></ItemsPanelTemplate>"""),
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = true,
            Height = MusicCardHeight + 16,
            ItemContainerStyle = style,
        };
        ScrollViewer.SetHorizontalScrollMode(cards, ScrollMode.Enabled);
        ScrollViewer.SetHorizontalScrollBarVisibility(cards, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollMode(cards, ScrollMode.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(cards, ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(cards, title);
        AutomationProperties.SetAutomationId(cards, "MusicShelf-" + id);
        cards.ContainerContentChanging += OnMusicCardShown;
        cards.ItemClick += (_, e) => (e.ClickedItem as MusicRow)?.Open?.Invoke();
        shelf.Children.Add(cards);
        return shelf;
    }

    /// <summary>A realised card asks for its picture once; a recycled one lets its picture go, so off-screen cards hold none.</summary>
    private void OnMusicCardShown(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is not MusicRow row)
        {
            return;
        }

        if (args.InRecycleQueue)
        {
            row.Picture = null;
            row.PictureAsked = false;
            return;
        }

        if (!row.PictureAsked)
        {
            row.PictureAsked = true;
            ShowCardPicture(PictureEntry(row.Item), row.PicturePath, picture => row.Picture = picture, _libraryPictures.Token);
        }
    }

    /// <summary>Shows a card's picture: a fetched photo from its file, else the media's own artwork through the library's picture pipeline.</summary>
    private void ShowCardPicture(LibraryEntry? art, string? photo, Action<ImageSource> show, CancellationToken token)
    {
        if (photo is not null)
        {
            Forget(ShowPhotoAsync());
        }
        else if (art is not null)
        {
            Forget(ShowPictureAsync(art.Path, art.Kind, art.Duration, show, token));
        }

        async Task ShowPhotoAsync()
        {
            if (await LocalPictureAsync(photo, _budget.LibraryPictureSide, token) is { } image && !token.IsCancellationRequested)
            {
                show(image);
            }
            else if (art is not null)
            {
                await ShowPictureAsync(art.Path, art.Kind, art.Duration, show, token);
            }
        }
    }

    /// <summary>The card's play button: plays what the card holds without opening it.</summary>
    internal static void OnMusicCardPlay(object sender)
    {
        if (sender is FrameworkElement { DataContext: MusicRow row })
        {
            row.PlayNow?.Invoke();
        }
    }

    internal void OnMusicRowPlay(object sender, RoutedEventArgs e) => OnRowPlay(sender, e);

    internal void OnMusicRowQueue(object sender, RoutedEventArgs e) => OnRowQueue(sender, e);

    /// <summary>A pin is a path from the album, never a title that could match another artist.</summary>
    private bool AlbumIsPinned(LibraryGroup album) => album.Entries.Any(entry =>
        _settings.PinnedAlbumPaths.Contains(entry.Path, StringComparer.OrdinalIgnoreCase));

    /// <summary>Pin or unpin a real album, persist the change, and redraw the current library view.</summary>
    private void ToggleAlbumPin(LibraryGroup album)
    {
        if (album.Entries.FirstOrDefault()?.Path is not { Length: > 0 } representative)
        {
            return;
        }

        var paths = _settings.PinnedAlbumPaths.Where(path =>
            !album.Entries.Any(entry => string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase))).ToList();
        var removing = paths.Count < _settings.PinnedAlbumPaths.Count;
        if (!removing)
        {
            paths.Insert(0, representative);
        }

        _settings = _settings with { PinnedAlbumPaths = paths.Take(40).ToArray() };
        _player.Settings = _settings;
        SaveSettings();
        _shownLibrary = "";
        _shownMusicPage = "";
        ShowLibrary();
        Say(removing ? "Album removed from Music Home pins" : "Album pinned to Music Home");
    }

    private MusicRow AlbumCard(LibraryGroup album) => new(album.Name, ArtistNames.Display(album.Detail) ?? Count(album.Entries.Count, "song"), "", album)
    {
        Open = () => OpenMusicGroup(album, artist: false),
        PlayNow = () => PlayGroup(album, shuffle: false),
    };

    private MusicRow ArtistCard(LibraryGroup artist) => new(artist.Name, "Artist", "", artist)
    {
        Round = true,
        PicturePath = ArtistPhotoPathIfKnown(artist.Name),
        Open = () => OpenMusicGroup(artist, artist: true),
        PlayNow = () => PlayGroup(artist, shuffle: true),
    };

    private MusicRow SongCard(LibraryEntry song, IReadOnlyList<LibraryEntry> row) => new(song.Title, ArtistNames.Display(song.Artist) ?? "", "", song,
        isPlaying: LibraryViews.SameLocation(song.Path, _player.Playlist.Current?.Location))
    {
        Open = () => Play(row, IndexOf(row, song)),
        PlayNow = () => Play(row, IndexOf(row, song)),
    };

    private MusicRow MixCard(MusicMix mix) => new(mix.Title, mix.Line, "", new LibraryGroup(mix.Title, mix.Line, mix.Songs))
    {
        Open = () => Play(mix.Songs, 0),
        PlayNow = () => Play(mix.Songs, 0),
    };

    private MusicRow PlaylistCard(NamedPlaylist playlist)
    {
        var entries = playlist.Items.Select(item => _library?.Entry(item.Location)).OfType<LibraryEntry>().ToList();
        return new(playlist.Name, "Playlist · " + Count(playlist.Items.Count, "song"), "", entries.Count > 0 ? new LibraryGroup(playlist.Name, null, entries) : playlist)
        {
            Open = () => _player.PlayPlaylist(playlist.Id),
            PlayNow = () => _player.PlayPlaylist(playlist.Id),
        };
    }

    private static int IndexOf(IReadOnlyList<LibraryEntry> row, LibraryEntry song) =>
        Math.Max(0, row.ToList().FindIndex(entry => string.Equals(entry.Path, song.Path, StringComparison.OrdinalIgnoreCase)));

    /// <summary>An artist's photo when the cache is known to hold one; only memory is read, the file is checked when the card is drawn.</summary>
    private string? ArtistPhotoPathIfKnown(string name) => MetadataArtist(name) is { HasPhoto: true }
        ? MetadataCacheNow.PathFor(Rex.Media.AppCore.Metadata.MetadataFetcher.ArtistPhotoKind, Rex.Media.AppCore.Metadata.MetadataFetcher.ArtistKey(name), Rex.Media.AppCore.Metadata.MetadataFetcher.ArtExtension)
        : null;

    /// <summary>Plays an album or an artist from the start, or shuffled.</summary>
    private void PlayGroup(LibraryGroup group, bool shuffle)
    {
        var songs = LibraryViews.Songs(group.Entries);
        if (songs.Count == 0)
        {
            Say("There is nothing here to play yet.");
            return;
        }

        if (shuffle)
        {
            _player.Playlist.Shuffle = true;
        }

        Play(songs, shuffle ? Random.Shared.Next(songs.Count) : 0);
        if (shuffle)
        {
            Say("Shuffle on");
        }
    }

    /// <summary>Opens an album's page (in Albums) or an artist's page (in Artists), as a card on a shelf does.</summary>
    private void OpenMusicGroup(LibraryGroup group, bool artist)
    {
        var source = artist ? LibrarySource.Artists : LibrarySource.Albums;
        KeepLibraryParent();
        _places.RememberSearch(source.ToString(), "");
        _changingLibraryRoute = true;
        _librarySource = source;
        _librarySeason = null;
        _libraryGroup = group;
        LibrarySources.SelectedIndex = Array.FindIndex(LibrarySourceItems, pair => pair.Source == source);
        _changingLibraryRoute = false;
        _libraryGroup = group;
        ShowLibraryAtPlace();
    }
}
