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
/// part-way, else what is watched most, else what arrived last) with Play and More, over horizontal
/// rows by category. <see cref="StreamingBrowse"/> decides what goes where; this file only draws it
/// on the same virtualised shelves Home uses, so artwork still loads lazily per visible card.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Whether the open view can be laid out as a streaming-style browse.</summary>
    private bool BrowseOffered => _libraryGroup is null && LibrarySearch.Text.Trim().Length == 0
        && _librarySource is LibrarySource.Movies or LibrarySource.TvShows;

    /// <summary>Movies or TV shows as a billboard over rows; rebuilt only when what it shows has changed.</summary>
    private void ShowStreamingBrowse(IReadOnlyList<LibraryEntry> entries, LibraryViewChoice choice)
    {
        ShowViewOptions(true, choice, groupsView: false, LibraryKind.Video);
        foreach (var control in new FrameworkElement[] { LibrarySortButton, LibraryGroupButton, LibraryFilterButton, LibrarySizePanel })
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
        string moreLabel;
        if (movies)
        {
            var browse = StreamingBrowse.Movies(entries, _player.LeftAt);
            billboardArt = browse.Billboard;
            billboardTitle = browse.Billboard is null ? "" : LibraryViews.MovieTitleOf(browse.Billboard);
            billboardLine = browse.Billboard is null ? "" : StreamingBrowse.MovieLine(browse.Billboard);
            if (browse.Billboard is { } movie)
            {
                play = () => Play([movie], 0, resume: true);
                more = () => RevealInCards(movie);
            }

            moreLabel = "Show in the grid";
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
                play = () => PlayShowFromNextUp(featured);
                more = () => OpenShow(featured);
            }

            moreLabel = "Episodes";
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
        var shown = "Browse\n" + billboardArt?.Path + "\n" + billboardTitle + "\n" + billboardLine + "\n"
            + string.Join("\n", shelves.Select(shelf => shelf.Title + ":" + string.Join(",", shelf.Rows.Select(RowIdentity))));
        if (shown == _shownLibrary)
        {
            return;
        }

        CancelLibraryPictures();
        _libraryRows = [];
        _homeShelfRows = [.. shelves.SelectMany(shelf => shelf.Rows)];
        _shownLibrary = shown;
        LibraryShelves.Children.Clear();
        if (billboardArt is not null)
        {
            LibraryShelves.Children.Add(Billboard(movies ? "FEATURED MOVIE" : "FEATURED SHOW",
                billboardTitle, billboardLine, billboardArt, play!, moreLabel, more!));
        }

        foreach (var (title, see, rows, wide, id) in shelves)
        {
            LibraryShelves.Children.Add(Shelf(title, see, rows, wide, seeAll: false, id: id));
        }
    }

    /// <summary>
    /// The billboard: the featured item's artwork filling a wide frame, a gradient keeping its
    /// words readable over any picture, its title and facts, and Play with one more action.
    /// </summary>
    private Grid Billboard(string kicker, string title, string line, LibraryEntry art, Action play, string moreLabel, Action more)
    {
        var frame = new Grid { Height = 320, CornerRadius = new CornerRadius(12), Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"] };
        AutomationProperties.SetAutomationId(frame, "LibraryBillboard");
        AutomationProperties.SetName(frame, title);
        var backdrop = new Image { Stretch = Stretch.UniformToFill };
        frame.Children.Add(backdrop);
        _ = ShowPictureAsync(art.Path, art.Kind, art.Duration, picture => backdrop.Source = picture, _libraryPictures.Token);

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
        var moreButton = new Button { Content = moreLabel };
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
        _libraryGroup = show;
        _librarySeason = null;
        ShowLibrary();
    }

    /// <summary>Shows the featured movie among all of them: the grid, with the movie selected and on screen.</summary>
    private void RevealInCards(LibraryEntry movie)
    {
        KeepViewChoice(ViewChoice(_libraryViewKey) with { Look = LibraryLook.Grid });
        RevealLibraryRow(movie.Path);
    }
}
