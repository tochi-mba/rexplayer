using AppKit;
using CoreGraphics;
using Foundation;
using Rex.Media.AppCore.Player;
using Rex.Media.Settings;
using Rex.Media.Subtitles;

namespace Rex.Media.App.Mac;

/// <summary>Native attributed subtitle rendering, including the shared text style and timed runs.</summary>
public sealed partial class MainWindow
{
    private PlayerSettings? _macRenderedSubtitleSettings;
    private nfloat _macRenderedSubtitleFontSize;

    private static NSColor MacSubtitleColour(Argb colour) =>
        NSColor.FromRgba(colour.R, colour.G, colour.B, colour.A);

    private static void RenderMacSubtitleLines(NSTextField label, IReadOnlyList<SubtitleCue> cues,
        TimeSpan position, SubtitleLook look, NSFont font, bool styleChanged)
    {
        // Preserve explicit per-run clocks, but do not fabricate timings for untimed text.
        var visibleLines = cues.SelectMany(cue => cue.Lines.Select(line =>
            line.Runs.Where(run => (run.At is null || run.At <= position - cue.Start) &&
                run.Text.Length > 0).ToArray())).Where(runs => runs.Length > 0).ToArray();
        var text = string.Join("\n", visibleLines.Select(line =>
            string.Concat(line.Select(run => run.Text))));
        if (label.StringValue == text && !styleChanged)
        {
            label.Hidden = text.Length == 0;
            return;
        }

        if (text.Length == 0)
        {
            label.StringValue = "";
            label.Hidden = true;
            return;
        }

        NSShadow? shadow = null;
        if (look.Shadow.A > 0 && look.ShadowOffset > 0)
        {
            shadow = new NSShadow
            {
                ShadowColor = MacSubtitleColour(look.Shadow),
                ShadowOffset = new CGSize((nfloat)look.ShadowOffset, -(nfloat)look.ShadowOffset),
                ShadowBlurRadius = (nfloat)Math.Max(1, look.ShadowOffset),
            };
        }

        try
        {
            using var styled = new NSMutableAttributedString("");
            for (var line = 0; line < visibleLines.Length; line++)
            {
                if (line > 0)
                {
                    using var separator = new NSAttributedString("\n");
                    styled.Append(separator);
                }

                foreach (var run in visibleLines[line])
                {
                    var runFont = look.IsBold(run)
                        ? NSFont.SystemFontOfSize(font.PointSize, NSFontWeight.Semibold)!
                        : font;
                    var attrs = new NSStringAttributes
                    {
                        Font = runFont,
                        ForegroundColor = MacSubtitleColour(look.ColorOf(run)),
                        StrokeColor = MacSubtitleColour(look.OutlineColor),
                        StrokeWidth = look.Outline > 0
                            ? -(float)(100 * look.Outline / Math.Max(1, (double)font.PointSize)) : 0,
                        Shadow = shadow,
                        BackgroundColor = look.Box.A > 0 ? MacSubtitleColour(look.Box) : null,
                        UnderlineStyle = look.RespectStyles && run.Underline
                            ? (int)NSUnderlineStyle.Single : (int)NSUnderlineStyle.None,
                    };
                    using var part = new NSAttributedString(run.Text, attrs);
                    styled.Append(part);
                }
            }

            // NSControl retains a copy. The temporary native attributed string is then released.
            label.AttributedStringValue = styled;
            label.Hidden = false;
        }
        finally
        {
            shadow?.Dispose();
        }
    }
}
