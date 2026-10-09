namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// The waveform mirrored about the middle (AU-18): a soft column of light for each slice of the
/// newest sound, edged by a bright line, the left channel above and the right below when they are
/// apart, the last frames lingering as echoes that widen away.
/// </summary>
public sealed class MirrorScene : VisualScene
{
    public override void Draw(VisualContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var canvas = context.Canvas;
        var (w, h) = (canvas.Width, canvas.Height);
        var mid = h / 2.0;
        canvas.Feedback((float)context.Number("trails"), 1.0 + (context.Pulse.Bass * 0.02), 0, 0, 0);
        var columns = (int)context.Number("columns");
        var stereo = context.Toggle("stereo");
        var top = new List<(double, double)>(columns);
        var bottom = new List<(double, double)>(columns);
        for (var c = 0; c < columns; c++)
        {
            var x = (c + 0.5) * w / columns;
            var (above, below) = (Loudest(context.Left, c, columns), Loudest(context.Right, c, columns));
            if (!stereo)
            {
                above = below = (above + below) / 2;
            }

            var (up, down) = (Math.Max(1, above * h * 0.46), Math.Max(1, below * h * 0.46));
            var color = context.Paint((double)c / columns);
            canvas.Line(x, mid - up, x, mid + down, Math.Max(1, w / columns * 0.55), color, 0.32f);
            top.Add((x, mid - up));
            bottom.Add((x, mid + down));
        }

        var edge = context.Paint(context.Pulse.Pitch);
        canvas.Path(top, context.Number("thickness"), edge, 0.9f, 1.5);
        canvas.Path(bottom, context.Number("thickness"), edge, 0.9f, 1.5);
        canvas.Line(0, mid, w, mid, 1, edge, 0.15f + context.Pulse.BeatStrength * 0.4f);
        canvas.Bloom(0.4f, 0.7f, 4);
    }

    /// <summary>The loudest sample of slice <paramref name="column"/> of <paramref name="columns"/>.</summary>
    private static float Loudest(float[] samples, int column, int columns)
    {
        var (from, to) = (column * samples.Length / columns, (column + 1) * samples.Length / columns);
        var loudest = 0f;
        for (var i = from; i < to; i++)
        {
            loudest = Math.Max(loudest, Math.Abs(samples[i]));
        }

        return loudest;
    }
}

