using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Player;
using Windows.Storage.Streams;

namespace Rex.Media.App;

/// <summary>
/// Intro and credits: the skip button beside the picture, its options, the sections drawn on the
/// timeline and the boundary editor. Every decision is made in AppCore (<see cref="EpisodeSkipPrompt"/>,
/// <see cref="EpisodeSectionView"/>); this only shows it.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The section commands in the menus, and how each is brought up to date.</summary>
    private readonly List<Action> _sectionMenuUpdates = [];
    private readonly List<(Rectangle Mark, TimeSpan Start, TimeSpan End)> _sectionMarks = [];
    private string _shownSkip = "";
    private string _shownSectionMarks = "";

    private void AddEpisodeSectionMenus(IList<MenuFlyoutItemBase> items, string prefix)
    {
        var menu = new MenuFlyoutSubItem { Text = "Intro and credits" };
        AutomationProperties.SetAutomationId(menu, prefix + "EpisodeSections");
        void Add(string title, string id, Action action, Func<bool> enabled)
        {
            var item = new MenuFlyoutItem { Text = title, IsEnabled = enabled() };
            AutomationProperties.SetAutomationId(item, prefix + id);
            item.Click += (_, _) =>
            {
                action();
                ShowEpisodeSkip();
            };
            menu.Items.Add(item);
            _sectionMenuUpdates.Add(() => item.IsEnabled = enabled());
        }

        void Toggle(string title, string id, Action<bool> action, Func<bool> enabled, Func<bool> chosen)
        {
            var item = new ToggleMenuFlyoutItem { Text = title, IsEnabled = enabled(), IsChecked = chosen() };
            AutomationProperties.SetAutomationId(item, prefix + id);
            item.Click += (_, _) =>
            {
                action(item.IsChecked);
                ShowEpisodeSkip();
            };
            menu.Items.Add(item);
            _sectionMenuUpdates.Add(() =>
            {
                item.IsEnabled = enabled();
                item.IsChecked = chosen();
            });
        }

        bool Video() => _player.CanMarkEpisodeSections;
        EpisodeSectionView? View(EpisodeSectionKind kind) => _player.SectionViews.FirstOrDefault(view => view.Kind == kind);
        bool Episode() => Video() && _player.CurrentEpisode is not null;
        foreach (var kind in new[] { EpisodeSectionKind.Intro, EpisodeSectionKind.Credits })
        {
            var (name, noun) = kind == EpisodeSectionKind.Intro ? ("Intro", "intro") : ("Credits", "credits");
            Add($"Mark {noun} start here", name + "Start", () => _player.MarkEpisodeSection(kind, true), Video);
            Add($"Mark {noun} end here", name + "End", () => _player.MarkEpisodeSection(kind, false), Video);
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        foreach (var kind in new[] { EpisodeSectionKind.Intro, EpisodeSectionKind.Credits })
        {
            var (name, noun) = kind == EpisodeSectionKind.Intro ? ("Intro", "intro") : ("Credits", "credits");
            Add($"Edit {noun} times...", name + "Adjust", () => _ = AdjustEpisodeSectionAsync(kind), Video);
            Add($"Clear saved {noun}", name + "Clear", () => _player.ClearEpisodeSection(kind), () => View(kind)?.HasSavedBoundary == true);
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        foreach (var kind in new[] { EpisodeSectionKind.Intro, EpisodeSectionKind.Credits })
        {
            var (name, noun) = kind == EpisodeSectionKind.Intro ? ("Intro", "intro") : ("Credits", "credits");
            Toggle($"Hide the detected {noun}", name + "Hide", hide => ChangeProposalFeedback(kind, hide),
                () => View(kind) is { } view && (view.CanHide || view.Hidden), () => View(kind)?.Hidden == true);
        }

        Add("Save intro and credits from chapter names...", "DetectChapters", () => _ = ConfirmChapterSuggestionsAsync(), Video);
        menu.Items.Add(new MenuFlyoutSeparator());
        Toggle("Always skip intros for this show", "ShowSkipIntros", skip => _player.SetShowSkipping(EpisodeSectionKind.Intro, skip),
            Episode, () => _player.CurrentShowSkipping.Intros);
        Toggle("Always skip credits for this show", "ShowSkipCredits", skip => _player.SetShowSkipping(EpisodeSectionKind.Credits, skip),
            Episode, () => _player.CurrentShowSkipping.Credits);
        Add("Skip settings for every show...", "SkipPreferences", () => _ = ShowPreferencesAsync(), () => true);
        items.Add(new MenuFlyoutSeparator());
        items.Add(menu);
    }

    /// <summary>
    /// Brings the skip buttons, the menus and the timeline marks in line with the player. Called on
    /// every position change, so it only rebuilds what changed.
    /// </summary>
    private void ShowEpisodeSkip()
    {
        foreach (var update in _sectionMenuUpdates)
        {
            update();
        }

        ShowSectionMarks();
        var prompt = LibraryPane.Visibility == Visibility.Visible ? null : _player.SkipPrompt;
        var key = _keymap.Label(CommandCatalog.SkipSection);
        var shown = prompt is null ? "" : string.Join("|", prompt, _player.NextEpisode, key);
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

        var nextTitle = _player.NextEpisode is { } next ? PlaylistItem.TitleOf(next) : null;
        EpisodeSkipButton.Content = prompt.PrimaryText;
        AutomationProperties.SetName(EpisodeSkipButton, prompt.PrimaryText);
        ToolTipService.SetToolTip(EpisodeSkipButton, prompt.Hint(nextTitle, key));
        EpisodeSkipButton.Visibility = Visibility.Visible;

        EpisodeNextButton.Visibility = prompt.Secondary == EpisodeSkipAction.NextEpisode ? Visibility.Visible : Visibility.Collapsed;
        var nextKey = _keymap.Label(CommandCatalog.NextEpisode);
        ToolTipService.SetToolTip(EpisodeNextButton, "Play " + nextTitle + (nextKey.Length > 0 ? $" ({nextKey})" : ""));
        EpisodeSkipCancel.Visibility = prompt.AutomaticInSeconds is null ? Visibility.Collapsed : Visibility.Visible;

        EpisodeSkipOptions.Visibility = Visibility.Visible;
        var noun = prompt.Kind == EpisodeSectionKind.Intro ? "intro" : "credits";
        var options = (prompt.Detected ? "Check the detected " : "Edit the saved ") + noun;
        AutomationProperties.SetName(EpisodeSkipOptions, options);
        ToolTipService.SetToolTip(EpisodeSkipOptions, options);
        EpisodeSkipOptions.Flyout = SkipOptions(prompt);
    }

    /// <summary>The options beside the skip button: keep, edit or reject what was found, and automatic skipping.</summary>
    private MenuFlyout SkipOptions(EpisodeSkipPrompt prompt)
    {
        var kind = prompt.Kind;
        var menu = new MenuFlyout();
        var view = _player.SectionViews.First(view => view.Kind == kind);
        MenuFlyoutItem Command(string title, string id, Action action)
        {
            var item = new MenuFlyoutItem { Text = title };
            AutomationProperties.SetAutomationId(item, "EpisodeSkip" + id);
            item.Click += (_, _) =>
            {
                action();
                ShowEpisodeSkip();
            };
            menu.Items.Add(item);
            return item;
        }

        if (view.Start is { } start && view.End is { } end)
        {
            var text = view.Saved ? "Saved: " + Span(start, end)
                : string.Create(CultureInfo.InvariantCulture, $"Found: {Span(start, end)} ({view.Detected!.Confidence:P0} sure)");
            menu.Items.Add(new MenuFlyoutItem { Text = text, IsEnabled = false });
            if (!view.Saved)
            {
                menu.Items.Add(new MenuFlyoutItem { Text = view.Detected!.Reason, IsEnabled = false });
            }
        }

        if (!view.Saved)
        {
            Command("Keep these times", "Confirm", () => _player.ConfirmDetectedSection(kind));
        }

        Command("Edit times...", "Adjust", () => _ = AdjustEpisodeSectionAsync(kind));
        if (view.Saved)
        {
            Command("Clear the saved " + view.Noun, "Clear", () => _player.ClearEpisodeSection(kind));
        }
        else
        {
            Command(kind == EpisodeSectionKind.Intro ? "Not the intro" : "Not the credits", "Reject", () => ChangeProposalFeedback(kind, true));
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
    /// outlined and dashed for detected ones, so the two never look alike.
    /// </summary>
    private void ShowSectionMarks()
    {
        var duration = _player.Duration;
        var marks = _player.SectionViews.Where(view => view.Start is not null && view.End is not null).ToList();
        var shown = duration.Ticks + ";" + string.Join(";", marks);
        if (shown == _shownSectionMarks)
        {
            return;
        }

        _shownSectionMarks = shown;
        SectionMarkers.Children.Clear();
        _sectionMarks.Clear();
        var accent = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        foreach (var view in marks)
        {
            var (start, end) = (view.Start!.Value, view.End!.Value);
            var mark = new Rectangle
            {
                Height = 6,
                RadiusX = 2,
                RadiusY = 2,
                Fill = view.Saved ? accent : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Stroke = accent,
                StrokeThickness = view.Saved ? 0 : 1.5,
                StrokeDashArray = view.Saved ? null : [2, 1.5],
                Opacity = view.Saved ? 0.85 : 0.95,
                IsHitTestVisible = false,
            };
            AutomationProperties.SetAutomationId(mark, "SectionMarker-" + (view.Kind == EpisodeSectionKind.Intro ? "Intro" : "Credits"));
            AutomationProperties.SetName(mark, Describe(view));
            SectionMarkers.Children.Add(mark);
            _sectionMarks.Add((mark, start, end));
        }

        AutomationProperties.SetName(SectionMarkers, marks.Count == 0 ? "No intro or credits marked" : string.Join(", ", marks.Select(Describe)));
        PositionSectionMarks();

        static string Describe(EpisodeSectionView view) =>
            $"{view.Noun}, {(view.Saved ? "saved" : "detected")}, {Span(view.Start!.Value, view.End!.Value)}";
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

    /// <summary>Hides a detected section for this episode, or shows it again; saved boundaries are untouched.</summary>
    private void ChangeProposalFeedback(EpisodeSectionKind kind, bool reject)
    {
        if (!_player.RejectDetectedSection(kind, reject))
        {
            Say("Nothing was detected here to hide.");
            return;
        }

        Say(reject ? "Hidden for this episode. It will not be offered again." : "The detected section is shown again.");
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

        var editing = _player.Item!;
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
            if (await Ask("Adjust the " + label.ToLowerInvariant(), panel, "Save", "Cancel"))
            {
                if (!ReferenceEquals(editing, _player.Item))
                {
                    Say("Another video opened while the times were being edited, so nothing was saved.");
                }
                else if (!_player.SetSectionBounds(kind, startInput.Value, endInput.Value, editing))
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
        var reviewed = _player.Item;
        if (_player.ChapterSectionSuggestions is not { } found || reviewed is null)
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
        if (await Ask("Save the suggested section markers?", description, "Save markers", "Cancel"))
        {
            if (!_player.ApplyChapterSectionSuggestions(reviewed) && !ReferenceEquals(reviewed, _player.Item))
            {
                Say("Another video opened while the suggestions were showing, so nothing was saved.");
            }

            ShowEpisodeSkip();
        }
    }
}
