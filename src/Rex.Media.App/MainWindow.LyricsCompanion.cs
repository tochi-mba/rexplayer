using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Rex.Media.AppCore.Player;

namespace Rex.Media.App;

/// <summary>
/// An independently resizable lyrics window, with its own keyboard focus and
/// native per-monitor DPI. It never takes ownership of the player or the audio
/// session; closing this view cannot stop, dispose or seek its owner.
/// </summary>
public sealed partial class MainWindow
{
    private Window? _lyricsCompanion;
    private ScrollViewer? _lyricsCompanionScroll;
    private StackPanel? _lyricsCompanionContent;
    private TextBlock? _lyricsCompanionHeading;
    private Button? _lyricsCompanionFollow;
    private Lyrics? _lyricsCompanionSource;
    private readonly List<TextBlock> _lyricsCompanionTexts = [];
    private readonly List<FrameworkElement> _lyricsCompanionRows = [];
    private readonly Dictionary<int, List<Run>> _lyricsCompanionWords = [];
    private int _lyricsCompanionLine = -1;
    private int _lyricsCompanionWord = -2;
    private bool _lyricsCompanionFollows = true;

    private void OnOpenLyricsCompanion(object sender, RoutedEventArgs e)
    {
        if (_closing || _closed)
        {
            return;
        }

        if (_lyricsCompanion is { } alreadyOpen)
        {
            alreadyOpen.Activate();
            return;
        }

        var window = new Window { Title = "Lyrics · rexplayer" };
        var root = new Grid { Padding = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 0, 0, 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new TextBlock
        {
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(heading, "CompanionSong");
        header.Children.Add(heading);

        var follow = new Button { Content = "Follow current line", Visibility = Visibility.Collapsed };
        AutomationProperties.SetAutomationId(follow, "CompanionFollow");
        AutomationProperties.SetName(follow, "Resume following the current lyric line");
        Grid.SetColumn(follow, 1);
        header.Children.Add(follow);
        root.Children.Add(header);

        var lines = new StackPanel { Spacing = 8, Padding = new Thickness(0, 8, 0, 8) };
        var scroll = new ScrollViewer
        {
            Content = lines,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        AutomationProperties.SetAutomationId(scroll, "CompanionLyrics");
        AutomationProperties.SetName(scroll, "Detached lyrics");
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        scroll.AddHandler(UIElement.PointerWheelChangedEvent,
            new PointerEventHandler((_, _) => PauseCompanionFollow()), handledEventsToo: true);
        scroll.AddHandler(UIElement.PointerPressedEvent,
            new PointerEventHandler((_, _) => PauseCompanionFollow()), handledEventsToo: true);
        scroll.AddHandler(UIElement.KeyDownEvent,
            new KeyEventHandler((_, args) =>
            {
                if (args.Key is Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down
                    or Windows.System.VirtualKey.PageUp or Windows.System.VirtualKey.PageDown
                    or Windows.System.VirtualKey.Home or Windows.System.VirtualKey.End)
                {
                    PauseCompanionFollow();
                }
            }), handledEventsToo: true);
        scroll.SizeChanged += (_, _) =>
        {
            if (_lyricsCompanionFollows)
            {
                BringCompanionLyricIntoView();
            }
        };
        follow.Click += (_, _) =>
        {
            _lyricsCompanionFollows = true;
            follow.Visibility = Visibility.Collapsed;
            BringCompanionLyricIntoView();
        };

        _lyricsCompanion = window;
        _lyricsCompanionScroll = scroll;
        _lyricsCompanionContent = lines;
        _lyricsCompanionHeading = heading;
        _lyricsCompanionFollow = follow;
        _lyricsCompanionFollows = true;
        _lyricsCompanionSource = null;
        _lyricsCompanionLine = -1;
        _lyricsCompanionWord = -2;
        window.Content = root;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_lyricsCompanion, window))
            {
                _lyricsCompanion = null;
                _lyricsCompanionScroll = null;
                _lyricsCompanionContent = null;
                _lyricsCompanionHeading = null;
                _lyricsCompanionFollow = null;
                _lyricsCompanionSource = null;
                _lyricsCompanionTexts.Clear();
                _lyricsCompanionRows.Clear();
                _lyricsCompanionWords.Clear();
                LyricsDetachButton.Content = "Open lyrics window";
            }
        };
        // Let Windows place the independent window on a connected display:
        // deliberately do not restore stale absolute coordinates when a
        // second monitor has been disconnected since the last opening.
        window.Activate();
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(460, 620));
        LyricsDetachButton.Content = "Focus lyrics window";
        RefreshLyricsCompanion();
    }

    private void CloseLyricsCompanion()
    {
        var window = _lyricsCompanion;
        if (window is not null)
        {
            window.Close();
        }
    }

    private void PauseCompanionFollow()
    {
        if (_lyricsCompanionSource is not { IsTimed: true })
        {
            return;
        }

        _lyricsCompanionFollows = false;
        if (_lyricsCompanionFollow is { } follow)
        {
            follow.Visibility = Visibility.Visible;
        }
    }

    private void BringCompanionLyricIntoView()
    {
        var index = _player.LyricIndex;
        if (index >= 0 && index < _lyricsCompanionRows.Count)
        {
            _lyricsCompanionRows[index].StartBringIntoView(new BringIntoViewOptions
            {
                VerticalAlignmentRatio = 0.5,
            });
        }
    }

    /// <summary>
    /// Called by track and position changes on the owner window thread. Never
    /// holds onto media files or registers callbacks on the player itself.
    /// </summary>
    private void RefreshLyricsCompanion()
    {
        if (_closed || _closing || _lyricsCompanion is not { } window
            || _lyricsCompanionContent is not { } content || _lyricsCompanionHeading is not { } heading)
        {
            return;
        }

        var lyrics = _settings.ShowLyrics && !HasVideo ? _player.Lyrics : null;
        var trackName = _player.Item?.Title ?? "No track";
        window.Title = trackName + " · Lyrics · rexplayer";
        heading.Text = trackName;
        if (!ReferenceEquals(_lyricsCompanionSource, lyrics))
        {
            _lyricsCompanionSource = lyrics;
            _lyricsCompanionLine = -1;
            _lyricsCompanionWord = -2;
            _lyricsCompanionFollows = true;
            if (_lyricsCompanionFollow is { } follow)
            {
                follow.Visibility = Visibility.Collapsed;
            }

            _lyricsCompanionTexts.Clear();
            _lyricsCompanionRows.Clear();
            _lyricsCompanionWords.Clear();
            content.Children.Clear();
            if (lyrics is null || lyrics.Lines.Count == 0)
            {
                content.Children.Add(new TextBlock
                {
                    Text = _player.Info is null ? "Loading local lyrics…" : "No lyrics are available for this track.",
                    FontSize = 16,
                    TextWrapping = TextWrapping.Wrap,
                });
            }
            else
            {
                var evidence = lyrics.DisplayMode switch
                {
                    LyricsDisplayMode.Plain => "Untimed source: no timestamps",
                    LyricsDisplayMode.LineTimed => "Source-timed lines",
                    LyricsDisplayMode.WordTimed => "Source-timed words",
                    LyricsDisplayMode.MixedTimed => "Some words have source timings",
                    _ => "Lyrics",
                };
                content.Children.Add(new TextBlock { Text = evidence, Opacity = 0.75, FontSize = 13 });
                foreach (var (index, line) in lyrics.Lines.Index())
                {
                    var text = new TextBlock
                    {
                        Text = line.Text.Length == 0 ? " " : line.Text,
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 20,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)),
                    };
                    if (line.Words.Count > 0)
                    {
                        text.Text = "";
                        var words = new List<Run>();
                        foreach (var segment in line.Words)
                        {
                            var run = new Run { Text = segment.Text };
                            text.Inlines.Add(run);
                            words.Add(run);
                        }

                        _lyricsCompanionWords[index] = words;
                    }

                    FrameworkElement row = text;
                    if (line.At is { } at)
                    {
                        var button = new Button
                        {
                            Content = text,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            HorizontalContentAlignment = HorizontalAlignment.Stretch,
                            Padding = new Thickness(10, 8, 10, 8),
                        };
                        AutomationProperties.SetAutomationId(button, $"CompanionLine-{index}");
                        AutomationProperties.SetName(button, $"Go to {TimeText.Format(at, _player.Duration)}: {line.Text}");
                        button.Click += (_, _) =>
                        {
                            _player.Seek(at);
                            _lyricsCompanionFollows = true;
                            if (_lyricsCompanionFollow is { } follow)
                            {
                                follow.Visibility = Visibility.Collapsed;
                            }
                        };
                        row = button;
                    }

                    _lyricsCompanionTexts.Add(text);
                    _lyricsCompanionRows.Add(row);
                    content.Children.Add(row);
                }
            }
        }

        var current = _player.LyricIndex;
        if (current != _lyricsCompanionLine)
        {
            if (_lyricsCompanionLine >= 0 && _lyricsCompanionLine < _lyricsCompanionTexts.Count)
            {
                _lyricsCompanionTexts[_lyricsCompanionLine].FontWeight = FontWeights.Normal;
                _lyricsCompanionTexts[_lyricsCompanionLine].Foreground =
                    new SolidColorBrush(Windows.UI.Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));
            }

            _lyricsCompanionLine = current;
            _lyricsCompanionWord = -2;
            if (current >= 0 && current < _lyricsCompanionTexts.Count)
            {
                _lyricsCompanionTexts[current].FontWeight = FontWeights.SemiBold;
                if (_lyricsCompanionFollows)
                {
                    BringCompanionLyricIntoView();
                }
            }
        }

        if (lyrics is not null && current >= 0 && current < _lyricsCompanionTexts.Count)
        {
            var accent = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            if (_lyricsCompanionWords.TryGetValue(current, out var runs))
            {
                var word = Lyrics.WordIndexAt(lyrics.Lines[current], _player.Position);
                if (_lyricsCompanionWord != word)
                {
                    _lyricsCompanionWord = word;
                    var neutral = new SolidColorBrush(Windows.UI.Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));
                    for (var i = 0; i < runs.Count; i++)
                    {
                        runs[i].Foreground = i <= word ? accent : neutral;
                    }
                }
            }
            else
            {
                _lyricsCompanionTexts[current].Foreground = accent;
            }
        }
    }
}
