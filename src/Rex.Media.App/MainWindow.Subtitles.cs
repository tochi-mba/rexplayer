using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Player;
using Rex.Media.Primitives;
using Rex.Media.Subtitles;
using Rex.Media.Video;
using Windows.Storage.Pickers;

namespace Rex.Media.App;

/// <summary>
/// Subtitles over the picture. Which cues show, and how they look, is worked out in AppCore
/// (<see cref="PlayerController.SubtitlesAt"/>, <see cref="SubtitleLook"/>); this draws them with
/// XAML text, which shapes every script Windows can (OSD-09), outlining each cue with copies of
/// itself drawn a little to every side.
/// </summary>
public sealed partial class MainWindow
{
    private IReadOnlyList<SubtitleCue> _shownCues = [];
    private IReadOnlyList<SubtitleCue> _shownSecondary = [];
    private SubtitleLook? _look;

    private void WireSubtitles()
    {
        _player.SubtitlesChanged += (_, _) => ShowSubtitles(redraw: true);
        _player.PositionChanged += (_, _) => ShowSubtitles(redraw: false);
        Stage.SizeChanged += (_, _) => LayOutSubtitles();
        Controls.SizeChanged += (_, _) => LayOutSubtitles();
        Controls.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => LayOutSubtitles());
    }

    /// <summary>Shows the cues due now, drawing them again only when they changed.</summary>
    private void ShowSubtitles(bool redraw)
    {
        var position = _player.Position;
        var cues = _player.SubtitlesAt(position);
        var secondary = _player.SecondarySubtitlesAt(position);
        if (!redraw && cues.SequenceEqual(_shownCues) && secondary.SequenceEqual(_shownSecondary))
        {
            return;
        }

        (_shownCues, _shownSecondary) = (cues, secondary);
        DrawSubtitles();
    }

    /// <summary>
    /// Fits the subtitle layer to the picture (or the picture and its bars), keeps it clear of the
    /// controls floating over a full-screen picture, and sizes the text to the picture.
    /// </summary>
    private void LayOutSubtitles()
    {
        var (width, height) = (Stage.ActualWidth, Stage.ActualHeight);
        var picture = new Area(0, 0, width, height);
        if (_player.Info?.FirstTrack(MediaKind.Video)?.Video is { } video)
        {
            var (_, across, down) = VideoGeometry.Shape(video.Width, video.Height, video.PixelAspect, _aspect.Ratio, _crop.Ratio);
            picture = SubtitleLook.Picture(width, height, across, down);
        }

        var area = SubtitleLook.SubtitleArea(picture, height, _settings.SubtitlesInBars);
        var look = SubtitleLook.For(_settings, picture.Height);
        var underControls = IsFullScreen && Controls.Visibility == Visibility.Visible ? Controls.ActualHeight : 0;
        var below = Math.Max(height - area.Y - area.Height + look.Margin, underControls + 8);
        SubtitleLayer.Margin = new Thickness(area.X, area.Y + look.Margin, Math.Max(0, width - area.X - area.Width), Math.Max(0, below));
        var maxWidth = Math.Max(1, area.Width * 0.92);
        SubtitlesTop.MaxWidth = SubtitlesMiddle.MaxWidth = SubtitlesBottom.MaxWidth = maxWidth;
        if (look != _look)
        {
            _look = look;
            DrawSubtitles();
        }
    }

    private void DrawSubtitles()
    {
        SubtitlesTop.Children.Clear();
        SubtitlesMiddle.Children.Clear();
        SubtitlesBottom.Children.Clear();
        if (_look is not { } look)
        {
            return;
        }

        // The second track sits at the top, out of the main one's way.
        foreach (var cue in _shownSecondary)
        {
            SubtitlesTop.Children.Add(CueView(cue, look));
        }

        foreach (var cue in _shownCues)
        {
            var placement = look.PlacementOf(cue);
            (placement == SubtitlePlacement.Top ? SubtitlesTop : placement == SubtitlePlacement.Middle ? SubtitlesMiddle : SubtitlesBottom).Children.Add(CueView(cue, look));
        }

        var text = string.Join("\n", _shownSecondary.Concat(_shownCues).Select(cue => cue.Text));
        AutomationProperties.SetName(SubtitleLayer, text);
    }

    /// <summary>One cue: its shadow, its outline, then the text itself, in a box when the user wants one.</summary>
    private static FrameworkElement CueView(SubtitleCue cue, SubtitleLook look)
    {
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
        if (look.ShadowOffset > 0)
        {
            grid.Children.Add(CueText(cue, look, look.Shadow, look.ShadowOffset, look.ShadowOffset));
        }

        if (look.Outline > 0)
        {
            // More copies for a thicker line, so its curve has no gaps.
            var copies = look.Outline > 3 ? 16 : 8;
            for (var i = 0; i < copies; i++)
            {
                var angle = 2 * Math.PI * i / copies;
                grid.Children.Add(CueText(cue, look, look.OutlineColor, look.Outline * Math.Cos(angle), look.Outline * Math.Sin(angle)));
            }
        }

        var text = CueText(cue, look, null, 0, 0);
        AutomationProperties.SetAutomationId(text, "SubtitleText");
        grid.Children.Add(text);
        if (look.Box.A == 0)
        {
            return grid;
        }

        return new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = Brush(look.Box),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(look.FontSize * 0.3, 0, look.FontSize * 0.3, look.FontSize * 0.05),
            Child = grid,
        };
    }

    /// <summary>The cue's text, all in <paramref name="color"/> for a shadow or outline copy, or in its own colours.</summary>
    private static TextBlock CueText(SubtitleCue cue, SubtitleLook look, Argb? color, double x, double y)
    {
        var block = new TextBlock
        {
            FontFamily = new FontFamily(look.Font),
            FontSize = look.FontSize,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextReadingOrder = TextReadingOrder.DetectFromContent,
            IsTextScaleFactorEnabled = false,
        };
        if (x != 0 || y != 0)
        {
            block.RenderTransform = new TranslateTransform { X = x, Y = y };
            AutomationProperties.SetAccessibilityView(block, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        }

        for (var i = 0; i < cue.Lines.Count; i++)
        {
            if (i > 0)
            {
                block.Inlines.Add(new LineBreak());
            }

            foreach (var run in cue.Lines[i].Runs)
            {
                block.Inlines.Add(new Run
                {
                    Text = run.Text,
                    FontWeight = look.IsBold(run) ? FontWeights.Bold : FontWeights.Normal,
                    FontStyle = run.Italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal,
                    TextDecorations = run.Underline ? Windows.UI.Text.TextDecorations.Underline : Windows.UI.Text.TextDecorations.None,
                    Foreground = Brush(color ?? look.ColorOf(run)),
                });
            }
        }

        return block;
    }

    private static SolidColorBrush Brush(Argb color) => new(Windows.UI.Color.FromArgb(color.A, color.R, color.G, color.B));

    /// <summary>The window's subtitle commands; false for any other.</summary>
    private bool RunSubtitleCommand(string command)
    {
        switch (command)
        {
            case CommandCatalog.AddSubtitles:
                _ = PickSubtitlesAsync();
                return true;
            case CommandCatalog.SubtitlesBigger:
                SetSubtitleSize(SubtitleLook.StepSize(_settings.SubtitleSize, 1));
                return true;
            case CommandCatalog.SubtitlesSmaller:
                SetSubtitleSize(SubtitleLook.StepSize(_settings.SubtitleSize, -1));
                return true;
            case CommandCatalog.ResetSubtitleSize:
                SetSubtitleSize(100);
                return true;
            default:
                return false;
        }
    }

    private void SetSubtitleSize(int percent)
    {
        _settings = _settings with { SubtitleSize = percent };
        _player.Settings = _settings;
        LayOutSubtitles();
        Say(string.Create(CultureInfo.InvariantCulture, $"Subtitle size {percent}%"));
        RememberLater();
    }

    private async Task PickSubtitlesAsync()
    {
        if (_player.Item is null)
        {
            Say(PlayerController.NothingToSubtitle);
            return;
        }

        var picker = Prepared(new FileOpenPicker { SuggestedStartLocation = PickerLocationId.VideosLibrary, ViewMode = PickerViewMode.List });
        foreach (var extension in SubtitleSidecars.Extensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        if (await picker.PickSingleFileAsync() is { } file)
        {
            _player.AddSubtitles(file.Path);
        }
    }
}
