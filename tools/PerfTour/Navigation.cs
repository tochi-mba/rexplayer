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
    public static void WaitForView(this AttachedApp app, string view) =>
        AttachedApp.For(() => (app.IsShown("LibraryTitle") && TitleOf(app) == view)
            || (view is "Movies" or "TV Shows" && app.IsShown("LibraryBillboard"))
            || (view == "Music" && app.IsShown("MusicHero")), "the view to open", TimeSpan.FromSeconds(90));

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

    /// <summary>The list's realised lines from top to bottom; the names stay inside the tour and never reach the report.</summary>
    public static string[] Lines(this AttachedApp app, string list = "LibraryList")
    {
        try
        {
            return app.TryFind(list) is not { } element
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

    /// <summary>The first line of the list, scrolled into view, ready to open. Card looks keep their items under the pane, not always under the list.</summary>
    public static AutomationElement FirstLine(this AttachedApp app)
    {
        var item = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem);
        var line = AttachedApp.Until(
            () => app.TryFind("LibraryList")?.FindFirst(TreeScope.Descendants, item)
                ?? app.TryFind("LibraryPane")?.FindFirst(TreeScope.Descendants, item),
            "a first line in the list", TimeSpan.FromSeconds(45));
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

    /// <summary>The view's scroller: the list itself, or the scroll viewer inside it or the pane.</summary>
    public static ScrollPattern? Scroller(this AttachedApp app)
    {
        try
        {
            foreach (var holder in new[] { app.TryFind("LibraryList"), app.TryFind("LibraryPane") })
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

    /// <summary>Chooses a Preferences section in the section drop-down and waits for the rail to stay visible.</summary>
    public static void ChoosePreferencesSection(this AttachedApp app, string name)
    {
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

    /// <summary>The names the Preferences section drop-down offers, read with the list open, then closed again.</summary>
    public static string[] PreferencesSections(this AttachedApp app)
    {
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
