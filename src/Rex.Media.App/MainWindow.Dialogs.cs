using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Shell;
using Rex.Media.AppCore.Player;
using Windows.Storage.Pickers;
using Peers = Microsoft.UI.Xaml.Automation.Peers;
using Props = Microsoft.UI.Xaml.Automation.AutomationProperties;

namespace Rex.Media.App;

/// <summary>
/// The window's dialogs share one look (UI-17): ordinary dialogs one width, wider ones only when
/// they set things side by side, none taller than the window less a margin (<see cref="DialogLayout"/>),
/// and a list too long to show at once fades out at its foot while more lies below. Here too are
/// two of them: the shortcut sheet, each shortcut drawn as keycaps beside what it does (A11Y-02),
/// and Open a location, with a way to browse for the file.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// What a WinUI dialog adds around its content, measured from its template: 24 of padding on each
    /// side and a border across; its title, padding, separator and buttons down; and the tallest it draws.
    /// </summary>
    private static readonly DialogChrome ContentDialogChrome = new(Across: 50, Around: 171, Tallest: 756);

    /// <summary>The width of a dialog's content that would like <paramref name="preferred"/>, in this window.</summary>
    private double DialogWidth(double preferred) =>
        DialogLayout.Width(preferred, Content.XamlRoot.Size.Width, Content.XamlRoot.Size.Height, ContentDialogChrome);

    /// <summary>The height a dialog's content may take in this window, up to <paramref name="preferred"/>.</summary>
    private double DialogHeight(double preferred) =>
        DialogLayout.Height(preferred, Content.XamlRoot.Size.Width, Content.XamlRoot.Size.Height, ContentDialogChrome);

    /// <summary>
    /// Lets <paramref name="dialog"/> grow to content <paramref name="width"/> across. WinUI caps
    /// every dialog at 548 pixels with a theme resource the dialog's template reads when it is first
    /// shown, so the dialog's own copy has to be set before then.
    /// </summary>
    private static void AllowWidth(ContentDialog dialog, double width) =>
        dialog.Resources["ContentDialogMaxWidth"] = width + ContentDialogChrome.Across;

    /// <summary>
    /// <paramref name="area"/> with a soft foot that shows while more of it lies below: the cue that it
    /// scrolls, since a scroll bar shows only under the pointer. The area is a scroll viewer or holds
    /// one (a list's own, found once it is drawn). The foot changes only when the view or its content does.
    /// </summary>
    private static Grid WithScrollCue(FrameworkElement area)
    {
        var host = new Grid();
        host.Children.Add(area);
        var foot = new Grid { Height = 32, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false, Visibility = Visibility.Collapsed };

        // A dialog's surface is its background with a lighter layer over it: fading into both draws it exactly.
        foot.Children.Add(new Rectangle { Fill = FadeInto("ContentDialogBackground") });
        foot.Children.Add(new Rectangle { Fill = FadeInto("ContentDialogTopOverlay") });
        host.Children.Add(foot);
        ScrollViewer? followed = null;
        void Follow(ScrollViewer scroll)
        {
            if (followed is not null)
            {
                return;
            }

            followed = scroll;
            void Show() => foot.Visibility = scroll.ScrollableHeight - scroll.VerticalOffset > 1 ? Visibility.Visible : Visibility.Collapsed;
            scroll.ViewChanged += (_, _) => Show();
            scroll.SizeChanged += (_, _) => Show();
            scroll.RegisterPropertyChangedCallback(ScrollViewer.ScrollableHeightProperty, (_, _) => Show());
            Show();
        }

        if (area is ScrollViewer own)
        {
            Follow(own);
        }
        else
        {
            area.Loaded += (_, _) =>
            {
                if (Descendant<ScrollViewer>(area) is { } inner)
                {
                    Follow(inner);
                }
            };
        }

        return host;
    }

    /// <summary>A gradient from clear to the window's theme brush <paramref name="key"/>, top to bottom.</summary>
    private static LinearGradientBrush FadeInto(string key)
    {
        var to = RexTheme.Brush(key) is SolidColorBrush solid
            ? Windows.UI.Color.FromArgb((byte)(solid.Color.A * solid.Opacity), solid.Color.R, solid.Color.G, solid.Color.B)
            : default;
        var brush = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(0, 1) };
        brush.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(0, to.R, to.G, to.B), Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = to, Offset = 1 });
        return brush;
    }

    /// <summary>The first element of type <typeparamref name="T"/> drawn inside <paramref name="root"/>, or null.</summary>
    private static T? Descendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            if (Descendant<T>(child) is { } deeper)
            {
                return deeper;
            }
        }

        return null;
    }

    /// <summary>
    /// Every shortcut, a line each under its group: what it does, then its keys as keycaps, with the
    /// keys' everyday names (A11Y-02). A screen reader hears each line as one sentence, such as
    /// "Play or pause: Space or Play/pause key"; commands with no shortcut are left to the menus.
    /// </summary>
    private async Task ShowShortcutsAsync()
    {
        var caption = (double)Application.Current.Resources["RexCaptionFontSize"];
        var list = new StackPanel { Spacing = 4 };
        foreach (var group in CommandCatalog.All.GroupBy(command => command.Group))
        {
            var lines = group.Select(command => (Command: command, Keys: _keymap.ShortcutsFor(command.Id))).Where(line => line.Keys.Count > 0).ToList();
            if (lines.Count == 0)
            {
                continue;
            }

            var heading = Heading(group.Key);
            if (list.Children.Count == 0)
            {
                heading.Margin = new Thickness(0);
            }

            Props.SetHeadingLevel(heading, Peers.AutomationHeadingLevel.Level2);
            list.Children.Add(heading);
            foreach (var (command, keys) in lines)
            {
                var title = CommandCatalog.TitleOf(command, _settings);
                var line = new Grid { ColumnSpacing = 16, MinHeight = 32 };
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var name = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
                Props.SetName(name, title + ": " + KeyNames.Spoken(keys));
                line.Children.Add(name);
                var caps = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
                for (var i = 0; i < keys.Count; i++)
                {
                    if (i > 0)
                    {
                        var either = new TextBlock { Text = "or", FontSize = caption, VerticalAlignment = VerticalAlignment.Center, Foreground = RexTheme.Brush("TextFillColorSecondaryBrush") };
                        Props.SetAccessibilityView(either, Peers.AccessibilityView.Raw);
                        caps.Children.Add(either);
                    }

                    caps.Children.Add(Keycaps(keys[i], caption));
                }

                Grid.SetColumn(caps, 1);
                line.Children.Add(caps);
                list.Children.Add(line);
            }
        }

        var scroll = new ScrollViewer
        {
            Content = list,
            Height = DialogHeight(560),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 16, 0),
        };
        var sheet = WithScrollCue(scroll);
        sheet.Width = DialogWidth(DialogLayout.WideWidth);
        var dialog = new ContentDialog
        {
            Title = "Keyboard shortcuts",
            Content = sheet,
            CloseButtonText = "Close",
            XamlRoot = Content.XamlRoot,
        };
        AllowWidth(dialog, DialogLayout.WideWidth);
        await ShowDialogAsync(dialog);
    }

    /// <summary>One shortcut as keycaps, a key each; the line's own name says them, so the caps are only drawn.</summary>
    private static StackPanel Keycaps(KeyChord chord, double fontSize)
    {
        var keys = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var key in KeyNames.Keycaps(chord))
        {
            var label = new TextBlock { Text = key, FontSize = fontSize, HorizontalAlignment = HorizontalAlignment.Center };
            Props.SetAccessibilityView(label, Peers.AccessibilityView.Raw);
            keys.Children.Add(new Border
            {
                MinWidth = 24,
                Padding = new Thickness(6, 1, 6, 2),
                CornerRadius = (CornerRadius)Application.Current.Resources["RexChipRadius"],
                // A deeper lower edge, as a key has.
                BorderThickness = new Thickness(1, 1, 1, 2),
                BorderBrush = RexTheme.Brush("ControlStrokeColorSecondaryBrush"),
                Background = RexTheme.Brush("ControlFillColorDefaultBrush"),
                Child = label,
            });
        }

        return keys;
    }

    /// <summary>Opens a file or folder typed in or browsed to (UI-17); only local places are offered.</summary>
    private async Task OpenLocationAsync()
    {
        var box = new TextBox { PlaceholderText = @"A local file or folder, such as C:\Music\Album" };
        Props.SetAutomationId(box, "LocationBox");
        Props.SetName(box, "Location");
        var browse = new Button { Content = "Browse..." };
        Props.SetAutomationId(browse, "LocationBrowse");
        Props.SetName(browse, "Browse for a file");
        browse.Click += async (_, _) =>
        {
            var picker = Prepared(new FileOpenPicker { SuggestedStartLocation = PickerLocationId.VideosLibrary, ViewMode = PickerViewMode.List });
            picker.FileTypeFilter.Add("*");
            if (await picker.PickSingleFileAsync() is { } file)
            {
                box.Text = file.Path;
            }
        };
        var place = new Grid { ColumnSpacing = 8, Width = DialogWidth(DialogLayout.OrdinaryWidth) };
        place.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        place.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        place.Children.Add(box);
        Grid.SetColumn(browse, 1);
        place.Children.Add(browse);
        if (await Ask("Open a location", place, "Open") && box.Text.Trim().Trim('"') is { Length: > 0 } location)
        {
            var (supported, _) = MediaFiles.FilterClipboardPaths([location], Directory.Exists);
            if (supported.Count == 0)
            {
                Say("Only local media files and folders can be opened. Network streams and remote locations are not supported.");
                return;
            }

            if (!File.Exists(location) && !Directory.Exists(location))
            {
                Say("That local file or folder could not be found.");
                return;
            }

            _player.Open(supported);
        }
    }
}
