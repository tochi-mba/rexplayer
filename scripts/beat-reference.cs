// An offline reference beat tracker for scripts/measure-beats.ps1 (AU-18): reads a WAV capture and
// the rexplay pulse JSON of the same file at several frame rates, and scores those beats against its own.
// Run: dotnet run scripts/beat-reference.cs -- <capture.wav> <pulse.json>...
// Onsets: log-compressed spectral flux at 100 frames a second (Bello et al. 2005).
// Tempo: autocorrelation weighted by a log-Gaussian around 120 bpm, sigma 1.4 octaves (Ellis 2007).
// Beats: Ellis 2007 dynamic programming. Prints one line per pulse file, never a file name.
using System.Text.Json;

var (x, rate) = ReadWav(args[0]);
const double hopS = 0.01;
const int n = 2048;
var hop = (int)Math.Round(rate * hopS);
var frames = 1 + ((x.Length - n) / hop);
var win = new double[n];
for (var i = 0; i < n; i++) win[i] = 0.5 - (0.5 * Math.Cos(2 * Math.PI * i / (n - 1)));
var top = (int)(5000.0 * n / rate);
var env = new double[frames];
var times = new double[frames];
var prev = new double[top];
var re = new double[n];
var im = new double[n];
for (var f = 0; f < frames; f++)
{
    for (var i = 0; i < n; i++) { re[i] = x[(f * hop) + i] * win[i]; im[i] = 0; }
    Fft(re, im);
    double d = 0;
    for (var k = 1; k < top; k++)
    {
        var mag = Math.Log(1 + (100 * Math.Sqrt((re[k] * re[k]) + (im[k] * im[k]))));
        if (f > 0) d += Math.Max(0, mag - prev[k]);
        prev[k] = mag;
    }

    env[f] = d;
    times[f] = ((f * hop) + (n / 2.0)) / rate;
}

var fps = 1 / hopS;
var mean = env.Average();
var maxLag = (int)(fps * 60 / 40);
var lo = (int)(fps * 60 / 240);
var (bestLag, bestScore) = (0, double.NegativeInfinity);
for (var lag = lo; lag < maxLag; lag++)
{
    double ac = 0;
    for (var i = 0; i + lag < env.Length; i++) ac += (env[i] - mean) * (env[i + lag] - mean);
    ac /= env.Length - lag;
    var w = Math.Exp(-0.5 * Math.Pow(Math.Log2(lag / fps / 0.5) / 1.4, 2));
    if (ac * w > bestScore) (bestLag, bestScore) = (lag, ac * w);
}

var bpm = 60 * fps / bestLag;
var beats = DpBeats(env, bestLag).Select(i => times[i]).ToArray();
foreach (var file in args.Skip(1))
{
    using var doc = JsonDocument.Parse(File.ReadAllText(file));
    var d = doc.RootElement.GetProperty("data");
    var est = d.GetProperty("beats").EnumerateArray().Select(b => b.GetProperty("at").GetDouble()).ToArray();
    var lockedAt = d.GetProperty("lockedAt").ValueKind == JsonValueKind.Number ? d.GetProperty("lockedAt").GetDouble() : 0;
    var from = Math.Max(lockedAt, 8);
    var refB = beats.Where(b => b > from).ToArray();
    var e = est.Where(b => b > from).ToArray();
    var (fm, offsets) = FMeasure(e, refB);
    var half = beats.Zip(beats.Skip(1), (a, b) => (a + b) / 2).Where(b => b > from).ToArray();
    var foff = FMeasure(e, half).F;
    var dbl = refB.Concat(half).Order().ToArray();
    var fdbl = FMeasure(e, dbl).F;
    var fhalf = Math.Max(FMeasure(e, refB.Where((_, i) => i % 2 == 0).ToArray()).F, FMeasure(e, refB.Where((_, i) => i % 2 == 1).ToArray()).F);
    var off = offsets.Count > 0 ? offsets.Average() * 1000 : double.NaN;
    var sd = offsets.Count > 1 ? Math.Sqrt(offsets.Select(o => (o - offsets.Average()) * (o - offsets.Average())).Average()) * 1000 : double.NaN;
    Console.WriteLine(FormattableString.Invariant(
        $"fps={d.GetProperty("fps").GetInt32(),3} ref={bpm,6:0.0} tempo={d.GetProperty("tempo").GetDouble(),6:0.0} locked={lockedAt,5:0.0} heard={d.GetProperty("beatsHeard").GetInt32(),4} filled={d.GetProperty("beatsFilled").GetInt32(),3} spread={d.GetProperty("beatGapSpread").GetDouble(),5:0.000} F={fm:0.00} Foff={foff:0.00} Fdbl={fdbl:0.00} Fhalf={fhalf:0.00} off={off,6:0.0}ms sd={sd,5:0.0}ms"));
}

static (double F, List<double> Offsets) FMeasure(double[] est, double[] refB, double tol = 0.07)
{
    var offsets = new List<double>();
    if (est.Length == 0 || refB.Length == 0) return (0, offsets);
    var used = new HashSet<int>();
    foreach (var b in est)
    {
        var j = 0;
        for (var k = 1; k < refB.Length; k++) if (Math.Abs(refB[k] - b) < Math.Abs(refB[j] - b)) j = k;
        if (Math.Abs(refB[j] - b) <= tol && used.Add(j)) offsets.Add(b - refB[j]);
    }

    double p = (double)offsets.Count / est.Length, r = (double)offsets.Count / refB.Length;
    return (p + r == 0 ? 0 : 2 * p * r / (p + r), offsets);
}

static int[] DpBeats(double[] env, double period, double tight = 100)
{
    var sd = Math.Sqrt(env.Select(v => v * v).Average() - Math.Pow(env.Average(), 2)) + 1e-12;
    var o = env.Select(v => v / sd).ToArray();
    var n = o.Length;
    var score = (double[])o.Clone();
    var back = Enumerable.Repeat(-1, n).ToArray();
    for (var t = 0; t < n; t++)
    {
        int lo = Math.Max(0, t - (int)Math.Round(2 * period)), hi = t - (int)Math.Round(period / 2);
        var best = double.NegativeInfinity;
        var at = -1;
        for (var p = lo; p <= hi; p++)
        {
            var c = score[p] - (tight * Math.Pow(Math.Log((t - p) / period), 2));
            if (c > best) (best, at) = (c, p);
        }

        if (at >= 0 && best > 0) (score[t], back[t]) = (o[t] + best, at);
    }

    var span = (int)(2 * period);
    var end = n - span;
    for (var t = n - span; t < n; t++) if (score[t] > score[end]) end = t;
    var beats = new List<int>();
    for (var t = end; t >= 0; t = back[t]) beats.Add(t);
    beats.Reverse();
    return [.. beats];
}

static void Fft(double[] re, double[] im)
{
    var n = re.Length;
    for (int i = 1, j = 0; i < n; i++)
    {
        var bit = n >> 1;
        for (; (j & bit) != 0; bit >>= 1) j ^= bit;
        j ^= bit;
        if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
    }

    for (var len = 2; len <= n; len <<= 1)
    {
        var ang = -2 * Math.PI / len;
        for (var i = 0; i < n; i += len)
        {
            for (var k = 0; k < len / 2; k++)
            {
                double wr = Math.Cos(ang * k), wi = Math.Sin(ang * k);
                int a = i + k, b = i + k + (len / 2);
                double xr = (re[b] * wr) - (im[b] * wi), xi = (re[b] * wi) + (im[b] * wr);
                (re[b], im[b]) = (re[a] - xr, im[a] - xi);
                (re[a], im[a]) = (re[a] + xr, im[a] + xi);
            }
        }
    }
}

static (double[] X, int Rate) ReadWav(string path)
{
    var data = File.ReadAllBytes(path);
    var pos = 12;
    int tag = 0, channels = 0, rate = 0, bits = 0, start = 0, size = 0;
    while (pos + 8 <= data.Length)
    {
        var id = System.Text.Encoding.ASCII.GetString(data, pos, 4);
        var len = BitConverter.ToInt32(data, pos + 4);
        if (id == "fmt ")
        {
            tag = BitConverter.ToUInt16(data, pos + 8);
            channels = BitConverter.ToUInt16(data, pos + 10);
            rate = BitConverter.ToInt32(data, pos + 12);
            bits = BitConverter.ToUInt16(data, pos + 22);
            if (tag == 0xFFFE) tag = BitConverter.ToUInt16(data, pos + 32);
        }
        else if (id == "data")
        {
            (start, size) = (pos + 8, Math.Min(len < 0 ? int.MaxValue : len, data.Length - pos - 8));
            break;
        }

        pos += 8 + len + (len & 1);
    }

    var bytes = bits / 8;
    var count = size / (bytes * channels);
    var x = new double[count];
    for (var i = 0; i < count; i++)
    {
        double sum = 0;
        for (var c = 0; c < channels; c++)
        {
            var at = start + (((i * channels) + c) * bytes);
            sum += tag == 3 ? BitConverter.ToSingle(data, at) : BitConverter.ToInt16(data, at) / 32768.0;
        }

        x[i] = sum / channels;
    }

    return (x, rate);
}
