using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Rex.Media.AppCore.Commands;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;

namespace Rex.Media.App;

public sealed partial class MainWindow
{
    // Keys whose press ran a shortcut: their release is the shortcut's too.
    private readonly HashSet<VirtualKey> _shortcutKeys = [];

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
        if (e.Handled || FocusManager.GetFocusedElement(Content.XamlRoot) is TextBox or PasswordBox or AutoSuggestBox)
        {
            return;
        }

        if (VirtualKeys.Chord((int)e.Key, IsDown(VirtualKey.Control), IsDown(VirtualKey.Menu), IsDown(VirtualKey.Shift)) is { } chord
            && _keymap.CommandFor(chord) is { } command)
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
        var items = await e.DataView.GetStorageItemsAsync();
        var paths = items.Select(item => item.Path).Where(path => !string.IsNullOrEmpty(path)).ToList();
        if (paths.Count > 0)
        {
            _player.Drop(paths, enqueue);
        }
    }

    private void OnStageDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => Run(CommandCatalog.ToggleFullScreen);

    /// <summary>In full screen, moving the mouse brings the controls back until it rests again.</summary>
    private void OnStagePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (IsFullScreen)
        {
            ShowFullScreenControls();
        }
    }

    /// <summary>
    /// The wheel over the picture, as <see cref="WheelGestures"/> decides: Ctrl or Alt with it (and
    /// a touchpad's pinch) zooms, zoomed in it moves about, otherwise it does what the settings say.
    /// </summary>
    private void OnStageWheel(object sender, PointerRoutedEventArgs e)
    {
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
        if (PlaylistView.SelectedIndex >= 0)
        {
            _player.PlayAt(PlaylistView.SelectedIndex);
        }
    }

    private void OnPlaylistKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
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

    /// <summary>Removes the selected entries, from the bottom up so the indexes stay right.</summary>
    private void RemoveSelected()
    {
        var indexes = PlaylistView.SelectedRanges
            .SelectMany(range => Enumerable.Range(range.FirstIndex, (int)range.Length))
            .OrderDescending()
            .ToList();
        foreach (var index in indexes)
        {
            _player.Playlist.RemoveAt(index);
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
