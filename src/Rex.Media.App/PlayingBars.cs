using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Rex.Media.App;

/// <summary>
/// The small three-bar equaliser that marks the playing song in a music list. The bars rise and fall
/// by composition animations the GPU runs on its own (no timers, no per-frame code), only while the
/// mark is shown, and hold still while playback is paused.
/// </summary>
public sealed partial class PlayingBars : Grid
{
    private static readonly List<WeakReference<PlayingBars>> Shown = [];
    private static bool _paused;
    private static bool _still;
    private readonly Rectangle[] _bars;
    private bool _running;

    public PlayingBars()
    {
        Width = 14;
        Height = 14;
        ColumnSpacing = 2;
        IsHitTestVisible = false;
        _bars = new Rectangle[3];
        for (var index = 0; index < _bars.Length; index++)
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var bar = new Rectangle { RadiusX = 1, RadiusY = 1, VerticalAlignment = VerticalAlignment.Stretch };
            bar.SetBinding(Shape.FillProperty, new Microsoft.UI.Xaml.Data.Binding { Source = this, Path = new PropertyPath(nameof(Foreground)) });
            SetColumn(bar, index);
            Children.Add(bar);
            _bars[index] = bar;
        }

        Loaded += (_, _) => Update();
        Unloaded += (_, _) => Stop();
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => Update());
    }

    /// <summary>The bars' colour.</summary>
    public Brush? Foreground
    {
        get => (Brush?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(PlayingBars), new PropertyMetadata(null));

    /// <summary>Holds every shown equaliser still while playback is paused, and lets them move again when it plays.</summary>
    public static void SetPaused(bool paused)
    {
        if (paused == _paused)
        {
            return;
        }

        _paused = paused;
        HoldAll();
    }

    /// <summary>
    /// With the window's animations off (the performance budget, or Windows' own animation setting),
    /// every equaliser stands still: it keeps marking the playing line without per-frame GPU work.
    /// </summary>
    public static void SetAnimationsEnabled(bool on)
    {
        if (on != _still)
        {
            return;
        }

        _still = !on;
        HoldAll();
    }

    private static void HoldAll()
    {
        lock (Shown)
        {
            Shown.RemoveAll(weak => !weak.TryGetTarget(out _));
            foreach (var weak in Shown)
            {
                if (weak.TryGetTarget(out var bars))
                {
                    bars.Hold(_paused || _still);
                }
            }
        }
    }

    private void Update()
    {
        if (Visibility == Visibility.Visible && IsLoaded)
        {
            Start();
        }
        else
        {
            Stop();
        }
    }

    private void Start()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        lock (Shown)
        {
            Shown.Add(new WeakReference<PlayingBars>(this));
        }

        // Each bar keeps its own pace, so together they read as music rather than a metronome.
        float[][] steps = [[0.35f, 1f, 0.5f, 0.8f, 0.35f], [1f, 0.4f, 0.9f, 0.3f, 1f], [0.6f, 0.85f, 0.3f, 1f, 0.6f]];
        int[] periods = [900, 700, 1100];
        for (var index = 0; index < _bars.Length; index++)
        {
            var visual = ElementCompositionPreview.GetElementVisual(_bars[index]);
            visual.CenterPoint = new Vector3(0, (float)Height, 0);
            var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
            for (var step = 0; step < steps[index].Length; step++)
            {
                animation.InsertKeyFrame(step / (float)(steps[index].Length - 1), steps[index][step]);
            }

            animation.Duration = TimeSpan.FromMilliseconds(periods[index]);
            animation.IterationBehavior = AnimationIterationBehavior.Forever;
            visual.StartAnimation("Scale.Y", animation);
            if (_paused || _still)
            {
                visual.TryGetAnimationController("Scale.Y")?.Pause();
            }
        }
    }

    private void Hold(bool paused)
    {
        foreach (var bar in _bars)
        {
            if (ElementCompositionPreview.GetElementVisual(bar).TryGetAnimationController("Scale.Y") is { } controller)
            {
                if (paused)
                {
                    controller.Pause();
                }
                else
                {
                    controller.Resume();
                }
            }
        }
    }

    private void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        lock (Shown)
        {
            Shown.RemoveAll(weak => !weak.TryGetTarget(out var bars) || bars == this);
        }

        foreach (var bar in _bars)
        {
            ElementCompositionPreview.GetElementVisual(bar).StopAnimation("Scale.Y");
        }
    }
}
