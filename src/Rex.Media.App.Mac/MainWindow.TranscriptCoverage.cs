using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Player;
using Rex.Media.AppCore.Transcription;

namespace Rex.Media.App.Mac;

/// <summary>
/// The transcript coverage drawn on the seek timeline: the stretches the portable subtitle buffer
/// reports as heard for what plays, or for the saved quality or edition shown as subtitles.
/// </summary>
public sealed partial class MainWindow
{
    private bool _macCoverageComplete;
    private TimeRanges? _macCoverageDone;
    private TimeSpan? _macCoverageDuration;
    private bool _macCoverageHearing;

    /// <summary>Forgets what was heard for the previous file, so its coverage never shows on the next.</summary>
    private void ResetMacCoverage()
    {
        (_macCoverageComplete, _macCoverageDone, _macCoverageDuration, _macCoverageHearing) = (false, null, null, false);
        ShowMacCoverage();
    }

    private void SetMacCoverage(bool complete, TimeRanges? done, TimeSpan? duration, bool hearing)
    {
        (_macCoverageComplete, _macCoverageDone, _macCoverageDuration, _macCoverageHearing) = (complete, done, duration, hearing);
        ShowMacCoverage();
    }

    /// <summary>Hands the slider the spans exactly as <see cref="SubtitleBuffer.View"/> reports them.</summary>
    private void ShowMacCoverage()
    {
        var spans = (IReadOnlyList<(double From, double To)>)[];
        var tip = "";
        if (_player.Item is { } item && !MediaFiles.IsPicture(item.Location))
        {
            var (done, duration, hearing) = DisplayedMacCoverage();
            var view = SubtitleBuffer.View(done, duration, _player.Position, hearing);
            if (SubtitleBuffer.RailShown(itemIsPicture: false, view.State, compact: false))
            {
                spans = view.Spans;
                tip = view.ToolTip;
            }
        }

        if (_seek.Coverage.SequenceEqual(spans)) return;
        _seek.Coverage = spans;
        ((NSView)_seek).AccessibilityHelp = tip.Length > 0 ? tip : null;
        _seek.NeedsDisplay = true;
    }

    /// <summary>The live quality's coverage, unless a saved variant is the subtitles shown.</summary>
    private (TimeRanges? Done, TimeSpan Duration, bool Hearing) DisplayedMacCoverage()
    {
        var shown = _player.Subtitles?.Name;
        var variant = shown is null ? null : TranscriptVariant.FromTrackName(shown);
        if (variant is { } named && (named.Model != MacModel.Id || named.Edited) &&
            _macSavedVariants.FirstOrDefault(saved => saved.Variant == named) is { } chosen)
        {
            var length = chosen.Duration ?? _player.Duration;
            return (chosen.Done ?? SubtitleBuffer.Coverage(true, null, requested: false, length), length, false);
        }

        var duration = _macCoverageDuration is { } known && known > TimeSpan.Zero ? known : _player.Duration;
        return (SubtitleBuffer.Coverage(_macCoverageComplete, _macCoverageDone, requested: false, duration), duration, _macCoverageHearing);
    }
}
