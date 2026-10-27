using System.Diagnostics;
using System.Windows.Automation;

namespace Rex.PerfTour;

/// <summary>One toured step: what was done (named generically, never what the library holds) and how long it took to settle.</summary>
public sealed record StepResult(string Name, double Ms, bool Ok, string? Note, Sample? After);

/// <summary>What the player's process cost at one moment, from its own metrics or from outside.</summary>
public sealed record Sample(double AtMs, double WorkingSetMb, double PrivateMb, double? HeapMb, double? CpuMs,
    double? Gc2, double? FramesDropped, double? UiWorstMs, double? UiBlockedMs, double? Threads)
{
    public static Sample Read(AttachedApp app, double atMs, bool startWatch = false)
    {
        if (app.PipeAnswers && app.Metrics(startWatch) is { } metrics)
        {
            return new Sample(
                atMs,
                metrics.GetValueOrDefault("working-set-mb"),
                metrics.GetValueOrDefault("private-mb"),
                Maybe(metrics, "managed-heap-mb"),
                Maybe(metrics, "cpu-ms"),
                Maybe(metrics, "gc2"),
                Maybe(metrics, "frames-dropped"),
                Maybe(metrics, "ui-worst-ms"),
                Maybe(metrics, "ui-blocked-ms"),
                Maybe(metrics, "threads"));
        }

        app.Process.Refresh();
        const double Megabyte = 1024 * 1024;
        return new Sample(atMs, app.Process.WorkingSet64 / Megabyte, app.Process.PrivateMemorySize64 / Megabyte,
            null, app.Process.TotalProcessorTime.TotalMilliseconds, null, null, null, null, app.Process.Threads.Count);
    }

    private static double? Maybe(IReadOnlyDictionary<string, double> metrics, string name) =>
        metrics.TryGetValue(name, out var value) ? value : null;
}

/// <summary>Memory around one library view: entering it, and after moving on, to spot pages that keep their cost.</summary>
public sealed record SectionMemory(string Name, Sample Before, Sample AfterLeft);

/// <summary>Everything one tour saw.</summary>
public sealed class TourResult
{
    public List<StepResult> Steps { get; } = [];

    public List<SectionMemory> Sections { get; } = [];

    public List<string> Notes { get; } = [];

    public Sample? StartSample { get; set; }

    public Sample? EndSample { get; set; }

    public bool PipeUsed { get; set; }
}

/// <summary>
/// The scripted tour of a live player: the library and each of its views, a details page per kind,
/// scrolling, search, layouts, Preferences, and (when something is loaded) seeks and full screen.
/// Every step is timed from the action to the window settling, and the player is left the way it
/// was found: no setting is saved, Preferences is cancelled, the layout and the playing position
/// are put back, and full screen is left.
/// </summary>
public sealed class Tour(AttachedApp app)
{
    /// <summary>The views toured when the sidebar lists them, with whether each has a details page to open.</summary>
    private static readonly (string View, bool Details)[] Views =
    [
        ("Home", false), ("Music", false), ("Songs", false), ("Albums", true), ("Artists", true),
        ("Genres", false), ("Playlists", false), ("Audiobooks", false), ("Podcasts", false),
        ("Recordings", false), ("Movies", true), ("TV Shows", true), ("Other videos", false),
        ("All videos", false), ("Folders", false), ("Pictures", false),
    ];

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly TourResult _result = new();

    public TourResult Run()
    {
        _result.PipeUsed = app.PipeAnswers;
        if (!app.PipeAnswers)
        {
            _result.Notes.Add("The player's automation pipe gave no answer; samples come from outside the process (no managed heap, hitch or frame figures) and the command-driven steps are skipped.");
        }

        _result.StartSample = Sample.Read(app, _clock.Elapsed.TotalMilliseconds, startWatch: true);
        var libraryWasOpen = app.IsShown("LibraryPane");
        TourLibrary(libraryWasOpen);
        TourPreferences();
        TourPlayback();
        Restore(libraryWasOpen);
        _result.EndSample = Sample.Read(app, _clock.Elapsed.TotalMilliseconds);
        return _result;
    }

    private void TourLibrary(bool wasOpen)
    {
        if (!OpenLibrary(wasOpen))
        {
            return;
        }

        Step("close the library", () => SetLibrary(false), () => !app.IsShown("LibraryPane"));
        Step("open the library again", () => SetLibrary(true), () => app.IsShown("LibraryPane"));

        var listed = app.ListedViews();
        var searched = false;
        foreach (var (view, details) in Views)
        {
            if (!listed.Contains(view) && !app.IsListed(view))
            {
                continue;
            }

            var before = Sample.Read(app, _clock.Elapsed.TotalMilliseconds);
            Step("open the " + view + " view", () => app.Visit(view), () => true);
            ScrollThrough(view);
            if (details)
            {
                OpenFirstDetails(view);
            }

            // Search and the layout switch are toured once, in the first view that offers them.
            if (!searched && app.IsShown("LibrarySearch"))
            {
                searched = true;
                SearchAndClear();
                SwitchLayoutAndBack(view);
            }

            // What the view left behind is read after moving on, back on Home.
            Step("return to Home from " + view, () => app.Visit("Home"), () => true);
            _result.Sections.Add(new SectionMemory(view, before, Sample.Read(app, _clock.Elapsed.TotalMilliseconds)));
        }
    }

    private bool OpenLibrary(bool wasOpen)
    {
        if (wasOpen)
        {
            return true;
        }

        var opened = Step("open the library", () => SetLibrary(true), () => app.IsShown("LibraryPane"));
        if (!opened)
        {
            _result.Notes.Add("The library pane never opened, so the library part of the tour was skipped.");
        }

        return opened;
    }

    private void SetLibrary(bool open)
    {
        if (app.IsShown("LibraryPane") == open)
        {
            return;
        }

        if (!app.PipeAnswers || !app.Run("toggle-library"))
        {
            app.Press("LibraryButton");
        }
    }

    private void ScrollThrough(string view)
    {
        if (app.Scroller() is not { } scroller || !scroller.Current.VerticallyScrollable)
        {
            return;
        }

        Step("scroll the " + view + " list three pages down", () =>
        {
            for (var page = 0; page < 3; page++)
            {
                var at = scroller.Current.VerticalScrollPercent;
                if (at >= 99.5)
                {
                    break;
                }

                scroller.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeIncrement);
                AttachedApp.Within(TimeSpan.FromSeconds(2), () => scroller.Current.VerticalScrollPercent > at + 0.1 || scroller.Current.VerticalScrollPercent >= 99.5);
            }
        }, () => true);
        Step("scroll the " + view + " list back to the top", () =>
        {
            scroller.SetScrollPercent(ScrollPattern.NoScroll, 0);
        }, () => scroller.Current.VerticalScrollPercent <= 0.5);
    }

    private void OpenFirstDetails(string view)
    {
        var opened = Step("open the first " + Singular(view) + " page", () =>
        {
            var line = app.FirstLine();
            var title = Title();
            Mouse.DoubleClick(app, line);
            AttachedApp.For(() => app.IsShown("LibraryBack") && Title() != title, "the details page to open");
        }, () => true);
        if (opened)
        {
            Step("go back from the " + Singular(view) + " page", () =>
            {
                app.Press("LibraryBack");
                app.WaitForView(view);
            }, () => true);
        }
    }

    private static string Singular(string view) => view switch
    {
        "Albums" => "album",
        "Artists" => "artist",
        "Movies" => "movie details",
        "TV Shows" => "show",
        _ => view,
    };

    private string? Title()
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

    private void SearchAndClear()
    {
        if (app.TryFind("LibrarySearch") is not { } search
            || !search.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
        {
            return;
        }

        var value = (ValuePattern)pattern;
        Step("search the library for one letter", () =>
        {
            value.SetValue("a");
            AttachedApp.For(() => Current(value) == "a", "the search text to land");
            SettleList();
        }, () => true);
        Step("clear the search", () =>
        {
            value.SetValue("");
            AttachedApp.For(() => Current(value).Length == 0, "the search text to clear");
            SettleList();
        }, () => true);
    }

    private static string Current(ValuePattern value)
    {
        try
        {
            return value.Current.Value;
        }
        catch (ElementNotAvailableException)
        {
            return "\0";
        }
    }

    /// <summary>Settled: the realised lines read the same twice in a row, bounded by the usual patience.</summary>
    private void SettleList()
    {
        string[]? last = null;
        AttachedApp.For(() =>
        {
            var now = app.Lines();
            var settled = last is not null && now.SequenceEqual(last, StringComparer.Ordinal);
            last = now;
            return settled;
        }, "the list to settle");
    }

    private void SwitchLayoutAndBack(string view)
    {
        if (app.TryFind("LibraryLook") is null)
        {
            return;
        }

        string? original = null;
        var switched = Step("switch the " + view + " layout", () =>
        {
            var choices = Choices("LibraryLook");
            original = choices.FirstOrDefault(choice => IsChosen(choice.Element))?.Name;
            var other = choices.FirstOrDefault(choice => choice.Name != original)
                ?? throw new TourStepException("The layout menu offered nothing else.");
            AttachedApp.Press(other.Element);
            SettleList();
        }, () => true);
        if (switched && original is not null)
        {
            Step("switch the " + view + " layout back", () =>
            {
                var back = Choices("LibraryLook").FirstOrDefault(choice => choice.Name == original)
                    ?? throw new TourStepException("The original layout was no longer offered.");
                AttachedApp.Press(back.Element);
                SettleList();
            }, () => true);
        }
    }

    private sealed record Choice(string Name, AutomationElement Element);

    /// <summary>Opens the option menu and reads its choices; the menu stays open for the pick that follows.</summary>
    private List<Choice> Choices(string menu)
    {
        app.Press(menu);
        return AttachedApp.Until<List<Choice>>(() =>
        {
            try
            {
                var items = Prefixed(menu + "-").ToList();
                return items.Count == 0 ? null : [.. items.Select(item => new Choice(item.Current.AutomationId, item))];
            }
            catch (ElementNotAvailableException)
            {
                return null;
            }
        }, "the option menu to open");
    }

    private IEnumerable<AutomationElement> Prefixed(string prefix) =>
        app.Window.FindAll(TreeScope.Descendants, new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.RadioButton),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem)))
            .Cast<AutomationElement>().Where(item => item.Current.AutomationId.StartsWith(prefix, StringComparison.Ordinal));

    private static bool IsChosen(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(TogglePattern.Pattern, out var toggle))
        {
            return ((TogglePattern)toggle).Current.ToggleState == ToggleState.On;
        }

        return element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var select)
            && ((SelectionItemPattern)select).Current.IsSelected;
    }

    private void TourPreferences()
    {
        if (!app.PipeAnswers)
        {
            _result.Notes.Add("Preferences were not toured: opening them needs the pipe's command.");
            return;
        }

        var opened = Step("open Preferences", () =>
        {
            if (!app.Run("preferences"))
            {
                throw new TourStepException("The player refused the Preferences command.");
            }
        }, () => app.IsShown("PreferencesSection"));
        if (!opened)
        {
            return;
        }

        try
        {
            foreach (var section in app.PreferencesSections())
            {
                Step("open the Preferences section " + section, () => app.ChoosePreferencesSection(section), () => true);
            }
        }
        finally
        {
            // Always cancelled, never saved: the tour must leave the person's settings alone.
            Step("close Preferences without saving", () => app.Press("CloseButton"), () => !app.IsShown("PreferencesSection"));
        }
    }

    private void TourPlayback()
    {
        if (!app.PipeAnswers)
        {
            return;
        }

        var status = app.Status();
        if (status is not { Ok: true, Duration: > 10 } || status.Position is not { } origin)
        {
            _result.Notes.Add("Nothing long enough was loaded, so the seek and full-screen steps were skipped.");
            return;
        }

        var duration = status.Duration ?? 0;
        var forward = Math.Min(origin + 30, duration - 10);
        Step("seek 30 seconds forward", () => SeekTo(forward), () => true);
        Step("seek 30 seconds back", () => SeekTo(Math.Max(forward - 30, 0)), () => true);
        Step("seek back to where playback was", () => SeekTo(origin), () => true);

        var bounds = app.Window.Current.BoundingRectangle;
        var grew = Step("enter full screen", () =>
        {
            if (!app.Run("toggle-full-screen"))
            {
                throw new TourStepException("The player refused the full-screen command.");
            }
        }, () => app.Window.Current.BoundingRectangle != bounds);
        if (grew)
        {
            Step("leave full screen", () =>
            {
                if (!app.Run("leave-full-screen"))
                {
                    throw new TourStepException("The player refused leaving full screen.");
                }
            }, () => app.Window.Current.BoundingRectangle == bounds);
        }
    }

    private void SeekTo(double seconds)
    {
        if (!app.Seek(seconds))
        {
            throw new TourStepException("The player refused the seek.");
        }

        AttachedApp.For(() => app.Status() is { Position: { } at } && Math.Abs(at - seconds) < 3, "the position to land");
    }

    private void Restore(bool libraryWasOpen)
    {
        Step("restore the window to how it was", () =>
        {
            if (app.IsShown("LibraryPane") && !libraryWasOpen)
            {
                SetLibrary(false);
                AttachedApp.For(() => !app.IsShown("LibraryPane"), "the library to close again");
            }
            else if (libraryWasOpen && !app.IsShown("LibraryPane"))
            {
                SetLibrary(true);
                AttachedApp.For(() => app.IsShown("LibraryPane"), "the library to open again");
            }
        }, () => true);
    }

    /// <summary>
    /// One timed step: the action, then the settling condition, measured together; what the library
    /// shows never reaches the step's name or its failure note.
    /// </summary>
    private bool Step(string name, Action action, Func<bool> settled)
    {
        var started = _clock.Elapsed.TotalMilliseconds;
        bool ok;
        string? note = null;
        try
        {
            action();
            AttachedApp.For(settled, "the step to settle");
            ok = true;
        }
        catch (TourStepException ex)
        {
            ok = false;
            note = ex.Message;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Windows.Automation.NoClickablePointException)
        {
            // Only the exception's type: its message could carry what the window shows.
            ok = false;
            note = "The window changed under the step (" + ex.GetType().Name + ").";
        }

        var ended = _clock.Elapsed.TotalMilliseconds;
        _result.Steps.Add(new StepResult(name, ended - started, ok, note, Sample.Read(app, ended)));
        return ok;
    }
}
