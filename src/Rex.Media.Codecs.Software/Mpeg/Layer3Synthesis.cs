// Spec: ISO/IEC 11172-3 clause 2.4.3.4.10 (alias reduction, IMDCT, windowing, overlap-add, frequency inversion) and Annex A figure A.2 with table 3-B.3 (synthesis subband filter).
namespace Rex.Media.Codecs.Software.Mpeg;

/// <summary>The hybrid filterbank's IMDCT half: 18 frequency lines of a subband to 36 windowed time samples.</summary>
internal static class Imdct
{
    private static readonly float[] LongCosines = BuildLong();
    private static readonly float[] ShortCosines = BuildShort();
    private static readonly float[][] Windows = BuildWindows();
    private static readonly float[] ShortWindow = [.. Enumerable.Range(0, 12).Select(i => (float)Math.Sin(Math.PI / 12 * (i + 0.5)))];

    /// <summary>A long block of type 0 (normal), 1 (start) or 3 (stop).</summary>
    public static void Long(ReadOnlySpan<float> input, int blockType, Span<float> output)
    {
        var window = Windows[blockType];
        for (var i = 0; i < 36; i++)
        {
            var sum = 0f;
            var row = i * 18;
            for (var k = 0; k < 18; k++)
            {
                sum += input[k] * LongCosines[row + k];
            }

            output[i] = sum * window[i];
        }
    }

    /// <summary>Three short blocks; the input holds them interleaved (line k of window w at 3k + w).</summary>
    public static void Short(ReadOnlySpan<float> input, Span<float> output)
    {
        output[..36].Clear();
        for (var w = 0; w < 3; w++)
        {
            for (var i = 0; i < 12; i++)
            {
                var sum = 0f;
                var row = i * 6;
                for (var k = 0; k < 6; k++)
                {
                    sum += input[(3 * k) + w] * ShortCosines[row + k];
                }

                output[6 + (6 * w) + i] += sum * ShortWindow[i];
            }
        }
    }

    private static float[] BuildLong()
    {
        var table = new float[36 * 18];
        for (var i = 0; i < 36; i++)
        {
            for (var k = 0; k < 18; k++)
            {
                table[(i * 18) + k] = (float)Math.Cos(Math.PI / 72 * ((2 * i) + 1 + 18) * ((2 * k) + 1));
            }
        }

        return table;
    }

    private static float[] BuildShort()
    {
        var table = new float[12 * 6];
        for (var i = 0; i < 12; i++)
        {
            for (var k = 0; k < 6; k++)
            {
                table[(i * 6) + k] = (float)Math.Cos(Math.PI / 24 * ((2 * i) + 1 + 6) * ((2 * k) + 1));
            }
        }

        return table;
    }

    private static float[][] BuildWindows()
    {
        var windows = new float[4][];
        for (var type = 0; type < 4; type++)
        {
            windows[type] = new float[36];
            for (var i = 0; i < 36; i++)
            {
                var normal = Math.Sin(Math.PI / 36 * (i + 0.5));
                windows[type][i] = (float)(type switch
                {
                    1 => i < 18 ? normal : i < 24 ? 1 : i < 30 ? Math.Sin(Math.PI / 12 * (i - 18 + 0.5)) : 0,
                    3 => i < 6 ? 0 : i < 12 ? Math.Sin(Math.PI / 12 * (i - 6 + 0.5)) : i < 18 ? 1 : normal,
                    _ => normal,
                });
            }
        }

        return windows;
    }
}

/// <summary>
/// The polyphase synthesis filterbank: each call turns one sample from each of the 32 subbands
/// into 32 output samples, keeping the 1024-value history the filter runs over.
/// </summary>
internal sealed class SynthesisFilter
{
    private static readonly float[] Matrix = BuildMatrix();
    private static readonly float[] Window = [.. Layer3Tables.Window.Select(k => k / 65536f)];

    private readonly float[] _history = new float[1024];
    private int _offset;

    public void Reset()
    {
        Array.Clear(_history);
        _offset = 0;
    }

    public void Process(ReadOnlySpan<float> subbands, Span<float> output)
    {
        _offset = (_offset - 64) & 1023;
        for (var i = 0; i < 64; i++)
        {
            var sum = 0f;
            var row = i * 32;
            for (var k = 0; k < 32; k++)
            {
                sum += Matrix[row + k] * subbands[k];
            }

            _history[(_offset + i) & 1023] = sum;
        }

        for (var j = 0; j < 32; j++)
        {
            var sum = 0f;
            for (var m = 0; m < 8; m++)
            {
                sum += _history[(_offset + (128 * m) + j) & 1023] * Window[(64 * m) + j];
                sum += _history[(_offset + (128 * m) + 96 + j) & 1023] * Window[(64 * m) + 32 + j];
            }

            output[j] = sum;
        }
    }

    private static float[] BuildMatrix()
    {
        var matrix = new float[64 * 32];
        for (var i = 0; i < 64; i++)
        {
            for (var k = 0; k < 32; k++)
            {
                matrix[(i * 32) + k] = (float)Math.Cos((16 + i) * ((2 * k) + 1) * Math.PI / 64);
            }
        }

        return matrix;
    }
}
