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
/// Visualisations of the sound while something without pictures plays (AU-18). The four classic
/// ones (spectrum bars, an oscilloscope, level meters and a spectrogram) are drawn here with shapes;
/// the rest (vinyl, halo, mirror wave, aurora, embers, ripples, strobe, the camera silhouette and the
/// beat edit) are drawn in software by AppCore's <see cref="VisualStage"/>, about thirty times a
/// second, and shown scaled up to fill the stage. Each has its own settings
/// (<see cref="VisualizerOptions"/>). The camera is asked for once, started only while a
/// visualisation that uses it shows, and stopped as soon as none does.
/// </summary>
public sealed partial class MainWindow
{
    private const int SpectrogramColumns = 320;
    private const int SpectrogramRows = 128;

    /// <summary>How long a camera that failed to start is left before it is tried again.</summary>
    private static readonly TimeSpan CameraRetry = TimeSpan.FromSeconds(4);

    private readonly float[] _soundLeft = new float[SpectrumAnalyzer.Size];
    private readonly float[] _soundRight = new float[SpectrumAnalyzer.Size];
    private readonly float[] _soundMono = new float[SpectrumAnalyzer.Size];
    private readonly float[] _stageLeft = new float[SpectrumAnalyzer.Size];
    private readonly float[] _stageRight = new float[SpectrumAnalyzer.Size];
    private readonly SpectrumAnalyzer _spectrogramBands = new(SpectrogramRows);
    private readonly LevelMeter _meterLeft = new();
    private readonly LevelMeter _meterRight = new();
    private readonly byte[] _spectrogramPixels = new byte[SpectrogramColumns * SpectrogramRows * 4];
    private readonly List<Rectangle> _barShapes = [];
    private readonly List<Rectangle> _peakShapes = [];
    private readonly Rectangle[] _meterShapes = new Rectangle[6];
    private readonly byte[] _cameraPicture = new byte[CameraFeed.PictureWidth * CameraFeed.PictureHeight * 4];
    private readonly byte[] _cameraMask = new byte[CameraFeed.MaskWidth * CameraFeed.MaskHeight];
    private SpectrumAnalyzer _bands = new(48);
    private Polyline? _wave;
    private TextBlock? _visualNote;
    private WriteableBitmap? _visualBitmap;
    private VisualStage? _visualStage;
    private VisualPicture? _visualCover;
    private CameraFeed? _camera;
    private Task _cameraClosed = Task.CompletedTask;
    private DateTime _cameraFailedAt;
    private VisualizerChoice? _builtFor;
    private Task? _stageWork;
    private bool _stageFailed;
    private bool _stageReset;
    private TimeSpan _lastDrawn;
    private bool _visualizing;

    private void WireVisualizer()
    {
        Visualizer.SizeChanged += (_, _) => BuildVisualizer();
        Visualizer.IsHitTestVisible = true;
        Visualizer.ContextFlyout = VisualizerMenu();
    }

    /// <summary>The visualisation's right-click menu: every visualisation, the lyrics, and the settings of this one.</summary>
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
        // The worker owns the stage until its frame finishes. Reset between frames, never during one.
        _stageReset = true;
        StopCamera();
    }

    private bool RunVisualizerCommand(string command)
    {
        switch (command)
        {
            case CommandCatalog.CycleVisualizer:
                _ = ChooseVisualizerAsync(Visualizers.Next(_settings.Visualizer), cycling: true);
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

    /// <summary>Whether <paramref name="choice"/> uses the camera.</summary>
    private bool UsesCamera(VisualizerChoice choice) => choice == VisualizerChoice.Silhouette
        || (choice == VisualizerChoice.BeatEdit && VisualizerOptions.Choice(_settings.VisualOptions, choice, "source") == 0);

    /// <summary>Asks once whether the camera may be used; true when it may.</summary>
    private async Task<bool> AllowCameraAsync()
    {
        if (_settings.CameraAllowed)
        {
            return true;
        }

        var note = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420,
            Text = "This visualisation uses your camera, cutting and lighting you with the music. Nothing is recorded, kept or sent anywhere: each picture replaces the last in memory, and the camera turns off when another visualisation is chosen or the music stops.",
        };
        if (!await Ask("Use the camera?", note, "Use the camera", "Not now"))
        {
            return false;
        }

        _settings = _settings with { CameraAllowed = true };
        SaveSettings();
        return true;
    }

    /// <summary>
    /// Changes the visualisation; one that uses the camera asks first, the first time. Declined, a
    /// chosen one leaves the visualisation as it was, and stepping through them passes the camera's by.
    /// </summary>
    private async Task ChooseVisualizerAsync(VisualizerChoice choice, bool cycling = false)
    {
        if (UsesCamera(choice) && !await AllowCameraAsync())
        {
            if (!cycling)
            {
                return;
            }

            do
            {
                choice = Visualizers.Next(choice);
            }
            while (UsesCamera(choice));
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
        var color = Visualizers.PaletteColor(palette, share, _player.Position.TotalSeconds, AccentArgb(), VisualizerOptions.Rgb(_settings.VisualOptions, _settings.Visualizer, VisualizerOptions.Color));
        return Windows.UI.Color.FromArgb(255, color.R, color.G, color.B);
    }

    private SolidColorBrush Brush(double share) => new(Paint(share));

    private static Argb AccentArgb() =>
        Application.Current.Resources.TryGetValue("SystemAccentColorLight2", out var color) && color is Windows.UI.Color accent
            ? new Argb(255, accent.R, accent.G, accent.B)
            : new Argb(255, 96, 205, 255);

    /// <summary>The spectrum split into <paramref name="count"/> bands, made again when the count changes.</summary>
    private SpectrumAnalyzer Bands(int count)
    {
        if (_bands.Levels.Count != count)
        {
            _bands = new SpectrumAnalyzer(count);
        }

        return _bands;
    }

    /// <summary>
    /// Readies the chosen visualisation at the size it has, with its settings: the classic ones'
    /// shapes, or the software stage (kept, with what it has drawn, through resizes and setting
    /// changes) and the camera when it needs one.
    /// </summary>
    private void BuildVisualizer()
    {
        _builtFor = _settings.Visualizer;
        VisualCanvas.Children.Clear();
        _barShapes.Clear();
        _peakShapes.Clear();
        (_wave, _visualNote) = (null, null);
        var (width, height) = (Visualizer.ActualWidth, Visualizer.ActualHeight);
        if (VisualStage.Draws(_settings.Visualizer))
        {
            BuildStage(width, height);
            return;
        }

        _visualStage = null;
        _visualBitmap = null;
        SpectrogramImage.Source = null;
        Visualizer.Margin = new Thickness(48, 120, 48, 48);
        StopCamera();
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
                SpectrogramImage.Stretch = Stretch.Fill;
                break;
        }
    }

    /// <summary>The software stage, filling the picture area edge to edge, and a note for the camera's news.</summary>
    private void BuildStage(double width, double height)
    {
        Visualizer.Margin = new Thickness(0);
        var (across, down) = VisualStage.SizeFor(width, height, _visualStage?.Quality ?? 1);
        if (_visualStage is null)
        {
            _visualStage = new VisualStage(across, down);
        }
        else if (_stageWork is null or { IsCompleted: true })
        {
            _visualStage.Resize(across, down);
        }
        else
        {
            // A frame is being drawn: the new size is taken up once it is done.
            (across, down) = (_visualStage.Canvas.Width, _visualStage.Canvas.Height);
        }

        if (_visualBitmap is null || _visualBitmap.PixelWidth != across || _visualBitmap.PixelHeight != down || SpectrogramImage.Source != _visualBitmap)
        {
            _visualBitmap = new WriteableBitmap(across, down);
            SpectrogramImage.Source = _visualBitmap;
        }

        SpectrogramImage.Stretch = Stretch.Fill;
        if (!UsesCamera(_settings.Visualizer))
        {
            StopCamera();
            return;
        }

        _visualNote = new TextBlock { Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(200, 255, 255, 255)), TextWrapping = TextWrapping.Wrap, MaxWidth = 460 };
        Canvas.SetLeft(_visualNote, 24);
        Canvas.SetTop(_visualNote, Math.Max(24, height - 72));
        VisualCanvas.Children.Add(_visualNote);
        if (_camera is not null)
        {
            ConfigureCamera(_camera);
        }
    }

    /// <summary>Starts the camera when a visualisation needs it and it may be used, and tries again a while after it failed.</summary>
    private void KeepCamera()
    {
        if (!_settings.CameraAllowed)
        {
            return;
        }

        if (_camera is { Problem: not null } && DateTime.UtcNow - _cameraFailedAt > CameraRetry)
        {
            StopCamera();
        }

        if (_camera is null)
        {
            var camera = new CameraFeed();
            ConfigureCamera(camera);
            _camera = camera;
            _ = StartCameraAsync(camera);
        }
    }

    /// <summary>Starts <paramref name="camera"/> once the last one has let the device go.</summary>
    private async Task StartCameraAsync(CameraFeed camera)
    {
        await _cameraClosed;
        await camera.StartAsync();
        if (camera.Problem is { } problem)
        {
            _cameraFailedAt = DateTime.UtcNow;
            App.Log.Warning(LogSource, "The camera could not be used: " + problem);
        }
        else
        {
            App.Log.Info(LogSource, "The camera is on for the visualisation.");
        }
    }

    /// <summary>
    /// The silhouette sees the user as a mirror does, at its own threshold; the beat edit mirrors the
    /// picture itself, by its own setting.
    /// </summary>
    private void ConfigureCamera(CameraFeed camera)
    {
        var silhouette = _settings.Visualizer == VisualizerChoice.Silhouette;
        camera.Mirror = silhouette && OptionOn("mirror");
        camera.Threshold = silhouette ? Option("threshold") : 0.12;
    }

    private void StopCamera()
    {
        if (_camera is { } camera)
        {
            _camera = null;
            var closing = camera.DisposeAsync().AsTask();
            _cameraClosed = closing;
            App.Log.Info(LogSource, "The camera is off.");
        }
    }

    private void OnRendering(object? sender, object e)
    {
        if (_closed || (_visualStage is not null && _stageWork is { IsCompleted: false }))
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
        if (_visualStage is { } stage && _visualBitmap is not null && _builtFor is { } choice && VisualStage.Draws(choice))
        {
            DrawStage(stage, _visualBitmap, choice, rate, elapsed);
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
        }
    }

    /// <summary>
    /// A frame of the software stage. Drawing happens away from the window, so the window never
    /// waits on it: each time round, the frame finished since is shown, and the next is begun with
    /// what is heard and seen now. A frame still being drawn is let finish (the visualisation runs a
    /// little slower rather than the window stuttering), and the stage sizes itself to keep up.
    /// </summary>
    private void DrawStage(VisualStage stage, WriteableBitmap bitmap, VisualizerChoice choice, int rate, TimeSpan elapsed)
    {
        if (_stageWork is { IsCompleted: false })
        {
            return;
        }

        if (_stageReset)
        {
            stage.Reset();
            _stageReset = false;
        }
        else if (_stageWork is { IsCompletedSuccessfully: true } && bitmap.PixelWidth == stage.Canvas.Width && bitmap.PixelHeight == stage.Canvas.Height)
        {
            using (var stream = bitmap.PixelBuffer.AsStream())
            {
                stream.Write(stage.Pixels);
            }

            bitmap.Invalidate();
        }
        else if (_stageWork is { IsFaulted: true } work && !_stageFailed)
        {
            _stageFailed = true;
            App.Log.Error(LogSource, "The visualisation could not be drawn: " + work.Exception?.InnerException?.Message);
        }

        // Resized, or drawing at a new quality to keep up: the canvas is made again between frames.
        if (stage.WantsResize || (stage.Canvas.Width, stage.Canvas.Height) != VisualStage.SizeFor(Visualizer.ActualWidth, Visualizer.ActualHeight, stage.Quality))
        {
            BuildVisualizer();
            return;
        }

        var context = stage.Context;
        context.Seconds = _player.Position.TotalSeconds;
        context.Progress = _player.Duration > TimeSpan.Zero ? Math.Clamp(_player.Position / _player.Duration, 0, 1) : 0;
        context.Accent = AccentArgb();
        context.Cover = _visualCover;
        (context.Camera, context.Mask) = (null, null);
        if (UsesCamera(choice))
        {
            KeepCamera();
            var camera = _camera;
            if (camera is { Problem: null, HasPicture: true })
            {
                camera.CopyPicture(_cameraPicture);
                camera.CopyMask(_cameraMask);
                context.Camera = new VisualPicture(_cameraPicture, CameraFeed.PictureWidth, CameraFeed.PictureHeight);
                if (!camera.IsLearning)
                {
                    (context.Mask, context.MaskWidth, context.MaskHeight) = (_cameraMask, CameraFeed.MaskWidth, CameraFeed.MaskHeight);
                }
            }

            if (_visualNote is not null)
            {
                _visualNote.Text = !_settings.CameraAllowed ? "Choose this visualisation from the menu to let it use the camera."
                    : camera?.Problem is { } problem ? problem + $" Trying again every {CameraRetry.TotalSeconds:0} seconds."
                    : camera is null || !camera.HasPicture ? "Starting the camera..."
                    : choice == VisualizerChoice.Silhouette && camera.IsLearning ? "Looking at the room. Stay still, or step out of view for a moment."
                    : "";
            }
        }

        // The sound and the settings as they are now go with the frame; the window keeps its own.
        _soundLeft.CopyTo(_stageLeft, 0);
        _soundRight.CopyTo(_stageRight, 0);
        var options = _settings.VisualOptions;
        _stageWork = Task.Run(() =>
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            stage.Draw(choice, options, _stageLeft, _stageRight, rate, elapsed);
            stage.Took(clock.Elapsed.TotalMilliseconds);
        });
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
