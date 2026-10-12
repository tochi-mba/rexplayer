using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Rex.Media.AppCore;
using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Player;
using Windows.Storage.Streams;

namespace Rex.Media.App;

/// <summary>
/// Intro and credits: the skip button beside the picture, its options, the sections drawn on the
/// timeline and the boundary editor. Every decision is the controller's (<see cref="EpisodeSkipPrompt"/>);
/// this only shows it.
/// </summary>
public sealed partial class MainWindow
{
    private readonly List<MenuFlyoutItem> _episodeMarkerCommands = [];
    private readonly List<(ToggleMenuFlyoutItem Item, EpisodeSectionKind Kind)> _showSkipToggles = [];
    private readonly List<(Rectangle Mark, TimeSpan Start, TimeSpan End)> _sectionMarks = [];
    private string _shownSkip = "";
    private string _shownSectionMarks = "";

    private void AddEpisodeSectionMenus(IList<MenuFlyoutItemBase> items, string prefix)
    {
        var menu = new MenuFlyoutSubItem { Text = "Intro and credits" };
        AutomationProperties.SetAutomationId(menu, prefix + "EpisodeSections");
        void Add(string title, string id, Action action)
        {
            var item = new MenuFlyoutItem { Text = title, IsEnabled = _player.CanMarkEpisodeSections };
            AutomationProperties.SetAutomationId(item, prefix + id);
            item.Click += (_, _) => action();
            menu.Items.Add(item);
            _episodeMarkerCommands.Add(item);
        }

        void Toggle(string title, string id, EpisodeSectionKind kind)
        {
            var item = new ToggleMenuFlyoutItem { Text = title, IsEnabled = _player.CurrentEpisode is not null };
            AutomationProperties.SetAutomationId(item, prefix + id);
            item.Click += (_, _) =>
            {
                _player.SetShowSkipping(kind, item.IsChecked);
                ShowEpisodeSkip();
            };
            menu.Items.Add(item);
            _showSkipToggles.Add((item, kind));
        }

        Add("Mark intro start here", "IntroStart", () => _player.MarkEpisodeSection(EpisodeSectionKind.Intro, true));
        Add("Mark intro end here", "IntroEnd", () => _player.MarkEpisodeSection(EpisodeSectionKind.Intro, false));
        Add("Adjust intro...", "IntroAdjust", () => _ = AdjustEpisodeSectionAsync(EpisodeSectionKind.Intro));
        Add("Clear intro markers", "IntroClear", () => _player.ClearEpisodeSection(EpisodeSectionKind.Intro));
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("Mark credits start here", "CreditsStart", () => _player.MarkEpisodeSection(EpisodeSectionKind.Credits, true));
        Add("Mark credits end here", "CreditsEnd", () => _player.MarkEpisodeSection(EpisodeSectionKind.Credits, false));
        Add("End credits at end of video", "CreditsVideoEnd", () => _player.MarkEpisodeSection(EpisodeSectionKind.Credits, false, atVideoEnd: true));
        Add("Adjust credits...", "CreditsAdjust", () => _ = AdjustEpisodeSectionAsync(EpisodeSectionKind.Credits));
        Add("Clear credits markers", "CreditsClear", () => _player.ClearEpisodeSection(EpisodeSectionKind.Credits));
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("Not an intro (hide the detected one)", "IntroReject", () => ChangeProposalFeedback(EpisodeSectionKind.Intro, true));
        Add("Not credits (hide the detected ones)", "CreditsReject", () => ChangeProposalFeedback(EpisodeSectionKind.Credits, true));
        Add("Show the detected intro again", "IntroRestore", () => ChangeProposalFeedback(EpisodeSectionKind.Intro, false));
        Add("Show the detected credits again", "CreditsRestore", () => ChangeProposalFeedback(EpisodeSectionKind.Credits, false));
        Add("Suggest sections from labeled chapters...", "DetectChapters", () => _ = ConfirmChapterSuggestionsAsync());
        menu.Items.Add(new MenuFlyoutSeparator());
        Toggle("Always skip intros for this show", "ShowSkipIntros", EpisodeSectionKind.Intro);
        Toggle("Always skip credits for this show", "ShowSkipCredits", EpisodeSectionKind.Credits);
        Add("Skip settings for every show...", "SkipPreferences", () => _ = ShowPreferencesAsync());
        items.Add(new MenuFlyoutSeparator());
        items.Add(menu);
    }

    /// <summary>
    /// Brings the skip buttons and the timeline marks in line with the player. Called on every
    /// position change, so it only rebuilds what changed.
    /// </summary>
    private void ShowEpisodeSkip()
    {
        var allowed = _player.CanMarkEpisodeSections;
        foreach (var action in _episodeMarkerCommands)
        {
            action.IsEnabled = allowed;
        }

        var episode = _player.CurrentEpisode is not null && allowed;
        var skipping = _player.CurrentShowSkipping;
        foreach (var (item, kind) in _showSkipToggles)
        {
            item.IsEnabled = episode;
            item.IsChecked = kind == EpisodeSectionKind.Intro ? skipping.Intros : skipping.Credits;
        }

        ShowSectionMarks();
        var prompt = LibraryPane.Visibility == Visibility.Visible ? null : _player.SkipPrompt;
        var shown = prompt is null ? "" : string.Join("|", prompt.Kind, prompt.PrimaryText, prompt.Secondary,
            prompt.Detected, prompt.Target.Ticks, _player.NextEpisode, _keymap.Label(CommandCatalog.SkipSection));
        if (shown == _shownSkip)
        {
            return;
        }

        _shownSkip = shown;
        if (prompt is null)
        {
            EpisodeSkipButton.Visibility = Visibility.Collapsed;
            EpisodeNextButton.Visibility = Visibility.Collapsed;
            EpisodeSkipCancel.Visibility = Visibility.Collapsed;
            EpisodeSkipOptions.Visibility = Visibility.Collapsed;
            return;
        }

        var key = _keymap.Label(CommandCatalog.SkipSection);
        var nextTitle = _player.NextEpisode is { } next ? PlaylistItem.TitleOf(next) : null;
        EpisodeSkipButton.Content = prompt.PrimaryText;
        AutomationProperties.SetName(EpisodeSkipButton, prompt.PrimaryText);
        ToolTipService.SetToolTip(EpisodeSkipButton, (prompt.Primary switch
        {
            EpisodeSkipAction.NextEpisode => "Play " + nextTitle,
            EpisodeSkipAction.SkipCredits when prompt.Target < _player.Duration - EpisodeSkipPrompt.SceneAfterCreditsThreshold =>
                "Go to the scene after the credits",
            EpisodeSkipAction.SkipCredits => "Go to the end",
            _ => "Go to where the intro ends",
        }) + (key.Length > 0 ? $" ({key})" : ""));
        EpisodeSkipButton.Visibility = Visibility.Visible;

        EpisodeNextButton.Visibility = prompt.Secondary == EpisodeSkipAction.NextEpisode ? Visibility.Visible : Visibility.Collapsed;
        var nextKey = _keymap.Label(CommandCatalog.NextEpisode);
        ToolTipService.SetToolTip(EpisodeNextButton, "Play " + nextTitle + (nextKey.Length > 0 ? $" ({nextKey})" : ""));
        EpisodeSkipCancel.Visibility = prompt.AutomaticInSeconds is null ? Visibility.Collapsed : Visibility.Visible;

        EpisodeSkipOptions.Visibility = Visibility.Visible;
        var label = prompt.Kind == EpisodeSectionKind.Intro ? "intro" : "credits";
        AutomationProperties.SetName(EpisodeSkipOptions, (prompt.Detected ? "Review the detected " : "Adjust the saved ") + label);
        ToolTipService.SetToolTip(EpisodeSkipOptions, prompt.Detected
            ? "Save, adjust or reject the detected " + label
            : "Adjust or clear the saved " + label);
        EpisodeSkipOptions.Flyout = SkipOptions(prompt);
    }

    private MenuFlyout SkipOptions(EpisodeSkipPrompt prompt)
    {
        var kind = prompt.Kind;
        var menu = new MenuFlyout();
        var label = kind == EpisodeSectionKind.Intro ? "intro" : "credits";
        var candidate = _player.CurrentProposals?.Best(kind, _player.Duration);
        MenuFlyoutItem Command(string title, string id, Action action)
        {
            var item = new MenuFlyoutItem { Text = title };
            AutomationProperties.SetAutomationId(item, "EpisodeSkip" + id);
            item.Click += (_, _) => action();
            menu.Items.Add(item);
            return item;
        }

        if (prompt.Detected && candidate is not null)
        {
            menu.Items.Add(new MenuFlyoutItem
            {
                Text = string.Create(CultureInfo.InvariantCulture,
                    $"Found automatically, {candidate.Confidence:P0} sure: {Span(candidate.Start, candidate.End)}"),
                IsEnabled = false,
            });
            menu.Items.Add(new MenuFlyoutItem { Text = candidate.Reason, IsEnabled = false });
            Command("Looks right: save it for this episode", "Confirm", () =>
            {
                _player.ConfirmDetectedSection(kind);
                ShowEpisodeSkip();
            });
        }
        else
        {
            var saved = _player.CurrentSections;
            var (start, end) = kind == EpisodeSectionKind.Intro ? (saved.IntroStart, saved.IntroEnd) : (saved.CreditsStart, saved.CreditsEnd);
            menu.Items.Add(new MenuFlyoutItem
            {
                Text = start is { } from && end is { } to ? "Saved by you: " + Span(from, to) : "Saved by you",
                IsEnabled = false,
            });
        }

        Command("Adjust start and end...", "Adjust", () => _ = AdjustEpisodeSectionAsync(kind));
        if (prompt.Detected)
        {
            Command(kind == EpisodeSectionKind.Intro ? "Not an intro" : "Not credits", "Reject", () => ChangeProposalFeedback(kind, true));
        }
        else
        {
            Command("Clear the saved " + label, "Clear", () =>
            {
                _player.ClearEpisodeSection(kind);
                ShowEpisodeSkip();
            });
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        var always = new ToggleMenuFlyoutItem
        {
            Text = "Always skip " + (kind == EpisodeSectionKind.Intro ? "intros" : "credits") + " for this show",
            IsChecked = kind == EpisodeSectionKind.Intro ? _player.CurrentShowSkipping.Intros : _player.CurrentShowSkipping.Credits,
            IsEnabled = _player.CurrentEpisode is not null,
        };
        AutomationProperties.SetAutomationId(always, "EpisodeSkipAlways");
        always.Click += (_, _) =>
        {
            _player.SetShowSkipping(kind, always.IsChecked);
            ShowEpisodeSkip();
        };
        menu.Items.Add(always);
        Command("Skip settings for every show...", "Preferences", () => _ = ShowPreferencesAsync());
        return menu;
    }

    private static string Span(TimeSpan start, TimeSpan end) =>
        TimeText.Format(start, end) + " to " + TimeText.Format(end, end);

    /// <summary>
    /// The intro and credits as bands on the timeline: filled for sections the viewer saved,
    /// outlined for detected ones, so the two never look alike.
    /// </summary>
    private void ShowSectionMarks()
    {
        var duration = _player.Duration;
        var marks = new List<(EpisodeSectionKind Kind, TimeSpan Start, TimeSpan End, bool Saved)>();
        if (_player.CanMarkEpisodeSections && duration > TimeSpan.Zero)
        {
            var saved = _player.CurrentSections;
            var proposals = _player.CurrentProposals;
            foreach (var kind in new[] { EpisodeSectionKind.Intro, EpisodeSectionKind.Credits })
            {
                var (start, end) = kind == EpisodeSectionKind.Intro ? (saved.IntroStart, saved.IntroEnd) : (saved.CreditsStart, saved.CreditsEnd);
                if (start is { } from && end is { } to && to > from)
                {
                    marks.Add((kind, from, to, true));
                }
                else if (start is null && end is null && proposals?.Best(kind, duration) is { } candidate)
                {
                    marks.Add((kind, candidate.Start, candidate.End, false));
                }
            }
        }

        var shown = duration.Ticks + ";" + string.Join(";", marks.Select(mark => $"{mark.Kind}{mark.Start.Ticks}-{mark.End.Ticks}{mark.Saved}"));
        if (shown == _shownSectionMarks)
        {
            return;
        }

        _shownSectionMarks = shown;
        SectionMarkers.Children.Clear();
        _sectionMarks.Clear();
        var accent = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        foreach (var (kind, start, end, saved) in marks)
        {
            var mark = new Rectangle
            {
                Height = 6,
                RadiusX = 2,
                RadiusY = 2,
                Fill = saved ? accent : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Stroke = accent,
                StrokeThickness = saved ? 0 : 1.5,
                StrokeDashArray = saved ? null : [2, 1.5],
                Opacity = saved ? 0.85 : 0.95,
                IsHitTestVisible = false,
            };
            var what = kind == EpisodeSectionKind.Intro ? "Intro" : "Credits";
            AutomationProperties.SetAutomationId(mark, "SectionMarker-" + what);
            AutomationProperties.SetName(mark, $"{what}, {(saved ? "saved" : "detected")}, {Span(start, end)}");
            SectionMarkers.Children.Add(mark);
            _sectionMarks.Add((mark, start, end));
        }

        AutomationProperties.SetName(SectionMarkers, marks.Count == 0 ? "No intro or credits marked"
            : string.Join(", ", marks.Select(mark => $"{(mark.Kind == EpisodeSectionKind.Intro ? "intro" : "credits")} {(mark.Saved ? "saved" : "detected")} {Span(mark.Start, mark.End)}")));
        PositionSectionMarks();
    }

    private void PositionSectionMarks()
    {
        var duration = _player.Duration.TotalSeconds;
        var width = SectionMarkers.ActualWidth;
        if (duration <= 0 || width <= 0)
        {
            return;
        }

        foreach (var (mark, start, end) in _sectionMarks)
        {
            var left = Math.Clamp(start.TotalSeconds / duration, 0, 1) * width;
            var right = Math.Clamp(end.TotalSeconds / duration, 0, 1) * width;
            mark.Width = Math.Max(3, right - left);
            Canvas.SetLeft(mark, left);
            Canvas.SetTop(mark, (SectionMarkers.ActualHeight - mark.Height) / 2);
        }
    }

    /// <summary>Feedback is remembered independently of saved manual boundaries.</summary>
    private void ChangeProposalFeedback(EpisodeSectionKind kind, bool reject)
    {
        if (!_player.RejectDetectedSection(kind, reject))
        {
            Say("There is no detected section to change.");
            return;
        }

        Say(reject ? "Hidden for this episode. It will not be offered again." :
            "Detection shown again. Sections you saved still come first.");
        ShowEpisodeSkip();
    }

    private void OnSkipEpisodeSection(object sender, RoutedEventArgs args)
    {
        _player.SkipEpisodeSection();
        ShowEpisodeSkip();
    }

    private void OnNextEpisode(object sender, RoutedEventArgs args)
    {
        _player.PlayNextEpisode();
        ShowEpisodeSkip();
    }

    private void OnCancelAutomaticSkip(object sender, RoutedEventArgs args)
    {
        _player.CancelAutomaticSkip();
        ShowEpisodeSkip();
    }

    /// <summary>
    /// Exact in and out points with the picture at each: the first picture of the section and the
    /// first one after it, decoded from the file, so the viewer sees what each boundary cuts.
    /// </summary>
    private async Task AdjustEpisodeSectionAsync(EpisodeSectionKind kind)
    {
        if (!_player.CanMarkEpisodeSections)
        {
            return;
        }

        var editingItem = _player.Item;
        var confirmed = _player.CurrentSections;
        var proposed = _player.CurrentProposals?.Best(kind, _player.Duration);
        var start = kind == EpisodeSectionKind.Intro ? confirmed.IntroStart : confirmed.CreditsStart;
        var end = kind == EpisodeSectionKind.Intro ? confirmed.IntroEnd : confirmed.CreditsEnd;
        var startAt = start ?? proposed?.Start ?? _player.Position;
        var endAt = end ?? proposed?.End ?? TimeSpan.FromTicks(Math.Min(_player.Duration.Ticks,
            startAt.Ticks + TimeSpan.FromSeconds(60).Ticks));
        var label = kind == EpisodeSectionKind.Intro ? "Intro" : "Credits";
        NumberBox Time(string header, TimeSpan value, string id)
        {
            var box = new NumberBox
            {
                Header = header,
                Value = Math.Round(value.TotalSeconds, 2),
                Minimum = 0,
                Maximum = _player.Duration.TotalSeconds,
                SmallChange = 0.25,
                LargeChange = 5,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            };
            AutomationProperties.SetAutomationId(box, id);
            AutomationProperties.SetName(box, header);
            return box;
        }

        var startInput = Time("Starts at (seconds)", startAt, "SectionAdjustStart");
        var endInput = Time("Ends at (seconds)", endAt, "SectionAdjustEnd");
        var startPicture = BoundaryPicture("SectionAdjustStartPicture", "First picture of the " + label.ToLowerInvariant());
        var endPicture = BoundaryPicture("SectionAdjustEndPicture", "First picture after it");
        var hereStart = new Button { Content = "Use the current position" };
        var hereEnd = new Button { Content = "Use the current position" };
        AutomationProperties.SetAutomationId(hereStart, "SectionAdjustStartHere");
        AutomationProperties.SetAutomationId(hereEnd, "SectionAdjustEndHere");
        AutomationProperties.SetName(hereStart, "Start at the current position");
        AutomationProperties.SetName(hereEnd, "End at the current position");
        hereStart.Click += (_, _) => startInput.Value = Math.Round(_player.Position.TotalSeconds, 2);
        hereEnd.Click += (_, _) => endInput.Value = Math.Round(_player.Position.TotalSeconds, 2);

        var previews = new CancellationTokenSource();
        startInput.ValueChanged += (_, _) => _ = ShowBoundaryAsync(startPicture.Image, startInput.Value, previews.Token);
        endInput.ValueChanged += (_, _) => _ = ShowBoundaryAsync(endPicture.Image, endInput.Value, previews.Token);
        _ = ShowBoundaryAsync(startPicture.Image, startInput.Value, previews.Token);
        _ = ShowBoundaryAsync(endPicture.Image, endInput.Value, previews.Token);

        var columns = new Grid { ColumnSpacing = 12 };
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel { Spacing = 6 };
        left.Children.Add(startInput);
        left.Children.Add(hereStart);
        left.Children.Add(startPicture.Panel);
        var right = new StackPanel { Spacing = 6 };
        right.Children.Add(endInput);
        right.Children.Add(hereEnd);
        right.Children.Add(endPicture.Panel);
        Grid.SetColumn(right, 1);
        columns.Children.Add(left);
        columns.Children.Add(right);
        var panel = new StackPanel { Spacing = 10, MinWidth = 440 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Set where the {label.ToLowerInvariant()} starts and ends. The arrows move a quarter of a second. What you save here comes before anything detected.",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(columns);
        try
        {
            // The episode may have changed while the editor was open: never save onto another one.
            if (await Ask("Adjust the " + label.ToLowerInvariant(), panel, "Save", "Cancel") &&
                ReferenceEquals(editingItem, _player.Item))
            {
                if (!double.IsFinite(startInput.Value) || !double.IsFinite(endInput.Value) ||
                    startInput.Value < 0 || startInput.Value > _player.Duration.TotalSeconds ||
                    endInput.Value < 0 || endInput.Value > _player.Duration.TotalSeconds ||
                    !_player.SetSectionBounds(kind, TimeSpan.FromSeconds(startInput.Value),
                        TimeSpan.FromSeconds(endInput.Value)))
                {
                    Say("The end must be at least two seconds after the start and inside the video.");
                }

                ShowEpisodeSkip();
            }
        }
        finally
        {
            previews.Cancel();
            previews.Dispose();
        }
    }

    private static (StackPanel Panel, Image Image) BoundaryPicture(string id, string caption)
    {
        var image = new Image { Stretch = Stretch.Uniform, Height = 112 };
        AutomationProperties.SetAutomationId(image, id);
        AutomationProperties.SetName(image, caption);
        var frame = new Border { Background = new SolidColorBrush(Microsoft.UI.Colors.Black), CornerRadius = new CornerRadius(4), Child = image };
        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(frame);
        panel.Children.Add(new TextBlock { Text = caption, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] });
        return (panel, image);
    }

    /// <summary>Decodes the picture at a boundary off the UI thread; a newer value wins.</summary>
    private async Task ShowBoundaryAsync(Image target, double seconds, CancellationToken token)
    {
        var location = _player.Item?.Location;
        if (double.IsNaN(seconds) || location is null || !File.Exists(location) || !HasVideo)
        {
            return;
        }

        var stamp = target.Tag = new object();
        try
        {
            await Task.Delay(150, token);
            var png = await Task.Run(() => PreviewPng(location, TimeSpan.FromSeconds(seconds), token), token);
            if (token.IsCancellationRequested || !ReferenceEquals(target.Tag, stamp))
            {
                return;
            }

            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(png);
                await writer.StoreAsync();
                writer.DetachStream();
            }

            stream.Seek(0);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            if (!token.IsCancellationRequested && ReferenceEquals(target.Tag, stamp))
            {
                target.Source = bitmap;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The editor closed.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Rex.Media.Primitives.MediaFormatException or NotSupportedException or System.Runtime.InteropServices.COMException)
        {
            App.Log.Debug(LogSource, "No boundary picture: " + ex.Message);
        }
    }

    /// <summary>Chapter names are hints only. The viewer approves the actual intervals before saving.</summary>
    private async Task ConfirmChapterSuggestionsAsync()
    {
        var editingItem = _player.Item;
        if (_player.ChapterSectionSuggestions is not { } found)
        {
            Say("No unambiguous intro or credits chapters were found. You can mark the sections manually.");
            return;
        }

        static string Label(string section, TimeSpan? start, TimeSpan? end) =>
            start is { } from && end is { } to
                ? section + ": " + from.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture) + " to " + to.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
                : section + ": not identified";
        var details = string.Join(Environment.NewLine,
            Label("Intro", found.IntroStart, found.IntroEnd),
            Label("Credits", found.CreditsStart, found.CreditsEnd));
        var description = new TextBlock
        {
            Text = details + Environment.NewLine + Environment.NewLine +
                "These come from the chapter names in this file. Sections you already saved stay as they are.",
            TextWrapping = TextWrapping.Wrap,
        };
        if (await Ask("Save the suggested section markers?", description, "Save markers", "Cancel") &&
            ReferenceEquals(editingItem, _player.Item))
        {
            _player.ApplyChapterSectionSuggestions();
            ShowEpisodeSkip();
        }
    }
}
