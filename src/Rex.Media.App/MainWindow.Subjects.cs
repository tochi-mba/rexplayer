using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Rex.Media.AppCore.Player;
using Rex.Media.Settings;
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
    // A box chosen from a click is outlined for a moment even when Show box is off, so the user
    // sees what the click selected.
    private bool _subjectShowChosen;
    private long _subjectChosenUntil;

    private bool IsInSubjectPanel(DependencyObject? element) => IsWithin(element, SubjectToolbar);

    private static bool IsWithin(DependencyObject? element, DependencyObject ancestor)
    {
        for (var current = element; current is not null; current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

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
        var lockItem = new MenuFlyoutItem { Text = "Subject Lock: select an object..." };
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

        _selectingSubject = true;
        _subjectErase = false;
        SubjectClickToLock.IsOn = _settings.SubjectSelection == SubjectSelectionMode.Click;
        SubjectFollowingSection.IsExpanded = _settings.SubjectFollowingOpen;
        SubjectRemovalSection.IsExpanded = _settings.SubjectRemovalOpen;
        SubjectHelpSection.IsExpanded = _settings.SubjectHelpOpen;
        _subjectOpen = true;
        UpdateSubjectPanelHelp();
        SubjectFeatherText.Text = SubjectPanel.FeatherText((int)SubjectFeather.Value);
        SubjectToleranceText.Text = SubjectPanel.ToleranceText((int)SubjectTolerance.Value);
        SubjectCanvas.Visibility = Visibility.Visible;
        SubjectToolbar.Visibility = Visibility.Visible;
        SizeSubjectToolbar();
        SubjectConfidenceText.Text = SubjectPanel.Pill(SubjectState.Waiting, 0);
        SubjectSelectButton.Content = SubjectPanel.SelectText(false);
        SubjectEraseButton.IsEnabled = false;
        SubjectEraseButton.Content = SubjectPanel.EraseText(false);
        SubjectStatusText.Text = SubjectPanel.Instruction(_settings.SubjectSelection, SubjectFollow.IsOn);
        _subjectTimer?.Start();
        Stage.Focus(FocusState.Programmatic);
    }

    private void OnSubjectSelect(object sender, RoutedEventArgs e)
    {
        // The previous subject must stop tracking immediately, including when the preview
        // was enabled. Otherwise a drag to replace it can continue erasing the old target.
        OnSubjectReset(sender, e);
        SubjectStatusText.Text = SubjectPanel.Instruction(_settings.SubjectSelection, SubjectFollow.IsOn);
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
        SubjectStatusText.Text = SubjectPanel.Instruction(_settings.SubjectSelection, SubjectFollow.IsOn);
        SubjectConfidenceText.Text = SubjectPanel.Pill(SubjectState.Waiting, 0);
        SubjectSelectButton.Content = SubjectPanel.SelectText(false);
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
        SubjectToolbar.Width = SubjectPanel.Width(Stage.ActualWidth);
        SubjectToolbar.MaxHeight = SubjectPanel.MaxHeight(Stage.ActualHeight);
        SubjectToolbar.HorizontalAlignment = _settings.SubjectPanelSide == PanelSide.Left
            ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        SubjectMoveButton.Content = SubjectPanel.MoveText(_settings.SubjectPanelSide);
    }

    private void OnSubjectMove(object sender, RoutedEventArgs e)
    {
        _settings = _settings with { SubjectPanelSide = SubjectPanel.Opposite(_settings.SubjectPanelSide) };
        SaveSettings();
        SizeSubjectToolbar();
    }

    private void OnSubjectModeChanged(object sender, RoutedEventArgs e)
    {
        if (!_subjectOpen || _closed)
        {
            return;
        }

        _settings = _settings with { SubjectSelection = SubjectClickToLock.IsOn ? SubjectSelectionMode.Click : SubjectSelectionMode.Draw };
        SaveSettings();
        UpdateSubjectPanelHelp();
        if (_selectingSubject)
        {
            SubjectStatusText.Text = SubjectPanel.Instruction(_settings.SubjectSelection, SubjectFollow.IsOn);
        }
    }

    private void OnSubjectExpanding(Expander sender, ExpanderExpandingEventArgs e) => SetSubjectSectionOpen(sender, true);

    private void OnSubjectCollapsed(Expander sender, ExpanderCollapsedEventArgs e) => SetSubjectSectionOpen(sender, false);

    private void SetSubjectSectionOpen(Expander sender, bool open)
    {
        if (!_subjectOpen || _closed)
        {
            return;
        }

        _settings = _settings with
        {
            SubjectFollowingOpen = sender == SubjectFollowingSection ? open : _settings.SubjectFollowingOpen,
            SubjectRemovalOpen = sender == SubjectRemovalSection ? open : _settings.SubjectRemovalOpen,
            SubjectHelpOpen = sender == SubjectHelpSection ? open : _settings.SubjectHelpOpen,
        };
        SaveSettings();
    }

    private void UpdateSubjectPanelHelp() =>
        SubjectHelpText.Text = string.Join("\n\n", SubjectPanel.Help(_settings.SubjectSelection));

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

        if (_selectingSubject)
        {
            SubjectStatusText.Text = SubjectPanel.Instruction(_settings.SubjectSelection, SubjectFollow.IsOn);
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
        if (!_subjectOpen || _closed)
        {
            return;
        }

        var feather = (int)SubjectFeather.Value;
        var tolerance = (int)SubjectTolerance.Value;
        SubjectFeatherText.Text = SubjectPanel.FeatherText(feather);
        SubjectToleranceText.Text = SubjectPanel.ToleranceText(tolerance);
        if (_presenter is null)
        {
            return;
        }

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
        if (!_subjectOpen || !_selectingSubject || IsInSubjectPanel(e.OriginalSource as DependencyObject))
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
        if (start is null || end is null || PictureShape() is null || _player.Info?.FirstTrack(MediaKind.Video)?.Video is not { } video)
        {
            SubjectStatusText.Text = "Selection was outside the picture. Try again.";
            e.Handled = true;
            return;
        }

        // Map from picture coordinates *through* zoom and source crop to decoded video pixels.
        var (source, _, _) = VideoGeometry.Shape(video.Width, video.Height, video.PixelAspect, _aspect.Ratio, _crop.Ratio);
        var visible = _view.Within(source);
        if (SubjectPanel.IsClick(_settings.SubjectSelection, last.X - first.X, last.Y - first.Y))
        {
            // The tracker chooses the box around the click that it can follow best, or refuses.
            var x = visible.Left + (float)start.Value.X * (visible.Right - visible.Left);
            var y = visible.Top + (float)start.Value.Y * (visible.Bottom - visible.Top);
            _subjectShowChosen = true;
            BeginSubjectLock(p => p.SelectSubjectAround(x, y));
            SubjectOutline.Visibility = Visibility.Collapsed;
            e.Handled = true;
            return;
        }

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

        _subjectShowChosen = false;
        BeginSubjectLock(p => p.SelectSubject(left, top, width, height));
    }

    private void BeginSubjectLock(Action<Rex.Media.Video.D3D11.D3D11Presenter> select)
    {
        var revision = ++_subjectRevision;
        var item = _player.Item;
        // A still image can remain "playing" without another decoded picture arriving.
        // Selecting a subject must request fresh image data even during its display interval.
        var stillPicture = item is not null && Rex.Media.AppCore.Player.MediaFiles.IsPicture(item.Location);
        var refreshSelectionFrame = _player.CanSeek && (stillPicture || !_player.IsPlaying);
        _selectingSubject = false;
        _subjectErase = false;
        SubjectEraseButton.IsEnabled = false;
        SubjectEraseButton.Content = "Preview removal";
        SubjectStatusText.Text = "Acquiring selected texture. If its identity is ambiguous, tracking stops.";
        SubjectConfidenceText.Text = SubjectPanel.Pill(SubjectState.Acquiring, 0);
        SubjectSelectButton.Content = SubjectPanel.SelectText(true);
        var feather = (int)SubjectFeather.Value;
        var tolerance = (int)SubjectTolerance.Value;
        OnPresenterThread(p =>
        {
            select(p);
            p.RefineSubject(feather, tolerance);
            // The presenter must know the selection before the seek can decode its
            // only picture. The same applies to a playing slideshow image: no later
            // frame would otherwise arrive to initialize the selected texture.
            if (refreshSelectionFrame)
            {
                OnWindowThread(() =>
                {
                    if (_subjectOpen && revision == _subjectRevision
                        && ReferenceEquals(item, _player.Item) && _player.CanSeek
                        && (stillPicture || !_player.IsPlaying))
                    {
                        // Images are static: rescan their first frame, never EOF.
                        // For paused video retain the chosen position when possible.
                        var at = stillPicture || _player.Position >= _player.Duration
                            ? TimeSpan.Zero : _player.Position;
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
                var state = SubjectPanel.State(snapshot.Locked, snapshot.Tracking, snapshot.Status);
                SubjectConfidenceText.Text = SubjectPanel.Pill(state, snapshot.Confidence);
                var tint = SubjectPanel.PillColour(state);
                SubjectConfidenceText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(
                    (byte)(tint >> 24), (byte)(tint >> 16), (byte)(tint >> 8), (byte)tint));
                SubjectSelectButton.Content = SubjectPanel.SelectText(snapshot.Locked);
                SubjectEraseButton.IsEnabled = snapshot.Tracking;
                _subjectErase = snapshot.Erase;
                SubjectEraseButton.Content = SubjectPanel.EraseText(snapshot.Erase);
                if (!snapshot.Locked)
                {
                    // A refused click, or a lock the tracker dropped, leaves the picture ready
                    // for the next selection; the status above says why.
                    _selectingSubject = true;
                    _subjectShowChosen = false;
                }
                else if (_subjectShowChosen && snapshot.Tracking)
                {
                    _subjectShowChosen = false;
                    _subjectChosenUntil = Environment.TickCount64 + 2000;
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
                SubjectOutline.Visibility = snapshot.Tracking
                    && (SubjectShowBox.IsOn || Environment.TickCount64 < _subjectChosenUntil) &&
                    shownRight > shownLeft && shownBottom > shownTop
                    ? Visibility.Visible : Visibility.Collapsed;
            });
        });
    }
}
