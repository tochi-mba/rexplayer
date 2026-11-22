using System.Diagnostics;
using Rex.Media.AppCore.Performance;
using Rex.Media.AppCore.Player;
using Rex.Media.AppCore.Visuals;
using Rex.Media.Audio;
using Rex.Media.Settings;

namespace Rex.Media.App.Mac;

/// <summary>
/// The music stage's visualisation: building and pacing the frames, reading the sound, drawing the
/// classic shapes (spectrum bars, level meters, oscilloscope and spectrogram) and the software
/// stage's scenes away from the window, the camera feed for the scenes that use it, and cycling
/// from one visualisation to the next.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The spectrogram's picture: a column a frame (about ten seconds across), a row for each of as many bands.</summary>
    private const int MusicSpectrogramColumns = 320;
    private const int MusicSpectrogramRows = 256;

    /// <summary>
    /// How long after a frame is begun it is on screen, beyond the frame itself: the window's
    /// composition, and the middle of the few milliseconds the levels are measured over. The sound is
    /// read that far ahead, so the picture moves with what is heard as it shows.
    /// </summary>
    private static readonly TimeSpan MusicShownAfter = TimeSpan.FromMilliseconds(16 + (MusicPulse.Window * 1000 / 2));

    /// <summary>
    /// The drawing budget of an ordinary computer, as the Windows stage has it before any slower tier
    /// applies: canvases up to full HD across, and a frame at most every 15 ms.
    /// </summary>
    private static readonly DeviceBudget MusicBudget = new();

    private bool _musicStageFailed;
    private long _musicLastDrawn;
    private readonly long _musicEpoch = Stopwatch.GetTimestamp();
    private readonly VisualPacing _musicPacing = new();
    private readonly byte[] _musicSpectrogramPixels = new byte[MusicSpectrogramColumns * MusicSpectrogramRows * 4];
    private readonly float[] _musicMono = new float[SpectrumAnalyzer.Size];
    private readonly float[] _musicLeft = new float[VisualStage.SoundSize];
    private readonly float[] _musicRight = new float[VisualStage.SoundSize];
    private readonly float[] _musicStageLeft = new float[VisualStage.SoundSize];
    private readonly float[] _musicStageRight = new float[VisualStage.SoundSize];
    private float[] _musicWave = [];
    private readonly VisualPicture _musicCameraPicture = new(
        new byte[MacCameraFeed.PictureWidth * MacCameraFeed.PictureHeight * 4],
        MacCameraFeed.PictureWidth, MacCameraFeed.PictureHeight);
    private readonly byte[] _musicCameraMask = new byte[MacCameraFeed.MaskWidth * MacCameraFeed.MaskHeight];

    /// <summary>
    /// Shows the chosen visualisation while music plays, and draws nothing otherwise: the stage's
    /// layout then gives it its band, or leaves it out of a stage too small for it.
    /// </summary>
    private void ApplyMusicVisualizer()
    {
        var choice = _settings.Visualizer;
        _musicVisualWanted = IsMusicPlaying() && choice != VisualizerChoice.Off;
        _musicVisualFullBleed = VisualScene.Draws(choice);
        if (_musicVisualizing && _musicBuiltFor != choice)
        {
            BuildMusicVisualizer();
        }

        if (_musicStage is not null)
        {
            _musicStage.NeedsLayout = true;
        }

        WakeMusicVisualizer();
    }

    /// <summary>Starts or stops drawing as the layout shows or leaves out the visualisation.</summary>
    private void ShowMusicVisualizer(bool show)
    {
        if (show && _musicBuiltFor != _settings.Visualizer)
        {
            BuildMusicVisualizer();
        }

        if (show == _musicVisualizing)
        {
            return;
        }

        _musicVisualizing = show;
        if (show)
        {
            WakeMusicVisualizer();
        }
        else
        {
            StopMusicVisualizing();
        }
    }

    /// <summary>
    /// Readies the chosen visualisation: the classic shapes, the spectrogram's history, or the
    /// software stage (kept, with what it has drawn, through resizes and setting changes).
    /// </summary>
    private void BuildMusicVisualizer()
    {
        if (_musicVisual is null || _musicBars is null)
        {
            return;
        }

        var choice = _settings.Visualizer;
        var changed = _musicBuiltFor != choice;
        _musicBuiltFor = choice;
        var scene = VisualScene.Draws(choice);
        var spectrogram = choice == VisualizerChoice.Spectrogram;
        if (changed)
        {
            _musicVisual.Clear();
            _musicBars.Clear();
        }

        _musicVisual.Stretches = spectrogram;
        _musicVisual.Hidden = !(scene || spectrogram);
        _musicBars.Hidden = scene || spectrogram || choice == VisualizerChoice.Off;
        if (_musicRecalibrate is { } recalibrate)
        {
            recalibrate.Hidden = choice != VisualizerChoice.Silhouette;
        }

        if (spectrogram && changed)
        {
            Array.Clear(_musicSpectrogramPixels);
        }

        if (!MacVisualizerNeedsCamera(choice))
        {
            StopMacVisualizerCamera();
        }

        WakeMusicVisualizer();
    }

    /// <summary>
    /// Draws again from the next frame, for at least <see cref="VisualPacing.Settle"/>: the music
    /// started, or the picture was rebuilt, resized or its settings changed while it was still.
    /// </summary>
    private void WakeMusicVisualizer()
    {
        _musicPacing.Wake();
        if (!_musicVisualizing || _musicFrameTimer is not null)
        {
            return;
        }

        // In the common modes, so the picture keeps moving while the window is resized or a menu is open.
        _musicFrameTimer = NSTimer.CreateRepeatingTimer(TimeSpan.FromMilliseconds(MusicBudget.VisualFrameMilliseconds), _ => DrawMusicFrame());
        NSRunLoop.Main.AddTimer(_musicFrameTimer, NSRunLoopMode.Common);
    }

    private void StopMusicFrames()
    {
        _musicFrameTimer?.Invalidate();
        _musicFrameTimer?.Dispose();
        _musicFrameTimer = null;
    }

    /// <summary>
    /// Stops drawing and lets the camera go. A frame still being drawn is cancelled and never
    /// shown; the stage is reset between frames, never during one, and let go once it is idle.
    /// </summary>
    private void StopMusicVisualizing()
    {
        StopMusicFrames();
        _musicVisualizing = false;
        _musicBands.Reset();
        _musicSpectrogramBands.Reset();
        _musicMeterLeft.Reset();
        _musicMeterRight.Reset();
        var cancellation = _musicFrameCancellation;
        _musicFrameCancellation = new CancellationTokenSource();
        cancellation.Cancel();
        if (_musicStageWork is { IsCompleted: false } work)
        {
            _ = work.ContinueWith(_ => cancellation.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        else
        {
            cancellation.Dispose();
        }

        _musicStageReset = true;
        StopMacVisualizerCamera();
        _musicBars?.Clear();
        ReleaseMusicVisualizerWhenIdle();
    }

    /// <summary>Lets the stage's canvas and the last picture go once no frame is using them; a later song builds them again.</summary>
    private void ReleaseMusicVisualizerWhenIdle()
    {
        if (_musicStageWork is { IsCompleted: false } work)
        {
            _ = work.ContinueWith(_ => OnWindowThread(ReleaseMusicVisualizerWhenIdle),
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return;
        }

        if (_musicVisualizing)
        {
            return;
        }

        _musicStageWork = null;
        _musicScene = null;
        _musicBuiltFor = null;
        _musicVisual?.Clear();
    }

    /// <summary>
    /// One tick of the visualisation. Sixty pictures a second while a scene draws quickly, so a beat
    /// shows within a frame of being heard; thirty otherwise, and for the shapes, which spares the
    /// processor. Paused or ended, the picture settles and then stops until the music plays again.
    /// </summary>
    private void DrawMusicFrame()
    {
        if (_musicStage is null || !_musicVisualizing || _musicBuiltFor is not { } choice)
        {
            StopMusicFrames();
            return;
        }

        var camera = MacVisualizerNeedsCamera(choice);
        var seen = _window.IsVisible && !_window.IsMiniaturized && _window.OcclusionState.HasFlag(NSWindowOcclusionState.Visible) && !LibraryShown;
        if (!seen || !_musicPacing.Draws(Stopwatch.GetElapsedTime(_musicEpoch), _player.IsPlaying, camera))
        {
            StopMusicFrames();
            return;
        }

        if (_musicStageWork is { IsCompleted: false })
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var elapsed = _musicLastDrawn == 0 ? TimeSpan.MaxValue : Stopwatch.GetElapsedTime(_musicLastDrawn, now);
        var fast = _musicScene is { DrawMilliseconds: < 12 } && VisualScene.Draws(choice);
        var interval = TimeSpan.FromMilliseconds(Math.Max(fast ? 15 : 30, MusicBudget.VisualFrameMilliseconds));
        if (elapsed < interval)
        {
            return;
        }

        _musicLastDrawn = now;
        elapsed = elapsed > TimeSpan.FromSeconds(1) ? interval : elapsed;
        if (!_player.ReadSound(_musicLeft, _musicRight, interval + MusicShownAfter))
        {
            Array.Clear(_musicLeft);
            Array.Clear(_musicRight);
        }

        var rate = Math.Max(1, _player.SoundSampleRate);
        if (VisualScene.Draws(choice))
        {
            DrawMusicStage(choice, rate, elapsed);
            return;
        }

        // Sensitivity: quiet music can fill the picture, loud music can be calmed. The shapes draw
        // the newest of the sound read.
        var gain = (float)MusicOption(choice, VisualizerOptions.Sensitivity);
        var skip = _musicLeft.Length - _musicMono.Length;
        for (var i = 0; i < _musicMono.Length; i++)
        {
            _musicMono[i] = (VisualStage.SafeSample(_musicLeft[skip + i] * gain) + VisualStage.SafeSample(_musicRight[skip + i] * gain)) / 2;
        }

        var accent = MusicAccent();
        var bars = _musicBars!;
        switch (choice)
        {
            case VisualizerChoice.Spectrum:
                var count = (int)MusicOption(choice, "bars");
                if (_musicBands.Levels.Count != count)
                {
                    _musicBands = new SpectrumAnalyzer(count);
                }

                _musicBands.Update(_musicMono, rate, elapsed);
                bars.Mirrored = MusicOptionOn(choice, "mirrored");
                bars.ShowsPeaks = MusicOptionOn(choice, "peaks");
                bars.ShowSpectrum(_musicBands.Levels, _musicBands.Peaks, i => MusicPaint(choice, (double)i / count, accent));
                break;
            case VisualizerChoice.Oscilloscope:
                // The newest twentieth of a second, drawn across the width.
                var span = Math.Min(_musicMono.Length, Math.Max(64, rate / 20));
                var steps = Math.Max(2, (int)Math.Min((double)bars.Bounds.Width / 2, span));
                if (_musicWave.Length != steps)
                {
                    _musicWave = new float[steps];
                }

                for (var step = 0; step < steps; step++)
                {
                    _musicWave[step] = _musicMono[_musicMono.Length - span + (step * (span - 1) / (steps - 1))];
                }

                bars.ShowWave(_musicWave, MusicPaint(choice, 0.5, accent), MusicOption(choice, "thickness"));
                break;
            case VisualizerChoice.Meters:
                // A meter shows what has just been heard: the newest twentieth of a second.
                var recent = Math.Min(_musicMono.Length, Math.Max(64, rate / 20));
                for (var i = _musicLeft.Length - recent; i < _musicLeft.Length; i++)
                {
                    _musicLeft[i] = VisualStage.SafeSample(_musicLeft[i] * gain);
                    _musicRight[i] = VisualStage.SafeSample(_musicRight[i] * gain);
                }

                _musicMeterLeft.Update(_musicLeft.AsSpan(^recent..), elapsed);
                _musicMeterRight.Update(_musicRight.AsSpan(^recent..), elapsed);
                bars.ShowMeter(0, _musicMeterLeft, MusicPaint(choice, 0, accent));
                bars.ShowMeter(1, _musicMeterRight, MusicPaint(choice, 0.5, accent));
                break;
            case VisualizerChoice.Spectrogram:
                _musicSpectrogramBands.Update(_musicMono, rate, elapsed);
                DrawMusicSpectrogramColumn(choice, accent);
                break;
        }
    }

    /// <summary>
    /// A frame of the software stage. Drawing happens away from the window, so the window never waits
    /// on it: each time round, the frame finished since is shown, and the next is begun with what is
    /// heard and seen now. The canvas is sized between frames, never during one.
    /// </summary>
    private void DrawMusicStage(VisualizerChoice choice, int rate, TimeSpan elapsed)
    {
        if (_musicVisual is null)
        {
            return;
        }

        var stage = _musicScene;
        if (stage is null)
        {
            var (across, down) = MusicStageSize(null);
            stage = _musicScene = new VisualStage(across, down);
            _musicStageReset = false;
        }
        else if (_musicStageReset)
        {
            stage.Reset();
            _musicStageReset = false;
        }
        else if (_musicStageWork is { IsCompletedSuccessfully: true } && stage.HasFrame)
        {
            _musicVisual.Show(stage.Pixels, stage.Canvas.Width, stage.Canvas.Height);
        }
        else if (_musicStageWork is { IsFaulted: true } failed && !_musicStageFailed)
        {
            _musicStageFailed = true;
            _log.Error(LogSource, "The visualisation could not be drawn: " + failed.Exception?.InnerException?.Message);
        }

        // Resized, moved to a screen of another scale, or drawing at a new quality to keep up: the
        // canvas is made again between frames, and the last picture stays up until the next is drawn.
        var wanted = MusicStageSize(stage);
        if (stage.WantsResize || (stage.Canvas.Width, stage.Canvas.Height) != wanted)
        {
            stage.Resize(wanted.Width, wanted.Height);
        }

        var context = stage.Context;
        context.Seconds = _player.Position.TotalSeconds;
        context.Progress = _player.Duration > TimeSpan.Zero
            ? Math.Clamp(_player.Position.TotalSeconds / _player.Duration.TotalSeconds, 0, 1) : 0;
        context.Accent = MusicAccent();
        context.Cover = _musicVisualCover;
        (context.Camera, context.Mask) = (null, null);
        if (MacVisualizerNeedsCamera(choice))
        {
            KeepMacVisualizerCamera();
            if (_musicCamera is { } camera)
            {
                // The silhouette cuts the user out at its own threshold.
                camera.Threshold = choice == VisualizerChoice.Silhouette ? MusicOption(choice, "threshold") : 0.12;
                if (camera.HasPicture)
                {
                    // Copied only between frames: the frame being drawn reads these buffers.
                    camera.CopyPicture(_musicCameraPicture.Bgra);
                    camera.CopyMask(_musicCameraMask);
                    context.Camera = _musicCameraPicture;
                    if (!camera.IsLearning)
                    {
                        (context.Mask, context.MaskWidth, context.MaskHeight) =
                            (_musicCameraMask, MacCameraFeed.MaskWidth, MacCameraFeed.MaskHeight);
                    }
                }
            }
        }
        else
        {
            StopMacVisualizerCamera();
        }

        // The sound and the settings as they are now go with the frame; the window keeps its own.
        _musicLeft.CopyTo(_musicStageLeft, 0);
        _musicRight.CopyTo(_musicStageRight, 0);
        var options = _settings.VisualOptions;
        var token = _musicFrameCancellation.Token;
        var (left, right) = (_musicStageLeft, _musicStageRight);
        _musicStageWork = Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            var started = Stopwatch.GetTimestamp();
            stage.Draw(choice, options, left, right, rate, elapsed, token);
            token.ThrowIfCancellationRequested();
            stage.Took(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }, token);
    }

    /// <summary>The canvas the stage wants now: the band's size in the screen's own pixels, at the quality the stage can keep up.</summary>
    private (int Width, int Height) MusicStageSize(VisualStage? stage)
    {
        var size = _musicVisual!.Bounds.Size;
        var scale = (double)(_musicVisual.Window?.BackingScaleFactor ?? _window.BackingScaleFactor);
        return VisualStage.SizeFor((double)size.Width, (double)size.Height, stage?.Quality ?? 1, scale, MusicBudget.VisualMaxSide);
    }

    /// <summary>Moves the spectrogram one column left and draws the newest on the right, low notes at the bottom.</summary>
    private void DrawMusicSpectrogramColumn(VisualizerChoice choice, Argb accent)
    {
        const int Stride = MusicSpectrogramColumns * 4;
        var palette = (VisualPalette)MusicOptionChoice(choice, VisualizerOptions.Colors);
        var own = VisualizerOptions.Rgb(_settings.VisualOptions, choice, VisualizerOptions.Color);
        var seconds = _player.Position.TotalSeconds;
        for (var row = 0; row < MusicSpectrogramRows; row++)
        {
            var start = row * Stride;
            Buffer.BlockCopy(_musicSpectrogramPixels, start + 4, _musicSpectrogramPixels, start, Stride - 4);
            var level = _musicSpectrogramBands.Levels[MusicSpectrogramRows - 1 - row];
            Argb color;
            if (palette == VisualPalette.Accent)
            {
                // Its own heat colours, unless asked for others.
                color = Visualizers.Heat(level);
            }
            else
            {
                var paint = Visualizers.PaletteColor(palette, (double)(MusicSpectrogramRows - 1 - row) / MusicSpectrogramRows, seconds, accent, own);
                color = new Argb(255, (byte)(paint.R * level), (byte)(paint.G * level), (byte)(paint.B * level));
            }

            var at = start + Stride - 4;
            (_musicSpectrogramPixels[at], _musicSpectrogramPixels[at + 1], _musicSpectrogramPixels[at + 2], _musicSpectrogramPixels[at + 3]) = (color.B, color.G, color.R, 255);
        }

        _musicVisual!.Show(_musicSpectrogramPixels, MusicSpectrogramColumns, MusicSpectrogramRows);
    }

    private double MusicOption(VisualizerChoice choice, string key) => VisualizerOptions.Number(_settings.VisualOptions, choice, key);

    private bool MusicOptionOn(VisualizerChoice choice, string key) => VisualizerOptions.Toggle(_settings.VisualOptions, choice, key);

    private int MusicOptionChoice(VisualizerChoice choice, string key) => VisualizerOptions.Choice(_settings.VisualOptions, choice, key);

    /// <summary>The visualisation's colour for <paramref name="share"/> (how far along the pitch, or the shapes), as its settings say.</summary>
    private CGColor MusicPaint(VisualizerChoice choice, double share, Argb accent)
    {
        var palette = (VisualPalette)MusicOptionChoice(choice, VisualizerOptions.Colors);
        var color = Visualizers.PaletteColor(palette, share, _player.Position.TotalSeconds, accent,
            VisualizerOptions.Rgb(_settings.VisualOptions, choice, VisualizerOptions.Color));
        return new CGColor((nfloat)(color.R / 255.0), (nfloat)(color.G / 255.0), (nfloat)(color.B / 255.0), 1);
    }

    /// <summary>
    /// The system accent as it reads on the always-dark stage: the dark appearance's own, which
    /// stays readable on black where the light one would be too deep.
    /// </summary>
    private static Argb MusicAccent()
    {
        var accent = new Argb(255, 96, 205, 255);
        NSAppearance.GetAppearance(NSAppearance.NameDarkAqua)?.PerformAsCurrentDrawingAppearance(() =>
        {
            if (MacTheme.Accent.UsingColorSpace(NSColorSpace.SRGBColorSpace) is { } color)
            {
                accent = new Argb(255, Channel(color.RedComponent), Channel(color.GreenComponent), Channel(color.BlueComponent));
            }
        });
        return accent;

        static byte Channel(nfloat value) => (byte)Math.Clamp(Math.Round((double)value * 255), 0, 255);
    }

    /// <summary>The cover as BGRA pixels at up to 400 across, for the scenes that show it; null when it will not draw.</summary>
    private static VisualPicture? MusicVisualPicture(NSImage image)
    {
        var rect = CGRect.Empty;
        using var source = image.AsCGImage(ref rect, null, null);
        if (source is null || source.Width <= 0 || source.Height <= 0)
        {
            return null;
        }

        var scale = Math.Min(1, 400.0 / Math.Max(source.Width, source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var pixels = new byte[width * height * 4];
        using var space = CGColorSpace.CreateSrgb();
        using var context = new CGBitmapContext(pixels, width, height, 8, width * 4, space,
            CGBitmapFlags.ByteOrder32Little | CGBitmapFlags.PremultipliedFirst);
        context.InterpolationQuality = CGInterpolationQuality.High;
        context.DrawImage(new CGRect(0, 0, width, height), source);
        return new VisualPicture(pixels, width, height);
    }

    private static bool MacVisualizerNeedsCamera(VisualizerChoice choice) => choice is
        VisualizerChoice.Silhouette or VisualizerChoice.Clones or VisualizerChoice.TimeWarp or
        VisualizerChoice.Aura or VisualizerChoice.LedWall or VisualizerChoice.Stardust or
        VisualizerChoice.HallOfMirrors or VisualizerChoice.LightPainting or
        VisualizerChoice.Scanner or VisualizerChoice.Shatter or VisualizerChoice.Spotlight or
        VisualizerChoice.InfinityMirror or VisualizerChoice.Water or VisualizerChoice.BeatEdit or
        // The graphics-card effects show their software stand-ins on a Mac; these two use the camera.
        VisualizerChoice.NeonSkeleton or VisualizerChoice.EchoClones;

    private void CycleMusicVisualizer()
    {
        var next = Visualizers.Next(_settings.Visualizer);

        _settings = _settings with { Visualizer = next };
        SaveMusicSettings();
        ApplyMusicVisualizer();
        Say("Audio visualisation: " + Visualizers.Name(next));
    }

    private void KeepMacVisualizerCamera()
    {
        if (!_settings.CameraAllowed || _musicCamera is not null) return;
        _musicCamera = new MacCameraFeed();
        _musicCamera.Start();
        if (_musicCamera.Problem is { } problem) _log.Warning(LogSource, problem);
    }

    private void StopMacVisualizerCamera()
    {
        _musicCamera?.Dispose();
        _musicCamera = null;
    }
}
