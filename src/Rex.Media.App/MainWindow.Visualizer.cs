using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Player;
using Rex.Media.Audio;
using Rex.Media.Engine;
using Rex.Media.Settings;
using Windows.Foundation;

namespace Rex.Media.App;

/// <summary>
/// Visualisations of the sound while something without pictures plays (AU-18): spectrum bars,
/// an oscilloscope, level meters, or a scrolling spectrogram. The analysis is in Rex.Media.Audio;
/// this draws it about thirty times a second, and only while it is on screen.
/// </summary>
public sealed partial class MainWindow
{
    private const int Bars = 48;
    private const int SpectrogramColumns = 320;
    private const int SpectrogramRows = 128;

    private readonly float[] _soundLeft = new float[SpectrumAnalyzer.Size];
    private readonly float[] _soundRight = new float[SpectrumAnalyzer.Size];
    private readonly float[] _soundMono = new float[SpectrumAnalyzer.Size];
    private readonly SpectrumAnalyzer _bars = new(Bars);
    private readonly SpectrumAnalyzer _spectrogramBands = new(SpectrogramRows);
    private readonly LevelMeter _meterLeft = new();
    private readonly LevelMeter _meterRight = new();
    private readonly byte[] _spectrogramPixels = new byte[SpectrogramColumns * SpectrogramRows * 4];
    private readonly List<Rectangle> _barShapes = [];
    private readonly List<Rectangle> _peakShapes = [];
    private readonly Rectangle[] _meterShapes = new Rectangle[6];
    private Polyline? _wave;
    private WriteableBitmap? _spectrogram;
    private VisualizerChoice _builtFor;
    private TimeSpan _lastDrawn;
    private bool _visualizing;

    private void WireVisualizer() => Visualizer.SizeChanged += (_, _) => BuildVisualizer();

    /// <summary>Shows the chosen visualisation while sound without pictures plays, and stops drawing otherwise.</summary>
    private void ApplyVisualizer()
    {
        // Lyrics, when the music has them, take the visualisation's place.
        var music = _player.Item is not null && _player.Info is not null && !HasVideo && _player.State is not (SessionState.Idle or SessionState.Faulted);
        var show = music && _settings.Visualizer != VisualizerChoice.Off && _player.Lyrics is null;
        Visualizer.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        var below = show || (music && _player.Lyrics is not null);
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
                CompositionTarget.Rendering -= OnRendering;
                _bars.Reset();
                _spectrogramBands.Reset();
                _meterLeft.Reset();
                _meterRight.Reset();
            }
        }
    }

    private bool RunVisualizerCommand(string command)
    {
        if (command != CommandCatalog.CycleVisualizer)
        {
            return false;
        }

        _settings = _settings with { Visualizer = Visualizers.Next(_settings.Visualizer) };
        _player.Settings = _settings;
        ApplyVisualizer();
        Say(Visualizers.Name(_settings.Visualizer));
        RememberLater();
        return true;
    }

    /// <summary>Makes the shapes for the chosen visualisation at the size it has.</summary>
    private void BuildVisualizer()
    {
        _builtFor = _settings.Visualizer;
        VisualCanvas.Children.Clear();
        _barShapes.Clear();
        _peakShapes.Clear();
        _wave = null;
        SpectrogramImage.Source = null;
        var (width, height) = (Visualizer.ActualWidth, Visualizer.ActualHeight);
        var accent = Accent();
        switch (_builtFor)
        {
            case VisualizerChoice.Spectrum:
                var slot = width / Bars;
                for (var i = 0; i < Bars; i++)
                {
                    var bar = new Rectangle { Width = Math.Max(1, slot * 0.7), Fill = accent, RadiusX = 2, RadiusY = 2 };
                    var peak = new Rectangle { Width = Math.Max(1, slot * 0.7), Height = 3, Fill = new SolidColorBrush(Microsoft.UI.Colors.White) };
                    Canvas.SetLeft(bar, (i * slot) + (slot * 0.15));
                    Canvas.SetLeft(peak, (i * slot) + (slot * 0.15));
                    _barShapes.Add(bar);
                    _peakShapes.Add(peak);
                    VisualCanvas.Children.Add(bar);
                    VisualCanvas.Children.Add(peak);
                }

                break;
            case VisualizerChoice.Oscilloscope:
                _wave = new Polyline { Stroke = accent, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
                VisualCanvas.Children.Add(_wave);
                break;
            case VisualizerChoice.Meters:
                // For each channel: the peak, the average inside it, and the hold mark.
                for (var channel = 0; channel < 2; channel++)
                {
                    var top = (height / 2) - 44 + (channel * 52);
                    var peak = new Rectangle { Height = 36, Fill = accent, Opacity = 0.45 };
                    var average = new Rectangle { Height = 36, Fill = accent };
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
                _spectrogram = new WriteableBitmap(SpectrogramColumns, SpectrogramRows);
                SpectrogramImage.Source = _spectrogram;
                break;
        }
    }

    private static SolidColorBrush Accent() =>
        Application.Current.Resources.TryGetValue("SystemAccentColorLight2", out var color) && color is Windows.UI.Color accent
            ? new SolidColorBrush(accent)
            : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 96, 205, 255));

    private void OnRendering(object? sender, object e)
    {
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

        for (var i = 0; i < _soundMono.Length; i++)
        {
            _soundMono[i] = (_soundLeft[i] + _soundRight[i]) / 2;
        }

        var rate = Math.Max(1, _player.SoundSampleRate);
        var (width, height) = (Visualizer.ActualWidth, Visualizer.ActualHeight);
        switch (_builtFor)
        {
            case VisualizerChoice.Spectrum:
                _bars.Update(_soundMono, rate, elapsed);
                for (var i = 0; i < _barShapes.Count; i++)
                {
                    var bar = _bars.Levels[i] * height;
                    _barShapes[i].Height = bar;
                    Canvas.SetTop(_barShapes[i], height - bar);
                    Canvas.SetTop(_peakShapes[i], Math.Max(0, height - (_bars.Peaks[i] * height) - 4));
                }

                break;
            case VisualizerChoice.Oscilloscope when _wave is not null:
                // The newest twentieth of a second, drawn across the width.
                var span = Math.Min(_soundMono.Length, Math.Max(64, rate / 20));
                var points = new PointCollection();
                var steps = Math.Max(2, (int)Math.Min(width / 2, span));
                for (var step = 0; step < steps; step++)
                {
                    var sample = _soundMono[_soundMono.Length - span + (step * (span - 1) / (steps - 1))];
                    points.Add(new Point(step * width / (steps - 1), (height / 2) - (Math.Clamp(sample, -1, 1) * height * 0.45)));
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
            case VisualizerChoice.Spectrogram when _spectrogram is not null:
                _spectrogramBands.Update(_soundMono, rate, elapsed);
                DrawSpectrogramColumn(_spectrogram);
                break;
        }
    }

    /// <summary>Moves the spectrogram one column left and draws the newest on the right, low notes at the bottom.</summary>
    private void DrawSpectrogramColumn(WriteableBitmap bitmap)
    {
        const int Stride = SpectrogramColumns * 4;
        for (var row = 0; row < SpectrogramRows; row++)
        {
            var start = row * Stride;
            Buffer.BlockCopy(_spectrogramPixels, start + 4, _spectrogramPixels, start, Stride - 4);
            var color = Visualizers.Heat(_spectrogramBands.Levels[SpectrogramRows - 1 - row]);
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
