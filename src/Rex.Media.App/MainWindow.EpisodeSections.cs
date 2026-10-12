using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Player;

namespace Rex.Media.App;

/// <summary>User-confirmed intro and credits ranges: a quiet action near the player controls.</summary>
public sealed partial class MainWindow
{
    private readonly List<MenuFlyoutItem> _episodeMarkerCommands = [];

    private void AddEpisodeSectionMenus(IList<MenuFlyoutItemBase> items, string prefix)
    {
        var menu = new MenuFlyoutSubItem { Text = "Episode sections" };
        AutomationProperties.SetAutomationId(menu, prefix + "EpisodeSections");
        void Add(string title, string id, Action action)
        {
            var item = new MenuFlyoutItem { Text = title, IsEnabled = _player.CanMarkEpisodeSections };
            AutomationProperties.SetAutomationId(item, prefix + id);
            item.Click += (_, _) => action();
            menu.Items.Add(item);
            _episodeMarkerCommands.Add(item);
        }

        Add("Mark intro start here", "IntroStart", () => _player.MarkEpisodeSection(EpisodeSectionKind.Intro, true));
        Add("Mark intro end here", "IntroEnd", () => _player.MarkEpisodeSection(EpisodeSectionKind.Intro, false));
        Add("Clear intro markers", "IntroClear", () => _player.ClearEpisodeSection(EpisodeSectionKind.Intro));
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("Mark credits start here", "CreditsStart", () => _player.MarkEpisodeSection(EpisodeSectionKind.Credits, true));
        Add("Mark credits end here", "CreditsEnd", () => _player.MarkEpisodeSection(EpisodeSectionKind.Credits, false));
        Add("End credits at end of video", "CreditsVideoEnd", () => _player.MarkEpisodeSection(EpisodeSectionKind.Credits, false, atVideoEnd: true));
        Add("Clear credits markers", "CreditsClear", () => _player.ClearEpisodeSection(EpisodeSectionKind.Credits));
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("Suggest sections from labeled chapters...", "DetectChapters", () => _ = ConfirmChapterSuggestionsAsync());
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("Adjust intro boundaries...", "IntroAdjust", () => _ = AdjustEpisodeSectionAsync(EpisodeSectionKind.Intro));
        Add("Adjust credits boundaries...", "CreditsAdjust", () => _ = AdjustEpisodeSectionAsync(EpisodeSectionKind.Credits));
        Add("Reject detected intro", "IntroReject", () => ChangeProposalFeedback(EpisodeSectionKind.Intro, true));
        Add("Reject detected credits", "CreditsReject", () => ChangeProposalFeedback(EpisodeSectionKind.Credits, true));
        Add("Restore intro detection", "IntroRestore", () => ChangeProposalFeedback(EpisodeSectionKind.Intro, false));
        Add("Restore credits detection", "CreditsRestore", () => ChangeProposalFeedback(EpisodeSectionKind.Credits, false));
        items.Add(new MenuFlyoutSeparator());
        items.Add(menu);
    }

    private void ShowEpisodeSkip()
    {
        var allowed = _player.CanMarkEpisodeSections;
        foreach (var action in _episodeMarkerCommands)
        {
            action.IsEnabled = allowed;
        }

        if (_player.AvailableEpisodeSkip is not { } skip || LibraryPane.Visibility == Visibility.Visible)
        {
            EpisodeSkipButton.Visibility = Visibility.Collapsed;
            EpisodeSkipOptions.Visibility = Visibility.Collapsed;
            return;
        }

        var name = skip.Kind == EpisodeSectionKind.Intro ? "Skip intro" : "Skip credits";
        EpisodeSkipButton.Content = name;
        AutomationProperties.SetName(EpisodeSkipButton, name);
        EpisodeSkipButton.Visibility = Visibility.Visible;
        var proposal = _player.CurrentProposals?.Best(skip.Kind, _player.Duration);
        EpisodeSkipOptions.Visibility = proposal is null ? Visibility.Collapsed : Visibility.Visible;
        if (proposal is not null)
        {
            var saved = _player.CurrentSections;
            var confirmed = skip.Kind == EpisodeSectionKind.Intro
                ? saved.IntroStart is not null && saved.IntroEnd is not null
                : saved.CreditsStart is not null && saved.CreditsEnd is not null;
            EpisodeSkipOptions.Content = "⋯";
            AutomationProperties.SetName(EpisodeSkipOptions,
                confirmed ? "Adjust saved " + (skip.Kind == EpisodeSectionKind.Intro ? "intro" : "credits") :
                    "Review detected " + (skip.Kind == EpisodeSectionKind.Intro ? "intro" : "credits"));
            ToolTipService.SetToolTip(EpisodeSkipOptions,
                "Optional: review detection, save boundaries, adjust or reject");
            EpisodeSkipOptions.Flyout = DetectionOptions(skip.Kind, proposal, confirmed);
        }
    }

    private MenuFlyout DetectionOptions(EpisodeSectionKind kind, SectionCandidate candidate, bool confirmed)
    {
        var menu = new MenuFlyout();
        var label = kind == EpisodeSectionKind.Intro ? "intro" : "credits";
        menu.Items.Add(new MenuFlyoutItem
        {
            Text = (confirmed ? "Confirmed" : "Detected automatically") +
                $" · {candidate.Confidence:P0} confidence",
            IsEnabled = false,
        });
        menu.Items.Add(new MenuFlyoutItem { Text = candidate.Reason, IsEnabled = false });
        static MenuFlyoutItem Command(string title, Action action)
        {
            var item = new MenuFlyoutItem { Text = title };
            item.Click += (_, _) => action();
            return item;
        }

        if (!confirmed)
        {
            menu.Items.Add(Command("Save " + label + " boundaries for this episode (optional)", () =>
            {
                _player.ConfirmDetectedSection(kind);
                ShowEpisodeSkip();
            }));
        }

        menu.Items.Add(Command("Adjust start and end...", () => _ = AdjustEpisodeSectionAsync(kind)));
        menu.Items.Add(Command("Not " + (kind == EpisodeSectionKind.Intro ? "an intro" : "credits"), () =>
            ChangeProposalFeedback(kind, true)));
        return menu;
    }

    /// <summary>Feedback is remembered independently of saved manual boundaries.</summary>
    private void ChangeProposalFeedback(EpisodeSectionKind kind, bool reject)
    {
        if (!_player.RejectDetectedSection(kind, reject))
        {
            Say("There is no detected section to change.");
            return;
        }

        Say(reject ? "Detection rejected for this episode. No automatic skip will be offered." :
            "Detection restored. Saved manual boundaries still take precedence.");
        ShowEpisodeSkip();
    }

    /// <summary>Choose exact in/out points; the values stay editable and never alter the video file.</summary>
    private async Task AdjustEpisodeSectionAsync(EpisodeSectionKind kind)
    {
        if (!_player.CanMarkEpisodeSections)
        {
            return;
        }

        var confirmed = _player.CurrentSections;
        var proposed = _player.CurrentProposals?.Best(kind, _player.Duration);
        var start = kind == EpisodeSectionKind.Intro ? confirmed.IntroStart : confirmed.CreditsStart;
        var end = kind == EpisodeSectionKind.Intro ? confirmed.IntroEnd : confirmed.CreditsEnd;
        var startAt = start ?? proposed?.Start ?? _player.Position;
        var endAt = end ?? proposed?.End ?? TimeSpan.FromTicks(Math.Min(_player.Duration.Ticks,
            startAt.Ticks + TimeSpan.FromSeconds(60).Ticks));
        var label = kind == EpisodeSectionKind.Intro ? "Intro" : "Credits";
        var startInput = new NumberBox
        {
            Header = "Starts at (seconds)",
            Value = startAt.TotalSeconds,
            Minimum = 0,
            Maximum = _player.Duration.TotalSeconds,
            SmallChange = 0.25,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
        };
        var endInput = new NumberBox
        {
            Header = "Ends at (seconds)",
            Value = endAt.TotalSeconds,
            Minimum = 0,
            Maximum = _player.Duration.TotalSeconds,
            SmallChange = 0.25,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
        };
        AutomationProperties.SetAutomationId(startInput, "SectionAdjustStart");
        AutomationProperties.SetAutomationId(endInput, "SectionAdjustEnd");
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Adjust the {label.ToLowerInvariant()} exactly. Use the small arrows for quarter-second changes. Your saved boundaries take precedence over detections.",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(startInput);
        panel.Children.Add(endInput);
        panel.Children.Add(new TextBlock
        {
            Text = "Tip: use the timeline to find the frame you want, then open this editor again. Nothing is skipped automatically unless enabled in Preferences.",
            TextWrapping = TextWrapping.Wrap,
        });
        if (await Ask("Adjust " + label.ToLowerInvariant(), panel, "Save boundaries", "Cancel"))
        {
            if (!_player.SetSectionBounds(kind, TimeSpan.FromSeconds(startInput.Value),
                    TimeSpan.FromSeconds(endInput.Value)))
            {
                Say("The end must follow the start by at least two seconds and stay inside the video.");
            }

            ShowEpisodeSkip();
        }
    }

    /// <summary>Chapter names are hints only. The viewer approves the actual intervals before saving.</summary>
    private async Task ConfirmChapterSuggestionsAsync()
    {
        if (_player.ChapterSectionSuggestions is not { } found)
        {
            Say("No unambiguous intro or credits chapters were found. You can mark the sections manually.");
            return;
        }

        static string Label(string section, TimeSpan? start, TimeSpan? end) =>
            start is { } from && end is { } to
                ? section + ": " + from.ToString(@"hh\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture) + " – " + to.ToString(@"hh\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture)
                : section + ": not identified";
        var details = string.Join(Environment.NewLine,
            Label("Intro", found.IntroStart, found.IntroEnd),
            Label("Credits", found.CreditsStart, found.CreditsEnd));
        var description = new TextBlock
        {
            Text = details + Environment.NewLine + Environment.NewLine +
                "These are suggestions from chapter labels in this file, not scene analysis. Existing markers remain unchanged. Nothing is skipped automatically.",
            TextWrapping = TextWrapping.Wrap,
        };
        if (await Ask("Save the suggested section markers?", description, "Save markers", "Cancel"))
        {
            _player.ApplyChapterSectionSuggestions();
            ShowEpisodeSkip();
        }
    }

    private void OnSkipEpisodeSection(object sender, RoutedEventArgs args)
    {
        _player.SkipEpisodeSection();
        ShowEpisodeSkip();
    }
}
