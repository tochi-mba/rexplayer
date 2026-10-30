using System.Runtime.InteropServices;
using Rex.Media.AppCore.Player;
using Rex.Media.AppCore.Visuals;
using Rex.Media.AppCore.Visuals.Stage;
using Rex.Media.Interop.Graphics;
using Rex.Media.Settings;
using Rex.Media.Video.D3D11;
using Rex.Media.Vision;
using Rex.Media.Vision.WindowsML;

namespace Rex.Media.App;

/// <summary>
/// The camera effects drawn on the graphics card (ADR-0022): a swap-chain panel over the
/// visualisation's area, a <see cref="CameraStage"/> drawing the chosen effect into it, and the
/// camera's pictures perceived on their own worker (the camera pack's models when installed and the
/// performance tier allows, rexplayer's own otherwise). Everything is made only while such an effect
/// shows, drawn one frame at a time off the window's thread, and let go when it stops. If the
/// graphics card cannot draw, the effect's software stand-in is drawn instead.
/// </summary>
public sealed partial class MainWindow
{
    private readonly SceneSense _stageScene = new();
    private readonly MusicPulse _stagePulse = new();
    private readonly float[] _stageMono = new float[VisualStage.SoundSize];
    private readonly Rgb[] _stagePalette = new Rgb[4];
    private D3D11StageGpu? _stageGpu;
    private CameraStage? _cameraStage;
    private CameraEffect? _cameraEffect;
    private VisualizerChoice? _cameraEffectFor;
    private SenseWorker? _senseWorker;
    private IDisposable? _senseModels;
    private ScenePerceiver? _scenePerceiver;
    private Task _stageFrame = Task.CompletedTask;
    private Task _senseStarting = Task.CompletedTask;
    private byte[] _stagePicture = [];
    private VisualPicture? _stageCamera;
    private long _stageCameraStamp;
    private bool _stageGpuFailed;
    private (int Width, int Height) _stagePixels;

    /// <summary>Whether <paramref name="choice"/> is drawn on the graphics card here: an effect for it exists and the card works.</summary>
    private bool DrawsOnStage(VisualizerChoice choice) => CameraEffects.OnStage(choice) && !_stageGpuFailed;

    /// <summary>Readies the panel, the stage and perception for <paramref name="choice"/>; false when the card cannot draw.</summary>
    private bool BuildCameraStage(VisualizerChoice choice)
    {
        try
        {
            var (width, height) = StageSize(null);
            if (_stageGpu is null)
            {
                nint swapChain = 0;
                // Made off the window's thread, like the video presenter: its device is used from the drawing tasks.
                _stageGpu = Task.Run(() => D3D11StageGpu.ForComposition(width, height, out swapChain, App.SoftwareGraphics)).GetAwaiter().GetResult();
                var panel = WinRT.MarshalInspectable<object>.FromManaged(CameraStagePanel);
                try
                {
                    SwapChainPanelNative.SetSwapChain(panel, swapChain);
                }
                finally
                {
                    Marshal.Release(panel);
                    Marshal.Release(swapChain);
                }

                _cameraStage = new CameraStage(_stageGpu);
                _stagePixels = (width, height);
                App.Log.Info(LogSource, $"The camera stage draws on the graphics card ({(_stageGpu.IsSoftware ? "in software" : "hardware")}).");
            }
            else if ((width, height) != _stagePixels)
            {
                var gpu = _stageGpu;
                _stageFrame = _stageFrame.ContinueWith(_ => gpu.Resize(width, height), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
                _stagePixels = (width, height);
            }

            ScaleCameraStage();
            if (_cameraEffectFor != choice)
            {
                _cameraEffect = CameraEffects.Make(choice);
                _cameraEffectFor = choice;
            }

            CameraStagePanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            SpectrogramImage.Source = null;
            StartSense();
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ExternalException)
        {
            _stageGpuFailed = true;
            App.Log.Error(LogSource, "The graphics card cannot draw the camera effects; their software stand-ins are shown: " + ex.Message);
            StopCameraStage();
            return false;
        }
    }

    /// <summary>The panel shows the stage's pixels at the panel's own size, whatever size the tier lets them be drawn at.</summary>
    private void ScaleCameraStage()
    {
        if (_stageGpu is not { } gpu || Visualizer.ActualWidth < 1 || Visualizer.ActualHeight < 1)
        {
            return;
        }

        var (sx, sy) = ((float)(_stagePixels.Width / Visualizer.ActualWidth), (float)(_stagePixels.Height / Visualizer.ActualHeight));
        _stageFrame = _stageFrame.ContinueWith(_ => gpu.SetScale(sx, sy), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    /// <summary>
    /// Starts perceiving the camera: the camera pack on this computer's chosen device when it is
    /// installed and the tier asks for models, rexplayer's own otherwise. Opening the models takes a
    /// moment, so it happens off the window's thread; the stage draws the camera meanwhile.
    /// </summary>
    private void StartSense()
    {
        if (_senseWorker is not null || !_senseStarting.IsCompleted)
        {
            return;
        }

        var level = _budget.CameraPerception;
        var device = _budget.CameraModelsOnGpu ? ExecutionDevice.Gpu : ExecutionDevice.Cpu;
        _senseStarting = Task.Run(() =>
        {
            ScenePerceiver? models = null;
            if (level != PerceptionLevel.HandWritten)
            {
                try
                {
                    models = ScenePerceiver.Open(SubjectModelStore, device);
                }
                catch (Exception ex) when (ex is Microsoft.ML.OnnxRuntime.OnnxRuntimeException or IOException or UnauthorizedAccessException)
                {
                    App.Log.Warning(LogSource, "The camera pack could not be opened; rexplayer's own outline is used: " + ex.Message);
                }
            }

            if (models is not null)
            {
                models.Plan.Level = level;
            }

            IScenePerceiver perceiver = models is not null ? models : new HandWrittenPerceiver();
            var worker = new SenseWorker(perceiver);
            OnWindowThread(() =>
            {
                if (_closed || !_visualizing || _builtFor is not { } built || !DrawsOnStage(built))
                {
                    worker.Dispose();
                    models?.Dispose();
                    return;
                }

                (_senseWorker, _scenePerceiver, _senseModels) = (worker, models, models);
                if (_camera is { } camera)
                {
                    camera.Sense = worker;
                }

                App.Log.Info(LogSource, models is null ? "The camera effects use rexplayer's own outline and motion." : $"The camera effects use the camera pack on the {models.Device}.");
            });
        });
    }

    /// <summary>A line under the stage for the camera's news, as the software stage has.</summary>
    private void NoteCamera(double height)
    {
        _visualNote = new Microsoft.UI.Xaml.Controls.TextBlock
        {
            Foreground = RexTheme.Brush("RexOverlaySecondaryTextBrush"),
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            MaxWidth = 460,
        };
        Microsoft.UI.Xaml.Controls.Canvas.SetLeft(_visualNote, 24);
        Microsoft.UI.Xaml.Controls.Canvas.SetTop(_visualNote, Math.Max(24, height - 72));
        VisualCanvas.Children.Add(_visualNote);
    }

    /// <summary>What the camera is doing, in words, while a visualisation that uses it shows; nothing once it works.</summary>
    private void ShowCameraNote(VisualizerChoice choice)
    {
        if (_visualNote is null)
        {
            return;
        }

        var camera = _camera;
        _visualNote.Text = !_settings.CameraAllowed ? "Choose this visualisation from the menu to let it use the camera."
            : camera?.Problem is { } problem ? problem + $" Trying again every {CameraRetry.TotalSeconds:0} seconds."
            : camera is null || !camera.HasPicture ? "Starting the camera..."
            : choice == VisualizerChoice.Silhouette && camera.IsLearning ? "Learning the EMPTY background. Step fully out of view until calibration finishes, then come back."
            : "";
    }

    /// <summary>A tier change applies to perception at once: the plan reads its level each picture.</summary>
    private void ApplyCameraPerception()
    {
        if (_scenePerceiver is { } models)
        {
            models.Plan.Level = _budget.CameraPerception;
        }
    }

    /// <summary>
    /// A frame of the camera stage: the newest camera picture and scene, the sound as heard now and
    /// the effect's palette go to a drawing task; a frame still being drawn is let finish first.
    /// </summary>
    private void DrawCameraStage(VisualizerChoice choice, int rate, TimeSpan elapsed)
    {
        if (!_stageFrame.IsCompleted || _cameraStage is not { } stage || _cameraEffect is not { } effect || _stageGpu is not { } gpu)
        {
            return;
        }

        if (_stageFrame.IsFaulted)
        {
            _stageGpuFailed = true;
            App.Log.Error(LogSource, "The camera stage stopped drawing; its software stand-in is shown: " + _stageFrame.Exception?.InnerException?.Message);
            StopCameraStage();
            BuildVisualizer();
            return;
        }

        KeepCamera();
        if (_camera is { } camera)
        {
            camera.Sense ??= _senseWorker;
            var stamp = camera.CopyFull(ref _stagePicture, out var width, out var height);
            if (stamp != 0 && stamp != _stageCameraStamp)
            {
                if (_stageCamera is null || _stageCamera.Bgra != _stagePicture)
                {
                    _stageCamera = new VisualPicture(_stagePicture, width, height);
                }

                _stageCameraStamp = stamp;
            }
        }

        _senseWorker?.TryTake(_stageScene, _stageScene.Timestamp);
        var accent = AccentArgb();
        var palette = (VisualPalette)VisualizerOptions.Choice(_settings.VisualOptions, choice, VisualizerOptions.Colors);
        var own = VisualizerOptions.Rgb(_settings.VisualOptions, choice, VisualizerOptions.Color);
        var seconds = _player.Position.TotalSeconds;
        for (var i = 0; i < _stagePalette.Length; i++)
        {
            _stagePalette[i] = Rgb.From(Visualizers.PaletteColor(palette, i / 3.0, seconds, accent, own));
        }

        var gain = (float)VisualizerOptions.Number(_settings.VisualOptions, choice, VisualizerOptions.Sensitivity);
        for (var i = 0; i < _stageMono.Length; i++)
        {
            _stageMono[i] = (VisualStage.SafeSample(_soundLeft[i]) + VisualStage.SafeSample(_soundRight[i])) * 0.5f;
        }

        var picture = _stageCamera;
        var stampNow = _stageCameraStamp;
        _stageFrame = Task.Run(() =>
        {
            _stagePulse.Sensitivity = gain;
            _stagePulse.Update(_stageMono, rate, elapsed);
            stage.Draw(effect, picture, stampNow, _stageScene, _stagePulse, _stagePalette, seconds, elapsed.TotalSeconds, mirror: true);
            gpu.Present(false);
        });
    }

    /// <summary>Lets the panel, the stage, perception and the camera's stage mode go, once the frame in hand is drawn.</summary>
    private void StopCameraStage()
    {
        CameraStagePanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        if (_camera is { } camera)
        {
            camera.Sense = null;
        }

        var (gpu, stage, worker, models) = (_stageGpu, _cameraStage, _senseWorker, _senseModels);
        (_stageGpu, _cameraStage, _senseWorker, _senseModels, _scenePerceiver, _cameraEffect, _cameraEffectFor) = (null, null, null, null, null, null, null);
        (_stageCamera, _stageCameraStamp, _stagePixels) = (null, 0, default);
        _stageScene.Clear();
        _stagePulse.Reset();
        var frame = _stageFrame;
        _stageFrame = Task.CompletedTask;
        if (gpu is null && worker is null)
        {
            return;
        }

        try
        {
            // The window waits for the frame in hand at most briefly; the device is let go after it.
            frame.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // A failed last frame changes nothing about letting go.
        }

        worker?.Dispose();
        models?.Dispose();
        stage?.Dispose();
        if (gpu is not null)
        {
            var panel = WinRT.MarshalInspectable<object>.FromManaged(CameraStagePanel);
            try
            {
                SwapChainPanelNative.SetSwapChain(panel, 0);
            }
            finally
            {
                Marshal.Release(panel);
            }

            gpu.Dispose();
        }
    }
}
