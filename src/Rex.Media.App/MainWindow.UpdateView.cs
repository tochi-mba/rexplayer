using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Rex.Media.AppCore.Updates;
using Rex.Media.Settings;

namespace Rex.Media.App;

/// <summary>
/// The update experience on screen (TOOL-09): the quiet indicator in the controls, the panel it
/// opens, and the card that asks only when a decision is needed. What each shows comes from
/// <see cref="UpdateView"/> and when the card may show from <see cref="UpdateFlow.Prompt"/>; this
/// file draws them and turns clicks into <see cref="UpdateFlow"/>'s answers.
/// </summary>
public sealed partial class MainWindow
{
    private static Style? _accentButton;
    private bool _updatePanelOpen;
    private UpdatePanel? _shownPanel;
    private UpdateCardView? _shownCard;
    private string? _shownNotes;
    private TextBlock? _updateStatusText;

    private static Style AccentButton => _accentButton ??= (Style)Application.Current.Resources["AccentButtonStyle"];

    /// <summary>The panel for the updater's state now.</summary>
    private UpdatePanel UpdatePanelNow() => UpdateView.Panel(new UpdateSituation(
        Version, Pending, _settings.UpdateMode, _updateChecking, _updateDownload is not null, _updateTransfer,
        _connection.Online, _connection.Metered, _settings.LastUpdateCheck, _checkProblem,
        _updatePanelOpen || _updateAsked || _updateStatusText is not null, DateTimeOffset.Now));

    /// <summary>Draws the indicator, the panel and the card as the updater stands, and tells a screen reader what changed.</summary>
    private void ShowUpdates()
    {
        if (_closing)
        {
            return;
        }

        var panel = UpdatePanelNow();
        var open = _updatePanelOpen || _updateAsked || _updateStatusText is not null;
        DrawIndicator(UpdateView.Indicator(panel, _updatePanelOpen || _updateAsked));
        DrawPanel(panel);
        if (_updateStatusText is { } status)
        {
            status.Text = panel.Line;
        }

        if (UpdateView.Announcement(_shownPanel, panel, open) is { } spoken)
        {
            Announce(spoken);
        }

        _shownPanel = panel;
        var prompt = _updatesStarted
            ? UpdateFlow.Prompt(Pending, _settings.UpdateMode, UpdateMomentNow(), DateTimeOffset.UtcNow, showing: _shownCard is not null)
            : UpdatePrompt.None;
        DrawCard(UpdateView.Card(prompt, Pending, _connection.Metered));
    }

    private void DrawIndicator(UpdateIndicatorView indicator)
    {
        UpdateIndicator.Visibility = indicator.Visible ? Visibility.Visible : Visibility.Collapsed;
        UpdateIndicatorIcon.Glyph = indicator.Glyph switch
        {
            UpdateGlyph.Paused => "\uE769",
            UpdateGlyph.Ready => "\uE777",
            UpdateGlyph.Checking => "\uE895",
            UpdateGlyph.UpToDate => "\uE73E",
            _ => "\uE896",
        };
        UpdateIndicatorRing.IsIndeterminate = indicator.Busy;
        UpdateIndicatorRing.Value = (indicator.Progress ?? 0) * 100;
        UpdateIndicatorRing.IsActive = indicator.Busy || indicator.Progress is not null;
        UpdateBadgeAttention.Visibility = indicator.Badge == UpdateBadge.Attention ? Visibility.Visible : Visibility.Collapsed;
        UpdateBadgeSuccess.Visibility = indicator.Badge == UpdateBadge.Success ? Visibility.Visible : Visibility.Collapsed;
        UpdateBadgeCritical.Visibility = indicator.Badge == UpdateBadge.Critical ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(UpdateIndicator, indicator.ToolTip);
        AutomationProperties.SetHelpText(UpdateIndicator, indicator.ToolTip);
    }

    private void DrawPanel(UpdatePanel panel)
    {
        var error = panel.Tone == UpdateTone.Error;
        UpdatePanelTitle.Text = panel.Title;
        UpdatePanelStatus.Text = panel.Status;
        UpdatePanelErrorBack.Visibility = UpdatePanelErrorIcon.Visibility = error ? Visibility.Visible : Visibility.Collapsed;
        UpdatePanelStatusBox.Margin = error ? new Thickness(10, 8, 10, 8) : new Thickness(0);
        UpdatePanelProgress.Visibility = panel.Progress is not null || panel.Busy ? Visibility.Visible : Visibility.Collapsed;
        UpdatePanelProgress.IsIndeterminate = panel.Progress is null;
        UpdatePanelProgress.Value = (panel.Progress ?? 0) * 100;
        UpdatePanelNote.Text = panel.Note ?? "";
        UpdatePanelNote.Visibility = panel.Note is null ? Visibility.Collapsed : Visibility.Visible;
        var focusLost = false;
        foreach (var (button, command) in new (ButtonBase Button, UpdateCommands Command)[]
        {
            (UpdateDownloadButton, UpdateCommands.Download), (UpdateResumeButton, UpdateCommands.Resume), (UpdateRetryButton, UpdateCommands.Retry),
            (UpdateRestartButton, UpdateCommands.Restart), (UpdateCheckAgainButton, UpdateCommands.CheckAgain), (UpdatePauseButton, UpdateCommands.Pause),
            (UpdateLaterButton, UpdateCommands.Later), (UpdateCancelButton, UpdateCommands.Cancel),
        })
        {
            var shown = panel.Commands.HasFlag(command);
            focusLost |= !shown && button.Visibility == Visibility.Visible && button.FocusState != FocusState.Unfocused;
            button.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            var accent = panel.Primary == command;
            if (button is Button plain && ReferenceEquals(plain.Style, AccentButton) != accent)
            {
                plain.Style = accent ? AccentButton : null;
            }
        }

        // The button just pressed went (Pause becomes Resume): the keyboard moves to what replaced it.
        if (focusLost && _updatePanelOpen)
        {
            ButtonBase[] buttons = [UpdateDownloadButton, UpdateResumeButton, UpdateRetryButton, UpdateRestartButton, UpdateCheckAgainButton, UpdatePauseButton, UpdateLaterButton, UpdateCancelButton];
            buttons.FirstOrDefault(button => button.Visibility == Visibility.Visible)?.Focus(FocusState.Keyboard);
        }

        DrawNotes(UpdateNotes, Pending);
    }

    private void DrawCard(UpdateCardView? card)
    {
        if (card == _shownCard)
        {
            return;
        }

        if (card is null)
        {
            FocusAwayFrom(UpdateCard);
            UpdateCard.Visibility = Visibility.Collapsed;
            _shownCard = null;
            return;
        }

        var fresh = _shownCard is null || _shownCard.Kind != card.Kind || _shownCard.Version != card.Version;
        UpdateCardText.Text = card.Message;
        UpdateCardAction.Content = card.Action;
        UpdateCardReadyIcon.Visibility = card.Kind == UpdatePrompt.Ready ? Visibility.Visible : Visibility.Collapsed;
        UpdateCardAvailableIcon.Visibility = card.Kind == UpdatePrompt.Ready ? Visibility.Collapsed : Visibility.Visible;
        _shownCard = card;
        if (fresh)
        {
            UpdateCardWhatsNew.IsExpanded = false;
            DrawNotes(UpdateCardNotes, Pending, force: true);
            Announce(card.Message);
            App.Log.Info(LogSource, $"Offered rexplayer {card.Version} ({card.Kind}).");
        }

        UpdateCard.Visibility = Visibility.Visible;
    }

    /// <summary>Release notes as headings, bullets with their bold labels, and paragraphs.</summary>
    private void DrawNotes(RichTextBlock box, PendingUpdate? pending, bool force = false)
    {
        var notes = pending is null ? "" : pending.Version + "\n" + pending.Notes;
        if (!force && ReferenceEquals(box, UpdateNotes) && notes == _shownNotes)
        {
            return;
        }

        if (ReferenceEquals(box, UpdateNotes))
        {
            _shownNotes = notes;
        }

        box.Blocks.Clear();
        var blocks = ReleaseNotes.Blocks(pending?.Notes ?? "");
        if (blocks.Count == 0)
        {
            box.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "The release page lists what changed." } } });
            return;
        }

        foreach (var block in blocks)
        {
            var paragraph = new Paragraph { Margin = new Thickness(block.Kind == NoteKind.Bullet ? 14 : 0, block.Kind == NoteKind.Heading ? 8 : 0, 0, 4) };
            if (block.Kind == NoteKind.Bullet)
            {
                paragraph.Inlines.Add(new Run { Text = "\u2022  " });
            }

            if (block.Label is { } label)
            {
                paragraph.Inlines.Add(new Run { Text = label + " ", FontWeight = FontWeights.SemiBold });
            }

            paragraph.Inlines.Add(new Run { Text = block.Text, FontWeight = block.Kind == NoteKind.Heading ? FontWeights.SemiBold : FontWeights.Normal });
            box.Blocks.Add(paragraph);
        }
    }

    /// <summary>Tells a screen reader, once, what the updater just did.</summary>
    private void Announce(string text)
    {
        var peer = FrameworkElementAutomationPeer.FromElement(Root) ?? FrameworkElementAutomationPeer.CreatePeerForElement(Root);
        peer?.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.ImportantMostRecent, text, "rexplayer.update");
    }

    /// <summary>
    /// Opens the update panel by the indicator (Check for updates does). In full screen the
    /// controls come up first; with the controls hidden it opens at the picture's corner instead.
    /// </summary>
    private void OpenUpdatePanel()
    {
        if (_dialog is not null)
        {
            return;
        }

        _updatePanelOpen = true;
        if (IsFullScreen)
        {
            ShowFullScreenControls();
        }

        ShowUpdates();
        if (Controls.Visibility == Visibility.Visible)
        {
            UpdateIndicator.UpdateLayout();
            UpdateFlyout.ShowAt(UpdateIndicator);
        }
        else
        {
            UpdateFlyout.ShowAt(Stage, new FlyoutShowOptions
            {
                Placement = FlyoutPlacementMode.BottomEdgeAlignedRight,
                Position = new Windows.Foundation.Point(Math.Max(0, Stage.ActualWidth - 16), 16),
            });
        }
    }

    private void OnUpdateFlyoutOpened(object sender, object e)
    {
        _updatePanelOpen = true;
        ShowUpdates();
    }

    private void OnUpdateFlyoutClosed(object sender, object e)
    {
        _updatePanelOpen = false;
        ShowUpdates();
    }

    /// <summary>Later's choices: remind in an hour, tomorrow or next week; for a downloaded version, install on closing or tonight; skip this version.</summary>
    private MenuFlyout LaterMenu(string id)
    {
        var menu = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            void Add(string text, string suffix, Action chosen)
            {
                var item = new MenuFlyoutItem { Text = text };
                AutomationProperties.SetAutomationId(item, id + suffix);
                item.Click += (_, _) => chosen();
                menu.Items.Add(item);
            }

            Add("Remind me in an hour", "Hour", () => RemindAboutUpdate(RemindChoice.InAnHour));
            Add("Remind me tomorrow", "Tomorrow", () => RemindAboutUpdate(RemindChoice.Tomorrow));
            Add("Remind me next week", "Week", () => RemindAboutUpdate(RemindChoice.NextWeek));
            if (_shownPanel?.OffersTiming == true)
            {
                menu.Items.Add(new MenuFlyoutSeparator());
                Add("Install when I close rexplayer", "OnClose", () => Answer(UpdateFlow.WhenClosed));
                Add("Install tonight", "Tonight", () => Answer(pending => UpdateFlow.ScheduledTonight(pending, DateTimeOffset.UtcNow, TimeZoneInfo.Local)));
            }

            menu.Items.Add(new MenuFlyoutSeparator());
            Add("Skip this version", "Skip", SkipUpdate);
        };
        return menu;
    }

    /// <summary>Applies the user's answer to the pending update and says what it now does.</summary>
    private void Answer(Func<PendingUpdate, PendingUpdate> answer)
    {
        UpdateFlyout.Hide();
        if (Pending is { } pending)
        {
            SetPending(answer(pending));
            Say(UpdatePanelNow().Line);
        }
    }

    private void RemindAboutUpdate(RemindChoice choice)
    {
        UpdateFlyout.Hide();
        if (Pending is { } pending)
        {
            SetPending(UpdateFlow.Remind(pending, choice, DateTimeOffset.UtcNow));
            Say(UpdateView.Reminded(choice, pending.Version));
        }
    }

    private void SkipUpdate()
    {
        UpdateFlyout.Hide();
        if (Pending is not { } pending)
        {
            return;
        }

        _updateDownload?.Cancel();
        _settings = _settings with { SkippedUpdateVersion = pending.Version };
        SetPending(null);
        Forget(Task.Run(() => UpdateDownloader.Clean(UpdateFolder, null)));
        Say($"rexplayer {pending.Version} will not be offered again; a newer version will be.");
    }

    private void OnUpdateCardActionClick(object sender, RoutedEventArgs e)
    {
        if (_shownCard?.Kind == UpdatePrompt.Ready)
        {
            Forget(InstallAsync(unattended: false));
        }
        else
        {
            OnUpdateDownloadClick(sender, e);
        }
    }

    private void OnUpdateDownloadClick(object sender, RoutedEventArgs e)
    {
        if (Pending is { } pending)
        {
            SetPending(UpdateFlow.Approve(pending));
            UpdateTick();
        }
    }

    /// <summary>Resume download, and Retry after a failed one: both go now, whatever the connection.</summary>
    private void OnUpdateResumeClick(object sender, RoutedEventArgs e)
    {
        if (Pending is { } pending)
        {
            SetPending(UpdateFlow.Resume(pending));
            UpdateTick();
        }
    }

    private void OnUpdatePauseClick(object sender, RoutedEventArgs e)
    {
        if (Pending is { } pending)
        {
            SetPending(UpdateFlow.Pause(pending));
            _updateDownload?.Cancel();
        }
    }

    private void OnUpdateCancelClick(object sender, RoutedEventArgs e)
    {
        if (Pending is { } pending)
        {
            SetPending(UpdateFlow.CancelDownload(pending));
            if (_updateDownload is { } running)
            {
                // Its partial file goes once the download lets go of it.
                running.Cancel();
            }
            else
            {
                Forget(Task.Run(() => UpdateDownloader.Clean(UpdateFolder, null)));
            }
        }
    }

    private void OnUpdateCheckAgainClick(object sender, RoutedEventArgs e) => Forget(CheckForUpdatesAsync(asked: true));

    private void OnUpdateRestartClick(object sender, RoutedEventArgs e)
    {
        UpdateFlyout.Hide();
        Forget(InstallAsync(unattended: false));
    }

    private void OnUpdateReleasePageClick(object sender, RoutedEventArgs e)
    {
        if (Pending is { } pending && Uri.TryCreate(pending.ReleasePage, UriKind.Absolute, out var page))
        {
            _ = Windows.System.Launcher.LaunchUriAsync(page);
        }
    }

    /// <summary>Escape with the keyboard in the update card is "Later: tomorrow"; true when it was.</summary>
    private bool EscapeFromUpdateCard()
    {
        if (_shownCard is null || !HasFocusIn(UpdateCard))
        {
            return false;
        }

        RemindAboutUpdate(RemindChoice.Tomorrow);
        return true;
    }

    /// <summary>A card that closes with the keyboard inside it hands the focus to the picture, not to nowhere.</summary>
    private void FocusAwayFrom(UIElement container)
    {
        if (HasFocusIn(container))
        {
            Stage.Focus(FocusState.Programmatic);
        }
    }

    private bool HasFocusIn(UIElement container)
    {
        if (Content?.XamlRoot is not { } root || Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root) is not DependencyObject focused)
        {
            return false;
        }

        for (var node = focused; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, container))
            {
                return true;
            }
        }

        return false;
    }
}
