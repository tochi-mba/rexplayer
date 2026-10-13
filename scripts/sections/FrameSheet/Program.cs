using System.Globalization;
using System.Text;
using Rex.Media.AppCore;
using Rex.Media.Codecs.MediaFoundation;
using Rex.Media.IO;
using Rex.Media.Primitives;

// FrameSheet <file> <from> <to> <step> <out-folder> [tile-width] [k]
//
// Decodes [from, to) of a video once, in order, and writes:
//   frames.csv   every picture's time, mean luma (0..255), and the fractions of dark (luma <= 30)
//                and bright (luma >= 180) pixels: black frames, cards and fades show up at once;
//   <ms>.ppm     a small picture every <step> seconds, for scripts/sections/compose_sheet.py.
// A negative <from> counts back from the end of the file. "k" decodes keyframes only, each with a
// decoder of its own: a quick survey of a long stretch. Used to find intro and credits boundaries
// by eye (tests/Rex.Media.Windows.Tests/Fixtures/episode-sections.json).
var file = args[0];
var from = double.Parse(args[1], CultureInfo.InvariantCulture);
var to = double.Parse(args[2], CultureInfo.InvariantCulture);
var step = double.Parse(args[3], CultureInfo.InvariantCulture);
var outFolder = args[4];
var tileWidth = args.Length > 5 ? int.Parse(args[5], CultureInfo.InvariantCulture) : 320;
var keyframesOnly = args.Length > 6 && args[6] == "k";
Directory.CreateDirectory(outFolder);
var decoders = MediaRegistries.Decoders([new MfDecoderFactory()]);
using var source = new FileByteSource(file);
using var demuxer = MediaRegistries.Demuxers().Open(source, CancellationToken.None);
var track = demuxer.Info.FirstTrack(MediaKind.Video) ?? throw new InvalidOperationException("The file has no video.");
var duration = demuxer.Info.Duration.ToTimeSpan().TotalSeconds;
Console.WriteLine("duration " + duration.ToString(CultureInfo.InvariantCulture));
if (from < 0)
{
    from += duration;
}

if (from > 0)
{
    demuxer.Seek(MediaTime.FromSeconds(from), CancellationToken.None);
}

var csv = new StringBuilder("t,mean,dark,bright\n");
var next = from;
var frames = new List<VideoFrame>();
using var decoder = decoders.CreateVideo(track).Decoder ?? throw new InvalidOperationException("No decoder for this video.");
var done = false;
while (!done && demuxer.ReadPacket(CancellationToken.None) is { } packet)
{
    using (packet)
    {
        if (packet.TrackId != track.Id || (keyframesOnly && (!packet.IsKeyframe || !packet.Pts.IsKnown)))
        {
            continue;
        }

        if (keyframesOnly)
        {
            using var own = decoders.CreateVideo(track).Decoder!;
            own.Decode(packet, frames);
            while (!own.Drain(frames))
            {
            }

            foreach (var frame in frames)
            {
                frame.Pts = packet.Pts;
            }
        }
        else
        {
            decoder.Decode(packet, frames);
        }
    }

    foreach (var frame in frames)
    {
        using (frame)
        {
            if (!frame.Pts.IsKnown || frame.Surface is not null)
            {
                continue;
            }

            var t = frame.Pts.ToTimeSpan().TotalSeconds;
            done |= t >= to;
            if (t < from || t >= to)
            {
                continue;
            }

            Measure(frame, out var mean, out var dark, out var bright);
            csv.Append(CultureInfo.InvariantCulture, $"{t:F3},{mean:F1},{dark:F3},{bright:F3}\n");
            if (t + 1e-6 >= next)
            {
                Tile(frame, Path.Combine(outFolder, ((long)Math.Round(t * 1000)).ToString("D8", CultureInfo.InvariantCulture) + ".ppm"), tileWidth);
                while (next <= t + 1e-6)
                {
                    next += step;
                }
            }
        }
    }

    frames.Clear();
}

File.WriteAllText(Path.Combine(outFolder, "frames.csv"), csv.ToString());

static double Luma(VideoFrame frame, Span<byte> line, int x) => frame.Format switch
{
    PixelFormat.P010 => ((line[(2 * x) + 1] << 2) | (line[2 * x] >> 6)) / 4.0,
    _ => line[x],
};

static void Measure(VideoFrame frame, out double mean, out double dark, out double bright)
{
    double sum = 0;
    int count = 0, darkCount = 0, brightCount = 0;
    for (var y = 0; y < frame.Height; y += 8)
    {
        var line = frame.Row(0, y);
        for (var x = 0; x < frame.Width; x += 8)
        {
            var value = Luma(frame, line, x);
            sum += value;
            count++;
            darkCount += value <= 30 ? 1 : 0;
            brightCount += value >= 180 ? 1 : 0;
        }
    }

    mean = sum / count;
    dark = darkCount / (double)count;
    bright = brightCount / (double)count;
}

// A small RGB picture (binary PPM), converted with the BT.709 matrix from 4:2:0 limited range.
static void Tile(VideoFrame frame, string path, int width)
{
    var height = Math.Max(1, (int)Math.Round(frame.Height * (double)width / frame.Width));
    var pixels = new byte[width * height * 3];
    for (var ty = 0; ty < height; ty++)
    {
        var sy = Math.Min(frame.Height - 1, ty * frame.Height / height);
        var line = frame.Row(0, sy);
        var chroma = frame.Row(1, sy / 2);
        for (var tx = 0; tx < width; tx++)
        {
            var sx = Math.Min(frame.Width - 1, tx * frame.Width / width);
            var cx = sx / 2;
            var (cb, cr) = frame.Format == PixelFormat.P010
                ? (((chroma[(4 * cx) + 1] << 2) | (chroma[4 * cx] >> 6)) / 4.0, ((chroma[(4 * cx) + 3] << 2) | (chroma[(4 * cx) + 2] >> 6)) / 4.0)
                : (chroma[2 * cx], chroma[(2 * cx) + 1]);
            var y = (Luma(frame, line, sx) - 16) * 255 / 219;
            var pb = (cb - 128) * 255 / 224;
            var pr = (cr - 128) * 255 / 224;
            var i = ((ty * width) + tx) * 3;
            pixels[i] = (byte)Math.Clamp(y + (1.5748 * pr), 0, 255);
            pixels[i + 1] = (byte)Math.Clamp(y - (0.1873 * pb) - (0.4681 * pr), 0, 255);
            pixels[i + 2] = (byte)Math.Clamp(y + (1.8556 * pb), 0, 255);
        }
    }

    using var stream = File.Create(path);
    stream.Write(Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n"));
    stream.Write(pixels);
}
