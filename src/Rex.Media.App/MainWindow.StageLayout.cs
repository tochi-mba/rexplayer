using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Rex.Media.AppCore.Player;
using Rex.Media.Settings;
using Windows.Foundation;

namespace Rex.Media.App;

/// <summary>
/// Arranges the stage while sound plays or nothing is open, as <see cref="StageLayout"/> decides: the
/// title block, the cover, the visualisation's band and the lyrics dock. It runs when the stage, the
/// title block, the lyrics status or the transport changes size, and when what plays changes; never
/// per frame.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The visualisation chosen for what plays now, kept so a resize need not look it up.</summary>
    private VisualizerChoice _visualizerChoice;

    /// <summary>A visualisation is wanted for what plays now; whether it shows depends on the room.</summary>
    private bool _visualizerWanted;

    /// <summary>The chosen visualisation is a software stage, which spans the stage's width.</summary>
    private bool _visualizerFullBleed;

    private Rect _stageClip;
    private bool? _titleBesideCover;

    private void WireStageLayout()
    {
        Idle.SizeChanged += (_, _) => LayOutStageContent();
        LyricsFooter.SizeChanged += (_, _) => LayOutStageContent();
        Controls.SizeChanged += (_, _) => LayOutStageContent();
    }

    /// <summary>
    /// The height of the stage left showing above the transport. A window too short for both lays the
    /// transport over the stage's foot. Full screen, the transport floats over the picture and comes
    /// and goes, so the stage keeps its whole height.
    /// </summary>
    private double StageShowing()
    {
        var height = Math.Max(0, Stage.ActualHeight);
        if (Grid.GetRow(Controls) != 2 || Controls.Visibility != Visibility.Visible || Controls.ActualHeight <= 0)
        {
            return height;
        }

        return Math.Clamp(Controls.TransformToVisual(Stage).TransformPoint(default).Y, 0, height);
    }

    /// <summary>Lays the stage out again from what it measures now.</summary>
    private void LayOutStageContent()
    {
        if (_closed)
        {
            return;
        }

        var width = Math.Max(0, Stage.ActualWidth);
        var showing = StageShowing();
        ClipStage(width, showing);
        var layout = StageLayout.Resolve(new StageLayoutInput(
            width,
            showing,
            HasCover: _shownCover is not null && CoverImage.Source is not null,
            LyricsShown: LyricsBox.Visibility == Visibility.Visible,
            VisualizerWanted: _visualizerWanted,
            FullBleed: _visualizerFullBleed,
            TitleHeight: Idle.ActualHeight,
            FooterHeight: LyricsFooter.ActualHeight));

        // Moving elements, never replacing them, keeps keyboard focus and every AutomationId.
        Grid.SetColumn(IdleText, layout.Compact ? 0 : 1);
        Grid.SetRow(IdleText, layout.Compact ? 1 : 0);
        Idle.ColumnDefinitions[1].Width = layout.Compact ? new GridLength(0) : GridLength.Auto;
        Idle.ColumnSpacing = layout.ColumnSpacing;
        Idle.RowSpacing = layout.RowSpacing;
        Idle.Margin = new Thickness(layout.Inset, layout.Inset, layout.Inset, layout.BottomInset);
        Idle.VerticalAlignment = layout.TitleAtTop ? VerticalAlignment.Top : VerticalAlignment.Center;
        IdleText.MaxWidth = layout.TextMaxWidth;
        IdleTitle.FontSize = layout.TitleSize;
        CoverBox.Width = layout.CoverSize;
        CoverBox.Height = layout.CoverSize;
        // Decoding is asynchronous: only this item's decoded cover shows, never a stale one.
        CoverBox.Visibility = layout.CoverShown ? Visibility.Visible : Visibility.Collapsed;
        AlignTitleBlock(layout.TextBesideCover);
        // An empty line (a song with no artist) takes no room. The tiniest welcome keeps its
        // title and its buttons; the hint and the tagline give way, rather than the buttons.
        var welcome = _player.Item is null;
        IdleHint.Visibility = IdleHint.Text.Length == 0 || (welcome && layout.Tiny) ? Visibility.Collapsed : Visibility.Visible;
        WelcomeTagline.Visibility = layout.Tiny ? Visibility.Collapsed : Visibility.Visible;
        WelcomeActions.Margin = new Thickness(0, layout.Tiny ? 4 : 18, 0, 0);

        LyricsDock.Margin = new Thickness(layout.DockSide, 0, layout.DockSide, layout.DockBottom);
        LyricsBox.Height = layout.LyricsHeight;
        LyricsFooter.Visibility = layout.FooterShown ? Visibility.Visible : Visibility.Collapsed;
        Visualizer.Margin = new Thickness(layout.Band.Left, layout.Band.Top, layout.Band.Right, layout.Band.Bottom);
        ShowVisualizer(layout.VisualizerShown);
    }

    /// <summary>Keeps everything on the stage, a visualisation or the lyrics, off the transport.</summary>
    private void ClipStage(double width, double showing)
    {
        var clip = new Rect(0, 0, width, showing);
        if (clip != _stageClip)
        {
            _stageClip = clip;
            Stage.Clip = new RectangleGeometry { Rect = clip };
        }
    }

    /// <summary>Beside a cover the words line up with it; otherwise they are centred.</summary>
    private void AlignTitleBlock(bool besideCover)
    {
        if (_titleBesideCover == besideCover)
        {
            return;
        }

        _titleBesideCover = besideCover;
        var alignment = besideCover ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        var textAlignment = besideCover ? TextAlignment.Left : TextAlignment.Center;
        foreach (var text in new[] { IdleKicker, IdleTitle, IdleHint, IdleAlbum, IdleMeta })
        {
            text.HorizontalAlignment = alignment;
            text.TextAlignment = textAlignment;
        }
    }

    /// <summary>Shows or hides the visualisation, which is drawn only while it shows.</summary>
    private void ShowVisualizer(bool show)
    {
        Visualizer.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show && _builtFor != _visualizerChoice)
        {
            BuildVisualizer();
        }

        if (show == _visualizing)
        {
            return;
        }

        _visualizing = show;
        if (show)
        {
            WakeVisualizer();
        }
        else
        {
            StopVisualizing();
        }
    }

    /// <summary>The row of lyrics actions takes room only while one of them shows.</summary>
    private void ShowLyricsActions() => LyricsActions.Visibility =
        LyricsDetachButton.Visibility == Visibility.Visible || FindLyricsButton.Visibility == Visibility.Visible
        || SaveLyricsButton.Visibility == Visibility.Visible || LyricsFollowButton.Visibility == Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;
}
