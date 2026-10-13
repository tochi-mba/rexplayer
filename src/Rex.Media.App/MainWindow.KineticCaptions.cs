using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Rex.Media.AppCore;
using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Player;
using Rex.Media.Codecs.MediaFoundation;
using Rex.Media.Engine;
using Rex.Media.Faces;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.Settings;
using Rex.Media.Subtitles;
using Rex.Media.Video;
using Windows.Foundation;

namespace Rex.Media.App;

/// <summary>
/// Kinetic captions (OSD-11): words that come in as they are spoken, sized, coloured and moved by
/// how they were said, placed clear of what matters in the picture. Every decision is AppCore's
/// <see cref="CaptionDirector"/>; this hears the sound, keeps time between position reports, reads
/// the picture ahead of what plays on a decoder of its own, and draws the words it is given with
/// XAML text, each laid out once and then only moved, scaled and faded.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>How far ahead of what plays the picture is read, in seconds.</summary>
    private const double PicturesAhead = 20;

    /// <summary>Pictures read a second ahead.</summary>
    private const double PicturesPerSecond = 3;

    /// <summary>How long after a frame is begun it shows, plus half the sound measured: the sound is read that far ahead.</summary>
    private static readonly TimeSpan CaptionSoundLead = TimeSpan.FromMilliseconds(16 + (1000.0 * SpeechPulse.Size / 48000 / 2));

    private readonly float[] _captionLeft = new float[SpeechPulse.Size];
    private readonly float[] _captionRight = new float[SpeechPulse.Size];
    private readonly float[] _captionMono = new float[SpeechPulse.Size];
    private readonly Dictionary<int, CaptionView> _captionViews = [];
    private readonly PictureTimeline _pictures = new();
    private CaptionDirector? _captionDirector;
    private CancellationTokenSource? _pictureScan;
    private string? _captionItem;
    private SubtitleTrack? _transcript;
    private bool _captioning;
    private bool _toldNoWords;
    private TimeSpan _captionLast;
    private TimeSpan _captionReported;
    private TimeSpan _captionReportedAt;
    private double _captionPlayhead;

    private void WireKineticCaptions()
    {
        _player.SubtitlesChanged += (_, _) => ApplyKineticCaptions();
    }

    /// <summary>
    /// Shows kinetic captions while a video plays with them on and words to show, and stops
    /// otherwise; the ordinary subtitles step aside unless the user keeps them.
    /// </summary>
    private void ApplyKineticCaptions()
    {
        var kinetic = _settings.KineticCaptions;
        var item = _player.Item?.Location;
        var playing = item is not null && HasVideo && _player.State is not (SessionState.Idle or SessionState.Faulted);
        if (item != _captionItem)
        {
            _captionItem = item;
            (_transcript, _heard, _toldNoWords) = (null, null, false);
            StopPictureScan();
            StopTranscribing();
            _pictures.Clear();
            if (item is not null && File.Exists(item))
            {
                LoadKeptTranscript(item);
            }
        }

        var words = HasCaptionWords();
        var show = kinetic.On && playing && words;
        if (kinetic.On && playing && !words && !_toldNoWords)
        {
            _toldNoWords = true;
            Say("No words for kinetic captions: add a subtitle file, or transcribe the speech (Subtitles menu).");
        }

        KineticLayer.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        SubtitleLayer.Visibility = show && !kinetic.KeepSubtitles ? Visibility.Collapsed : Visibility.Visible;
        if (show == _captioning)
        {
            return;
        }

        _captioning = show;
        if (show)
        {
            _captionDirector = new CaptionDirector(CaptionDirector.StyleFor(_settings));
            CompositionTarget.Rendering += OnCaptionRendering;
            if (kinetic.AvoidPicture && item is not null && File.Exists(item))
            {
                StartPictureScan(item);
            }
        }
        else
        {
            CompositionTarget.Rendering -= OnCaptionRendering;
            StopPictureScan();
            ClearCaptionViews();
            _captionDirector = null;
        }
    }

    /// <summary>Whether there are words to animate, from where the user chose.</summary>
    private bool HasCaptionWords() => _settings.KineticCaptions.Words switch
    {
        CaptionWordSource.Transcript => _transcript is not null,
        CaptionWordSource.SubtitlesTimedByTranscript or CaptionWordSource.Subtitles => _player.Subtitles is not null,
        _ => _player.Subtitles is not null || _transcript is not null,
    };

    /// <summary>The cues to animate at <paramref name="position"/>, from where the user chose.</summary>
    private IReadOnlyList<SubtitleCue> CaptionCues(TimeSpan position) => _settings.KineticCaptions.Words switch
    {
        CaptionWordSource.Transcript => _transcript?.At(position, _player.SubtitleDelay) ?? [],
        CaptionWordSource.Automatic when _player.Subtitles is null => _transcript?.At(position, _player.SubtitleDelay) ?? [],
        _ => _player.SubtitlesAt(position),
    };

    private void OnCaptionRendering(object? sender, object e)
    {
        if (_closed || _captionDirector is not { } director)
        {
            return;
        }

        var now = e is RenderingEventArgs rendering ? rendering.RenderingTime : TimeSpan.Zero;
        var elapsed = now - _captionLast;
        if (elapsed < TimeSpan.FromMilliseconds(15) && elapsed >= TimeSpan.Zero)
        {
            return;
        }

        _captionLast = now;
        elapsed = elapsed < TimeSpan.Zero || elapsed > TimeSpan.FromSeconds(0.5) ? TimeSpan.FromMilliseconds(16) : elapsed;
        var position = CaptionPosition(now);
        Volatile.Write(ref _captionPlayhead, position.TotalSeconds);

        var heard = _player.IsPlaying && _player.ReadSound(_captionLeft, _captionRight, CaptionSoundLead);
        if (heard)
        {
            for (var i = 0; i < _captionMono.Length; i++)
            {
                _captionMono[i] = (_captionLeft[i] + _captionRight[i]) / 2;
            }
        }

        var picture = CaptionPicture();
        var look = SubtitleLook.For(_settings, picture.Height);
        var words = director.Frame(
            position,
            heard ? _captionMono : [],
            _player.SoundSampleRate,
            elapsed,
            CaptionCues(position),
            new Area(0, 0, picture.Width, picture.Height),
            look.FontSize,
            _settings.KineticCaptions.AvoidPicture ? _pictures : null);
        DrawCaptions(words, picture, look);
        _pictures.Forget(position.TotalSeconds);
    }

    /// <summary>
    /// Where playback is now: the last position reported, moved on by the time since at the playing
    /// speed (reports come ten times a second; words land on syllables, which come faster).
    /// </summary>
    private TimeSpan CaptionPosition(TimeSpan now)
    {
        var reported = _player.Position;
        if (reported != _captionReported)
        {
            (_captionReported, _captionReportedAt) = (reported, now);
        }

        if (!_player.IsPlaying)
        {
            return reported;
        }

        var ahead = TimeSpan.FromTicks((long)((now - _captionReportedAt).Ticks * _player.Speed));
        return reported + (ahead < TimeSpan.Zero ? TimeSpan.Zero : ahead > TimeSpan.FromMilliseconds(250) ? TimeSpan.FromMilliseconds(250) : ahead);
    }

    /// <summary>Where the picture is on the stage, after the aspect ratio and crop.</summary>
    private Area CaptionPicture()
    {
        var (width, height) = (Stage.ActualWidth, Stage.ActualHeight);
        if (_player.Info?.FirstTrack(MediaKind.Video)?.Video is not { } video)
        {
            return new Area(0, 0, width, height);
        }

        var (_, across, down) = VideoGeometry.Shape(video.Width, video.Height, video.PixelAspect, _aspect.Ratio, _crop.Ratio);
        return SubtitleLook.Picture(width, height, across, down);
    }

    private void DrawCaptions(IReadOnlyList<CaptionWord> words, Area picture, SubtitleLook look)
    {
        var seen = new HashSet<int>();
        foreach (var word in words)
        {
            seen.Add(word.Id);
            var fontSize = Math.Max(8, look.FontSize * word.Settled);
            if (!_captionViews.TryGetValue(word.Id, out var view) || Math.Abs(view.FontSize - fontSize) > fontSize * 0.02 || view.Shown != word.Text)
            {
                if (view is not null)
                {
                    KineticLayer.Children.Remove(view.Root);
                }

                view = CaptionView.Build(word, fontSize, FontFor(word.Font, look.Font));
                _captionViews[word.Id] = view;
                KineticLayer.Children.Add(view.Root);
            }

            view.Show(word, picture, look.FontSize);
        }

        foreach (var gone in _captionViews.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            KineticLayer.Children.Remove(_captionViews[gone].Root);
            _captionViews.Remove(gone);
        }

        AutomationProperties.SetName(KineticLayer, string.Join(" ", words.Select(word => word.Text)));
    }

    private void ClearCaptionViews()
    {
        KineticLayer.Children.Clear();
        _captionViews.Clear();
    }

    /// <summary>Reads the picture ahead of what plays, on a decoder of its own, a few pictures a second.</summary>
    private void StartPictureScan(string location)
    {
        StopPictureScan();
        var scan = new CancellationTokenSource();
        _pictureScan = scan;
        var token = scan.Token;
        _ = Task.Run(() => ScanPictures(location, token), token).ContinueWith(
            task => App.Log.Debug(LogSource, "The picture was not read for captions: " + task.Exception?.GetBaseException().Message),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private void StopPictureScan()
    {
        _pictureScan?.Cancel();
        _pictureScan?.Dispose();
        _pictureScan = null;
    }

    private void ScanPictures(string location, CancellationToken token)
    {
        using var source = new FileByteSource(location);
        using var demuxer = MediaRegistries.Demuxers(TimeSpan.FromSeconds(_settings.PictureSeconds)).Open(source, token);
        if (demuxer.Info.FirstTrack(MediaKind.Video) is not { } track)
        {
            return;
        }

        var opened = MediaRegistries.Decoders(new MfDecoderFactory(), new WicDecoderFactory()).CreateVideo(track);
        using var decoder = opened.Decoder ?? throw new NotSupportedException(opened.Reason);
        var frames = new List<VideoFrame>();
        using var faces = FaceFinder.Create();
        PictureSpace? before = null;
        var next = double.NegativeInfinity;
        var from = double.NaN;
        while (!token.IsCancellationRequested)
        {
            var playhead = Volatile.Read(ref _captionPlayhead);
            if (double.IsNaN(from) || playhead < from - 1 || playhead > _pictures.ReadTo + 10)
            {
                // Started, or the viewer jumped away from what was read: read on from where they are.
                from = playhead;
                demuxer.Seek(MediaTime.FromSeconds(Math.Max(0, playhead)), token);
                decoder.Flush();
                (before, next) = (null, playhead);
            }

            if (_pictures.ReadTo > playhead + PicturesAhead)
            {
                token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(250));
                continue;
            }

            if (demuxer.ReadPacket(token) is not { } packet)
            {
                token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(500));
                continue;
            }

            using (packet)
            {
                if (packet.TrackId == track.Id)
                {
                    decoder.Decode(packet, frames);
                }
            }

            foreach (var frame in frames)
            {
                using (frame)
                {
                    var at = frame.Pts.IsKnown ? frame.Pts.ToTimeSpan().TotalSeconds : next;
                    if (at < next)
                    {
                        continue;
                    }

                    using var bgra = ColorConverter.ToBgra(frame);
                    var found = faces?.Find(bgra.Plane(0), bgra.Width, bgra.Height, bgra.Stride(0)).Select(face => new Area(face.X, face.Y, face.Width, face.Height)).ToList();
                    var space = PictureSpace.Read(bgra.Plane(0), bgra.Width, bgra.Height, bgra.Stride(0), before, faces: found);
                    _pictures.Add(at, space);
                    (before, next) = (space, at + (1 / PicturesPerSecond));
                }
            }

            frames.Clear();
        }
    }

    /// <summary>The window's kinetic caption commands; false for any other.</summary>
    private bool RunKineticCaptionCommand(string command)
    {
        switch (command)
        {
            case CommandCatalog.KineticCaptionSettings:
                _ = ShowKineticCaptionSettingsAsync();
                return true;
            case CommandCatalog.Transcribe or CommandCatalog.SaveTranscript:
                RunTranscriptCommand(command);
                return true;
        }

        if (CaptionCommands.Apply(command, _settings.KineticCaptions) is not { } change)
        {
            return false;
        }

        SetKineticCaptions(change.Settings);
        Say(change.Message);
        return true;
    }

    private void SetKineticCaptions(KineticCaptionSettings kinetic)
    {
        _settings = _settings with { KineticCaptions = kinetic };
        _player.Settings = _settings;
        _captionDirector?.Restyle(CaptionDirector.StyleFor(_settings));
        ClearCaptionViews();
        _toldNoWords = false;
        ApplyKineticCaptions();
        RememberLater();
    }

    /// <summary>Every kinetic caption choice in one place, each showing at once behind the dialog; Cancel puts them back.</summary>
    private async Task ShowKineticCaptionSettingsAsync()
    {
        var before = _settings.KineticCaptions;
        var k = before with { On = true };
        var on = Check("Kinetic captions on", true);
        var presets = Enum.GetValues<CaptionPreset>().ToList();
        var preset = Choice("Look", [.. presets.Select(CaptionPresets.Name)], presets.IndexOf(k.Preset));
        AutomationProperties.SetAutomationId(preset, "CaptionPreset");
        var about = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 440, Text = CaptionPresets.Describe(k.Preset), Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] };
        var font = Choice("Typeface (custom look)", ["Sans", "Serif", "Monospace", "Display", "Rounded"], (int)k.Font);
        var wordCase = Choice("Case (custom look)", ["As said", "Lower case", "Capitals"], (int)k.Case);
        var motion = Choice("Arrival (custom look)", ["Pop", "Slide", "Typewriter", "Bounce", "Drop"], (int)k.Motion);
        var highlight = Check("Only the word being said is bright (custom look)", k.Highlight);
        var flicker = Check("A shout flickers as it lands (custom look)", k.Flicker);
        var tilt = Number("Tilt (%, custom look)", k.Tilt, 0, 300);
        var outline = Number("Outline (%, custom look)", k.Outline, 0, 200);
        var plate = Check("A dark plate behind every word (custom look)", k.PlateAlways);
        var words = Choice("Words from", [.. Enum.GetValues<CaptionWordSource>().Select(CaptionCommands.WordsName)], (int)k.Words);
        var layout = Choice("Layout", [.. Enum.GetValues<CaptionLayoutChoice>().Select(CaptionCommands.LayoutName)], (int)k.Layout);
        var colors = Choice("Colours for loud words", [.. Enum.GetNames<CaptionColors>()], (int)k.Colors);
        var energy = Number("Energy (%): how far the voice moves the words", k.Energy, KineticCaptionSettings.LeastEnergy, KineticCaptionSettings.MostEnergy);
        var (accent, accentValue) = ColorChoice("Accent colour", k.Accent);
        var avoid = Check("Keep words off faces, text and movement in the picture", k.AvoidPicture);
        var glyphs = Check("Small pictures beside some words", k.Glyphs);
        var capitals = Check("Shouted words in capitals", k.ShoutsInCapitals);
        var keep = Check("Keep the ordinary subtitles as well", k.KeepSubtitles);
        var model = Choice("Transcription model", ["Tiny (fastest)", "Base", "Small (hears best)"], (int)k.TranscriptModel);
        var language = Words("Spoken language (two letters, such as \"en\"; empty to follow the audio)", k.TranscriptLanguage);

        KineticCaptionSettings Read() => (before with
        {
            On = on.IsChecked == true,
            Preset = presets[Math.Max(0, preset.SelectedIndex)],
            Font = (CaptionFontChoice)Math.Max(0, font.SelectedIndex),
            Case = (CaptionCaseChoice)Math.Max(0, wordCase.SelectedIndex),
            Motion = (CaptionMotionChoice)Math.Max(0, motion.SelectedIndex),
            Highlight = highlight.IsChecked == true,
            Flicker = flicker.IsChecked == true,
            Tilt = double.IsFinite(tilt.Value) ? (int)tilt.Value : before.Tilt,
            Outline = double.IsFinite(outline.Value) ? (int)outline.Value : before.Outline,
            PlateAlways = plate.IsChecked == true,
            Words = (CaptionWordSource)Math.Max(0, words.SelectedIndex),
            Layout = (CaptionLayoutChoice)Math.Max(0, layout.SelectedIndex),
            Colors = (CaptionColors)Math.Max(0, colors.SelectedIndex),
            Energy = double.IsFinite(energy.Value) ? (int)energy.Value : before.Energy,
            Accent = accentValue(),
            AvoidPicture = avoid.IsChecked == true,
            Glyphs = glyphs.IsChecked == true,
            ShoutsInCapitals = capitals.IsChecked == true,
            KeepSubtitles = keep.IsChecked == true,
            TranscriptModel = (TranscriptModelChoice)Math.Max(0, model.SelectedIndex),
            TranscriptLanguage = language.Text,
        }).Normalize();

        void Preview()
        {
            var chosen = presets[Math.Max(0, preset.SelectedIndex)];
            about.Text = CaptionPresets.Describe(chosen);
            foreach (var custom in new FrameworkElement[] { font, wordCase, motion, highlight, flicker, tilt, outline, plate, layout, colors, accent, capitals })
            {
                custom.Visibility = chosen == CaptionPreset.Custom ? Visibility.Visible : Visibility.Collapsed;
            }

            SetKineticCaptions(Read());
        }

        foreach (var box in new[] { preset, font, wordCase, motion, words, layout, colors, accent, model })
        {
            box.SelectionChanged += (_, _) => Preview();
        }

        foreach (var check in new[] { on, highlight, flicker, plate, avoid, glyphs, capitals, keep })
        {
            check.Click += (_, _) => Preview();
        }

        foreach (var number in new[] { energy, tilt, outline })
        {
            number.ValueChanged += (_, _) => Preview();
        }

        var panel = new StackPanel { Spacing = 10, MinWidth = 440 };
        foreach (var control in new UIElement[] { on, preset, about, energy, words, avoid, glyphs, keep, font, wordCase, motion, layout, colors, accent, capitals, highlight, flicker, tilt, outline, plate, model, language })
        {
            panel.Children.Add(control);
        }

        Preview();
        var save = await Ask("Kinetic captions", new ScrollViewer { Content = panel, MaxHeight = 520, Padding = new Thickness(0, 0, 16, 0) }, "Save");
        SetKineticCaptions(save ? Read() : before);
        if (save)
        {
            SaveSettings();
        }
    }

    /// <summary>The typeface a kind of font is set in, the user's subtitle font standing in for sans.</summary>
    private static string FontFor(CaptionFont font, string subtitleFont) => font switch
    {
        CaptionFont.Serif => "Georgia",
        CaptionFont.Mono => "Consolas",
        CaptionFont.Display => "Impact",
        CaptionFont.Rounded => "Bahnschrift",
        _ => subtitleFont,
    };

    /// <summary>One word's XAML: a flash behind it, a dark plate, its outline and glow copies, the text and its glyph.</summary>
    private sealed class CaptionView
    {
        private readonly CompositeTransform _transform = new();
        private readonly Border _plate;
        private readonly Ellipse _flash;
        private readonly SolidColorBrush _plateBrush = new(Microsoft.UI.Colors.Black);

        private CaptionView(Grid root, Border plate, Ellipse flash, double fontSize, string text)
        {
            (Root, _plate, _flash, FontSize, Shown) = (root, plate, flash, fontSize, text);
            Root.RenderTransform = _transform;
            _plate.Background = _plateBrush;
        }

        public Grid Root { get; }

        public double FontSize { get; }

        public string Shown { get; }

        public static CaptionView Build(CaptionWord word, double fontSize, string font)
        {
            var family = new FontFamily(word.Serif ? "Georgia" : font);
            var weight = word.Bold ? FontWeights.Black : FontWeights.SemiBold;
            var text = new Grid();
            var outline = Math.Max(1.2, fontSize * 0.045 * (0.6 + word.Backing.Outline));
            if (word.Glow > 0)
            {
                AddCopies(text, word, fontSize, family, weight, word.Color with { A = (byte)(80 * word.Glow) }, outline * 2.4);
            }

            if (word.Split)
            {
                text.Children.Add(Text(word, fontSize, family, weight, new Argb(200, 255, 40, 40), -fontSize * 0.06, 0));
                text.Children.Add(Text(word, fontSize, family, weight, new Argb(200, 40, 240, 255), fontSize * 0.06, 0));
            }

            if (word.Backing.Outline > 0)
            {
                AddCopies(text, word, fontSize, family, weight, word.PlateLight ? new Argb(60, 0, 0, 0) : new Argb(230, 0, 0, 0), outline);
            }

            text.Children.Add(Text(word, fontSize, family, weight, word.Color, 0, 0));
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = fontSize * 0.2 };
            line.Children.Add(text);
            if (word.Glyph is { } glyph)
            {
                line.Children.Add(new TextBlock { Text = glyph, FontSize = fontSize * 0.9, FontFamily = new FontFamily("Segoe UI Emoji"), VerticalAlignment = VerticalAlignment.Center, IsTextScaleFactorEnabled = false });
            }

            var plate = new Border { CornerRadius = new CornerRadius(fontSize * (word.PlateLight ? 0.3 : 0.18)), Padding = new Thickness(fontSize * 0.14, 0, fontSize * 0.14, fontSize * 0.04), Child = line };
            var flash = new Ellipse { Fill = new SolidColorBrush(Microsoft.UI.Colors.White), Opacity = 0, IsHitTestVisible = false };
            var root = new Grid();
            root.Children.Add(flash);
            root.Children.Add(plate);
            root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = root.DesiredSize;
            flash.Margin = new Thickness(-size.Width * 0.25, -size.Height * 0.45, -size.Width * 0.25, -size.Height * 0.45);
            var view = new CaptionView(root, plate, flash, fontSize, word.Text);
            view._plateBrush.Color = word.PlateLight ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
            return view;
        }

        /// <summary>Places, scales, turns, shakes and fades the word as <paramref name="word"/> says, over <paramref name="picture"/>.</summary>
        public void Show(CaptionWord word, Area picture, double em)
        {
            var size = Root.DesiredSize;
            var t = Environment.TickCount64 / 1000.0;
            var shakeX = (word.Shake * em * Math.Sin(t * 61)) + (word.OffsetX * em);
            var shakeY = (word.Shake * em * Math.Cos(t * 47)) + (word.OffsetY * em);
            Canvas.SetLeft(Root, picture.X + word.X - (size.Width / 2) + shakeX);
            Canvas.SetTop(Root, picture.Y + word.Y - (size.Height / 2) + shakeY);
            var scale = word.Settled > 0 ? word.Size / word.Settled : 1;
            (_transform.CenterX, _transform.CenterY) = (size.Width / 2, size.Height / 2);
            (_transform.ScaleX, _transform.ScaleY, _transform.Rotation) = (scale, scale, word.Rotation);
            Root.Opacity = word.Opacity;
            _plateBrush.Opacity = word.Backing.Plate;
            _flash.Opacity = word.Flash * 0.85;
        }

        private static void AddCopies(Grid grid, CaptionWord word, double fontSize, FontFamily family, Windows.UI.Text.FontWeight weight, Argb color, double reach)
        {
            for (var i = 0; i < 8; i++)
            {
                var angle = Math.PI * i / 4;
                var copy = Text(word, fontSize, family, weight, color, reach * Math.Cos(angle), reach * Math.Sin(angle));
                AutomationProperties.SetAccessibilityView(copy, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
                grid.Children.Add(copy);
            }
        }

        private static TextBlock Text(CaptionWord word, double fontSize, FontFamily family, Windows.UI.Text.FontWeight weight, Argb color, double x, double y) => new()
        {
            Text = word.Text,
            FontFamily = family,
            FontSize = fontSize,
            FontWeight = weight,
            FontStyle = word.Italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(color.A, color.R, color.G, color.B)),
            IsTextScaleFactorEnabled = false,
            RenderTransform = x == 0 && y == 0 ? null : new TranslateTransform { X = x, Y = y },
        };
    }

}
