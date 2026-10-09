using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Player;
using Rex.Media.AppCore.Visuals;
using Rex.Media.Audio;
using Rex.Media.Engine;
using Rex.Media.Settings;
using Windows.Foundation;

namespace Rex.Media.App;

/// <summary>
/// Visualisations of the sound while something without pictures plays (AU-18): spectrum bars, an
/// oscilloscope, level meters, a spectrogram, a vinyl record whose groove is the sound, a halo,
/// a mirrored wave, an aurora, embers, ripples on the beat, a colour strobe, and the listener's
/// own silhouette from the camera. Each has its own settings (<see cref="VisualizerOptions"/>):
/// colours, sensitivity and its particular knobs. The analysis is in Rex.Media.Audio; this draws
/// it about thirty times a second, and only while it is on screen.
/// </summary>
public sealed partial class MainWindow
{
    private const int SpectrogramColumns = 320;
    private const int SpectrogramRows = 128;
    private const int RippleCount = 12;

    private readonly float[] _soundLeft = new float[SpectrumAnalyzer.Size];
    private readonly float[] _soundRight = new float[SpectrumAnalyzer.Size];
    private readonly float[] _soundMono = new float[SpectrumAnalyzer.Size];
    private readonly SpectrumAnalyzer _spectrogramBands = new(SpectrogramRows);
    private readonly LevelMeter _meterLeft = new();
    private readonly LevelMeter _meterRight = new();
    private readonly byte[] _spectrogramPixels = new byte[SpectrogramColumns * SpectrogramRows * 4];
    private readonly List<Rectangle> _barShapes = [];
    private readonly List<Rectangle> _peakShapes = [];
    private readonly Rectangle[] _meterShapes = new Rectangle[6];
    private readonly List<Line> _rays = [];
    private readonly List<Ellipse> _sparks = [];
    private readonly List<Ellipse> _rings = [];
    private SpectrumAnalyzer _bands = new(48);
    private Polyline? _wave;
    private Ellipse? _haloRing;
    private RotateTransform? _labelSpin;
    private Line? _arm;
    private Ellipse? _stylus;
    private Polygon? _aurora;
    private Rectangle? _strobeFill;
    private SolidColorBrush? _strobeBrush;
    private TextBlock? _visualNote;
    private WriteableBitmap? _visualBitmap;
    private byte[] _visualPixels = [];
    private byte[] _cameraMask = [];
    private CameraSilhouette? _camera;
    private VisualStage? _visualStage;
    private VisualPicture? _visualCover;
    private readonly byte[] _cameraPicture = new byte[CameraSilhouette.Width * CameraSilhouette.Height * 4];
    private double[] _sparkX = [], _sparkY = [], _sparkVx = [], _sparkVy = [], _sparkLife = [];
    private double[] _ringAge = [];
    private float _beatAverage;
    private double _beatRest;
    private double _flash;
    private VisualizerChoice? _builtFor;
    private TimeSpan _lastDrawn;
    private bool _visualizing;

    private void WireVisualizer()
    {
        Visualizer.SizeChanged += (_, _) => BuildVisualizer();
        Visualizer.IsHitTestVisible = true;
        Visualizer.ContextFlyout = VisualizerMenu();
    }

    /// <summary>The visualisation's right-click menu: every visualisation, and the settings of this one.</summary>
    private MenuFlyout VisualizerMenu()
    {
        var menu = new MenuFlyout();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            foreach (var choice in Enum.GetValues<VisualizerChoice>().Where(choice => choice != VisualizerChoice.Off))
            {
                var item = new ToggleMenuFlyoutItem { Text = Visualizers.Name(choice), IsChecked = choice == _settings.Visualizer };
                item.Click += (_, _) => _ = ChooseVisualizerAsync(choice);
                menu.Items.Add(item);
            }

            menu.Items.Add(new MenuFlyoutSeparator());
            if (_player.Lyrics is not null)
            {
                var lyrics = new ToggleMenuFlyoutItem { Text = "Show the lyrics", IsChecked = _settings.ShowLyrics };
                lyrics.Click += (_, _) => RunVisualizerCommand(CommandCatalog.ToggleLyrics);
                menu.Items.Add(lyrics);
            }

            menu.Items.Add(MenuItem("Settings of this visualisation...", "VisualizerMenuSettings", () => _ = ShowVisualizerSettingsAsync()));
            menu.Items.Add(MenuItem("No visualisation", null, () => _ = ChooseVisualizerAsync(VisualizerChoice.Off)));
        };
        return menu;
    }

    /// <summary>
    /// Shows the chosen visualisation while sound without pictures plays, and stops drawing
    /// otherwise. Lyrics, when the song has them and they are wanted, show over it.
    /// </summary>
    private void ApplyVisualizer()
    {
        var music = _player.Item is not null && _player.Info is not null && !HasVideo && _player.State is not (SessionState.Idle or SessionState.Faulted);
        var show = music && _settings.Visualizer != VisualizerChoice.Off;
        Visualizer.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        var below = show || (music && _player.Lyrics is not null && _settings.ShowLyrics);
        Idle.VerticalAlignment = below ? VerticalAlignment.Top : VerticalAlignment.Center;
        Idle.Margin = below ? new Thickness(24, 24, 24, 0) : new Thickness(24);
        if (show && _builtFor != _settings.Visualizer)
        {
            BuildVisualizer();
        }

        if (show != _visualizing)
        {
            _visualizing = show;
            if (show)
            {
                CompositionTarget.Rendering += OnRendering;
            }
            else
            {
                StopVisualizing();
            }
        }
    }

    /// <summary>
    /// Stops drawing and lets the camera go. The drawing hangs off the whole app's rendering, not
    /// this window, so it must be taken off before the window closes.
    /// </summary>
    private void StopVisualizing()
    {
        CompositionTarget.Rendering -= OnRendering;
        _visualizing = false;
        _bands.Reset();
        _spectrogramBands.Reset();
        _meterLeft.Reset();
        _meterRight.Reset();
        StopCamera();
    }

    private bool RunVisualizerCommand(string command)
    {
        switch (command)
        {
            case CommandCatalog.CycleVisualizer:
                _ = ChooseVisualizerAsync(Visualizers.Next(_settings.Visualizer));
                return true;
            case CommandCatalog.VisualizerSettings:
                _ = ShowVisualizerSettingsAsync();
                return true;
            case CommandCatalog.ToggleLyrics:
                _settings = _settings with { ShowLyrics = !_settings.ShowLyrics };
                Say(_settings.ShowLyrics ? "Lyrics on" : "Lyrics off");
                ShowPresentation();
                ApplyVisualizer();
                RememberLater();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Changes the visualisation; the camera's asks first, the first time.</summary>
    private async Task ChooseVisualizerAsync(VisualizerChoice choice)
    {
        if ((choice is VisualizerChoice.Silhouette or VisualizerChoice.BeatEdit) && !_settings.CameraAllowed)
        {
            var note = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 420,
                Text = "This visualisation uses your camera to draw you, in light that moves with the music. Nothing is recorded, kept or sent anywhere, and the camera turns off when another visualisation is chosen or the music stops.",
            };
            if (!await Ask("Use the camera?", note, "Use the camera", "Not now"))
            {
                choice = VisualizerChoice.Off;
            }
            else
            {
                _settings = _settings with { CameraAllowed = true };
            }
        }

        _settings = _settings with { Visualizer = choice };
        _player.Settings = _settings;
        ApplyVisualizer();
        BuildVisualizer();
        Say(Visualizers.Name(choice));
        RememberLater();
    }

    private double Option(string key) => VisualizerOptions.Number(_settings.VisualOptions, _settings.Visualizer, key);

    private bool OptionOn(string key) => VisualizerOptions.Toggle(_settings.VisualOptions, _settings.Visualizer, key);

    private int OptionChoice(string key) => VisualizerOptions.Choice(_settings.VisualOptions, _settings.Visualizer, key);

    /// <summary>The visualisation's colour for <paramref name="share"/> (how far along the pitch, or the shapes), as its settings say.</summary>
    private Windows.UI.Color Paint(double share)
    {
        var palette = (VisualPalette)OptionChoice(VisualizerOptions.Colors);
        var accent = Accent().Color;
        var own = VisualizerOptions.Rgb(_settings.VisualOptions, _settings.Visualizer, VisualizerOptions.Color);
        var color = Visualizers.PaletteColor(palette, share, _player.Position.TotalSeconds, new Argb(255, accent.R, accent.G, accent.B), own);
        return Windows.UI.Color.FromArgb(255, color.R, color.G, color.B);
    }

    private SolidColorBrush Brush(double share) => new(Paint(share));

    /// <summary>The spectrum split into <paramref name="count"/> bands, made again when the count changes.</summary>
    private SpectrumAnalyzer Bands(int count)
    {
        if (_bands.Levels.Count != count)
        {
            _bands = new SpectrumAnalyzer(count);
        }

        return _bands;
    }

    /// <summary>Makes the shapes for the chosen visualisation at the size it has, with its settings.</summary>
    private void BuildVisualizer()
    {
        _builtFor = _settings.Visualizer;
        VisualCanvas.Children.Clear();
        _barShapes.Clear();
        _peakShapes.Clear();
        _rays.Clear();
        _sparks.Clear();
        _rings.Clear();
        (_wave, _haloRing, _labelSpin, _arm, _stylus, _aurora, _strobeFill, _strobeBrush, _visualNote, _visualBitmap) = (null, null, null, null, null, null, null, null, null, null);
        SpectrogramImage.Source = null;
        SpectrogramImage.Stretch = Stretch.Fill;
        (_beatAverage, _beatRest, _flash) = (0, 0, 0);
        _visualStage = null;
        if (_settings.Visualizer is not (VisualizerChoice.Silhouette or VisualizerChoice.BeatEdit))
        {
            StopCamera();
        }

        var (width, height) = (Visualizer.ActualWidth, Visualizer.ActualHeight);
        if (VisualStage.Draws(_settings.Visualizer))
        {
            var size = VisualStage.SizeFor(width, height);
            _visualStage = new VisualStage(size.Width, size.Height);
            _visualBitmap = new WriteableBitmap(size.Width, size.Height);
            SpectrogramImage.Source = _visualBitmap;
            SpectrogramImage.Stretch = Stretch.Fill;
            if (_visualStage.UsesCamera)
            {
                _cameraMask = new byte[CameraSilhouette.Width * CameraSilhouette.Height];
                _visualNote = new TextBlock { Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(200, 255, 255, 255)), TextWrapping = TextWrapping.Wrap, MaxWidth = 460 };
                Canvas.SetLeft(_visualNote, 12);
                Canvas.SetTop(_visualNote, 12);
                VisualCanvas.Children.Add(_visualNote);
                if (_camera is null)
                {
                    _camera = new CameraSilhouette();
                    _ = _camera.StartAsync();
                }

                _camera.Mirror = _settings.Visualizer == VisualizerChoice.Silhouette && OptionOn("mirror");
                if (_settings.Visualizer == VisualizerChoice.Silhouette)
                {
                    _camera.Threshold = Option("threshold");
                }
            }

            return;
        }

        switch (_settings.Visualizer)
        {
            case VisualizerChoice.Spectrum:
                var count = (int)Option("bars");
                Bands(count);
                var slot = width / count;
                for (var i = 0; i < count; i++)
                {
                    var bar = new Rectangle { Width = Math.Max(1, slot * 0.7), Fill = Brush((double)i / count), RadiusX = 2, RadiusY = 2 };
                    Canvas.SetLeft(bar, (i * slot) + (slot * 0.15));
                    _barShapes.Add(bar);
                    VisualCanvas.Children.Add(bar);
                    if (OptionOn("peaks"))
                    {
                        var peak = new Rectangle { Width = Math.Max(1, slot * 0.7), Height = 3, Fill = new SolidColorBrush(Microsoft.UI.Colors.White) };
                        Canvas.SetLeft(peak, (i * slot) + (slot * 0.15));
                        _peakShapes.Add(peak);
                        VisualCanvas.Children.Add(peak);
                    }
                }

                break;
            case VisualizerChoice.Oscilloscope:
                _wave = new Polyline { Stroke = Brush(0.5), StrokeThickness = Option("thickness"), StrokeLineJoin = PenLineJoin.Round };
                VisualCanvas.Children.Add(_wave);
                break;
            case VisualizerChoice.Meters:
                // For each channel: the peak, the average inside it, and the hold mark.
                for (var channel = 0; channel < 2; channel++)
                {
                    var top = (height / 2) - 44 + (channel * 52);
                    var fill = Brush(channel * 0.5);
                    var peak = new Rectangle { Height = 36, Fill = fill, Opacity = 0.45 };
                    var average = new Rectangle { Height = 36, Fill = fill };
                    var hold = new Rectangle { Width = 3, Height = 36, Fill = new SolidColorBrush(Microsoft.UI.Colors.White) };
                    foreach (var shape in new[] { peak, average, hold })
                    {
                        Canvas.SetTop(shape, top);
                        VisualCanvas.Children.Add(shape);
                    }

                    (_meterShapes[channel * 3], _meterShapes[(channel * 3) + 1], _meterShapes[(channel * 3) + 2]) = (peak, average, hold);
                }

                break;
            case VisualizerChoice.Spectrogram:
                Array.Clear(_spectrogramPixels);
                _visualBitmap = new WriteableBitmap(SpectrogramColumns, SpectrogramRows);
                SpectrogramImage.Source = _visualBitmap;
                break;
            case VisualizerChoice.Vinyl:
                BuildVinyl(width, height);
                break;
            case VisualizerChoice.Halo:
                var rays = (int)Option("rays");
                Bands(rays);
                _haloRing = new Ellipse { Stroke = Brush(0), StrokeThickness = 1.5, Opacity = 0.35 };
                VisualCanvas.Children.Add(_haloRing);
                for (var i = 0; i < rays; i++)
                {
                    var ray = new Line { Stroke = Brush((double)i / rays), StrokeThickness = Option("thickness"), StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                    _rays.Add(ray);
                    VisualCanvas.Children.Add(ray);
                }

                break;
            case VisualizerChoice.Mirror:
                var columns = (int)Option("columns");
                var column = width / columns;
                for (var i = 0; i < columns; i++)
                {
                    var bar = new Rectangle { Width = Math.Max(1, column * 0.6), Fill = Brush((double)i / columns), RadiusX = 1.5, RadiusY = 1.5, Opacity = 0.9 };
                    Canvas.SetLeft(bar, (i * column) + (column * 0.2));
                    _barShapes.Add(bar);
                    VisualCanvas.Children.Add(bar);
                }

                break;
            case VisualizerChoice.Aurora:
                Bands((int)Option("bands"));
                var color = Paint(0.4);
                Brush glow = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                if (OptionOn("glow"))
                {
                    var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                    gradient.GradientStops.Add(new GradientStop { Color = WithAlpha(color, 190), Offset = 0 });
                    gradient.GradientStops.Add(new GradientStop { Color = WithAlpha(color, 15), Offset = 1 });
                    glow = gradient;
                }

                _aurora = new Polygon { Fill = glow, Stroke = new SolidColorBrush(color), StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
                VisualCanvas.Children.Add(_aurora);
                break;
            case VisualizerChoice.Embers:
                BuildSparks((int)Option("amount"));
                break;
            case VisualizerChoice.Ripples:
                Bands(48);
                _ringAge = new double[RippleCount];
                for (var i = 0; i < RippleCount; i++)
                {
                    var ring = new Ellipse { Stroke = Brush((double)i / RippleCount), StrokeThickness = Option("thickness"), Opacity = 0 };
                    _rings.Add(ring);
                    VisualCanvas.Children.Add(ring);
                }

                break;
            case VisualizerChoice.Strobe:
                Bands(48);
                _strobeBrush = new SolidColorBrush(Microsoft.UI.Colors.Black);
                _strobeFill = new Rectangle { Fill = _strobeBrush, RadiusX = 8, RadiusY = 8 };
                VisualCanvas.Children.Add(_strobeFill);
                break;
            case VisualizerChoice.Silhouette:
                BuildSilhouette();
                break;
        }
    }

    private static Windows.UI.Color WithAlpha(Windows.UI.Color color, byte alpha) => Windows.UI.Color.FromArgb(alpha, color.R, color.G, color.B);

    private static SolidColorBrush Accent() =>
        Application.Current.Resources.TryGetValue("SystemAccentColorLight2", out var color) && color is Windows.UI.Color accent
            ? new SolidColorBrush(accent)
            : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 96, 205, 255));

    private void BuildSparks(int count)
    {
        (_sparkX, _sparkY, _sparkVx, _sparkVy, _sparkLife) = (new double[count], new double[count], new double[count], new double[count], new double[count]);
        for (var i = 0; i < count; i++)
        {
            var spark = new Ellipse { Width = 3 + (i % 4), Height = 3 + (i % 4), Fill = Brush((double)i / count), Opacity = 0 };
            _sparks.Add(spark);
            VisualCanvas.Children.Add(spark);
        }
    }

    /// <summary>The record: the disc and its faint rings, the groove the sound draws, the label, the arm.</summary>
    private void BuildVinyl(double width, double height)
    {
        var radius = Math.Min(width, height) * 0.46;
        var (cx, cy) = (width / 2, height / 2);
        var disc = new Ellipse { Width = radius * 2, Height = radius * 2, Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 17, 18, 17)) };
        Canvas.SetLeft(disc, cx - radius);
        Canvas.SetTop(disc, cy - radius);
        VisualCanvas.Children.Add(disc);
        foreach (var ring in new[] { 0.95, 0.88, 0.56, 0.44 })
        {
            var sheen = new Ellipse { Width = radius * 2 * ring, Height = radius * 2 * ring, Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 44, 42)), StrokeThickness = 1 };
            Canvas.SetLeft(sheen, cx - (radius * ring));
            Canvas.SetTop(sheen, cy - (radius * ring));
            VisualCanvas.Children.Add(sheen);
        }

        _wave = new Polyline { Stroke = Brush(0.5), StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round };
        VisualCanvas.Children.Add(_wave);

        // The song's cover is the label, turning with the record.
        var labelRadius = radius * 0.3;
        _labelSpin = new RotateTransform { CenterX = labelRadius, CenterY = labelRadius };
        var label = new Ellipse
        {
            Width = labelRadius * 2,
            Height = labelRadius * 2,
            RenderTransform = _labelSpin,
            Fill = OptionOn("label") && CoverImage.Source is { } art ? new ImageBrush { ImageSource = art, Stretch = Stretch.UniformToFill } : Brush(0.2),
        };
        Canvas.SetLeft(label, cx - labelRadius);
        Canvas.SetTop(label, cy - labelRadius);
        VisualCanvas.Children.Add(label);
        var spindle = new Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(Microsoft.UI.Colors.White) };
        Canvas.SetLeft(spindle, cx - 3.5);
        Canvas.SetTop(spindle, cy - 3.5);
        VisualCanvas.Children.Add(spindle);
        if (OptionOn("arm"))
        {
            _arm = new Line { Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 204, 200)), StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            _stylus = new Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(Microsoft.UI.Colors.White) };
            VisualCanvas.Children.Add(_arm);
            VisualCanvas.Children.Add(_stylus);
        }
    }

    /// <summary>The camera's picture of the listener, and a note while it watches the room or cannot start.</summary>
    private void BuildSilhouette()
    {
        _visualBitmap = new WriteableBitmap(CameraSilhouette.Width, CameraSilhouette.Height);
        _visualPixels = new byte[CameraSilhouette.Width * CameraSilhouette.Height * 4];
        _cameraMask = new byte[CameraSilhouette.Width * CameraSilhouette.Height];
        SpectrogramImage.Source = _visualBitmap;
        SpectrogramImage.Stretch = Stretch.Uniform;
        Bands(48);
        if (OptionChoice("style") == 2)
        {
            BuildSparks(160);
        }

        _visualNote = new TextBlock { Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(200, 255, 255, 255)), TextWrapping = TextWrapping.Wrap, MaxWidth = 460 };
        Canvas.SetLeft(_visualNote, 12);
        Canvas.SetTop(_visualNote, 12);
        VisualCanvas.Children.Add(_visualNote);
        if (_camera is null)
        {
            _camera = new CameraSilhouette();
            _ = _camera.StartAsync();
        }

        (_camera.Mirror, _camera.Threshold) = (OptionOn("mirror"), Option("threshold"));
    }

    private void StopCamera()
    {
        if (_camera is { } camera)
        {
            _camera = null;
            _ = camera.DisposeAsync().AsTask();
        }
    }

    private void OnRendering(object? sender, object e)
    {
        if (_closed)
        {
            return;
        }

        // About thirty pictures a second is smooth for this, and spares the processor.
        var now = e is RenderingEventArgs rendering ? rendering.RenderingTime : TimeSpan.Zero;
        var elapsed = now - _lastDrawn;
        if (elapsed < TimeSpan.FromMilliseconds(30) && elapsed >= TimeSpan.Zero)
        {
            return;
        }

        _lastDrawn = now;
        elapsed = elapsed < TimeSpan.Zero || elapsed > TimeSpan.FromSeconds(1) ? TimeSpan.FromMilliseconds(30) : elapsed;
        if (!_player.ReadSound(_soundLeft, _soundRight))
        {
            Array.Clear(_soundLeft);
            Array.Clear(_soundRight);
        }

        var rate = Math.Max(1, _player.SoundSampleRate);
        if (_visualStage is { } stage && _visualBitmap is not null && _builtFor is { } choice)
        {
            stage.Context.Seconds = _player.Position.TotalSeconds;
            stage.Context.Progress = _player.Duration > TimeSpan.Zero ? Math.Clamp(_player.Position / _player.Duration, 0, 1) : 0;
            var accent = Accent().Color;
            stage.Context.Accent = new Argb(255, accent.R, accent.G, accent.B);
            stage.Context.Cover = _visualCover;
            var camera = _camera;
            if (camera is not null && camera.Problem is null)
            {
                camera.CopyPicture(_cameraPicture);
                camera.CopyMask(_cameraMask);
                stage.Context.Camera = new VisualPicture(_cameraPicture, CameraSilhouette.Width, CameraSilhouette.Height);
                stage.Context.Mask = _cameraMask;
                stage.Context.MaskWidth = CameraSilhouette.Width;
                stage.Context.MaskHeight = CameraSilhouette.Height;
            }

            if (_visualNote is not null)
            {
                _visualNote.Text = camera?.Problem ?? (camera?.IsLearning == true ? "Looking at the room. Stay still, or step out of view for a moment." : "");
            }

            stage.Draw(choice, _settings.VisualOptions, _soundLeft, _soundRight, rate, elapsed);
            using (var stream = _visualBitmap.PixelBuffer.AsStream())
            {
                stream.Write(stage.Pixels);
            }

            _visualBitmap.Invalidate();
            return;
        }

        // Sensitivity: quiet music can fill the picture, loud music can be calmed.
        var gain = (float)Option(VisualizerOptions.Sensitivity);
        for (var i = 0; i < _soundMono.Length; i++)
        {
            _soundLeft[i] = Math.Clamp(_soundLeft[i] * gain, -1, 1);
            _soundRight[i] = Math.Clamp(_soundRight[i] * gain, -1, 1);
            _soundMono[i] = (_soundLeft[i] + _soundRight[i]) / 2;
        }

        var (width, height) = (Visualizer.ActualWidth, Visualizer.ActualHeight);
        switch (_builtFor)
        {
            case VisualizerChoice.Spectrum:
                DrawSpectrum(height, elapsed, rate);
                break;
            case VisualizerChoice.Oscilloscope when _wave is not null:
                // The newest twentieth of a second, drawn across the width.
                var span = Math.Min(_soundMono.Length, Math.Max(64, rate / 20));
                var points = new PointCollection();
                var steps = Math.Max(2, (int)Math.Min(width / 2, span));
                for (var step = 0; step < steps; step++)
                {
                    var sample = _soundMono[_soundMono.Length - span + (step * (span - 1) / (steps - 1))];
                    points.Add(new Point(step * width / (steps - 1), (height / 2) - (sample * height * 0.45)));
                }

                _wave.Points = points;
                break;
            case VisualizerChoice.Meters:
                // A meter shows what has just been heard: the newest twentieth of a second.
                var recent = Math.Min(_soundLeft.Length, Math.Max(64, rate / 20));
                _meterLeft.Update(_soundLeft.AsSpan(^recent..), elapsed);
                _meterRight.Update(_soundRight.AsSpan(^recent..), elapsed);
                foreach (var (meter, at) in new[] { (_meterLeft, 0), (_meterRight, 3) })
                {
                    _meterShapes[at].Width = LevelMeter.Position(meter.PeakDb) * width;
                    _meterShapes[at + 1].Width = LevelMeter.Position(meter.RmsDb) * width;
                    Canvas.SetLeft(_meterShapes[at + 2], Math.Max(0, (LevelMeter.Position(meter.HoldDb) * width) - 3));
                }

                break;
            case VisualizerChoice.Spectrogram when _visualBitmap is not null:
                _spectrogramBands.Update(_soundMono, rate, elapsed);
                DrawSpectrogramColumn(_visualBitmap);
                break;
            case VisualizerChoice.Vinyl when _wave is not null:
                DrawVinyl(width, height);
                break;
            case VisualizerChoice.Halo:
                DrawHalo(width, height, elapsed, rate);
                break;
            case VisualizerChoice.Mirror:
                DrawMirror(height);
                break;
            case VisualizerChoice.Aurora when _aurora is not null:
                _bands.Update(_soundMono, rate, elapsed);
                var ridge = new PointCollection { new Point(0, height) };
                for (var i = 0; i < _bands.Levels.Count; i++)
                {
                    ridge.Add(new Point((i + 0.5) * width / _bands.Levels.Count, height - (Math.Clamp(_bands.Levels[i], 0, 1) * height * 0.9)));
                }

                ridge.Add(new Point(width, height));
                _aurora.Points = ridge;
                break;
            case VisualizerChoice.Embers:
                DrawEmbers(width, height, elapsed, null);
                break;
            case VisualizerChoice.Ripples:
                DrawRipples(width, height, elapsed, rate);
                break;
            case VisualizerChoice.Strobe when _strobeFill is not null:
                DrawStrobe(width, height, elapsed, rate);
                break;
            case VisualizerChoice.Silhouette when _visualBitmap is not null:
                DrawSilhouette(width, height, elapsed, rate);
                break;
        }
    }

    private void DrawSpectrum(double height, TimeSpan elapsed, int rate)
    {
        _bands.Update(_soundMono, rate, elapsed);
        var mirrored = OptionOn("mirrored");
        for (var i = 0; i < _barShapes.Count && i < _bands.Levels.Count; i++)
        {
            var bar = Math.Max(1, _bands.Levels[i] * height * (mirrored ? 0.95 : 1));
            _barShapes[i].Height = bar;
            Canvas.SetTop(_barShapes[i], mirrored ? (height - bar) / 2 : height - bar);
            if (i < _peakShapes.Count)
            {
                var peak = _bands.Peaks[i] * height;
                Canvas.SetTop(_peakShapes[i], mirrored ? Math.Max(0, ((height - peak) / 2) - 4) : Math.Max(0, height - peak - 4));
            }
        }
    }

    /// <summary>Each column is the loudest moment of its slice of the newest sound, about the middle.</summary>
    private void DrawMirror(double height)
    {
        var count = _barShapes.Count;
        for (var i = 0; i < count; i++)
        {
            var from = i * _soundMono.Length / count;
            var to = (i + 1) * _soundMono.Length / count;
            var loudest = 0f;
            for (var at = from; at < to; at++)
            {
                loudest = Math.Max(loudest, Math.Abs(_soundMono[at]));
            }

            var bar = Math.Max(2, loudest * height * 0.92);
            _barShapes[i].Height = bar;
            Canvas.SetTop(_barShapes[i], (height - bar) / 2);
        }
    }

    /// <summary>
    /// The groove is the newest sound wound once round the disc, turning at the record's speed (by
    /// the song's position, so pausing stops it); the arm rides inwards as the song goes on.
    /// </summary>
    private void DrawVinyl(double width, double height)
    {
        var radius = Math.Min(width, height) * 0.46;
        var (cx, cy) = (width / 2, height / 2);
        var turnsPerSecond = OptionChoice("speed") switch
        {
            1 => 45.0 / 60,
            2 => 78.0 / 60,
            _ => 100.0 / 180,
        };
        var phase = _player.Position.TotalSeconds * turnsPerSecond * Math.Tau;
        var depth = Option("depth");
        var points = new PointCollection();
        const int Winding = 240;
        for (var i = 0; i <= Winding; i++)
        {
            var angle = ((double)i / Winding * Math.Tau) + phase;
            var sample = _soundMono[Math.Min(_soundMono.Length - 1, i * _soundMono.Length / Winding)];
            var r = (radius * 0.70) + (sample * radius * depth);
            points.Add(new Point(cx + (r * Math.Cos(angle)), cy + (r * Math.Sin(angle))));
        }

        _wave!.Points = points;
        if (_labelSpin is not null)
        {
            _labelSpin.Angle = phase * 180 / Math.PI;
        }

        if (_stylus is not null && _arm is not null)
        {
            var progress = _player.Duration > TimeSpan.Zero ? Math.Clamp(_player.Position / _player.Duration, 0, 1) : 0;
            var needle = radius * (0.94 - (0.36 * progress));
            var (sx, sy) = (cx + (needle * Math.Cos(-Math.PI / 3)), cy + (needle * Math.Sin(-Math.PI / 3)));
            Canvas.SetLeft(_stylus, sx - 4.5);
            Canvas.SetTop(_stylus, sy - 4.5);
            (_arm.X1, _arm.Y1) = (Math.Min(width - 4, cx + (radius * 1.08)), Math.Max(4, cy - (radius * 1.02)));
            (_arm.X2, _arm.Y2) = (sx, sy);
        }
    }

    /// <summary>Each band is a ray from a circle that swells with the bass, the whole halo turning slowly when asked.</summary>
    private void DrawHalo(double width, double height, TimeSpan elapsed, int rate)
    {
        _bands.Update(_soundMono, rate, elapsed);
        var size = Math.Min(width, height);
        var (cx, cy) = (width / 2, height / 2);
        var inner = size * (0.17 + (Bass() * 0.06));
        var reach = size * 0.27;
        var spin = OptionOn("spin") ? _player.Position.TotalSeconds * 0.05 * Math.Tau : 0;
        for (var i = 0; i < _rays.Count && i < _bands.Levels.Count; i++)
        {
            var angle = ((double)i / _rays.Count * Math.Tau) - (Math.PI / 2) + spin;
            var length = Math.Max(2, _bands.Levels[i] * reach);
            (_rays[i].X1, _rays[i].Y1) = (cx + (inner * Math.Cos(angle)), cy + (inner * Math.Sin(angle)));
            (_rays[i].X2, _rays[i].Y2) = (cx + ((inner + length) * Math.Cos(angle)), cy + ((inner + length) * Math.Sin(angle)));
        }

        if (_haloRing is not null)
        {
            _haloRing.Width = _haloRing.Height = inner * 2 * 0.92;
            Canvas.SetLeft(_haloRing, cx - (inner * 0.92));
            Canvas.SetTop(_haloRing, cy - (inner * 0.92));
        }
    }

    /// <summary>The average of the lowest bands: the bass, for things that move on the beat.</summary>
    private float Bass() => _bands.Levels.Count < 4 ? 0 : (_bands.Levels[0] + _bands.Levels[1] + _bands.Levels[2] + _bands.Levels[3]) / 4;

    /// <summary>Whether this moment is a beat: the bass jumping past its own recent average, no sooner than <paramref name="rest"/> after the last.</summary>
    private bool Beat(double dt, double jump, double rest)
    {
        var bass = Bass();
        _beatAverage += (bass - _beatAverage) * 0.04f;
        _beatRest -= dt;
        if (bass > 0.08f && bass > _beatAverage * jump && _beatRest <= 0)
        {
            _beatRest = rest;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Sparks rise as the music plays, more and faster the louder it is: from the bottom, or (for
    /// the silhouette) from points of the listener's outline given in <paramref name="from"/>.
    /// </summary>
    private void DrawEmbers(double width, double height, TimeSpan elapsed, IReadOnlyList<Point>? from)
    {
        var dt = elapsed.TotalSeconds;
        var speed = _settings.Visualizer == VisualizerChoice.Embers ? Option("speed") : 1;
        var loudness = 0f;
        for (var i = _soundMono.Length / 2; i < _soundMono.Length; i++)
        {
            loudness += Math.Abs(_soundMono[i]);
        }

        loudness /= _soundMono.Length / 2f;
        var wanted = (int)Math.Round(Math.Min(1, loudness * 6) * Math.Max(2, _sparks.Count / 20));
        for (var i = 0; i < _sparks.Count; i++)
        {
            if (_sparkLife[i] <= 0 && wanted > 0 && (from is null || from.Count > 0))
            {
                wanted--;
                _sparkLife[i] = 1;
                var start = from is null ? new Point(Random.Shared.NextDouble() * width, height - 2) : from[Random.Shared.Next(from.Count)];
                (_sparkX[i], _sparkY[i]) = (start.X, start.Y);
                _sparkVx[i] = (Random.Shared.NextDouble() - 0.5) * 40;
                _sparkVy[i] = -(50 + (Random.Shared.NextDouble() * 160)) * (0.4 + Math.Min(1.6, loudness * 6)) * speed;
            }
            else if (_sparkLife[i] > 0)
            {
                _sparkLife[i] -= dt / 2.2;
                _sparkX[i] += _sparkVx[i] * dt;
                _sparkY[i] += _sparkVy[i] * dt;
                _sparkVy[i] *= 1 - (0.5 * dt);
            }

            var alive = _sparkLife[i] > 0 && _sparkY[i] > -8;
            _sparks[i].Opacity = alive ? Math.Clamp(_sparkLife[i], 0, 1) : 0;
            if (alive)
            {
                Canvas.SetLeft(_sparks[i], _sparkX[i]);
                Canvas.SetTop(_sparks[i], _sparkY[i]);
            }
        }
    }

    /// <summary>A ring bursts from the middle on each beat and fades as it grows.</summary>
    private void DrawRipples(double width, double height, TimeSpan elapsed, int rate)
    {
        _bands.Update(_soundMono, rate, elapsed);
        var dt = elapsed.TotalSeconds;
        if (Beat(dt, Option("beat"), 0.16))
        {
            var free = _rings.FindIndex(ring => ring.Opacity <= 0);
            if (free >= 0)
            {
                _ringAge[free] = 0.001;
                _rings[free].Opacity = 1;
            }
        }

        var (cx, cy) = (width / 2, height / 2);
        var reach = Math.Min(width, height) * 0.55;
        const double Lifetime = 1.3;
        for (var i = 0; i < _rings.Count; i++)
        {
            if (_ringAge[i] <= 0)
            {
                continue;
            }

            _ringAge[i] += dt;
            var share = _ringAge[i] / Lifetime;
            if (share >= 1)
            {
                _ringAge[i] = 0;
                _rings[i].Opacity = 0;
                continue;
            }

            var radius = Math.Max(1, share * reach);
            _rings[i].Width = _rings[i].Height = radius * 2;
            _rings[i].Opacity = 1 - share;
            Canvas.SetLeft(_rings[i], cx - radius);
            Canvas.SetTop(_rings[i], cy - radius);
        }
    }

    /// <summary>
    /// The whole stage glows with the music, bright on each beat and fading between, never flashing
    /// more often than its setting allows (three a second at first, the limit guidelines give for
    /// people with photosensitive epilepsy).
    /// </summary>
    private void DrawStrobe(double width, double height, TimeSpan elapsed, int rate)
    {
        _bands.Update(_soundMono, rate, elapsed);
        var dt = elapsed.TotalSeconds;
        var loud = _bands.Levels.Average();
        _flash = Math.Max(_flash - (dt / Option("fade")), Math.Min(1, loud * 1.3));
        if (Beat(dt, 1.4, 1 / Option("flashes")))
        {
            _flash = 1;
        }

        var color = Paint((double)Visualizers.DominantBand(_bands.Levels) / Math.Max(1, _bands.Levels.Count - 1));
        var level = Math.Clamp(_flash, 0, 1);
        _strobeBrush!.Color = Windows.UI.Color.FromArgb(255, (byte)Math.Round(color.R * level), (byte)Math.Round(color.G * level), (byte)Math.Round(color.B * level));
        _strobeFill!.Width = Math.Max(1, width);
        _strobeFill.Height = Math.Max(1, height);
    }

    /// <summary>
    /// The listener as the camera sees them: a glowing outline that brightens with the beat, a
    /// figure filled with the spectrum (low notes at the feet), or sparks thrown off the outline.
    /// </summary>
    private void DrawSilhouette(double width, double height, TimeSpan elapsed, int rate)
    {
        _bands.Update(_soundMono, rate, elapsed);
        var dt = elapsed.TotalSeconds;
        if (Beat(dt, 1.4, 0.12))
        {
            _flash = 1;
        }

        _flash = Math.Max(0, _flash - (dt / 0.35));
        var camera = _camera;
        if (_visualNote is not null)
        {
            _visualNote.Text = camera?.Problem ?? (camera?.IsLearning == true ? "Looking at the room. Stay still, or step out of view for a moment." : "");
        }

        if (camera is null || camera.Problem is not null)
        {
            return;
        }

        camera.CopyMask(_cameraMask);
        const int W = CameraSilhouette.Width, H = CameraSilhouette.Height;
        var style = OptionChoice("style");
        var glow = 0.55 + (0.45 * _flash);
        var edges = new List<Point>();
        var (scale, left, top) = Fit(width, height, W, H);
        for (var y = 0; y < H; y++)
        {
            var band = _bands.Levels[Math.Min(_bands.Levels.Count - 1, (H - 1 - y) * _bands.Levels.Count / H)];
            var color = Paint((double)(H - 1 - y) / H);
            for (var x = 0; x < W; x++)
            {
                var at = ((y * W) + x) * 4;
                double level;
                if (_cameraMask[(y * W) + x] == 0)
                {
                    level = 0;
                }
                else if (SilhouetteMask.IsEdge(_cameraMask, W, H, x, y))
                {
                    level = glow;
                    if (style == 2 && (x + y) % 3 == 0)
                    {
                        edges.Add(new Point(left + (x * scale), top + (y * scale)));
                    }
                }
                else
                {
                    level = style == 1 ? 0.2 + (0.8 * band) : style == 2 ? 0.08 : 0.12 + (0.2 * _flash);
                }

                (_visualPixels[at], _visualPixels[at + 1], _visualPixels[at + 2], _visualPixels[at + 3]) =
                    ((byte)(color.B * level), (byte)(color.G * level), (byte)(color.R * level), 255);
            }
        }

        using (var stream = _visualBitmap!.PixelBuffer.AsStream())
        {
            stream.Write(_visualPixels);
        }

        _visualBitmap.Invalidate();
        if (style == 2)
        {
            DrawEmbers(width, height, elapsed, edges);
        }
    }

    /// <summary>Where a picture of <paramref name="across"/> by <paramref name="down"/> sits, uniformly scaled, in the visualisation.</summary>
    private static (double Scale, double Left, double Top) Fit(double width, double height, int across, int down)
    {
        var scale = Math.Min(width / across, height / down);
        return (scale, (width - (across * scale)) / 2, (height - (down * scale)) / 2);
    }

    /// <summary>Moves the spectrogram one column left and draws the newest on the right, low notes at the bottom.</summary>
    private void DrawSpectrogramColumn(WriteableBitmap bitmap)
    {
        const int Stride = SpectrogramColumns * 4;
        var palette = (VisualPalette)OptionChoice(VisualizerOptions.Colors);
        for (var row = 0; row < SpectrogramRows; row++)
        {
            var start = row * Stride;
            Buffer.BlockCopy(_spectrogramPixels, start + 4, _spectrogramPixels, start, Stride - 4);
            var level = _spectrogramBands.Levels[SpectrogramRows - 1 - row];
            Argb color;
            if (palette == VisualPalette.Accent)
            {
                // Its own heat colours, unless asked for others.
                color = Visualizers.Heat(level);
            }
            else
            {
                var paint = Paint((double)(SpectrogramRows - 1 - row) / SpectrogramRows);
                color = new Argb(255, (byte)(paint.R * level), (byte)(paint.G * level), (byte)(paint.B * level));
            }

            var at = start + Stride - 4;
            (_spectrogramPixels[at], _spectrogramPixels[at + 1], _spectrogramPixels[at + 2], _spectrogramPixels[at + 3]) = (color.B, color.G, color.R, color.A);
        }

        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Write(_spectrogramPixels);
        }

        bitmap.Invalidate();
    }
}
