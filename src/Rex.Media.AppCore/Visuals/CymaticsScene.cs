namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// Sand on a vibrating plate (AU-18): the figures a plate sings into, its sand gathering on the
/// lines that stay still (E. Chladni's figures; for a square plate held at its middle, the still
/// lines of the mode (n, m) are where cos nπx cos mπy − cos mπx cos nπy is zero). The pitch picks
/// the mode, higher notes ringing finer figures; each beat throws the sand up and it settles into
/// the new figure; the bass sets the plate humming, blurring the lines as it shakes and glowing
/// under it.
/// </summary>
public sealed class CymaticsScene : VisualScene
{
    private static readonly (int N, int M)[] Modes =
    [
        (1, 2), (1, 3), (2, 3), (1, 4), (2, 5), (3, 4), (1, 6), (3, 5), (2, 7), (4, 5), (3, 7), (5, 6), (4, 7), (5, 8), (6, 7),
    ];

    private (int N, int M) _from = Modes[0];
    private (int N, int M) _to = Modes[0];
    private double _settle = 1;
    private float[] _cosN = [];
    private float[] _cosM = [];
    private float[] _fromN = [];
    private float[] _fromM = [];

    public override void Draw(VisualContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var canvas = context.Canvas;
        var pulse = context.Pulse;
        var (w, h) = (canvas.Width, canvas.Height);
        var side = Math.Min(w, h) * 0.92;
        var (left, top) = ((w - side) / 2, (h - side) / 2);
        var round = context.Pick("plate") == 1;

        // Each beat the sand jumps, and lands in the figure the pitch now rings.
        var detail = context.Number("detail");
        var mode = Modes[(int)Math.Clamp(Math.Round(pulse.Pitch * detail * (Modes.Length - 1)), 0, Modes.Length - 1)];
        if (pulse.Beat && mode != _to)
        {
            (_from, _to, _settle) = (_to, mode, 0);
        }

        _settle = Math.Min(1, _settle + (context.Dt / 0.35));
        var count = (int)Math.Ceiling(side) + 1;
        Table(ref _cosN, ref _cosM, _to, count);
        Table(ref _fromN, ref _fromM, _from, count);

        // The louder the plate hums, the more the sand dances off the lines.
        var spread = 0.04 + (0.1 * pulse.Bass) + (0.25 * (1 - _settle) * (1 - _settle));
        var light = (float)(context.Number("glow") * (0.35 + (0.9 * pulse.Loudness) + (0.6 * pulse.Punch)));
        var under = context.Paint(0.15).Times(0.05f + (0.25f * pulse.Bass));
        var settle = (float)_settle;
        canvas.Clear(Rgb.Black);
        Raster.Rows(h, y =>
        {
            var v = (int)(y - top);
            if (v < 0 || v >= count)
            {
                return;
            }

            for (var x = 0; x < w; x++)
            {
                var u = (int)(x - left);
                if (u < 0 || u >= count)
                {
                    continue;
                }

                var (du, dv) = ((2.0 * u / side) - 1, (2.0 * v / side) - 1);
                var radius = Math.Sqrt((du * du) + (dv * dv));
                if (round && radius > 1)
                {
                    continue;
                }

                var now = (_cosN[u] * _cosM[v]) - (_cosM[u] * _cosN[v]);
                var before = (_fromN[u] * _fromM[v]) - (_fromM[u] * _fromN[v]);
                var still = Math.Abs((now * settle) + (before * (1 - settle)));
                var sand = (float)Math.Exp(-(still * still) / (spread * spread));
                var color = context.Paint(Math.Min(1, radius * 0.8)).Times(sand * light).Plus(under);
                var i = (y * w) + x;
                (canvas.R[i], canvas.G[i], canvas.B[i]) = (color.R, color.G, color.B);
            }
        });

        canvas.Bloom(0.5f, 0.5f, 3);
    }

    /// <summary>cos nπu and cos mπu across the plate, from −1 to 1.</summary>
    private static void Table(ref float[] cosN, ref float[] cosM, (int N, int M) mode, int count)
    {
        if (cosN.Length != count)
        {
            (cosN, cosM) = (new float[count], new float[count]);
        }

        for (var i = 0; i < count; i++)
        {
            var u = ((2.0 * i / (count - 1)) - 1) * Math.PI;
            (cosN[i], cosM[i]) = ((float)Math.Cos(mode.N * u), (float)Math.Cos(mode.M * u));
        }
    }
}
