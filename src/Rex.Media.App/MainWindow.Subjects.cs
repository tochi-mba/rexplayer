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
    private PictureView _viewBeforeSubjectFollow;
    private bool _subjectAutoFollowing;
    private long _subjectRevision;

    private void WireSubjectTools()
    {
        Stage.PointerPressed += OnSubjectPointerPressed;
        Stage.PointerMoved += OnSubjectPointerMoved;
        Stage.PointerReleased += OnSubjectPointerReleased;
        Stage.PointerCaptureLost += (_, _) => _subjectAnchor = null;
        Stage.SizeChanged += (_, _) =>
        {
            SizeSubjectToolbar();
            PollSubjectOverlay();
        };
        SubjectToolbar.Loaded += (_, _) => SizeSubjectToolbar();
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

        if (_subjectOpen)
        {
            CloseSubjectTools();
        }

        _subjectOpen = true;
        _selectingSubject = true;
        _subjectErase = false;
        SubjectCanvas.Visibility = Visibility.Visible;
        SubjectToolbar.Visibility = Visibility.Visible;
        SizeSubjectToolbar();
        SubjectConfidenceText.Text = "Waiting for selection";
        SubjectSelectButton.Content = "Select again";
        SubjectEraseButton.IsEnabled = false;
        SubjectEraseButton.Content = "Preview removal";
        SubjectStatusText.Text = "Drag around a distinctive texture or clothing detail. If it disappears, auto-follow widens the view and keeps looking for the original pattern.";
        _subjectTimer?.Start();
        Stage.Focus(FocusState.Programmatic);
    }

    private void OnSubjectSelect(object sender, RoutedEventArgs e)
    {
        // The previous subject must stop tracking immediately, including when the preview
        // was enabled. Otherwise a drag to replace it can continue erasing the old target.
        OnSubjectReset(sender, e);
        SubjectStatusText.Text = "Drag across the picture to select the visible object again.";
    }

    private void OnSubjectReset(object sender, RoutedEventArgs e)
    {
        _subjectRevision++;
        _subjectAnchor = null;
        RestoreSubjectFollowView();
        _selectingSubject = true;
        _subjectErase = false;
        SubjectEraseButton.IsEnabled = false;
        SubjectEraseButton.Content = "Preview removal";
        SubjectStatusText.Text = "Selection reset. Drag to choose another object.";
        SubjectConfidenceText.Text = "Waiting for selection";
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
        _subjectRevision++;
        RestoreSubjectFollowView();
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

    private void SizeSubjectToolbar()
    {
        // WinUI's layout is constrained by the available stage, not by a hard-coded
        // desktop resolution. The inner ScrollViewer keeps every control reachable.
        SubjectToolbar.Width = Math.Max(140, Math.Min(336, Stage.ActualWidth - 24));
        SubjectToolbar.MaxHeight = Math.Max(48, Stage.ActualHeight - 24);
    }

    private void RestoreSubjectFollowView()
    {
        if (!_subjectAutoFollowing)
        {
            return;
        }

        _subjectAutoFollowing = false;
        SetView(_viewBeforeSubjectFollow, fromSubject: true);
    }

    private void OnSubjectFollowChanged(object sender, RoutedEventArgs e)
    {
        if (!_subjectOpen || _closed)
        {
            return;
        }

        if (!SubjectFollow.IsOn)
        {
            RestoreSubjectFollowView();
        }
        else
        {
            PollSubjectOverlay();
        }
    }

    private void OnSubjectShowBoxChanged(object sender, RoutedEventArgs e)
    {
        if (!_subjectOpen || _closed)
        {
            return;
        }

        if (!SubjectShowBox.IsOn)
        {
            SubjectOutline.Visibility = Visibility.Collapsed;
        }
        else
        {
            PollSubjectOverlay();
        }
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
        if (_subjectAnchor is not null)
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

        LockSubjectRegion(left, top, width, height);
        e.Handled = true;
    }

    /// <summary>
    /// Accessible selection fallback: after panning/zooming to an object, keyboard users can
    /// lock the central 20 percent of the visible picture without a mouse or touchscreen.
    /// </summary>
    private void OnSubjectCenter(object sender, RoutedEventArgs e)
    {
        if (!_subjectOpen || !HasVideo ||
            _player.Info?.FirstTrack(MediaKind.Video)?.Video is not { } video)
        {
            return;
        }

        var (source, _, _) = VideoGeometry.Shape(video.Width, video.Height,
            video.PixelAspect, _aspect.Ratio, _crop.Ratio);
        var visible = _view.Within(source);
        var width = (visible.Right - visible.Left) * 0.2f;
        var height = (visible.Bottom - visible.Top) * 0.2f;
        LockSubjectRegion(visible.Left + (visible.Right - visible.Left - width) / 2,
            visible.Top + (visible.Bottom - visible.Top - height) / 2, width, height);
    }

    private void LockSubjectRegion(float left, float top, float width, float height)
    {
        if (width < 0.01f || height < 0.01f)
        {
            SubjectStatusText.Text = "Zoom out or select a larger visible area before locking.";
            return;
        }

        var revision = ++_subjectRevision;
        var item = _player.Item;
        var seekPausedPicture = !_player.IsPlaying && _player.CanSeek;
        _selectingSubject = false;
        _subjectErase = false;
        SubjectEraseButton.IsEnabled = false;
        SubjectEraseButton.Content = "Preview removal";
        SubjectStatusText.Text = "Acquiring selected texture. If its identity is ambiguous, tracking stops.";
        SubjectConfidenceText.Text = "Acquiring target";
        var feather = (int)SubjectFeather.Value;
        var tolerance = (int)SubjectTolerance.Value;
        OnPresenterThread(p =>
        {
            p.SelectSubject(left, top, width, height);
            p.RefineSubject(feather, tolerance);
            // Order the paused seek after selection actually reaches the presenter.
            // Otherwise the one decoded picture can arrive before the selection exists.
            if (seekPausedPicture)
            {
                OnWindowThread(() =>
                {
                    if (_subjectOpen && revision == _subjectRevision
                        && ReferenceEquals(item, _player.Item) && !_player.IsPlaying && _player.CanSeek)
                    {
                        // An ended still picture is parked at EOF. Seeking there yields no
                        // decoded frame, so the new selection never receives a texture sample.
                        // Revisit the first frame instead, while preserving position for paused video.
                        var at = _player.Position >= _player.Duration ? TimeSpan.Zero : _player.Position;
                        _player.Seek(at);
                    }
                });
            }
        });
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
        var revision = _subjectRevision;
        OnPresenterThread(p =>
        {
            var snapshot = p.SubjectStatus();
            OnWindowThread(() =>
            {
                _subjectPollBusy = false;
                if (!_subjectOpen || _closed || _selectingSubject || revision != _subjectRevision)
                {
                    return;
                }

                SubjectStatusText.Text = snapshot.Status;
                SubjectConfidenceText.Text = snapshot.Tracking
                    ? $"Match confidence {snapshot.Confidence:P0}" + (SubjectShowBox.IsOn ? "" : " · Outline hidden")
                    : snapshot.Locked ? "Searching original pattern · Wide view" : "Waiting for selection";
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
                // Keep the original identity search running on the decoded picture.
                // A lost lock changes only framing: never clear the selection or its template.
                if (!snapshot.Tracking && snapshot.Locked && SubjectFollow.IsOn && _subjectAutoFollowing)
                {
                    var wider = _view.Reveal();
                    if (Math.Abs(wider.Zoom - _view.Zoom) > 0.0008 ||
                        Math.Abs(wider.CenterX - _view.CenterX) > 0.0008 ||
                        Math.Abs(wider.CenterY - _view.CenterY) > 0.0008)
                    {
                        SetView(wider, fromSubject: true);
                    }
                }

                if (snapshot.Tracking && SubjectFollow.IsOn)
                {
                    var sourceWidth = Math.Max(0.000001f, source.Right - source.Left);
                    var sourceHeight = Math.Max(0.000001f, source.Bottom - source.Top);
                    var x = (snapshot.Left + snapshot.Width / 2 - source.Left) / sourceWidth;
                    var y = (snapshot.Top + snapshot.Height / 2 - source.Top) / sourceHeight;
                    if (x is >= 0 and <= 1 && y is >= 0 and <= 1)
                    {
                        if (!_subjectAutoFollowing)
                        {
                            _viewBeforeSubjectFollow = _view;
                            _subjectAutoFollowing = true;
                        }

                        var next = _view.Follow(x, y, snapshot.Width / sourceWidth, snapshot.Height / sourceHeight);
                        if (Math.Abs(next.Zoom - _view.Zoom) > 0.002 ||
                            Math.Abs(next.CenterX - _view.CenterX) > 0.0008 ||
                            Math.Abs(next.CenterY - _view.CenterY) > 0.0008)
                        {
                            SetView(next, fromSubject: true);
                        }
                    }
                }

                var visible = _view.Within(source);
                var picture = Rex.Media.AppCore.Player.SubtitleLook.Picture(Stage.ActualWidth,
                    Stage.ActualHeight, shape.Across, shape.Down);
                var dx = Math.Max(0.000001f, visible.Right - visible.Left);
                var dy = Math.Max(0.000001f, visible.Bottom - visible.Top);
                var left = picture.X + picture.Width * ((snapshot.Left - visible.Left) / dx);
                var top = picture.Y + picture.Height * ((snapshot.Top - visible.Top) / dy);
                var right = left + picture.Width * snapshot.Width / dx;
                var bottom = top + picture.Height * snapshot.Height / dy;
                var shownLeft = Math.Clamp(left, 0, Stage.ActualWidth);
                var shownTop = Math.Clamp(top, 0, Stage.ActualHeight);
                var shownRight = Math.Clamp(right, 0, Stage.ActualWidth);
                var shownBottom = Math.Clamp(bottom, 0, Stage.ActualHeight);
                Canvas.SetLeft(SubjectOutline, shownLeft);
                Canvas.SetTop(SubjectOutline, shownTop);
                SubjectOutline.Width = Math.Max(1, shownRight - shownLeft);
                SubjectOutline.Height = Math.Max(1, shownBottom - shownTop);
                SubjectOutline.Visibility = snapshot.Tracking && SubjectShowBox.IsOn &&
                    shownRight > shownLeft && shownBottom > shownTop
                    ? Visibility.Visible : Visibility.Collapsed;
            });
        });
    }
}
