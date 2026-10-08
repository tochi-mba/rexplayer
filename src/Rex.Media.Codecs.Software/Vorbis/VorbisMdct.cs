// Spec: Vorbis I specification, section 1.3.2 and 4.3.5 (the inverse MDCT: y[n] = sum over k of X[k] cos(2 pi / N (n + 1/2 + N/4)(k + 1/2))), computed through a DCT-IV of half the size and a complex FFT of a quarter.
namespace Rex.Media.Codecs.Software.Vorbis;

/// <summary>
/// The inverse modified discrete cosine transform of one block size: N/2 spectral values become N
/// samples. The DCT-IV inside it is worked out with a complex FFT of N/4 points, so a block costs
/// O(N log N) rather than O(N²).
/// </summary>
internal sealed class VorbisMdct
{
    private readonly int _n;
    private readonly int _half;
    private readonly int _quarter;
    private readonly float[] _preCos;
    private readonly float[] _preSin;
    private readonly float[] _postCos;
    private readonly float[] _postSin;
    private readonly float[] _twiddleCos;
    private readonly float[] _twiddleSin;
    private readonly int[] _reverse;
    private readonly float[] _re;
    private readonly float[] _im;
    private readonly float[] _u;

    public VorbisMdct(int n)
    {
        if (n < 16 || (n & (n - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(n), "The block size is a power of two of at least 16.");
        }

        _n = n;
        _half = n / 2;
        _quarter = n / 4;
        _preCos = new float[_quarter];
        _preSin = new float[_quarter];
        _postCos = new float[_quarter];
        _postSin = new float[_quarter];
        for (var i = 0; i < _quarter; i++)
        {
            var pre = Math.PI * i / _half;
            (_preCos[i], _preSin[i]) = ((float)Math.Cos(pre), (float)Math.Sin(pre));
            var post = Math.PI * (i + 0.25) / _half;
            (_postCos[i], _postSin[i]) = ((float)Math.Cos(post), (float)Math.Sin(post));
        }

        _twiddleCos = new float[_quarter / 2];
        _twiddleSin = new float[_quarter / 2];
        for (var i = 0; i < _quarter / 2; i++)
        {
            var angle = -2 * Math.PI * i / _quarter;
            (_twiddleCos[i], _twiddleSin[i]) = ((float)Math.Cos(angle), (float)Math.Sin(angle));
        }

        _reverse = new int[_quarter];
        var bits = System.Numerics.BitOperations.Log2((uint)_quarter);
        for (var i = 0; i < _quarter; i++)
        {
            var r = 0;
            for (var b = 0; b < bits; b++)
            {
                r |= ((i >> b) & 1) << (bits - 1 - b);
            }

            _reverse[i] = r;
        }

        _re = new float[_quarter];
        _im = new float[_quarter];
        _u = new float[_half];
    }

    /// <summary>Transforms <paramref name="spectrum"/> (N/2 values) into <paramref name="output"/> (N samples).</summary>
    public void Inverse(ReadOnlySpan<float> spectrum, Span<float> output)
    {
        // DCT-IV of size M = N/2: pack pairs into complex values, twist, transform, twist back.
        var m = _half;
        for (var i = 0; i < _quarter; i++)
        {
            var re = spectrum[2 * i];
            var im = spectrum[m - 1 - (2 * i)];
            var c = _preCos[i];
            var s = _preSin[i];
            var at = _reverse[i];
            _re[at] = (re * c) + (im * s);
            _im[at] = (im * c) - (re * s);
        }

        Fft();
        for (var i = 0; i < _quarter; i++)
        {
            var c = _postCos[i];
            var s = _postSin[i];
            var re = (_re[i] * c) + (_im[i] * s);
            var im = (_im[i] * c) - (_re[i] * s);
            _u[2 * i] = re;
            _u[m - 1 - (2 * i)] = -im;
        }

        // Unfold the DCT-IV into the N outputs by its symmetries.
        for (var i = 0; i < _n; i++)
        {
            var k = i + (m / 2);
            output[i] = k < m ? _u[k] : k < 2 * m ? -_u[(2 * m) - 1 - k] : -_u[k - (2 * m)];
        }
    }

    /// <summary>The inverse MDCT straight from its definition, for checking the fast one.</summary>
    internal static void Direct(ReadOnlySpan<float> spectrum, Span<float> output)
    {
        var n = output.Length;
        for (var i = 0; i < n; i++)
        {
            double sum = 0;
            for (var k = 0; k < n / 2; k++)
            {
                sum += spectrum[k] * Math.Cos(2 * Math.PI / n * (i + 0.5 + (n / 4.0)) * (k + 0.5));
            }

            output[i] = (float)sum;
        }
    }

    /// <summary>An in-place radix-2 FFT of the bit-reversed data in <see cref="_re"/> and <see cref="_im"/>.</summary>
    private void Fft()
    {
        for (var length = 2; length <= _quarter; length <<= 1)
        {
            var halfLength = length / 2;
            var step = _quarter / length;
            for (var start = 0; start < _quarter; start += length)
            {
                for (var k = 0; k < halfLength; k++)
                {
                    var c = _twiddleCos[k * step];
                    var s = _twiddleSin[k * step];
                    var a = start + k;
                    var b = a + halfLength;
                    var re = (_re[b] * c) - (_im[b] * s);
                    var im = (_re[b] * s) + (_im[b] * c);
                    _re[b] = _re[a] - re;
                    _im[b] = _im[a] - im;
                    _re[a] += re;
                    _im[a] += im;
                }
            }
        }
    }
}
