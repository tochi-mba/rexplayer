using Rex.Media.AppCore.Design;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Rex.Media.AppCore.Library;
using Rex.Media.Library;
using Rex.Media.Settings;

namespace Rex.Media.App;

/// <summary>
/// The horizontal Browse layout for every root media view. Movies and TV shows add a featured
/// billboard and curated categories; the other views retain their own grouping as shelves. All use
/// the same virtualised rows as Home, so artwork still loads lazily per visible card.
/// </summary>
public sealed partial class MainWindow
{
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _browseHeroTimer;
    private int _browseHeroIndex;
    private string _browseHeroSet = "";
    // Visual regression tests capture the actual native UI; they only freeze the
    // rotating hero and fade animation so a slow runner cannot change the frame.
    private static bool FixedBrowseFixture => Environment.GetEnvironmentVariable("REXPLAYER_UI_CAPTURE") == "1";
    private bool _browseHeroHeld;
    private bool _browseHeroPointerHeld;
    private bool _browseHeroFocusHeld;

    /// <summary>How tall the band is where the billboard's ink fades in above its words.</summary>
    private const double BillboardFade = 40;

    /// <summary>Whether the open view can be laid out as a streaming-style browse.</summary>
    private bool BrowseOffered => _libraryGroup is null && LibrarySearch.Text.Trim().Length == 0
        && _librarySource is not (LibrarySource.Home or LibrarySource.Music or LibrarySource.Playlists or LibrarySource.Folders);

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
        var shows = _librarySource == LibrarySource.TvShows;
        LibraryEntry? billboardArt = null;
        var billboardTitle = "";
        var billboardLine = "";
        var shelves = new List<(string Title, LibrarySource See, IReadOnlyList<LibraryRow> Rows, bool Wide, string Id)>();
        Action? play = null, more = null;
        var playLabel = "Play";
        Description? synopsis = null;
        Uri? backdrop = null;
        if (movies)
        {
            var browse = StreamingBrowse.Movies(LibraryViews.Movies(entries, MovieLookupOf), _player.LeftAt);
            SetBrowseHeroSet(string.Join("\n", browse.Features.Select(feature => feature.Path)), browse.Features.Count);
            billboardArt = PickFeature(browse.Features);
            billboardTitle = billboardArt is null ? "" : LibraryViews.MovieTitleOf(billboardArt);
            billboardLine = "";
            if (billboardArt is { } movie)
            {
                var facts = MetadataMovie(movie);
                billboardLine = TitleFacts.Movie(movie, facts);
                synopsis = TitleSections.MovieSynopsis(facts);
                backdrop = TitleSections.AbsoluteUri(facts?.BackdropUrl);
                play = () => Play([movie], 0, resume: true);
                playLabel = _player.LeftAt(movie.Path) is null ? "Play" : "Resume";
                more = () => OpenMovieDetails(movie);
            }

            foreach (var shelf in browse.Shelves)
            {
                var continues = shelf.Title == "Continue watching";
                // Featured and topical shelves have their own meaningful order. "All movies"
                // is the ordinary browsable collection: honor its saved sort and Reverse here,
                // just as List, Cards and Wall do.
                var ordered = shelf.Title == "All movies"
                    ? LibraryArrangement.Sort(shelf.Entries, choice.Sort, choice.Descending)
                    : shelf.Entries;
                shelves.Add((shelf.Title, continues ? LibrarySource.ContinueWatching : LibrarySource.Movies,
                    [.. ordered.Select(EntryRow)], continues, "Movies-" + shelf.Title));
            }
        }
        else if (shows)
        {
            var browse = StreamingBrowse.TvShows(entries, _player.LeftAt);
            SetBrowseHeroSet(string.Join("\n", browse.Features.Select(feature => feature.Name)), browse.Features.Count);
            var featured = PickFeature(browse.Features);
            billboardArt = featured is { Entries.Count: > 0 } show ? show.Entries[0] : null;
            billboardTitle = featured?.Name ?? "";
            billboardLine = featured?.Detail ?? "";
            if (featured is not null)
            {
                var facts = ShowFacts(featured.Name);
                billboardLine = TitleFacts.ShowLine(facts, featured.Detail ?? "");
                synopsis = TitleSections.ShowSynopsis(facts);
                play = () => PlayShowFromNextUp(featured);
                if (StreamingBrowse.NextUp(featured, _player.LeftAt) is { } next)
                {
                    var episode = LibraryViews.EpisodeOf(next);
                    var context = episode is null ? "" : $" S{episode.Season:00} E{episode.Episode:00}";
                    playLabel = (_player.LeftAt(next.Path) is null ? "Play" : "Resume") + context;
                }
                more = () => OpenShow(featured);
            }

            if (browse.ContinueWatching.Count > 0)
            {
                shelves.Add(("Continue watching", LibrarySource.ContinueWatching, [.. browse.ContinueWatching.Select(EntryRow)], true, "TvShows-Continue watching"));
            }

            foreach (var shelf in browse.Shelves)
            {
                var ordered = shelf.Title == "All shows"
                    ? LibraryArrangement.Sort(shelf.Groups, choice.Sort, choice.Descending)
                    : shelf.Groups;
                shelves.Add((shelf.Title, LibrarySource.TvShows, [.. ordered.Select(GroupRow)], false, "TvShows-" + shelf.Title));
            }
        }
        else
        {
            // A non-catalogue Browse view is the same collection and ordering as its list/card
            // view, expressed as horizontal shelves. Listening views retain useful shelves such as
            // Continue listening, while ordinary media respects its chosen sort and grouping.
            SetBrowseHeroSet("", 0);
            foreach (var shelf in BrowseShelves(entries, choice).Where(shelf => shelf.Rows.Count > 0))
            {
                shelves.Add((shelf.Title, shelf.Source, shelf.Rows, shelf.Wide,
                    _librarySource + "-" + shelf.Title));
            }
        }

        // A filtered Browse page needs the same clear-filter escape hatch as a List,
        // Cards, Wall or Collage page, including when every shelf disappears.
        ShowLibraryEmpty(shelves.Count == 0, false, _librarySource, choice);
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
                billboardTitle, billboardLine, synopsis, billboardArt, backdrop, playLabel, play!, more!));
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

    private IReadOnlyList<(string Title, LibrarySource Source, IReadOnlyList<LibraryRow> Rows, bool Wide)> BrowseShelves(
        IReadOnlyList<LibraryEntry> entries, LibraryViewChoice choice)
    {
        if (IsListening(_librarySource))
        {
            var listening = ListeningView(entries).Sections;
            if (_librarySource == LibrarySource.Podcasts)
            {
                var allowed = entries.Select(entry => entry.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var latest = ListeningViews.NewEpisodes(Listening(), _player.LeftAt, DateTime.UtcNow)
                    .Where(episode => allowed.Contains(episode.Path)).Select(PodcastEpisodeRow).ToList();
                return [.. (latest.Count == 0 ? [] : new[] { ("New episodes", LibrarySource.Podcasts, (IReadOnlyList<LibraryRow>)latest, false) }),
                    .. listening.Where(section => section.Count > 0).Select(section =>
                        (section.Header.Length > 0 ? section.Header : "Podcast shows", LibrarySource.Podcasts, (IReadOnlyList<LibraryRow>)[.. section], false))];
            }

            var fallback = LibrarySourceItems.First(pair => pair.Source == _librarySource).Item.Name;
            return [.. listening.Where(section => section.Count > 0).Select(section =>
                (section.Header.Length > 0 ? section.Header : fallback, _librarySource, (IReadOnlyList<LibraryRow>)[.. section], false))];
        }

        if (_librarySource == LibrarySource.Completed)
        {
            return CompletedShelves(entries);
        }

        if (_librarySource is LibrarySource.Albums or LibrarySource.Artists or LibrarySource.Genres)
        {
            var groups = LibraryArrangement.Sort(Groups(_librarySource, entries), choice.Sort, choice.Descending);
            List<LibraryRowGroup> groupSections = choice.Grouping == LibraryGrouping.Letter
                ? [.. groups.GroupBy(group => LibraryArrangement.LetterOf(group.Name))
                    .OrderBy(group => group.Key == "#").ThenBy(group => group.Key, StringComparer.CurrentCulture)
                    .Select(group => new LibraryRowGroup(group.Key, group.Select(GroupRow)))]
                : [new LibraryRowGroup("", groups.Select(GroupRow))];
            var fallback = LibrarySourceItems.First(pair => pair.Source == _librarySource).Item.Name;
            return [.. groupSections.Select(section =>
                (section.Header.Length > 0 ? section.Header : fallback, _librarySource, (IReadOnlyList<LibraryRow>)[.. section], false))];
        }

        if (_librarySource == LibrarySource.Videos)
        {
            var videos = LibraryMemo.Videos(entries);
            var movies = LibraryViews.Movies(videos, MovieLookupOf);
            var episodes = LibraryViews.TvShows(videos);
            var claimed = movies.Select(movie => movie.Path).Concat(episodes.SelectMany(show => show.Entries).Select(episode => episode.Path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var other = videos.Where(video => !claimed.Contains(video.Path)).ToList();
            return [
                ("Movies", LibrarySource.Movies, [.. movies.Select(EntryRow)], false),
                ("TV shows", LibrarySource.TvShows, [.. episodes.Select(GroupRow)], false),
                ("Other videos", LibrarySource.OtherVideos, [.. other.Select(EntryRow)], true),
            ];
        }

        IReadOnlyList<LibraryEntry> media = _librarySource switch
        {
            LibrarySource.Songs => LibraryMemo.Songs(entries),
            LibrarySource.OtherVideos => LibraryViews.OtherVideos(entries, MovieLookupOf),
            LibrarySource.Pictures => LibraryMemo.Pictures(entries),
            LibrarySource.ContinueWatching => LibraryViews.ContinueWatching(entries, _player.LeftAt),
            LibrarySource.RecentlyPlayed => LibraryViews.RecentlyPlayed(entries, ExistingRecent()),
            LibrarySource.RecentlyAdded => LibraryMemo.RecentlyAdded(entries),
            _ => entries,
        };

        if (_librarySource is LibrarySource.RecentlyPlayed or LibrarySource.RecentlyAdded)
        {
            return [
                ("Audio", LibrarySource.Songs, [.. media.Where(entry => entry.Kind == LibraryKind.Music).Select(EntryRow)], false),
                ("Videos", LibrarySource.Videos, [.. media.Where(entry => entry.Kind == LibraryKind.Video).Select(EntryRow)], true),
                ("Pictures", LibrarySource.Pictures, [.. media.Where(entry => entry.Kind == LibraryKind.Picture).Select(EntryRow)], false),
            ];
        }

        if (_librarySource == LibrarySource.Songs && choice.Grouping == LibraryGrouping.None)
        {
            return [
                ("Recently added", LibrarySource.Songs, [.. LibraryViews.RecentlyAdded(media).Take(30).Select(EntryRow)], false),
                ("Most played", LibrarySource.Songs, [.. media.OrderByDescending(entry => entry.Plays).ThenBy(entry => entry.Title, StringComparer.CurrentCultureIgnoreCase).Take(30).Select(EntryRow)], false),
                ("All songs", LibrarySource.Songs, [.. LibraryArrangement.Sort(media, choice.Sort, choice.Descending).Select(EntryRow)], false),
            ];
        }

        var arranged = _librarySource == LibrarySource.Pictures && choice.Grouping == LibraryGrouping.None
            ? choice with { Grouping = LibraryGrouping.Added }
            : choice;
        var sections = EntrySections(media, arranged);
        var title = LibrarySourceItems.First(pair => pair.Source == _librarySource).Item.Name;
        var wide = _librarySource is LibrarySource.OtherVideos or LibrarySource.ContinueWatching;
        return [.. sections.Select(section =>
            (section.Header.Length > 0 ? section.Header : title, _librarySource, (IReadOnlyList<LibraryRow>)[.. section], wide))];
    }

    private T? PickFeature<T>(IReadOnlyList<T> features) => features.Count == 0 ? default : features[_browseHeroIndex % features.Count];

    /// <summary>Keeps a stable hero selection until the slow rotation advances it.</summary>
    private void SetBrowseHeroSet(string identity, int count)
    {
        if (!string.Equals(identity, _browseHeroSet, StringComparison.Ordinal))
        {
            _browseHeroSet = identity;
            _browseHeroIndex = 0;
        }

        _browseHeroTimer ??= Timer(TimeSpan.FromSeconds(12), AdvanceBrowseHero);
        _browseHeroTimer.IsRepeating = true;
        Run(_browseHeroTimer, count > 1 && !_browseHeroHeld && AppWindow.IsVisible && !FixedBrowseFixture);
    }

    private void AdvanceBrowseHero()
    {
        if (FixedBrowseFixture || _browseHeroHeld || !AppWindow.IsVisible || !BrowseOffered)
        {
            return;
        }

        _browseHeroIndex++;
        _shownLibrary = "";
        ShowLibrary();
    }

    private void StopBrowseHeroRotation()
    {
        if (_browseHeroTimer is not null)
        {
            _browseHeroTimer.Stop();
        }
    }

    private void EndBrowseHeroRotation()
    {
        StopBrowseHeroRotation();
        _browseHeroPointerHeld = false;
        _browseHeroFocusHeld = false;
        _browseHeroHeld = false;
    }

    private void UpdateBrowseHeroHold()
    {
        _browseHeroHeld = _browseHeroPointerHeld || _browseHeroFocusHeld;
        if (_browseHeroHeld)
        {
            StopBrowseHeroRotation();
        }
        else if (!FixedBrowseFixture && _browseHeroTimer is not null && AppWindow.IsVisible && BrowseOffered)
        {
            _browseHeroTimer.Start();
        }
    }

    /// <summary>
    /// The billboard: the featured item's backdrop (else its own artwork) filling a wide frame, a
    /// gradient keeping its words readable over any picture, its title, facts and synopsis (with
    /// Wikipedia's credit when the synopsis is theirs), Play and More info.
    /// </summary>
    private Grid Billboard(string kicker, string title, string line, Description? synopsis, LibraryEntry art, Uri? wide, string playLabel, Action play, Action more)
    {
        // Respect the user's system text colors instead of drawing white text
        // over a dark photograph while high contrast is on.
        var highContrast = _libraryContrastSettings.HighContrast;
        var policy = BrowseHeroPresentation.For(highContrast, _budget.Animations);
        var colors = policy.UsesSystemColors ? new Windows.UI.ViewManagement.UISettings() : null;
        var systemText = colors is null ? null : new SolidColorBrush(colors.GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground));
        var standardLabel = systemText ?? RexTheme.Brush("RexOverlaySecondaryTextBrush");
        var standardTitle = systemText ?? RexTheme.Brush("RexOverlayTextBrush");
        var background = colors is null
            ? RexTheme.Brush("LayerFillColorDefaultBrush")
            : new SolidColorBrush(colors.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background));
        // As tall as leaves the first shelf in view (FitBillboard keeps it so), and taller only when its words need it.
        var frame = new Grid { MinHeight = LibraryHeaderLayout.BillboardHeight(LibraryHome.ActualHeight), CornerRadius = new CornerRadius(12), Background = background };
        AutomationProperties.SetAutomationId(frame, "LibraryBillboard");
        AutomationProperties.SetName(frame, title);
        frame.Tapped += (_, args) =>
        {
            // Routed Tapped also bubbles out of the hero's Play, More info and
            // Wikipedia actions. These buttons own their action: a Play tap
            // must not also navigate to the movie/show information screen.
            for (var target = args.OriginalSource as DependencyObject;
                target is not null && !ReferenceEquals(target, frame);
                target = VisualTreeHelper.GetParent(target))
            {
                if (target is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase)
                {
                    return;
                }
            }

            more();
        };
        frame.PointerEntered += (_, _) =>
        {
            _browseHeroPointerHeld = true;
            UpdateBrowseHeroHold();
        };
        frame.PointerExited += (_, _) =>
        {
            _browseHeroPointerHeld = false;
            UpdateBrowseHeroHold();
        };
        frame.GotFocus += (_, _) =>
        {
            _browseHeroFocusHeld = true;
            UpdateBrowseHeroHold();
        };
        frame.LostFocus += (_, _) =>
        {
            // A focus move between the hero's buttons is still inside the hero. Check after
            // focus settles rather than restarting the carousel between keyboard actions.
            DispatcherQueue.TryEnqueue(() =>
            {
                _browseHeroFocusHeld = HasFocusIn(frame);
                UpdateBrowseHeroHold();
            });
        };
        if (policy.FadesIn && !FixedBrowseFixture)
        {
            frame.Opacity = 0;
            var fade = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(240), EasingFunction = new QuadraticEase() };
            Storyboard.SetTarget(fade, frame);
            Storyboard.SetTargetProperty(fade, "Opacity");
            var transition = new Storyboard();
            transition.Children.Add(fade);
            frame.Loaded += (_, _) => transition.Begin();
        }
        if (policy.DrawsBackdrop)
        {
            // A system-colored solid frame is the correct fallback under high contrast. The picture is a
            // brush, so however tall it is it fills the frame without making it taller.
            var backdrop = new ImageBrush { Stretch = Stretch.UniformToFill };
            frame.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle { Fill = backdrop });
            var token = _libraryPictures.Token;
            Forget(ShowPictureAsync(art.Path, art.Kind, art.Duration, picture => backdrop.ImageSource ??= picture, token));
            if (wide is not null)
            {
                // The backdrop, once it comes, takes the place of the artwork shown meanwhile.
                ShowRemotePicture(wide, ShownWidth(LibraryShelves), picture => backdrop.ImageSource = picture, token);
            }
        }

        var words = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(28, 0, 28, 24), MaxWidth = 720 };
        words.Children.Add(new TextBlock
        {
            Text = kicker,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            CharacterSpacing = 80,
            Foreground = standardLabel,
        });
        var name = new TextBlock { Text = title, Style = (Style)Application.Current.Resources["TitleLargeTextBlockStyle"], TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 2, Foreground = standardTitle };
        AutomationProperties.SetAutomationId(name, "LibraryBillboardTitle");
        words.Children.Add(name);
        if (line.Length > 0)
        {
            var facts = new TextBlock { Text = line, Foreground = standardLabel };
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
                Foreground = standardTitle,
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
                    Foreground = standardLabel,
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
            Children = { new FontIcon { Glyph = "", FontSize = 14 }, new TextBlock { Text = playLabel } },
        };
        AutomationProperties.SetAutomationId(playButton, "LibraryBillboardPlay");
        AutomationProperties.SetName(playButton, playLabel + " " + title);
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
        AutomationProperties.SetName(moreButton, "More info about " + title);
        moreButton.Click += (_, _) => more();
        actions.Children.Add(moreButton);
        words.Children.Add(actions);
        frame.Children.Add(BillboardShade(words, policy.DrawsBackdrop));

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

    /// <summary>
    /// The billboard's words on REX ink as opaque as the banner's plate, fading out above them, so they
    /// read over any picture; the words, and Play and More info with them, take the dark theme. Under
    /// high contrast they sit on the system's own colours instead.
    /// </summary>
    private static Grid BillboardShade(StackPanel words, bool shaded)
    {
        var shade = new Grid { VerticalAlignment = VerticalAlignment.Bottom };
        shade.RowDefinitions.Add(new RowDefinition { Height = new GridLength(BillboardFade) });
        shade.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (shaded)
        {
            var ink = RexTheme.Color(DesignTokens.WithAlpha(DesignTokens.DarkInk[0], HeroPlateAlpha));
            var fade = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(0, 1) };
            fade.GradientStops.Add(new GradientStop { Color = RexTheme.Color(DesignTokens.WithAlpha(DesignTokens.DarkInk[0], 0)), Offset = 0 });
            fade.GradientStops.Add(new GradientStop { Color = ink, Offset = 1 });
            shade.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle { Fill = fade });
            var plate = new Microsoft.UI.Xaml.Shapes.Rectangle { Fill = new SolidColorBrush(ink) };
            Grid.SetRow(plate, 1);
            shade.Children.Add(plate);
            shade.RequestedTheme = ElementTheme.Dark;
        }

        Grid.SetRow(words, 1);
        shade.Children.Add(words);
        return shade;
    }

    /// <summary>The billboard keeps the first shelf in view as the shelves' viewport changes.</summary>
    private void FitBillboard(double shelves)
    {
        if (LibraryShelves.Children.Count > 0 && LibraryShelves.Children[0] is Grid frame
            && AutomationProperties.GetAutomationId(frame) == "LibraryBillboard")
        {
            frame.MinHeight = LibraryHeaderLayout.BillboardHeight(shelves);
        }
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
        KeepLibraryParent();
        _libraryGroup = show;
        _librarySeason = null;
        ShowLibraryAtPlace();
    }
}
