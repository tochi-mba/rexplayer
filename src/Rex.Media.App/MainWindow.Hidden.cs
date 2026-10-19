using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Media;
using Rex.Media.AppCore.Player;

namespace Rex.Media.App;

/// <summary>
/// A minimised window draws nothing (<see cref="HiddenWindow"/>): the presenter leaves pictures
/// undrawn and the visualisation and kinetic captions stop drawing, while decoding, sound and the
/// timeline carry on. Coming back, the latest picture is drawn at once.
/// </summary>
public sealed partial class MainWindow
{
    private readonly HiddenWindow _hidden = new();
    private bool _captionsDrawing;

    private void WireHiddenWindow()
    {
        AppWindow.Changed += OnAppWindowChanged;
        VisibilityChanged += (_, e) => Hide(_hidden.Visible(e.Visible));
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPresenterChange || args.DidSizeChange || args.DidVisibilityChange)
        {
            Hide(_hidden.Minimised(sender.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized }));
        }
    }

    /// <summary>Follows the window into or out of sight, when <paramref name="changed"/>.</summary>
    private void Hide(bool changed)
    {
        if (!changed || _closing)
        {
            return;
        }

        if (_hidden.Hidden)
        {
            OnPresenterThread(presenter => presenter.HidePictures());
        }
        else
        {
            var playing = _player.IsPlaying;
            OnPresenterThread(presenter =>
            {
                if (HiddenWindow.Restore(playing, presenter.ShowPictures()) == RestoreAction.Redraw)
                {
                    presenter.Redraw();
                }
                else
                {
                    OnWindowThread(() => _player.Seek(_player.Position));
                }
            });
            WakeVisualizer();
        }

        KeepCaptionsDrawing();
    }

    /// <summary>
    /// Kinetic captions are drawn on every frame while they show and the window is in sight; a
    /// minimised window keeps them as they are, untouched, and they move on again when it is back.
    /// </summary>
    private void KeepCaptionsDrawing()
    {
        var wanted = _captioning && _hidden.Draws;
        if (wanted != _captionsDrawing)
        {
            _captionsDrawing = wanted;
            if (wanted)
            {
                CompositionTarget.Rendering += OnCaptionRendering;
            }
            else
            {
                CompositionTarget.Rendering -= OnCaptionRendering;
            }
        }
    }
}
