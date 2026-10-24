using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Rex.Media.AppCore.Commands;
using Rex.Media.Settings;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;

namespace Rex.Media.App;

public sealed partial class MainWindow
{
    // Keys whose press ran a shortcut: their release is the shortcut's too.
    private readonly HashSet<VirtualKey> _shortcutKeys = [];
    private List<(PlaylistRow Row, Rex.Media.AppCore.Player.PlaylistItem Item)>? _playlistDragOrder;
    private IReadOnlyList<Rex.Media.AppCore.Player.PlaylistItem>? _queueUndoOrder;
    private Rex.Media.AppCore.Player.PlaylistRemovalSnapshot? _undoQueueRemoval;
    private Rex.Media.AppCore.Player.PlaylistItem? _undoQueuePlayerItem;

    /// <summary>
    /// Whether <paramref name="key"/> is held. For Ctrl, Alt and Shift either of the pair counts:
    /// the general key's state does not always follow the right-hand one.
    /// </summary>
    private static bool IsDown(VirtualKey key) => key switch
    {
        VirtualKey.Control => Held(VirtualKey.Control) || Held(VirtualKey.LeftControl) || Held(VirtualKey.RightControl),
        VirtualKey.Menu => Held(VirtualKey.Menu) || Held(VirtualKey.LeftMenu) || Held(VirtualKey.RightMenu),
        VirtualKey.Shift => Held(VirtualKey.Shift) || Held(VirtualKey.LeftShift) || Held(VirtualKey.RightShift),
        _ => Held(key),
    };

    private static bool Held(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    /// <summary>
    /// Every key goes through the keymap first, before a focused slider or list takes the arrows,
    /// except while the user is typing into a text box.
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        _lastInputTick = Environment.TickCount64;
        if (e.Key == VirtualKey.Escape && !e.Handled && EscapeFromUpdateCard())
        {
            e.Handled = true;
            return;
        }

        if (!e.Handled && _subjectOpen && e.Key == VirtualKey.Escape)
        {
            CloseSubjectTools();
            Stage.Focus(FocusState.Programmatic);
            e.Handled = true;
            return;
        }

        // Controls in the tool panel own their activation and navigation keys.
        // In particular, Space toggles a switch and arrows adjust a slider.
        if (_subjectOpen && IsInSubjectPanel(FocusManager.GetFocusedElement(Content.XamlRoot) as DependencyObject))
        {
            return;
        }

        if (!e.Handled && HandleShellMediaKey((int)e.Key))
        {
            e.Handled = true;
            _shortcutKeys.Add(e.Key);
            return;
        }

        if (e.Handled || FocusManager.GetFocusedElement(Content.XamlRoot) is TextBox or PasswordBox or AutoSuggestBox)
        {
            return;
        }

        // WinUI already moves gamepad focus between buttons/cards with its
        // directional keys and invokes the focused control with A. Handle
        // only B here, consistently with Alt+Left/Escape: return from a
        // nested library route before leaving the whole browsing surface.
        if (e.Key == VirtualKey.GamepadB)
        {
            if (LibraryOpen)
            {
                if (_details.Count > 0 || _libraryParents.Count > 0
                    || _librarySeason is not null || _libraryGroup is not null)
                {
                    OnLibraryBack(sender, e);
                }
                else
                {
                    Run(CommandCatalog.ToggleLibrary);
                }
            }
            else if (IsFullScreen)
            {
                Run(CommandCatalog.LeaveFullScreen);
            }
            else
            {
                return;
            }

            e.Handled = true;
            _shortcutKeys.Add(e.Key);
            return;
        }

        if (VirtualKeys.Chord((int)e.Key, IsDown(VirtualKey.Control), IsDown(VirtualKey.Menu), IsDown(VirtualKey.Shift)) is not { } chord)
        {
            return;
        }

        var focused = FocusManager.GetFocusedElement(Content.XamlRoot) as DependencyObject;
        // Space/Enter on the focused transport belong to the native Button. Let
        // WinUI invoke it once on key release and keep keyboard focus there.
        // The global shortcut path below deliberately moves focus to Stage,
        // which is correct for unrelated controls but not for Play/Pause itself.
        if (ReferenceEquals(focused, PlayPauseButton)
            && (e.Key == VirtualKey.Space || e.Key == VirtualKey.Enter)
            && !IsDown(VirtualKey.Control) && !IsDown(VirtualKey.Menu) && !IsDown(VirtualKey.Shift))
        {
            return;
        }

        // Alt+Up/Down reorder selected queue entries. The root preview
        // handler runs before PlaylistView.KeyDown: do not consume those keys
        // as the global picture-pan shortcuts while keyboard focus is in the queue.
        if (IsWithin(focused, PlaylistView) && IsDown(VirtualKey.Menu)
            && (e.Key is VirtualKey.Up or VirtualKey.Down))
        {
            return;
        }

        if (ShortcutRouting.ListOwns(chord) && (IsWithin(focused, LibraryList)
            || IsWithin(focused, LibraryCollage) || IsWithin(focused, PlaylistView)))
        {
            return;
        }

        if (_keymap.CommandFor(chord) is { } command)
        {
            e.Handled = true;
            _shortcutKeys.Add(e.Key);
            Run(command);

            // Space and Enter activate a focused button or menu as well as being shortcuts. Move
            // focus to the picture before their release so one press cannot do both things.
            if (e.Key is VirtualKey.Space or VirtualKey.Enter)
            {
                Stage.Focus(FocusState.Programmatic);
            }
        }
    }

    /// <summary>
    /// The release of a key that ran a shortcut goes no further: buttons and menus act on Space and
    /// Enter when they are released, so a focused menu would open, or a focused button click, as well.
    /// </summary>
    private void OnPreviewKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (_shortcutKeys.Remove(e.Key))
        {
            e.Handled = true;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = IsDown(VirtualKey.Control) ? "Add to the playlist" : "Play";
        }
    }

    /// <summary>A drop plays what was dropped; with Ctrl held it joins the playlist instead. Subtitle files join what plays.</summary>
    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var enqueue = IsDown(VirtualKey.Control);
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = items.Select(item => item.Path).Where(path => !string.IsNullOrEmpty(path)).ToList();
            if (paths.Count > 0)
            {
                _player.Drop(paths, enqueue);
            }
        }
        catch (Exception ex)
        {
            // A drop from somewhere that stops answering (or a file that cannot be opened) is told of, not fatal.
            App.Log.Error("input", "A drop could not be taken: " + ex);
            Say("What was dropped could not be opened.");
        }
    }

    private void OnStageDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_subjectOpen || IsInSubjectPanel(e.OriginalSource as DependencyObject))
        {
            return;
        }

        Run(CommandCatalog.ToggleFullScreen);
    }

    private long _lastLensPointerTick;

    /// <summary>Interactive shaders use only the current pointer location; neither coordinates nor images are persisted.</summary>
    private void OnStagePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (IsFullScreen)
        {
            ShowFullScreenControls();
        }

        if (!Rex.Media.Primitives.VideoEffects.UsesPointer(_settings.VideoEffect) || !HasVideo
            || PictureShape() is not { } shape || Stage.ActualWidth <= 0 || Stage.ActualHeight <= 0)
        {
            return;
        }

        // Pointer notifications can arrive hundreds of times a second; bound queued GPU updates.
        var tick = Environment.TickCount64;
        if (tick - _lastLensPointerTick < 16)
        {
            return;
        }

        _lastLensPointerTick = tick;
        var position = e.GetCurrentPoint(Stage).Position;
        var picture = Rex.Media.AppCore.Player.SubtitleLook.Picture(Stage.ActualWidth, Stage.ActualHeight, shape.Across, shape.Down);
        var x = (position.X - picture.X) / picture.Width;
        var y = (position.Y - picture.Y) / picture.Height;
        var active = x >= 0 && x <= 1 && y >= 0 && y <= 1;
        var redraw = !_player.IsPlaying;
        OnPresenterThread(presenter =>
        {
            presenter.SetPointer((float)x, (float)y, active);
            if (redraw)
            {
                presenter.Redraw();
            }
        });
    }

    private void OnStagePointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!Rex.Media.Primitives.VideoEffects.UsesPointer(_settings.VideoEffect))
        {
            return;
        }

        var redraw = !_player.IsPlaying;
        OnPresenterThread(presenter =>
        {
            presenter.SetPointer(0.5f, 0.5f, false);
            if (redraw)
            {
                presenter.Redraw();
            }
        });
    }

    /// <summary>
    /// The wheel over the picture, as <see cref="WheelGestures"/> decides: Ctrl or Alt with it (and
    /// a touchpad's pinch) zooms, zoomed in it moves about, otherwise it does what the settings say.
    /// </summary>
    private void OnStageWheel(object sender, PointerRoutedEventArgs e)
    {
        if (IsInSubjectPanel(e.OriginalSource as DependencyObject))
        {
            return;
        }

        var point = e.GetCurrentPoint(Stage);
        var delta = point.Properties.MouseWheelDelta;
        if (delta == 0)
        {
            return;
        }

        var sideways = point.Properties.IsHorizontalMouseWheel;
        var action = WheelGestures.Interpret(_settings, IsDown(VirtualKey.Control), IsDown(VirtualKey.Menu), sideways, delta > 0, _view.IsZoomed && HasVideo);
        switch (action.Effect)
        {
            case WheelEffect.Zoom:
                ZoomWithWheel(point.Position, delta);
                break;
            case WheelEffect.Pan:
                PanWithWheel(delta, sideways);
                break;
            case WheelEffect.Command:
                Run(action.Command!);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnPlaylistDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_settings.MediaOpenClickMode == MediaOpenClick.DoubleClick && TappedPlaylistIndex(e.OriginalSource) is var index && index >= 0)
        {
            _player.PlayAt(index);
        }
    }

    private void OnPlaylistTapped(object sender, TappedRoutedEventArgs e)
    {
        if (_settings.MediaOpenClickMode == MediaOpenClick.SingleClick && !IsDown(VirtualKey.Control) && !IsDown(VirtualKey.Shift)
            && TappedPlaylistIndex(e.OriginalSource) is var index && index >= 0)
        {
            _player.PlayAt(index);
        }
    }

    /// <summary>
    /// A context click acts on the row under the pointer. Keep an existing multi-selection when it
    /// is clicked, but make an unselected row the sole choice so the menu never asks for a selection
    /// the user's click mode makes awkward to create.
    /// </summary>
    private void OnPlaylistRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var index = TappedPlaylistIndex(e.OriginalSource);
        if (index >= 0 && !PlaylistView.SelectedRanges.Any(range => index >= range.FirstIndex && index < range.FirstIndex + (int)range.Length))
        {
            PlaylistView.SelectedIndex = index;
        }
    }

    private void OnPlaylistDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        var items = _player.Playlist.Items;
        _playlistDragOrder = _playlistRows.Select((row, index) => (row, items[index])).ToList();
        e.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private void OnPlaylistDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        var before = _playlistDragOrder;
        _playlistDragOrder = null;
        if (args.DropResult != DataPackageOperation.Move || before is null || before.Count != _playlistRows.Count)
        {
            return;
        }

        // Rows are distinct objects even when the same file occurs twice. Mapping by identity keeps
        // duplicate playlist entries in exactly the order the user dropped them.
        var wanted = _playlistRows.Select(row => before.First(pair => ReferenceEquals(pair.Row, row)).Item).ToList();
        var previous = before.Select(pair => pair.Item).ToArray();
        var changed = false;
        for (var target = 0; target < wanted.Count; target++)
        {
            var from = _player.Playlist.IndexOf(wanted[target]);
            if (from != target)
            {
                _player.Playlist.Move(from, target);
                changed = true;
            }
        }

        if (changed)
        {
            OfferQueueUndo(previous);
        }
    }

    private int TappedPlaylistIndex(object source)
    {
        if (source is not DependencyObject current)
        {
            return -1;
        }

        for (; current is not null; current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { DataContext: PlaylistRow row })
            {
                return PlaylistView.Items.IndexOf(row);
            }
        }

        return -1;
    }

    private void OnPlaylistKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Up when IsDown(VirtualKey.Menu) && PlaylistView.SelectedItems.Count > 0:
                MoveSelectedPlaylistEntries(-1);
                e.Handled = true;
                break;
            case VirtualKey.Down when IsDown(VirtualKey.Menu) && PlaylistView.SelectedItems.Count > 0:
                MoveSelectedPlaylistEntries(1);
                e.Handled = true;
                break;
            case VirtualKey.Enter when PlaylistView.SelectedIndex >= 0:
                _player.PlayAt(PlaylistView.SelectedIndex);
                e.Handled = true;
                break;
            case VirtualKey.Delete when PlaylistView.SelectedItems.Count > 0:
                RemoveSelected();
                e.Handled = true;
                break;
        }
    }

    /// <summary>Moves the chosen playlist rows together and leaves them chosen at their new positions.</summary>
    private void MoveSelectedPlaylistEntries(int direction)
    {
        var chosen = ChosenEntries();
        if (chosen.Count == 0)
        {
            return;
        }

        var indexes = chosen.Select(_player.Playlist.IndexOf).Order().ToList();
        if ((direction < 0 && indexes[0] == 0) || (direction > 0 && indexes[^1] == _player.Playlist.Items.Count - 1))
        {
            return;
        }

        var previous = _player.Playlist.Items.ToArray();
        IEnumerable<int> moving = direction < 0 ? indexes : indexes.OrderDescending();
        foreach (var index in moving)
        {
            _player.Playlist.Move(index, index + direction);
        }

        OfferQueueUndo(previous);
        PlaylistView.SelectedItems.Clear();
        foreach (var item in chosen)
        {
            if (_player.Playlist.IndexOf(item) is >= 0 and var index)
            {
                PlaylistView.SelectedItems.Add(_playlistRows[index]);
            }
        }

        PlaylistView.ScrollIntoView(_playlistRows[_player.Playlist.IndexOf(chosen[0])]);
        Say(chosen.Count == 1 ? $"Moved to position {_player.Playlist.IndexOf(chosen[0]) + 1}" : $"Moved {chosen.Count} entries");
    }

    private void OfferQueueUndo(IReadOnlyList<Rex.Media.AppCore.Player.PlaylistItem> before)
    {
        _queueUndoOrder = before;
        PlaylistUndoReorder.IsEnabled = true;
    }

    private void OnPlaylistUndoReorder(object sender, RoutedEventArgs e)
    {
        if (_queueUndoOrder is not { } before)
        {
            return;
        }

        _queueUndoOrder = null;
        if (_player.Playlist.RestoreOrder(before))
        {
            Say("Queue order restored");
        }

        PlaylistUndoReorder.IsEnabled = false;
    }

    /// <summary>Remove the exact selected entry instances with one state change, preserving playback.</summary>
    private void RemoveSelected()
    {
        var chosen = ChosenEntries();
        if (chosen.Count == 0)
        {
            return;
        }

        _undoQueueRemoval = _player.Playlist.CaptureRemoval();
        _undoQueuePlayerItem = _player.Item;
        _player.Playlist.RemoveEntries(chosen);
        PlaylistUndoRemoval.IsEnabled = true;
        Say(chosen.Count == 1 ? "Removed entry from the queue. Undo removal is available"
            : $"Removed {chosen.Count} entries from the queue. Undo removal is available");
    }

    private void OnPlaylistUndoRemoval(object sender, RoutedEventArgs e)
    {
        if (_undoQueueRemoval is not { } saved)
        {
            return;
        }

        var canRestore = ReferenceEquals(_undoQueuePlayerItem, _player.Item)
            && _player.Playlist.UndoRemoval(saved);
        _undoQueueRemoval = null;
        _undoQueuePlayerItem = null;
        PlaylistUndoRemoval.IsEnabled = false;
        Say(canRestore ? "Removed queue entries restored" : "The queue changed; removal cannot be undone");
    }

    /// <summary>Explicit navigation does not change the queue, playback or other selection.</summary>
    private void OnPlaylistJumpNow(object sender, RoutedEventArgs e)
    {
        if (_player.Playlist.CurrentIndex is >= 0 and var index && index < _playlistRows.Count)
        {
            PlaylistView.ScrollIntoView(_playlistRows[index]);
        }
    }

    private void OnPrevious(object sender, RoutedEventArgs e) => Run(CommandCatalog.Previous);

    private void OnPlayPause(object sender, RoutedEventArgs e) => Run(CommandCatalog.PlayPause);

    private void OnStop(object sender, RoutedEventArgs e) => Run(CommandCatalog.Stop);

    private void OnNext(object sender, RoutedEventArgs e) => Run(CommandCatalog.Next);

    private void OnShuffle(object sender, RoutedEventArgs e) => Run(CommandCatalog.ToggleShuffle);

    private void OnRepeat(object sender, RoutedEventArgs e) => Run(CommandCatalog.CycleRepeat);

    private void OnTogglePlaylist(object sender, RoutedEventArgs e) => Run(CommandCatalog.TogglePlaylist);

    private void OnToggleLibrary(object sender, RoutedEventArgs e) => Run(CommandCatalog.ToggleLibrary);

    private void OnFullScreen(object sender, RoutedEventArgs e) => Run(CommandCatalog.ToggleFullScreen);

    private void OnMute(object sender, RoutedEventArgs e) => Run(CommandCatalog.Mute);

    /// <summary>Picked files: the picker's own files, which are ordinary paths for an unpackaged app.</summary>
    private static List<string> PathsOf(IEnumerable<IStorageItem> items) =>
        [.. items.Select(item => item.Path).Where(path => !string.IsNullOrEmpty(path))];
}
