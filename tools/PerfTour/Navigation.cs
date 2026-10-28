using System.Windows.Automation;

namespace Rex.PerfTour;

/// <summary>Moving about the library the way a person does, through UI Automation, against a live player.</summary>
internal static class Navigation
{
    /// <summary>Chooses a view in the sidebar and waits for it to open.</summary>
    public static void Visit(this AttachedApp app, string view)
    {
        var byName = new PropertyCondition(AutomationElement.NameProperty, view);
        AttachedApp.For(() =>
        {
            if (app.IsListed(view))
            {
                return true;
            }

            try
            {
                var item = app.Find("LibrarySources").FindFirst(TreeScope.Descendants, byName);
                if (item?.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var scroll) == true)
                {
                    ((ScrollItemPattern)scroll).ScrollIntoView();
                }
            }
            catch (ElementNotAvailableException)
            {
                // The list was rebuilt between finding the item and scrolling it.
            }
            catch (InvalidOperationException)
            {
                // A collapsed section cannot be scrolled to yet.
            }

            return app.IsListed(view);
        }, "the sidebar to offer a view");
        var source = AttachedApp.Until(() => app.Find("LibrarySources").FindFirst(TreeScope.Descendants, byName), "the sidebar view");
        ((SelectionItemPattern)source.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        app.WaitForView(view);
    }

    /// <summary>
    /// Waits until <paramref name="view"/> is what the pane shows (streaming views show a billboard
    /// or hero first). A large live library can take far longer than a test fixture, so the wait is
    /// long: the point is to measure how long, not to give up.
    /// </summary>
    public static void WaitForView(this AttachedApp app, string view)
    {
        // A view in the browse look shows shelves without the plain title, so the fallback is the
        // sidebar choice landing and the pane's cards settling: the same count three polls in a row.
        var settledPolls = 0;
        var lastCount = -1;
        AttachedApp.For(() =>
        {
            // The title is compared wherever it is: a compact header keeps it out of view.
            if (TitleOf(app) == view
                // In the tree is enough: a view put back at its remembered scroll keeps these above the fold.
                || (view is "Movies" or "TV Shows" && app.SidebarSelected(view) && app.TryFind("LibraryBillboard") is not null)
                || (view == "Music" && app.TryFind("MusicHero") is not null))
            {
                return true;
            }

            if (!app.SidebarSelected(view))
            {
                return false;
            }

            var count = app.CardCount();
            settledPolls = count > 0 && count == lastCount ? settledPolls + 1 : 0;
            lastCount = count;
            return settledPolls >= 3;
        }, "the view to open", TimeSpan.FromSeconds(90));
    }

    /// <summary>Whether the sidebar's choice is <paramref name="view"/> now.</summary>
    public static bool SidebarSelected(this AttachedApp app, string view)
    {
        try
        {
            var item = app.TryFind("LibrarySources")?.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, view));
            return item is not null && item.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selection)
                && ((SelectionItemPattern)selection).Current.IsSelected;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    /// <summary>How many cards or lines the pane has realised now; -1 while it is being rebuilt.</summary>
    public static int CardCount(this AttachedApp app)
    {
        try
        {
            return app.TryFind("LibraryPane") is { } pane
                ? pane.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem)).Count
                : -1;
        }
        catch (ElementNotAvailableException)
        {
            return -1;
        }
    }

    private static string? TitleOf(AttachedApp app)
    {
        try
        {
            return app.TryFind("LibraryTitle")?.Current.Name;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    /// <summary>The views the sidebar lists now, in order.</summary>
    public static string[] ListedViews(this AttachedApp app)
    {
        try
        {
            return app.TryFind("LibrarySources") is not { } sources
                ? []
                : [.. sources.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem))
                    .Cast<AutomationElement>().Where(item => !item.Current.IsOffscreen && item.Current.BoundingRectangle.Height > 0)
                    .OrderBy(item => item.Current.BoundingRectangle.Top).Select(item => item.Current.Name)];
        }
        catch (ElementNotAvailableException)
        {
            return [];
        }
    }

    public static bool IsListed(this AttachedApp app, string view) => app.ListedViews().Contains(view);

    /// <summary>The surfaces a view shows its media on, in the order they are looked at.</summary>
    private static readonly string[] Surfaces = ["LibraryList", "LibraryCollage", "LibraryHome"];

    /// <summary>Whether the sidebar has <paramref name="view"/> at all, scrolled into view or not.</summary>
    public static bool Offers(this AttachedApp app, string view)
    {
        try
        {
            return app.TryFind("LibrarySources")?.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
                new PropertyCondition(AutomationElement.NameProperty, view))) is not null;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    /// <summary>Every list and list item under the library pane as "type id (n items)", never with a name.</summary>
    public static IEnumerable<string> AutomationIds(this AttachedApp app)
    {
        var pane = app.Find("LibraryPane");
        foreach (var id in new[] { "LibraryTitle", "LibraryBillboard", "LibraryList", "LibraryCollage", "LibraryHome", "LibraryBack", "LibrarySearch" })
        {
            yield return FormattableString.Invariant($"{id}: inTree={app.TryFind(id) is not null} shown={app.IsShown(id)}");
        }

        foreach (var list in pane.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.List)).Cast<AutomationElement>())
        {
            var items = list.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem)).Count;
            var deep = list.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem)).Count;
            yield return FormattableString.Invariant($"list id='{list.Current.AutomationId}' class={list.Current.ClassName} children={items} descendants={deep} offscreen={list.Current.IsOffscreen}");
        }
    }

    /// <summary>
    /// The realised lines of whichever surface the view shows (list, collage or shelves), top to
    /// bottom; the names stay inside the tour and never reach the report.
    /// </summary>
    public static string[] Lines(this AttachedApp app)
    {
        try
        {
            var shown = Surfaces.FirstOrDefault(app.IsShown);
            return shown is null || app.TryFind(shown) is not { } element
                ? []
                : [.. element.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem))
                    .Cast<AutomationElement>().OrderBy(line => line.Current.BoundingRectangle.Top).ThenBy(line => line.Current.BoundingRectangle.Left)
                    .Select(line => line.Current.Name)];
        }
        catch (ElementNotAvailableException)
        {
            return [];
        }
    }

    /// <summary>Shelves whose cards play or resume rather than open a page; the tour never clicks them.</summary>
    private static readonly string[] PlayingShelves = ["Continue", "Jump", "Downloading", "Recently", "New episodes"];

    /// <summary>
    /// The first card of the view's own collection, scrolled into view, ready to open. A plain list
    /// gives its first line; the browse look gives the first card of a shelf that opens pages, never
    /// the sidebar and never a shelf whose cards start playback.
    /// </summary>
    public static AutomationElement FirstLine(this AttachedApp app)
    {
        var item = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem);
        var line = AttachedApp.Until(
            () => (app.IsShown("LibraryList") ? app.TryFind("LibraryList")?.FindFirst(TreeScope.Descendants, item) : null)
                ?? (app.IsShown("LibraryCollage") ? app.TryFind("LibraryCollage")?.FindFirst(TreeScope.Descendants, item) : null)
                ?? PageShelf(app)?.FindFirst(TreeScope.Children, item)
                ?? PageShelf(app)?.FindFirst(TreeScope.Descendants, item),
            "a first card that opens a page", TimeSpan.FromSeconds(45));
        if (line.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var pattern))
        {
            ((ScrollItemPattern)pattern).ScrollIntoView();
        }

        AttachedApp.For(() =>
        {
            try
            {
                return !line.Current.IsOffscreen && line.Current.BoundingRectangle.Height > 0;
            }
            catch (ElementNotAvailableException)
            {
                return false;
            }
        }, "the first line to scroll into view");
        return line;
    }

    /// <summary>The first browse shelf whose cards open pages (an "All" shelf first), or null.</summary>
    private static AutomationElement? PageShelf(AttachedApp app)
    {
        try
        {
            var shelves = app.TryFind("LibraryPane")?.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.List))
                .Cast<AutomationElement>()
                .Where(list => list.Current.AutomationId.StartsWith("LibraryShelfCards-", StringComparison.Ordinal)
                    && !PlayingShelves.Any(word => list.Current.AutomationId.Contains(word, StringComparison.Ordinal)))
                .OrderBy(list => list.Current.IsOffscreen)
                .ToList() ?? [];
            // A shelf on screen first: a card's own ScrollIntoView moves its row sideways, not the page.
            return shelves.FirstOrDefault(list => !list.Current.IsOffscreen && list.Current.AutomationId.Contains("All", StringComparison.Ordinal))
                ?? shelves.FirstOrDefault();
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    /// <summary>The view's scroller: the list itself, or the scroll viewer inside it or the pane.</summary>
    public static ScrollPattern? Scroller(this AttachedApp app)
    {
        try
        {
            foreach (var holder in new[] { app.TryFind("LibraryList"), app.TryFind("LibraryCollage"), app.TryFind("LibraryHome"), app.TryFind("LibraryPane") })
            {
                if (holder is null)
                {
                    continue;
                }

                if (Scrollable(holder) is { } own)
                {
                    return own;
                }

                var inner = holder.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.IsScrollPatternAvailableProperty, true));
                if (inner is not null && Scrollable(inner) is { } found)
                {
                    return found;
                }
            }

            return null;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    private static ScrollPattern? Scrollable(AutomationElement element) =>
        element.TryGetCurrentPattern(ScrollPattern.Pattern, out var pattern)
            && ((ScrollPattern)pattern).Current.VerticallyScrollable
                ? (ScrollPattern)pattern
                : null;

    /// <summary>
    /// Opens the drop-down <paramref name="menu"/> until an item named <paramref name="name"/> is on
    /// offer, and returns it. A flyout still closing from the last choice swallows a new Expand, so
    /// the menu is expanded again until the item really shows.
    /// </summary>
    public static AutomationElement OpenMenu(this AttachedApp app, string menu, string name, bool itemOnScreen = true)
    {
        var item = new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
            new PropertyCondition(AutomationElement.NameProperty, name));
        return AttachedApp.Until(() =>
        {
            if (app.Reachable(item, itemOnScreen) is { } found)
            {
                return found;
            }

            try
            {
                if (app.TryFind(menu) is { } element
                    && element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var pattern))
                {
                    var expander = (ExpandCollapsePattern)pattern;
                    if (expander.Current.ExpandCollapseState == ExpandCollapseState.Expanded)
                    {
                        expander.Collapse();
                    }

                    expander.Expand();
                }
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException)
            {
                // Replaced or disabled between the look-up and the call: look it up again.
            }

            AttachedApp.Within(TimeSpan.FromSeconds(2), () => app.Reachable(item, itemOnScreen) is not null);
            return app.Reachable(item, itemOnScreen);
        }, "the menu to offer its choice");
    }

    /// <summary>The control matching <paramref name="condition"/> if it is in the tree, enabled and on screen now.</summary>
    public static AutomationElement? Reachable(this AttachedApp app, Condition condition, bool onScreen = true)
    {
        try
        {
            var element = app.Window.FindFirst(TreeScope.Descendants, condition);
            return element is { Current.IsEnabled: true } && !(onScreen && element.Current.IsOffscreen) ? element : null;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    /// <summary>
    /// Chooses a Preferences section: on the rail beside the page in a wide window, else in the
    /// section drop-down, then waits for the navigation to stay visible.
    /// </summary>
    public static void ChoosePreferencesSection(this AttachedApp app, string name)
    {
        if (app.IsShown("PreferencesCategoryList"))
        {
            var entry = AttachedApp.Until(() => app.TryFind("PreferencesCategoryList")?.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
                new PropertyCondition(AutomationElement.NameProperty, name))), "the section " + name + " on the rail");
            ((SelectionItemPattern)entry.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
            AttachedApp.For(() => app.IsShown("PreferencesCategoryList"), "the section rail to stay visible");
            return;
        }

        var item = app.OpenMenu("PreferencesSection", name, itemOnScreen: false);
        ((SelectionItemPattern)item.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        try
        {
            ((ExpandCollapsePattern)app.Find("PreferencesSection").GetCurrentPattern(ExpandCollapsePattern.Pattern)).Collapse();
        }
        catch (InvalidOperationException)
        {
            // Choosing already closed the drop-down.
        }

        AttachedApp.For(() => app.IsShown("PreferencesSection"), "the section navigation to stay visible");
    }

    /// <summary>
    /// The names of Preferences' sections: read from the rail in a wide window, else from the section
    /// drop-down with its list open, then closed again.
    /// </summary>
    public static string[] PreferencesSections(this AttachedApp app)
    {
        if (app.IsShown("PreferencesCategoryList"))
        {
            return AttachedApp.Until<string[]>(() =>
            {
                try
                {
                    var entries = app.Find("PreferencesCategoryList").FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem))
                        .Cast<AutomationElement>().Select(item => item.Current.Name).ToArray();
                    return entries.Length > 0 ? entries : null;
                }
                catch (ElementNotAvailableException)
                {
                    return null;
                }
            }, "the section rail");
        }

        var selector = app.Find("PreferencesSection");
        var expander = (ExpandCollapsePattern)selector.GetCurrentPattern(ExpandCollapsePattern.Pattern);
        expander.Expand();
        var names = AttachedApp.Until<string[]>(() =>
        {
            try
            {
                var items = selector.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem))
                    .Cast<AutomationElement>().Select(item => item.Current.Name).ToArray();
                return items.Length > 0 ? items : null;
            }
            catch (ElementNotAvailableException)
            {
                return null;
            }
        }, "the section list to open");
        try
        {
            expander.Collapse();
        }
        catch (InvalidOperationException)
        {
            // Already closed.
        }

        return names;
    }
}
