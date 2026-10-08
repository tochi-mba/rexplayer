using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Player;
using Rex.Media.Primitives;
using Rex.Media.Video;
using Windows.Foundation;

namespace Rex.Media.App;

/// <summary>
/// Zooming into the picture and moving about in it (VID-07): Alt with the wheel zooms at the
/// pointer, dragging moves the view, and the navigator (the whole picture, small, in a corner)
/// shows where the view is and moves it with a click or a drag. The presenter draws both; the
/// window draws the navigator's frame and the box marking the view, and takes the pointer.
/// </summary>
public sealed partial class MainWindow
{
    private const double WheelZoom = 1.25;
    private const double PanStep = 0.1;

    private PictureView _view = PictureView.Whole;
    private Point? _dragFrom;

    private void WireZoom()
    {
        Stage.PointerPressed += OnStagePressed;
        Stage.PointerMoved += OnStageDragged;
        Stage.PointerReleased += OnStageReleased;
        Stage.PointerCaptureLost += (_, _) => _dragFrom = null;
        Navigator.PointerPressed += OnNavigatorPointer;
        Navigator.PointerMoved += OnNavigatorPointer;
        Navigator.PointerReleased += (_, e) => Navigator.ReleasePointerCapture(e.Pointer);
        Stage.SizeChanged += (_, _) => ShowNavigator();
    }

    /// <summary>The picture's shape on screen as across : down, or null when there is no picture.</summary>
    private (long Across, long Down)? PictureShape()
    {
        if (_player.Info?.FirstTrack(MediaKind.Video)?.Video is not { } video)
        {
            return null;
        }

        var (_, across, down) = VideoGeometry.Shape(video.Width, video.Height, video.PixelAspect, _aspect.Ratio, _crop.Ratio);
        return (across, down);
    }

    private bool RunZoomCommand(string command)
    {
        switch (command)
        {
            case CommandCatalog.ZoomIn:
                SetView(_view.Step(1), announce: true);
                return true;
            case CommandCatalog.ZoomOut:
                SetView(_view.Step(-1), announce: true);
                return true;
            case CommandCatalog.ResetZoom:
                SetView(PictureView.Whole, announce: true);
                return true;
            case CommandCatalog.PanLeft:
                SetView(_view.PanBy(PanStep, 0));
                return true;
            case CommandCatalog.PanRight:
                SetView(_view.PanBy(-PanStep, 0));
                return true;
            case CommandCatalog.PanUp:
                SetView(_view.PanBy(0, PanStep));
                return true;
            case CommandCatalog.PanDown:
                SetView(_view.PanBy(0, -PanStep));
                return true;
            case CommandCatalog.ToggleNavigator:
                _settings = _settings with { ShowNavigator = !_settings.ShowNavigator };
                Say(_settings.ShowNavigator ? "Navigator on" : "Navigator off");
                SetView(_view);
                RememberLater();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Shows <paramref name="view"/> of the picture, and the navigator when it helps.</summary>
    private void SetView(PictureView view, bool announce = false)
    {
        if (!HasVideo)
        {
            view = PictureView.Whole;
        }

        _view = view;
        var navigator = NavigatorShown;
        var redraw = !_player.IsPlaying;
        OnPresenterThread(presenter =>
        {
            presenter.SetView(view, navigator);
            if (redraw)
            {
                presenter.Redraw();
            }
        });
        ShowNavigator();
        if (announce)
        {
            Say(view.IsZoomed ? string.Create(CultureInfo.InvariantCulture, $"Zoom {view.Zoom:0.#}x") : "Zoom off");
        }
    }

    private bool NavigatorShown => _settings.ShowNavigator && _view.IsZoomed && HasVideo;

    /// <summary>Puts the navigator's frame where the presenter draws the small picture, and the box where the view is.</summary>
    private void ShowNavigator()
    {
        if (!NavigatorShown || PictureShape() is not { } shape)
        {
            Navigator.Visibility = Visibility.Collapsed;
            OsdBox.Margin = new Thickness(16);
            return;
        }

        var (x, y, width, height) = PictureView.Navigator(Stage.ActualWidth, Stage.ActualHeight, shape.Across, shape.Down);
        Navigator.Margin = new Thickness(x, y, 0, 0);
        Navigator.Width = width;
        Navigator.Height = height;
        var (left, top, viewWidth, viewHeight) = _view.Visible;
        Canvas.SetLeft(NavigatorView, left * width);
        Canvas.SetTop(NavigatorView, top * height);
        NavigatorView.Width = Math.Max(2, viewWidth * width);
        NavigatorView.Height = Math.Max(2, viewHeight * height);
        Navigator.Visibility = Visibility.Visible;
        OsdBox.Margin = new Thickness(16, y + height + 8, 16, 16);
    }

    /// <summary>Where on the picture a point of the stage falls, from 0 to 1 each way; null outside the picture.</summary>
    private (double X, double Y)? OnPicture(Point point)
    {
        if (PictureShape() is not { } shape)
        {
            return null;
        }

        var picture = SubtitleLook.Picture(Stage.ActualWidth, Stage.ActualHeight, shape.Across, shape.Down);
        var x = (point.X - picture.X) / picture.Width;
        var y = (point.Y - picture.Y) / picture.Height;
        return x is >= 0 and <= 1 && y is >= 0 and <= 1 ? (x, y) : null;
    }

    /// <summary>Alt with the wheel zooms about the point under the pointer.</summary>
    private bool ZoomWithWheel(PointerRoutedEventArgs e, int delta)
    {
        if (!IsDown(Windows.System.VirtualKey.Menu) || OnPicture(e.GetCurrentPoint(Stage).Position) is not { } at)
        {
            return false;
        }

        SetView(_view.ZoomAt(delta > 0 ? WheelZoom : 1 / WheelZoom, at.X, at.Y), announce: true);
        return true;
    }

    private void OnStagePressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Stage);
        var button = point.Properties.IsMiddleButtonPressed ? CommandCatalog.PlayPause
            : point.Properties.IsXButton1Pressed ? CommandCatalog.Previous
            : point.Properties.IsXButton2Pressed ? CommandCatalog.Next
            : null;
        if (button is not null)
        {
            Run(button);
            e.Handled = true;
            return;
        }

        if (_view.IsZoomed && point.Properties.IsLeftButtonPressed && OnPicture(point.Position) is not null)
        {
            _dragFrom = point.Position;
            Stage.CapturePointer(e.Pointer);
        }
    }

    /// <summary>A drag moves the picture with the pointer.</summary>
    private void OnStageDragged(object sender, PointerRoutedEventArgs e)
    {
        if (_dragFrom is not { } from || PictureShape() is not { } shape)
        {
            return;
        }

        var point = e.GetCurrentPoint(Stage).Position;
        var picture = SubtitleLook.Picture(Stage.ActualWidth, Stage.ActualHeight, shape.Across, shape.Down);
        SetView(_view.PanBy((point.X - from.X) / picture.Width, (point.Y - from.Y) / picture.Height));
        _dragFrom = point;
        e.Handled = true;
    }

    private void OnStageReleased(object sender, PointerRoutedEventArgs e)
    {
        _dragFrom = null;
        Stage.ReleasePointerCapture(e.Pointer);
    }

    /// <summary>A click or drag in the navigator centres the view there.</summary>
    private void OnNavigatorPointer(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Navigator);
        if (!point.Properties.IsLeftButtonPressed || Navigator.ActualWidth <= 0)
        {
            return;
        }

        Navigator.CapturePointer(e.Pointer);
        SetView(_view.CenteredOn(point.Position.X / Navigator.ActualWidth, point.Position.Y / Navigator.ActualHeight));
        e.Handled = true;
    }
}
