using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Rex.Media.Primitives;
using Rex.Media.Video;
using Windows.Foundation;

namespace Rex.Media.App;

/// <summary>
/// Subject Lock and Ghost Peel are independent of colour looks and visual shaders. The player
/// never uploads selected frames, changes source files, or claims inferred pixels were observed.
/// </summary>
public sealed partial class MainWindow
{
    private DispatcherQueueTimer? _subjectTimer;
    private bool _subjectOpen;
    private bool _selectingSubject;
    private bool _subjectErase;
    private bool _subjectPollBusy;
    private Point? _subjectAnchor;

    private void WireSubjectTools()
    {
        Stage.PointerPressed += OnSubjectPointerPressed;
        Stage.PointerMoved += OnSubjectPointerMoved;
        Stage.PointerReleased += OnSubjectPointerReleased;
        Stage.PointerCaptureLost += (_, _) => _subjectAnchor = null;
        Stage.SizeChanged += (_, _) => PollSubjectOverlay();
        _subjectTimer = DispatcherQueue.CreateTimer();
        _subjectTimer.Interval = TimeSpan.FromMilliseconds(200);
        _subjectTimer.IsRepeating = true;
        _subjectTimer.Tick += (_, _) => PollSubjectOverlay();
    }

    /// <summary>Tools in both the video menu and the picture's context menu.</summary>
    private void AddSubjectTools(IList<MenuFlyoutItemBase> items, string prefix)
    {
        items.Add(new MenuFlyoutSeparator());
        var lockItem = new MenuFlyoutItem { Text = "Subject Lock — select an object..." };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(lockItem, prefix + "SubjectLock");
        lockItem.Click += (_, _) => OpenSubjectTools();
        items.Add(lockItem);
    }

    private void OpenSubjectTools()
    {
        if (!HasVideo)
        {
            Say("Open a video to select something in its picture.");
            return;
        }

        _subjectOpen = true;
        _selectingSubject = true;
        _subjectErase = false;
        SubjectCanvas.Visibility = Visibility.Visible;
        SubjectToolbar.Visibility = Visibility.Visible;
        SubjectSelectButton.Content = "Select again";
        SubjectEraseButton.IsEnabled = false;
        SubjectEraseButton.Content = "Preview removal";
        SubjectStatusText.Text = "Drag a rectangle around the object or body part. Tracking follows visible texture; use Select again if it loses the target.";
        _subjectTimer?.Start();
        Stage.Focus(FocusState.Programmatic);
    }

    private void OnSubjectSelect(object sender, RoutedEventArgs e)
    {
        _selectingSubject = true;
        _subjectErase = false;
        SubjectEraseButton.Content = "Preview removal";
        SubjectStatusText.Text = "Drag across the picture to select the visible object again.";
    }

    private void OnSubjectReset(object sender, RoutedEventArgs e)
    {
        _selectingSubject = true;
        _subjectErase = false;
        SubjectEraseButton.IsEnabled = false;
        SubjectEraseButton.Content = "Preview removal";
        SubjectStatusText.Text = "Selection reset. Drag to choose another object.";
        SubjectOutline.Visibility = Visibility.Collapsed;
        var redraw = !_player.IsPlaying;
        OnPresenterThread(p =>
        {
            p.ClearSubject();
            if (redraw)
            {
                p.Redraw();
            }
        });
    }

    private void OnSubjectClose(object sender, RoutedEventArgs e) => CloseSubjectTools();

    private void CloseSubjectTools()
    {
        _subjectOpen = false;
        _selectingSubject = false;
        _subjectAnchor = null;
        _subjectErase = false;
        _subjectTimer?.Stop();
        SubjectCanvas.Visibility = Visibility.Collapsed;
        SubjectToolbar.Visibility = Visibility.Collapsed;
        SubjectOutline.Visibility = Visibility.Collapsed;
        var redraw = !_player.IsPlaying;
        OnPresenterThread(p =>
        {
            p.ClearSubject();
            if (redraw)
            {
                p.Redraw();
            }
        });
    }

    private void OnSubjectMaskChanged(object sender,
        Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_subjectOpen || _closed || _presenter is null)
        {
            return;
        }

        var feather = (int)SubjectFeather.Value;
        var tolerance = (int)SubjectTolerance.Value;
        var redraw = _subjectErase && !_player.IsPlaying;
        OnPresenterThread(p =>
        {
            p.RefineSubject(feather, tolerance);
            if (redraw)
            {
                p.Redraw();
            }
        });
    }

    private void OnSubjectErase(object sender, RoutedEventArgs e)
    {
        if (!_subjectOpen || _selectingSubject)
        {
            return;
        }

        _subjectErase = !_subjectErase;
        SubjectEraseButton.Content = _subjectErase ? "Show original" : "Preview removal";
        var erase = _subjectErase;
        var redraw = !_player.IsPlaying;
        OnPresenterThread(p =>
        {
            p.SetSubjectErasure(erase);
            if (redraw)
            {
                p.Redraw();
            }
        });
    }

    private void OnSubjectPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_subjectOpen || !_selectingSubject)
        {
            return;
        }

        var point = e.GetCurrentPoint(Stage);
        if (!point.Properties.IsLeftButtonPressed || OnPicture(point.Position) is null)
        {
            return;
        }

        _subjectAnchor = point.Position;
        Stage.CapturePointer(e.Pointer);
        SubjectOutline.Visibility = Visibility.Visible;
        ShowSubjectDrag(point.Position);
        e.Handled = true;
    }

    private void OnSubjectPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_subjectAnchor is { } anchor)
        {
            ShowSubjectDrag(e.GetCurrentPoint(Stage).Position);
            e.Handled = true;
        }
    }

    private void OnSubjectPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_subjectAnchor is not { } first)
        {
            return;
        }

        _subjectAnchor = null;
        Stage.ReleasePointerCapture(e.Pointer);
        var last = e.GetCurrentPoint(Stage).Position;
        var start = OnPicture(first);
        var end = OnPicture(last);
        if (start is null || end is null || PictureShape() is not { } || _player.Info?.FirstTrack(MediaKind.Video)?.Video is not { } video)
        {
            SubjectStatusText.Text = "Selection was outside the picture. Try again.";
            e.Handled = true;
            return;
        }

        // Map from picture coordinates *through* zoom and source crop to decoded video pixels.
        var (source, _, _) = VideoGeometry.Shape(video.Width, video.Height, video.PixelAspect, _aspect.Ratio, _crop.Ratio);
        var visible = _view.Within(source);
        var sx = visible.Left + (float)start.Value.X * (visible.Right - visible.Left);
        var sy = visible.Top + (float)start.Value.Y * (visible.Bottom - visible.Top);
        var ex = visible.Left + (float)end.Value.X * (visible.Right - visible.Left);
        var ey = visible.Top + (float)end.Value.Y * (visible.Bottom - visible.Top);
        var left = Math.Min(sx, ex);
        var top = Math.Min(sy, ey);
        var width = Math.Abs(ex - sx);
        var height = Math.Abs(ey - sy);
        if (width < 0.01f || height < 0.01f)
        {
            SubjectStatusText.Text = "Select a visible area at least one percent wide and tall.";
            e.Handled = true;
            return;
        }

        _selectingSubject = false;
        _subjectErase = false;
        SubjectEraseButton.Content = "Preview removal";
        SubjectStatusText.Text = "Acquiring selected texture. If the pattern becomes ambiguous, tracking will stop.";
        var feather = (int)SubjectFeather.Value;
        var tolerance = (int)SubjectTolerance.Value;
        OnPresenterThread(p =>
        {
            p.SelectSubject(left, top, width, height);
            p.RefineSubject(feather, tolerance);
        });
        // A paused picture has already been presented. Ask the decoder for this exact image
        // again so the tracker acquires the user-selected subject without auto-playing.
        if (!_player.IsPlaying && _player.CanSeek)
        {
            _player.Seek(_player.Position);
        }

        e.Handled = true;
    }

    private void ShowSubjectDrag(Point current)
    {
        if (_subjectAnchor is not { } start)
        {
            return;
        }

        Canvas.SetLeft(SubjectOutline, Math.Max(0, Math.Min(start.X, current.X)));
        Canvas.SetTop(SubjectOutline, Math.Max(0, Math.Min(start.Y, current.Y)));
        SubjectOutline.Width = Math.Max(1, Math.Abs(start.X - current.X));
        SubjectOutline.Height = Math.Max(1, Math.Abs(start.Y - current.Y));
    }

    private void PollSubjectOverlay()
    {
        if (!_subjectOpen || _subjectPollBusy || _presenter is null || _closed)
        {
            return;
        }

        _subjectPollBusy = true;
        OnPresenterThread(p =>
        {
            var snapshot = p.SubjectStatus();
            OnWindowThread(() =>
            {
                _subjectPollBusy = false;
                if (!_subjectOpen || _closed || _selectingSubject)
                {
                    return;
                }

                SubjectStatusText.Text = snapshot.Status + (snapshot.Tracking
                    ? $" Match {snapshot.Confidence:P0}." : "");
                SubjectEraseButton.IsEnabled = snapshot.Tracking;
                if (!snapshot.Tracking)
                {
                    _subjectErase = false;
                    SubjectEraseButton.Content = "Preview removal";
                }

                if (PictureShape() is not { } shape || _player.Info?.FirstTrack(MediaKind.Video)?.Video is not { } video)
                {
                    SubjectOutline.Visibility = Visibility.Collapsed;
                    return;
                }

                var (source, _, _) = VideoGeometry.Shape(video.Width, video.Height, video.PixelAspect, _aspect.Ratio, _crop.Ratio);
                var visible = _view.Within(source);
                var picture = Rex.Media.AppCore.Player.SubtitleLook.Picture(Stage.ActualWidth,
                    Stage.ActualHeight, shape.Across, shape.Down);
                var dx = Math.Max(0.000001f, visible.Right - visible.Left);
                var dy = Math.Max(0.000001f, visible.Bottom - visible.Top);
                Canvas.SetLeft(SubjectOutline, picture.X + picture.Width * ((snapshot.Left - visible.Left) / dx));
                Canvas.SetTop(SubjectOutline, picture.Y + picture.Height * ((snapshot.Top - visible.Top) / dy));
                SubjectOutline.Width = Math.Max(1, picture.Width * snapshot.Width / dx);
                SubjectOutline.Height = Math.Max(1, picture.Height * snapshot.Height / dy);
                SubjectOutline.Visibility = snapshot.Locked ? Visibility.Visible : Visibility.Collapsed;
            });
        });
    }
}
