// The removal audit: Subject Lock's removal preview run on real episodes, so it can be judged by
// eye and timed. Nothing here ships; no media, frames or outputs are committed.
//
// Build:  dotnet build scripts/removal-audit -c Release
// Run:    dotnet run --project scripts/removal-audit -c Release --no-build -- <mode> ...
//
//   scenes <tag> [scene ...]                        every built-in scene below (or the named ones),
//                                                  episodes found under REXPLAYER_MOVIES
//   erase <video> <name> <start> <end> <left> <top> <width> <height> [every]
//                                                  select the region at <start>, preview the removal
//                                                  until <end>, a panel every [every] seconds
//   bench <video> <name> <start> <frames>          CPU milliseconds per 1080p preview for small,
//                                                  medium, large and huge subjects, and by stage
//   sheet <video> <name> <t1> <t2> ...             a contact sheet of pictures, four across
//
// Outputs go to out/<name>/ beside the program: per picture a panel (top: a crop around the
// subject, the original with the matte's outline in green and the box in red, then the preview;
// bottom: the whole preview, then the whole original) and frames.csv with each picture's tracking
// state, CPU milliseconds, observed and estimated pixels, coverage and status. Times are this
// thread's cycles at the processor's nominal clock (REXPLAYER_CLOCK_GHZ, 1.8 by default), so
// other work on the machine does not count.
using System.Globalization;
using System.Reflection;
using Rex.Media.AppCore;
using Rex.Media.Codecs.MediaFoundation;
using Rex.Media.Containers;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.Video;
using Rex.Media.Video.D3D11;

var culture = CultureInfo.InvariantCulture;
var registry = MediaRegistries.Decoders([new MfDecoderFactory()]);
var mode = args[0];

if (mode == "scenes")
{
    var movies = Environment.GetEnvironmentVariable("REXPLAYER_MOVIES")
        ?? throw new InvalidOperationException("Set REXPLAYER_MOVIES to the folder holding the episodes.");
    var tag = args[1];
    var only = args.Skip(2).ToHashSet(StringComparer.Ordinal);
    // Episode file name patterns, then start and end seconds and the region selected at the start.
    var scenes = new (string Name, string Pattern, double Start, double End, float Left, float Top, float Width, float Height)[]
    {
        ("static", "*Rehearsal*E01*.mp4", 535, 541, 0.635f, 0.41f, 0.0625f, 0.35f),
        ("walk", "*Rehearsal*E01*.mp4", 725, 731, 0.546f, 0.363f, 0.0625f, 0.311f),
        ("close", "*Rehearsal*E01*.mp4", 345, 349, 0.25f, 0.03f, 0.35f, 0.9f),
        ("dark", "*Rehearsal*E01*.mp4", 915, 918, 0.333f, 0.21f, 0.13f, 0.77f),
        ("pan", "*Utopia*S01E01*.mkv", 2088, 2094, 0.27f, 0f, 0.35f, 0.99f),
    };
    foreach (var scene in scenes)
    {
        if (only.Count > 0 && !only.Contains(scene.Name))
        {
            continue;
        }

        var video = Directory.EnumerateFiles(movies, scene.Pattern, SearchOption.AllDirectories).First();
        Console.WriteLine($"== {tag}-{scene.Name}");
        Erase(video, $"{tag}-{scene.Name}", scene.Start, scene.End, [scene.Left, scene.Top, scene.Width, scene.Height], 1.5);
    }

    return;
}

var path = args[1];
var name = args[2];
switch (mode)
{
    case "sheet":
        Sheet(path, name, [.. args.Skip(3).Select(s => double.Parse(s, culture))]);
        break;
    case "bench":
        Bench(path, double.Parse(args[3], culture), int.Parse(args[4], culture));
        break;
    case "erase":
        Erase(path, name, double.Parse(args[3], culture), double.Parse(args[4], culture),
            [.. args.Skip(5).Take(4).Select(s => float.Parse(s, culture))],
            args.Length > 9 ? double.Parse(args[9], culture) : 1.0);
        break;
    default:
        throw new ArgumentException("Unknown mode " + mode);
}

string Output(string folder)
{
    var output = Path.Combine(AppContext.BaseDirectory, "out", folder);
    Directory.CreateDirectory(output);
    return output;
}

void Sheet(string video, string folder, double[] times)
{
    const int Tile = 480;
    const int Across = 4;
    var down = (times.Length + Across - 1) / Across;
    using var sheet = VideoFrame.Rent(PixelFormat.Bgra32, Tile * Across, 270 * down);
    sheet.Plane(0).Clear();
    for (var i = 0; i < times.Length; i++)
    {
        using var demuxer = Open(video);
        var (picture, _) = Snapshot.Take(demuxer, registry, MediaTime.FromSeconds(times[i]), default);
        using (picture)
        using (var bgra = ColorConverter.ToBgra(picture))
        {
            Blit(bgra, (0, 0, bgra.Width, bgra.Height), sheet, i % Across * Tile, i / Across * 270, Tile, 270);
        }
    }

    Save(sheet, Path.Combine(Output(folder), "sheet.png"));
    Console.WriteLine("Sheet order: " + string.Join(", ", times.Select(t => t.ToString(culture))));
}

void Bench(string video, double start, int count)
{
    using var demuxer = Open(video);
    var pictures = Decode(demuxer, start, count);
    var (w, h) = (pictures[0].Width, pictures[0].Height);
    foreach (var (label, share) in new[] { ("small", 0.06), ("medium", 0.16), ("large", 0.35), ("huge", 0.55) })
    {
        var bw = (int)(w * share * 0.6);
        var bh = Math.Min((int)(h * share * 1.6), h - 10);
        var removal = new SubjectRemoval();
        var recorder = new SubjectRemoval();
        var motion = new SubjectRemovalMotion();
        var matte = new SubjectRemovalMatte();
        using var preview = VideoFrame.Rent(PixelFormat.Bgra32, w, h);
        List<double> previews = [], records = [], motions = [], mattes = [];
        for (var i = 0; i < pictures.Count; i++)
        {
            // The box drifts as a walking subject's would.
            var left = (int)(w * 0.3) + i * 3;
            var top = (h - bh) / 2;
            var box = (left, top, left + bw, top + bh);
            var detail = (Math.Max(0, left - 60), Math.Max(0, top - 60), Math.Min(w, left + bw + 60), Math.Min(h, top + bh + 60));
            Time(previews, i, () => removal.Apply(pictures[i], box, advance: true, feather: 3, coverage: 50, preview));
            Time(records, i, () => recorder.Apply(pictures[i], box, advance: true, 3, 50, null));
            Time(motions, i, () => motion.Estimate(pictures[i], box, detail));
            Time(mattes, i, () => matte.Compute(pictures[i], box, null, 50, true));
        }

        Console.WriteLine($"{label} box {bw}x{bh} (CPU ms): preview {Summary(previews)}; recording only {Summary(records)}; motion {Summary(motions)}; matte {Summary(mattes)}");
    }

    foreach (var picture in pictures)
    {
        picture.Dispose();
    }
}

static void Time(List<double> times, int index, Action work)
{
    var before = Cpu();
    work();
    // The first pictures warm up: buffers are sized and code is compiled.
    if (index >= 3)
    {
        times.Add(Cpu() - before);
    }
}

static string Summary(List<double> times)
{
    times.Sort();
    return string.Create(CultureInfo.InvariantCulture, $"median {times[times.Count / 2]:F1}, p90 {times[(int)(times.Count * 0.9)]:F1}");
}

void Erase(string video, string folder, double start, double end, float[] region, double every)
{
    var output = Output(folder);
    using var demuxer = Open(video);
    using var session = new SubjectEditSession();
    var removalField = typeof(SubjectEditSession).GetField("_removal", BindingFlags.Instance | BindingFlags.NonPublic)!;
    session.Select(region[0], region[1], region[2], region[3]);
    var track = demuxer.Info.FirstTrack(MediaKind.Video)!;
    using var decoder = registry.CreateVideo(track).Decoder!;
    demuxer.Seek(MediaTime.FromSeconds(start), default);
    var frames = new List<VideoFrame>();
    var nextPanel = start;
    var done = false;
    var times = new List<double>();
    using var log = new StreamWriter(Path.Combine(output, "frames.csv"));
    log.WriteLine("seconds,tracking,erase,cpu_ms,observed,estimated,coverage,status");
    while (!done && demuxer.ReadPacket(default) is { } packet)
    {
        using (packet)
        {
            if (packet.TrackId == track.Id)
            {
                decoder.Decode(packet, frames);
            }
        }

        foreach (var decoded in frames)
        {
            using (decoded)
            {
                var seconds = decoded.Pts.TotalSeconds;
                if (seconds < start)
                {
                    continue;
                }

                if (seconds > end)
                {
                    done = true;
                    continue;
                }

                using var frame = ColorConverter.ToBgra(decoded);
                if (session.Tracking && !session.Erase)
                {
                    session.SetErase(true);
                }

                var before = Cpu();
                using var edited = session.Process(frame);
                var ms = Cpu() - before;
                if (edited is not null)
                {
                    times.Add(ms);
                }

                var removal = (SubjectRemoval)removalField.GetValue(session)!;
                log.WriteLine(string.Create(culture, $"{seconds:F3},{session.Tracking},{session.Erase},{ms:F1},{session.ObservedPixels},{session.EstimatedPixels},{removal.Coverage:F2},\"{session.Status}\""));
                if (seconds >= nextPanel && edited is not null)
                {
                    nextPanel = seconds + every;
                    var r = session.Region;
                    var box = ((int)(r.Left * frame.Width), (int)(r.Top * frame.Height),
                        (int)((r.Left + r.Width) * frame.Width), (int)((r.Top + r.Height) * frame.Height));
                    Panel(frame, edited, removal, box, Path.Combine(output, string.Create(culture, $"{seconds:0000.00}.png")));
                }
            }
        }

        frames.Clear();
    }

    times.Sort();
    if (times.Count > 0)
    {
        Console.WriteLine(string.Create(culture, $"{times.Count} previews (CPU ms, tracking included): median {times[times.Count / 2]:F1}, p90 {times[(int)(times.Count * 0.9)]:F1}, max {times[^1]:F1}"));
    }

    Console.WriteLine("Last status: " + session.Status);
}

static IDemuxer Open(string path) => MediaRegistries.Demuxers().Open(new FileByteSource(path), default);

List<VideoFrame> Decode(IDemuxer demuxer, double start, int count)
{
    var track = demuxer.Info.FirstTrack(MediaKind.Video)!;
    using var decoder = registry.CreateVideo(track).Decoder!;
    demuxer.Seek(MediaTime.FromSeconds(start), default);
    var frames = new List<VideoFrame>();
    var result = new List<VideoFrame>();
    while (result.Count < count && demuxer.ReadPacket(default) is { } packet)
    {
        using (packet)
        {
            if (packet.TrackId == track.Id)
            {
                decoder.Decode(packet, frames);
            }
        }

        foreach (var decoded in frames)
        {
            using (decoded)
            {
                if (decoded.Pts.TotalSeconds >= start && result.Count < count)
                {
                    result.Add(ColorConverter.ToBgra(decoded));
                }
            }
        }

        frames.Clear();
    }

    return result;
}

static void Save(VideoFrame frame, string path)
{
    using var file = File.Create(path);
    PngWriter.Write(file, frame);
}

static void Panel(VideoFrame original, VideoFrame preview, SubjectRemoval removal, (int L, int T, int R, int B) box, string path)
{
    var (bw, bh) = (box.R - box.L, box.B - box.T);
    var (cx, cy) = ((box.L + box.R) / 2, (box.T + box.B) / 2);
    var side = (int)(Math.Max(bw * 1.2, bh * 1.2 * 0.75) + 40);
    var cw = Math.Min(original.Width, side);
    var ch = Math.Min(original.Height, (int)(side / 0.75));
    var cl = Math.Clamp(cx - cw / 2, 0, original.Width - cw);
    var ct = Math.Clamp(cy - ch / 2, 0, original.Height - ch);
    var crop = (cl, ct, cl + cw, ct + ch);
    const int PanelHeight = 600;
    var pw = (int)(PanelHeight * cw / (double)ch);
    using var marked = VideoFrame.Rent(PixelFormat.Bgra32, original.Width, original.Height);
    original.Plane(0).CopyTo(marked.Plane(0));
    for (var y = ct; y < ct + ch; y++)
    {
        var row = marked.Row(0, y);
        for (var x = cl; x < cl + cw; x++)
        {
            var inside = removal.IsSubject(x, y);
            var edge = inside != removal.IsSubject(x + 1, y) || inside != removal.IsSubject(x, y + 1);
            var onBox = ((x == box.L || x == box.R - 1) && y >= box.T && y < box.B) || ((y == box.T || y == box.B - 1) && x >= box.L && x < box.R);
            if (edge)
            {
                (row[x * 4], row[x * 4 + 1], row[x * 4 + 2]) = (0, 255, 0);
            }
            else if (onBox)
            {
                (row[x * 4], row[x * 4 + 1], row[x * 4 + 2]) = (0, 0, 255);
            }
        }
    }

    const int SmallWidth = 640;
    const int SmallHeight = 360;
    using var panel = VideoFrame.Rent(PixelFormat.Bgra32, pw * 2 + 8, PanelHeight + 8 + SmallHeight);
    panel.Plane(0).Clear();
    Blit(marked, crop, panel, 0, 0, pw, PanelHeight);
    Blit(preview, crop, panel, pw + 8, 0, pw, PanelHeight);
    Blit(preview, (0, 0, preview.Width, preview.Height), panel, 0, PanelHeight + 8, Math.Min(SmallWidth, panel.Width), SmallHeight);
    Blit(original, (0, 0, original.Width, original.Height), panel, SmallWidth + 8, PanelHeight + 8, Math.Min(SmallWidth, panel.Width - SmallWidth - 8), SmallHeight);
    Save(panel, path);
}

static void Blit(VideoFrame from, (int L, int T, int R, int B) area, VideoFrame to, int x0, int y0, int w, int h)
{
    if (w <= 0 || h <= 0)
    {
        return;
    }

    var (sw, sh) = (area.R - area.L, area.B - area.T);
    for (var y = 0; y < h; y++)
    {
        var sy0 = area.T + (int)((long)y * sh / h);
        var sy1 = Math.Max(sy0 + 1, area.T + (int)((long)(y + 1) * sh / h));
        var target = to.Row(0, y0 + y);
        for (var x = 0; x < w; x++)
        {
            var sx0 = area.L + (int)((long)x * sw / w);
            var sx1 = Math.Max(sx0 + 1, area.L + (int)((long)(x + 1) * sw / w));
            int b = 0, g = 0, r = 0, n = 0;
            for (var sy = sy0; sy < sy1; sy += Math.Max(1, (sy1 - sy0) / 3))
            {
                var row = from.Row(0, sy);
                for (var sx = sx0; sx < sx1; sx += Math.Max(1, (sx1 - sx0) / 3))
                {
                    (b, g, r, n) = (b + row[sx * 4], g + row[sx * 4 + 1], r + row[sx * 4 + 2], n + 1);
                }
            }

            var at = (x0 + x) * 4;
            (target[at], target[at + 1], target[at + 2], target[at + 3]) = ((byte)(b / n), (byte)(g / n), (byte)(r / n), 255);
        }
    }
}

// Time this thread actually ran, from its cycle count at the processor's nominal clock.
static double Cpu()
{
    QueryThreadCycleTime(GetCurrentThread(), out var cycles);
    var gigahertz = double.TryParse(Environment.GetEnvironmentVariable("REXPLAYER_CLOCK_GHZ"), CultureInfo.InvariantCulture, out var clock) ? clock : 1.8;
    return cycles / (gigahertz * 1e6);
}

[System.Runtime.InteropServices.DllImport("kernel32.dll")]
static extern bool QueryThreadCycleTime(IntPtr thread, out ulong cycles);

[System.Runtime.InteropServices.DllImport("kernel32.dll")]
static extern IntPtr GetCurrentThread();
