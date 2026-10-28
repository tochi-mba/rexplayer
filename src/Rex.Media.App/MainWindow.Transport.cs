using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Player;
using Rex.Media.AppCore.Transcription;

namespace Rex.Media.App;

/// <summary>
/// The transport bar's layout (UI-10): the buttons each mode shows, the ones that move into More when
/// the bar is too narrow for them, the line the speed and volume group takes, the bar's spacing, what
/// its buttons can act on and whether the coverage rail has room. <see cref="TransportFit"/> decides;
/// this part measures the buttons and applies the answer, only when the window, the bar or the mode
/// changes and never per frame, and changes only what differs.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The space after each button: TransportButtons' and AudioControlStrip's spacing.</summary>
    private const double TransportSpacing = 4;

    /// <summary>Below this width the bar keeps tighter margins.</summary>
    private const double NarrowTransport = 820;

    private readonly Dictionary<FrameworkElement, double> _transportWidths = [];
    private readonly List<double> _transportMovable = [];
    private readonly List<ButtonBase> _transportMoved = [];
    private ButtonBase[]? _transportOrder;
    private ButtonBase[]? _transportMinimalOrder;
    private (ButtonBase Button, string Command)[]? _transportCommands;
    private bool _transportSpeedMoved;
    private bool _transportCompact;
    private bool _transportPicture;
    private bool _layingOutTransport;

    /// <summary>The bar follows its own buttons and group as they change width.</summary>
    private void WireTransport()
    {
        TransportButtons.SizeChanged += (_, _) => LayOutTransport();
        AudioControlStrip.SizeChanged += (_, _) => LayOutTransport();
    }

    /// <summary>Lays the bar out for the window's size and mode.</summary>
    private void LayOutTransport()
    {
        if (_layingOutTransport || _closed)
        {
            return;
        }

        _layingOutTransport = true;
        try
        {
            ArrangeTransport();
        }
        finally
        {
            _layingOutTransport = false;
        }
    }

    private void ArrangeTransport()
    {
        var minimal = _settings.MinimalInterface && !IsFullScreen;
        var narrow = minimal || Root.ActualWidth < NarrowTransport;
        var isShort = !IsFullScreen && TransportFit.IsShort(Root.ActualHeight);
        var padding = narrow ? new Thickness(12, 8, 12, 8) : new Thickness(16, 12, 16, 12);
        var gap = narrow ? 8.0 : 12.0;
        var lines = narrow ? 4.0 : 8.0;
        if (Controls.Padding != padding)
        {
            Controls.Padding = padding;
        }

        Controls.RowSpacing = lines;
        SeekRow.ColumnSpacing = gap;
        TransportRow.ColumnSpacing = gap;
        VolumeBar.Width = minimal || isShort ? 88 : 120;

        // The minimal interface keeps Play, the library and full screen; the rest wait for the full one.
        foreach (var element in new UIElement[] { StartOverButton, PreviousButton, NextButton })
        {
            SetShown(element, !minimal);
        }

        _transportOrder ??= [ShuffleButton, RepeatButton, PlaylistButton, LibraryButton, FullScreenButton, StopButton];
        _transportMinimalOrder ??= [LibraryButton, FullScreenButton];
        var order = minimal ? _transportMinimalOrder : _transportOrder;
        var picture = ItemIsPicture;
        var fixedWidth = TransportWidth(PlayPauseButton)
            + (minimal ? TransportWidth(MinimalInterfaceButton) : TransportWidth(PreviousButton) + TransportWidth(NextButton))
            + (StageChaptersButton.Visibility == Visibility.Visible ? TransportWidth(StageChaptersButton) : 0);
        _transportMovable.Clear();
        foreach (var button in order)
        {
            // The separator goes with the last of the toggles to leave.
            _transportMovable.Add(TransportWidth(button) + (!minimal && ReferenceEquals(button, FullScreenButton) ? TransportWidth(TransportSeparator) : 0));
        }

        var group = (picture ? 0 : TransportWidth(SpeedButton) + TransportWidth(MuteButton) + VolumeBar.Width + TransportSpacing)
            + PickerWidth(MovieVersionButton) + PickerWidth(PictureIntervalButton) + PickerWidth(UpdateIndicator);
        var layout = Root.ActualWidth > 0
            ? TransportFit.Decide(new TransportRoom(Root.ActualWidth - padding.Left - padding.Right, gap, fixedWidth,
                _transportMovable, TransportWidth(TransportMoreButton), group, picture ? 0 : TransportWidth(SpeedButton), isShort))
            : default;

        var focused = Content?.XamlRoot is { } root ? FocusManager.GetFocusedElement(root) : null;
        var focusMore = false;
        _transportMoved.Clear();
        for (var i = 0; i < order.Length; i++)
        {
            var moved = i < layout.Moved;
            if (moved)
            {
                _transportMoved.Add(order[i]);
                focusMore |= ReferenceEquals(focused, order[i]) && order[i].Visibility == Visibility.Visible;
            }

            SetShown(order[i], !moved);
        }

        if (minimal)
        {
            foreach (var hidden in new UIElement[] { StopButton, ShuffleButton, RepeatButton, PlaylistButton })
            {
                SetShown(hidden, false);
            }
        }

        // Shuffle, Repeat, Playlist, Library and Full screen lead the order: one of them left keeps the separator.
        SetShown(TransportSeparator, !minimal && layout.Moved < 5);
        _transportSpeedMoved = layout.SpeedMoved && !picture;
        SetShown(SpeedButton, !picture && !_transportSpeedMoved);
        SetShown(MuteButton, !picture);
        SetShown(VolumeBar, !picture);
        var more = _transportMoved.Count > 0 || _transportSpeedMoved;
        if (!more && TransportMoreButton.Visibility == Visibility.Visible)
        {
            TransportMoreMenu.Hide();
        }

        SetShown(TransportMoreButton, more);
        PlaceTransportGroup(layout.Wrapped, lines);
        if (focusMore)
        {
            // A button that moved into More had keyboard focus: More holds it once it is laid out.
            OnWindowThread(() => TransportMoreButton.Focus(FocusState.Programmatic));
        }

        var compact = minimal || isShort;
        if (compact != _transportCompact)
        {
            _transportCompact = compact;
            ShowTranscriptRail(Rex.Media.AppCore.Transcription.SubtitleBuffer.StateAt(DisplayedCoverage, DisplayedDuration, _player.Position, DisplayedHearing));
        }
    }

    /// <summary>The speed and volume group beside the title, or right-aligned on a line of its own.</summary>
    private void PlaceTransportGroup(bool wrapped, double lines)
    {
        if (Grid.GetRow(AudioControlStrip) != (wrapped ? 1 : 0))
        {
            Grid.SetRow(AudioControlStrip, wrapped ? 1 : 0);
            Grid.SetColumn(AudioControlStrip, wrapped ? 0 : 2);
            Grid.SetColumnSpan(AudioControlStrip, wrapped ? 3 : 1);
            AudioControlStrip.HorizontalAlignment = wrapped ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        }

        TransportRow.RowSpacing = wrapped ? lines : 0;
    }

    /// <summary>
    /// How wide <paramref name="element"/> is with the space after it: its set width, else the width it
    /// last had on screen, so a button in More still counts what it needs to come back.
    /// </summary>
    private double TransportWidth(FrameworkElement element)
    {
        if (!double.IsNaN(element.Width))
        {
            return element.Width + TransportSpacing;
        }

        if (element.Visibility == Visibility.Visible && element.ActualWidth > 0)
        {
            _transportWidths[element] = element.ActualWidth;
        }

        return Math.Max(element.MinWidth, _transportWidths.GetValueOrDefault(element, 44)) + TransportSpacing;
    }

    /// <summary>The width of a picker the media shows, or nothing when it is not shown.</summary>
    private double PickerWidth(FrameworkElement element) => element.Visibility == Visibility.Visible ? TransportWidth(element) : 0;

    private static void SetShown(UIElement element, bool shown)
    {
        var visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (element.Visibility != visibility)
        {
            element.Visibility = visibility;
        }
    }

    /// <summary>A picture has nothing to speed up or hear, so a slideshow shows no speed or volume.</summary>
    private void ShowTransportState()
    {
        if (ItemIsPicture != _transportPicture)
        {
            _transportPicture = ItemIsPicture;
            LayOutTransport();
        }
    }

    /// <summary>The coverage rail shows only coverage there is, and only where the bar has room for it.</summary>
    private void ShowTranscriptRail(BufferState state) =>
        TranscriptRail.Visibility = Rex.Media.AppCore.Transcription.SubtitleBuffer.RailShown(ItemIsPicture, state, _transportCompact) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>More holds what did not fit, in the bar's order and ticked to match, made as it opens.</summary>
    private void OnTransportMoreOpening(object sender, object e)
    {
        _transportCommands ??=
        [
            (StopButton, CommandCatalog.Stop), (ShuffleButton, CommandCatalog.ToggleShuffle), (RepeatButton, CommandCatalog.CycleRepeat),
            (PlaylistButton, CommandCatalog.TogglePlaylist), (LibraryButton, CommandCatalog.ToggleLibrary), (FullScreenButton, CommandCatalog.ToggleFullScreen),
        ];
        var items = TransportMoreMenu.Items;
        items.Clear();
        foreach (var (button, command) in _transportCommands)
        {
            if (!_transportMoved.Contains(button))
            {
                continue;
            }

            MenuFlyoutItem item = MenuBarLayout.IsOnOff(command) ? new ToggleMenuFlyoutItem { IsChecked = IsOn(command) } : new MenuFlyoutItem();
            item.Text = AutomationProperties.GetName(button);
            item.Icon = new FontIcon { Glyph = button.Content is FontIcon icon ? icon.Glyph : "" };
            item.IsEnabled = button.IsEnabled;
            item.KeyboardAcceleratorTextOverride = _keymap.Label(command);
            AutomationProperties.SetAutomationId(item, "Command-" + command);
            item.Click += (_, _) => Run(command);
            items.Add(item);
        }

        if (_transportSpeedMoved)
        {
            var speed = new MenuFlyoutSubItem { Text = "Playback speed, " + SpeedLabel(_player.Speed) };
            AutomationProperties.SetAutomationId(speed, "TransportMoreSpeed");
            FillSpeedMenu(speed.Items);
            items.Add(speed);
        }
    }
}
