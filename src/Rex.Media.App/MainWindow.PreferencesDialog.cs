using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore;
using Rex.Media.AppCore.Shell;
using Rex.Media.Settings;
using Windows.System;
using Peers = Microsoft.UI.Xaml.Automation.Peers;
using Props = Microsoft.UI.Xaml.Automation.AutomationProperties;

namespace Rex.Media.App;

/// <summary>
/// The Preferences dialog's frame (UI-10). In a wide window the sections are a rail beside the
/// page, with the search above the rail; in a narrower one a section picker and the search sit
/// above the page. The search leads to what it finds: it shows the section, opens a closed group
/// holding the setting and brings the setting into view, and says so in the page when nothing
/// matches. The dialog is sized to the window by <see cref="PreferencesLayout"/> rather than held
/// to WinUI's usual 548 pixels, and keeps its size while it is open, whatever the page or search.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The Preferences dialog around <paramref name="sections"/>, and how to stop it following the window's size.</summary>
    private (ContentDialog Dialog, Action StopFitting) PreferencesDialog(LazySections<StackPanel, PlayerSettings> sections, IReadOnlyList<string> names)
    {
        // Every page's fields fill the page's width as it is built, so they line up across sections.
        var built = sections.Built;
        sections.Built = page =>
        {
            built?.Invoke(page);
            FillWidth(page);
        };

        // The page shown, with room for focus rings at its left and the scroll bar at its right.
        var scroll = new ScrollViewer
        {
            Content = sections.Page(0),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(4, 0, 16, 0),
        };
        Props.SetAutomationId(scroll, "PreferencesScroll");
        var page = WithScrollCue(scroll);

        // Nothing found: said in the page itself, with the way back.
        var emptyMessage = new TextBlock { TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] };
        Props.SetAutomationId(emptyMessage, "PreferencesSearchEmpty");
        Props.SetLiveSetting(emptyMessage, Peers.AutomationLiveSetting.Polite);
        var clearSearch = new Button { Content = "Clear search" };
        Props.SetAutomationId(clearSearch, "PreferencesSearchClear");
        var emptyIcon = new FontIcon { Glyph = "\uE721", FontSize = 32, HorizontalAlignment = HorizontalAlignment.Left, Foreground = RexTheme.Brush("TextFillColorSecondaryBrush") };
        Props.SetAccessibilityView(emptyIcon, Peers.AccessibilityView.Raw);
        var empty = new StackPanel { Spacing = 12, Margin = new Thickness(4, 8, 16, 0), Visibility = Visibility.Collapsed };
        empty.Children.Add(emptyIcon);
        empty.Children.Add(emptyMessage);
        empty.Children.Add(Note("Try another word, or clear the search to see every section."));
        empty.Children.Add(clearSearch);
        var pageArea = new Grid();
        pageArea.Children.Add(page);
        pageArea.Children.Add(empty);

        // One line under the page about Save, which every section shares.
        var saveHint = Note("Changes in all sections apply when you select Save.");
        saveHint.Margin = new Thickness(4, 0, 16, 0);
        Props.SetAutomationId(saveHint, "PreferencesSaveHint");
        var content = new Grid { RowSpacing = 12 };
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.Children.Add(pageArea);
        Grid.SetRow(saveHint, 1);
        content.Children.Add(saveHint);

        // The narrow layout's picker; its name, read out, says what it is, and it shows the section.
        var section = Choice("Preferences section", [.. names], 0);
        section.Header = null;
        section.HorizontalAlignment = HorizontalAlignment.Stretch;
        // Keep every section directly reachable when the picker reopens near the end: a default-height
        // popup virtualized the first sections above its viewport, out of reach of the keyboard and
        // UI Automation.
        section.MaxDropDownHeight = 560;
        Props.SetAutomationId(section, "PreferencesSection");

        // The search, in both layouts, with its glyph inside the box.
        var search = new TextBox { PlaceholderText = "Search Preferences", Padding = new Thickness(32, 5, 6, 6) };
        Props.SetAutomationId(search, "PreferencesCategorySearch");
        Props.SetName(search, "Search Preferences");
        var searchGlyph = new FontIcon
        {
            Glyph = "\uE721",
            FontSize = 14,
            Margin = new Thickness(11, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Foreground = RexTheme.Brush("TextFillColorSecondaryBrush"),
        };
        Props.SetAccessibilityView(searchGlyph, Peers.AccessibilityView.Raw);
        var searchBox = new Grid();
        searchBox.Children.Add(search);
        searchBox.Children.Add(searchGlyph);

        // The wide layout's rail: every section, or those a search matches.
        var railItem = new Style(typeof(ListViewItem));
        if (Application.Current.Resources.TryGetValue("DefaultListViewItemStyle", out var basis) && basis is Style itemStyle)
        {
            railItem.BasedOn = itemStyle;
        }

        railItem.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 32d));
        railItem.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 0, 12, 0)));
        var categories = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            ItemsSource = names.ToList(),
            SelectedIndex = 0,
            ItemContainerStyle = railItem,
        };
        Props.SetAutomationId(categories, "PreferencesCategoryList");
        Props.SetName(categories, "Preferences sections");
        var rail = WithScrollCue(categories);

        // Switching section keeps each page where it was scrolled to.
        var offsets = new double[sections.Count];
        var shown = 0;
        section.SelectionChanged += (_, _) =>
        {
            var index = section.SelectedIndex;
            if (index < 0)
            {
                return;
            }

            offsets[shown] = scroll.VerticalOffset;
            shown = index;
            scroll.Content = sections.Page(index);
            scroll.UpdateLayout();
            // A view already at its saved place is left alone: a redundant change lands after layout
            // and would undo a scroll the user starts straight after switching.
            if (Math.Abs(scroll.VerticalOffset - offsets[index]) > 0.5)
            {
                scroll.ChangeView(null, offsets[index], null, disableAnimation: true);
            }

            // A section chosen outside a search's matches ends the search, so the rail always holds the section shown.
            if (categories.ItemsSource is List<string> listed && !listed.Contains(names[index]))
            {
                search.Text = "";
            }
            else if (!Equals(categories.SelectedItem, names[index]))
            {
                categories.SelectedItem = names[index];
            }
        };
        categories.SelectionChanged += (_, _) =>
        {
            if (categories.SelectedItem is string name && sections.IndexOf(name) is var index and >= 0 && section.SelectedIndex != index)
            {
                section.SelectedIndex = index;
            }
        };

        // A search matches section names, the labels and choices inside each page (closed groups
        // too), and what each control says to a screen reader; it builds the pages not yet shown.
        FrameworkElement? found = null;
        void Search()
        {
            var query = search.Text.Trim();
            var matching = query.Length == 0 ? [.. names] : sections.All()
                .Where(item => SettingsSearch.Says(item.Name, query) || SettingMatching(item.Page, query, []) is not null)
                .Select(item => item.Name).ToList();
            categories.ItemsSource = matching;
            found = null;
            var chosen = SettingsSearch.Section(names, matching, names[shown], query);
            empty.Visibility = chosen is null ? Visibility.Visible : Visibility.Collapsed;
            page.Visibility = chosen is null ? Visibility.Collapsed : Visibility.Visible;
            if (chosen is null)
            {
                emptyMessage.Text = $"Nothing in Preferences matches \"{query}\".";
                categories.SelectedItem = null;
                return;
            }

            // The picker leads: changing the rail's items can clear its selection without a reliable
            // SelectionChanged, so the section is chosen first and the rail then follows it.
            var index = sections.IndexOf(chosen);
            if (section.SelectedIndex != index)
            {
                section.SelectedIndex = index;
            }

            categories.SelectedItem = chosen;
            categories.ScrollIntoView(chosen);
            if (query.Length > 0)
            {
                found = Reveal(sections.Page(index), chosen, query);
            }
        }

        // The setting found: its closed groups opened and the setting brought into view. A search for
        // the section itself shows the section from its top.
        FrameworkElement? Reveal(StackPanel shownPage, string name, string query)
        {
            if (SettingsSearch.Says(name, query))
            {
                scroll.ChangeView(null, 0, null, disableAnimation: true);
                return null;
            }

            var within = new List<Expander>();
            if (SettingMatching(shownPage, query, within) is not { } match)
            {
                return null;
            }

            foreach (var expander in within)
            {
                expander.IsExpanded = true;
            }

            if (match is Expander group)
            {
                group.IsExpanded = true;
            }

            scroll.UpdateLayout();
            match.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0.25 });
            return match;
        }

        search.TextChanged += (_, _) => Search();
        clearSearch.Click += (_, _) => search.Text = "";

        // Enter goes to the setting found rather than saving and closing Preferences.
        search.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                if (found is Control control)
                {
                    control.Focus(FocusState.Keyboard);
                }
            }
        };
        search.KeyUp += (_, e) => e.Handled |= e.Key == VirtualKey.Enter;

        var panel = new Grid();
        panel.Children.Add(section);
        panel.Children.Add(searchBox);
        panel.Children.Add(rail);
        panel.Children.Add(content);
        var dialog = new ContentDialog
        {
            Title = "Preferences",
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };
        AllowWidth(dialog, PreferencesLayout.WideWidth);

        // The layout follows the window while the dialog is open; it is set out again only when it changes.
        var root = Content.XamlRoot;
        (bool Wide, bool Stacked)? arranged = null;
        void Fit(XamlRoot sender, XamlRootChangedEventArgs args)
        {
            var layout = PreferencesLayout.For(root.Size.Width, root.Size.Height, ContentDialogChrome);
            panel.Width = layout.Width;
            panel.Height = layout.Height;
            if (arranged == (layout.Wide, layout.Stacked))
            {
                return;
            }

            arranged = (layout.Wide, layout.Stacked);
            panel.RowDefinitions.Clear();
            panel.ColumnDefinitions.Clear();
            section.Visibility = layout.Wide ? Visibility.Collapsed : Visibility.Visible;
            rail.Visibility = layout.Wide ? Visibility.Visible : Visibility.Collapsed;
            if (layout.Wide)
            {
                (panel.ColumnSpacing, panel.RowSpacing) = (PreferencesLayout.Gap, 12);
                Lines(panel.RowDefinitions, GridLength.Auto, new GridLength(1, GridUnitType.Star));
                Lines(panel.ColumnDefinitions, new GridLength(PreferencesLayout.RailWidth), new GridLength(1, GridUnitType.Star));
                Place(searchBox, 0, 0);
                Place(rail, 1, 0);
                Place(content, 0, 1, rows: 2);
            }
            else if (!layout.Stacked)
            {
                (panel.ColumnSpacing, panel.RowSpacing) = (12, 16);
                Lines(panel.RowDefinitions, GridLength.Auto, new GridLength(1, GridUnitType.Star));
                Lines(panel.ColumnDefinitions, new GridLength(1, GridUnitType.Star), new GridLength(1, GridUnitType.Star));
                Place(section, 0, 0);
                Place(searchBox, 0, 1);
                Place(content, 1, 0, columns: 2);
            }
            else
            {
                (panel.ColumnSpacing, panel.RowSpacing) = (0, 12);
                Lines(panel.RowDefinitions, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star));
                Lines(panel.ColumnDefinitions, new GridLength(1, GridUnitType.Star));
                Place(section, 0, 0);
                Place(searchBox, 1, 0);
                Place(content, 2, 0);
            }
        }

        Fit(root, null!);
        root.Changed += Fit;
        return (dialog, () => root.Changed -= Fit);
    }

    /// <summary>Sets <paramref name="lines"/> to rows or columns of these sizes.</summary>
    private static void Lines<T>(ICollection<T> lines, params GridLength[] sizes)
        where T : DependencyObject, new()
    {
        foreach (var size in sizes)
        {
            var line = new T();
            if (line is RowDefinition row)
            {
                row.Height = size;
            }
            else if (line is ColumnDefinition column)
            {
                column.Width = size;
            }

            lines.Add(line);
        }
    }

    /// <summary>Puts <paramref name="element"/> in a grid's cell, across <paramref name="rows"/> and <paramref name="columns"/>.</summary>
    private static void Place(FrameworkElement element, int row, int column, int rows = 1, int columns = 1)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetRowSpan(element, rows);
        Grid.SetColumnSpan(element, columns);
    }

    /// <summary>Makes every field of a page as wide as the page, so the fields line up within and across sections.</summary>
    private static void FillWidth(UIElement element)
    {
        switch (element)
        {
            case ComboBox or NumberBox or TextBox:
                ((FrameworkElement)element).HorizontalAlignment = HorizontalAlignment.Stretch;
                break;
            case Panel group:
                foreach (var child in group.Children)
                {
                    FillWidth(child);
                }

                break;
            case Expander { Content: UIElement body } expander:
                expander.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                FillWidth(body);
                break;
        }
    }

    /// <summary>
    /// The first control or line in <paramref name="node"/> that says <paramref name="query"/>, or
    /// null; <paramref name="within"/> gathers the groups (expanders) it sits in, outermost first.
    /// A label counts whether it is the control's own text, its header, one of its choices, or
    /// what it says to a screen reader; long check-box labels are TextBlocks after WrapLabels.
    /// </summary>
    private static FrameworkElement? SettingMatching(UIElement node, string query, List<Expander> within)
    {
        static string? Label(object? value) => value switch
        {
            string text => text,
            TextBlock block => block.Text,
            _ => null,
        };

        bool Says(object? value) => SettingsSearch.Says(Label(value), query);

        if (node is FrameworkElement element
            && (Says(Props.GetName(element)) || Says(Props.GetHelpText(element))
                || node is TextBlock text && Says(text.Text)
                || node is CheckBox check && Says(check.Content)
                || node is ToggleSwitch toggle && Says(toggle.Header)
                || node is ComboBox choice && (Says(choice.Header) || choice.Items.Any(item => Says(item is ComboBoxItem option ? option.Content : item)))
                || node is NumberBox number && Says(number.Header)
                || node is TextBox words && Says(words.Header)
                || node is Button button && Says(button.Content)
                || node is Expander titled && Says(titled.Header)))
        {
            return element;
        }

        switch (node)
        {
            case Expander { Content: UIElement body } expander:
                within.Add(expander);
                if (SettingMatching(body, query, within) is { } inside)
                {
                    return inside;
                }

                within.RemoveAt(within.Count - 1);
                return null;
            case Panel group:
                foreach (var child in group.Children)
                {
                    if (SettingMatching(child, query, within) is { } match)
                    {
                        return match;
                    }
                }

                return null;
            case Border { Child: UIElement child }:
                return SettingMatching(child, query, within);
            case UserControl { Content: UIElement content }:
                return SettingMatching(content, query, within);
            default:
                return node is ContentControl { Content: UIElement inner } ? SettingMatching(inner, query, within) : null;
        }
    }
}
