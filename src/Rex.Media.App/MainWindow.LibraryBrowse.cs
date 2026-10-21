using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Rex.Media.AppCore.Library;
using Rex.Media.Library;
using Rex.Media.Settings;

namespace Rex.Media.App;

/// <summary>
/// The streaming-style browse of the Movies and TV shows views: a featured billboard (what was left
/// part-way, else what is watched most, else what arrived last) with its synopsis, its facts, Play
/// and More info, over horizontal rows by category. <see cref="StreamingBrowse"/> decides what goes where; this file only draws it
/// on the same virtualised shelves Home uses, so artwork still loads lazily per visible card.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Whether the open view can be laid out as a streaming-style browse.</summary>
    private bool BrowseOffered => _libraryGroup is null && LibrarySearch.Text.Trim().Length == 0
        && _librarySource is LibrarySource.Movies or LibrarySource.TvShows;

    /// <summary>Movies or TV shows as a billboard over rows; rebuilt only when what it shows has changed.</summary>
    private void ShowStreamingBrowse(IReadOnlyList<LibraryEntry> entries, LibraryViewChoice choice, bool sameRoute)
    {
        ShowViewOptions(true, choice, groupsView: false, LibraryKind.Video);
        // Browse is a layout, not a dead end: ordering and filters remain available here. Grouping
        // and card size do not describe shelves, so only those layout-only controls are hidden.
        foreach (var control in new FrameworkElement[] { LibraryGroupButton, LibrarySizePanel })
        {
            control.Visibility = Visibility.Collapsed;
        }

        LibraryBack.Visibility = Visibility.Collapsed;
        LibraryFolderActions.Visibility = Visibility.Collapsed;

        var movies = _librarySource == LibrarySource.Movies;
        LibraryEntry? billboardArt;
        string billboardTitle, billboardLine;
        var shelves = new List<(string Title, LibrarySource See, IReadOnlyList<LibraryRow> Rows, bool Wide, string Id)>();
        Action? play = null, more = null;
        Description? synopsis = null;
        Uri? backdrop = null;
        if (movies)
        {
            var browse = StreamingBrowse.Movies(LibraryViews.Movies(entries, MovieLookupOf), _player.LeftAt);
            billboardArt = browse.Billboard;
            billboardTitle = browse.Billboard is null ? "" : LibraryViews.MovieTitleOf(browse.Billboard);
            billboardLine = "";
            if (browse.Billboard is { } movie)
            {
                var facts = MetadataMovie(movie);
                billboardLine = TitleFacts.Movie(movie, facts);
                synopsis = TitleSections.MovieSynopsis(facts);
                backdrop = TitleSections.AbsoluteUri(facts?.BackdropUrl);
                play = () => Play([movie], 0, resume: true);
                more = () => OpenMovieDetails(movie);
            }

            foreach (var shelf in browse.Shelves)
            {
                var continues = shelf.Title == "Continue watching";
                shelves.Add((shelf.Title, continues ? LibrarySource.ContinueWatching : LibrarySource.Movies,
                    [.. shelf.Entries.Select(EntryRow)], continues, "Movies-" + shelf.Title));
            }
        }
        else
        {
            var browse = StreamingBrowse.TvShows(entries, _player.LeftAt);
            billboardArt = browse.Billboard is { Entries.Count: > 0 } show ? show.Entries[0] : null;
            billboardTitle = browse.Billboard?.Name ?? "";
            billboardLine = browse.Billboard?.Detail ?? "";
            if (browse.Billboard is { } featured)
            {
                var facts = ShowFacts(featured.Name);
                billboardLine = TitleFacts.ShowLine(facts, featured.Detail ?? "");
                synopsis = TitleSections.ShowSynopsis(facts);
                play = () => PlayShowFromNextUp(featured);
                more = () => OpenShow(featured);
            }

            if (browse.ContinueWatching.Count > 0)
            {
                shelves.Add(("Continue watching", LibrarySource.ContinueWatching, [.. browse.ContinueWatching.Select(EntryRow)], true, "TvShows-Continue watching"));
            }

            foreach (var shelf in browse.Shelves)
            {
                shelves.Add((shelf.Title, LibrarySource.TvShows, [.. shelf.Groups.Select(GroupRow)], false, "TvShows-" + shelf.Title));
            }
        }

        ShowLibraryEmpty(shelves.Count == 0, false, _librarySource);
        var shown = "Browse\n" + billboardArt?.Path + "\n" + billboardTitle + "\n" + billboardLine + "\n" + synopsis + "\n" + backdrop + "\n"
            + string.Join("\n", shelves.Select(shelf => shelf.Title + ":" + string.Join(",", shelf.Rows.Select(RowIdentity))));
        if (shown == _shownLibrary)
        {
            return;
        }

        CancelLibraryPictures();
        _libraryRows = [];
        _homeShelfRows = [.. shelves.SelectMany(shelf => shelf.Rows)];
        _shownLibrary = shown;
        var place = sameRoute ? CapturePlace() : null;
        LibraryShelves.Children.Clear();
        if (billboardArt is not null)
        {
            LibraryShelves.Children.Add(Billboard(movies ? "FEATURED MOVIE" : "FEATURED SHOW",
                billboardTitle, billboardLine, synopsis, billboardArt, backdrop, play!, more!));
        }

        foreach (var (title, see, rows, wide, id) in shelves)
        {
            LibraryShelves.Children.Add(Shelf(title, see, rows, wide, seeAll: false, id: id));
        }

        if (place is not null)
        {
            ApplyPlaceAfterLayout(place);
        }
    }

    /// <summary>
    /// The billboard: the featured item's backdrop (else its own artwork) filling a wide frame, a
    /// gradient keeping its words readable over any picture, its title, facts and synopsis (with
    /// Wikipedia's credit when the synopsis is theirs), Play and More info.
    /// </summary>
    private Grid Billboard(string kicker, string title, string line, Description? synopsis, LibraryEntry art, Uri? wide, Action play, Action more)
    {
        var frame = new Grid { Height = 320, CornerRadius = new CornerRadius(12), Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"] };
        AutomationProperties.SetAutomationId(frame, "LibraryBillboard");
        AutomationProperties.SetName(frame, title);
        var backdrop = new Image { Stretch = Stretch.UniformToFill };
        frame.Children.Add(backdrop);
        var token = _libraryPictures.Token;
        Forget(ShowPictureAsync(art.Path, art.Kind, art.Duration, picture => backdrop.Source ??= picture, token));
        if (wide is not null)
        {
            // The backdrop, once it comes, takes the place of the artwork shown meanwhile.
            ShowRemotePicture(wide, ShownWidth(LibraryShelves), picture => backdrop.Source = picture, token);
        }

        var shade = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 1), EndPoint = new Windows.Foundation.Point(0.9, 0) };
        shade.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(0xD8, 0x10, 0x14, 0x1B), Offset = 0 });
        shade.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(0x00, 0x10, 0x14, 0x1B), Offset = 1 });
        frame.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle { Fill = shade });

        var words = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(28, 0, 28, 24), MaxWidth = 720 };
        words.Children.Add(new TextBlock
        {
            Text = kicker,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            CharacterSpacing = 80,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xDC, 0xE3, 0xED)),
        });
        var name = new TextBlock { Text = title, Style = (Style)Application.Current.Resources["TitleLargeTextBlockStyle"], TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 2, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) };
        AutomationProperties.SetAutomationId(name, "LibraryBillboardTitle");
        words.Children.Add(name);
        if (line.Length > 0)
        {
            var facts = new TextBlock { Text = line, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xDC, 0xE3, 0xED)) };
            AutomationProperties.SetAutomationId(facts, "LibraryBillboardLine");
            words.Children.Add(facts);
        }

        if (synopsis is not null)
        {
            var about = new TextBlock
            {
                Text = synopsis.Text,
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 3,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            };
            AutomationProperties.SetAutomationId(about, "LibraryBillboardSynopsis");
            words.Children.Add(about);
            if (synopsis.Source == DescriptionSource.Wikipedia)
            {
                var credit = new HyperlinkButton
                {
                    Content = Rex.Media.AppCore.Metadata.Wikipedia.DescriptionCredit,
                    NavigateUri = synopsis.Link,
                    Padding = new Thickness(0),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xDC, 0xE3, 0xED)),
                };
                AutomationProperties.SetAutomationId(credit, "LibraryBillboardCredit");
                AutomationProperties.SetName(credit, Rex.Media.AppCore.Metadata.Wikipedia.DescriptionCredit);
                words.Children.Add(credit);
            }
        }

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        var playButton = new Button { Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        playButton.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { new FontIcon { Glyph = "", FontSize = 14 }, new TextBlock { Text = "Play" } },
        };
        AutomationProperties.SetAutomationId(playButton, "LibraryBillboardPlay");
        playButton.Click += (_, _) => play();
        actions.Children.Add(playButton);
        var moreButton = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { new FontIcon { Glyph = "\uE946", FontSize = 14 }, new TextBlock { Text = "More info" } },
            },
        };
        AutomationProperties.SetAutomationId(moreButton, "LibraryBillboardMore");
        moreButton.Click += (_, _) => more();
        actions.Children.Add(moreButton);
        words.Children.Add(actions);
        frame.Children.Add(words);

        if (_player.LeftAt(art.Path) is { } left && art.Duration is { TotalSeconds: > 0 } length)
        {
            frame.Children.Add(new ProgressBar
            {
                Value = Math.Clamp(left / length, 0, 1),
                Maximum = 1,
                VerticalAlignment = VerticalAlignment.Bottom,
            });
        }

        return frame;
    }

    /// <summary>Plays a show from the episode to go on with, resuming one left part-way.</summary>
    private void PlayShowFromNextUp(LibraryGroup show)
    {
        if (StreamingBrowse.NextUp(show, _player.LeftAt) is { } episode)
        {
            var queue = LibraryViews.AutoplayVideos(show.Entries, episode).ToList();
            Play(queue, queue.FindIndex(entry => string.Equals(entry.Path, episode.Path, StringComparison.OrdinalIgnoreCase)), resume: true);
        }
    }

    /// <summary>Opens a show's own page from the billboard.</summary>
    private void OpenShow(LibraryGroup show)
    {
        KeepPlace();
        _libraryGroup = show;
        _librarySeason = null;
        ShowLibraryAtPlace();
    }
}
