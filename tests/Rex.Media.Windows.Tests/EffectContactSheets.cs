using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using Rex.Media.AppCore;
using Rex.Media.Codecs.MediaFoundation;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.Video;
using Rex.Media.Video.D3D11;

namespace Rex.Media.Windows.Tests;

/// <summary>
/// Draws every effect and look over real pictures into labelled contact sheets for a person to
/// judge, and measures how steady each effect is from one picture to the next. Opt-in, as it needs
/// real video and takes minutes:
/// <list type="bullet">
/// <item>REXPLAYER_EFFECT_SHEETS: the folder the sheets and the steadiness report go into.</item>
/// <item>REXPLAYER_EFFECT_SOURCES: "label|file|seconds[|options]" entries separated by semicolons;
/// options are "bars" (black bars recorded into the top and bottom of the picture) and
/// "crop=x,y" (draw at the picture's own size and show the part around x,y, each 0 to 1, at one
/// screen pixel per picture pixel).</item>
/// <item>REXPLAYER_EFFECT_ONLY: effect names separated by commas, to draw only those.</item>
/// <item>REXPLAYER_EFFECT_EXTREMES: 1 to draw each effect at the ends of each of its controls.</item>
/// <item>REXPLAYER_EFFECT_TILE: the size of each picture on a sheet, "480x270" unless set.</item>
/// <item>REXPLAYER_EFFECT_SCALE: draw each picture this many times larger and average it down, so
/// a tile shows a full-screen picture shrunk (4 for 1920x1080 behind 480x270 tiles).</item>
/// </list>
/// </summary>
[SupportedOSPlatform("windows8.0")]
public sealed class EffectContactSheets
{
    private const int Columns = 4;
    private const int LabelHeight = 22;

    private sealed record Source(string Label, string File, double Seconds, bool Bars, (double X, double Y)? Crop);

    private sealed record Item(string Label, VideoEffect Effect, EffectControls Controls, VideoLook Look = VideoLook.Original);

    [Fact]
    public void DrawEveryEffectOverRealPictures()
    {
        var folder = Environment.GetEnvironmentVariable("REXPLAYER_EFFECT_SHEETS");
        var sources = Environment.GetEnvironmentVariable("REXPLAYER_EFFECT_SOURCES");
        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(sources))
        {
            Assert.Skip("Set REXPLAYER_EFFECT_SHEETS and REXPLAYER_EFFECT_SOURCES to draw the contact sheets.");
        }

        Directory.CreateDirectory(folder);
        var only = (Environment.GetEnvironmentVariable("REXPLAYER_EFFECT_ONLY") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Enum.Parse<VideoEffect>).ToHashSet();
        var extremes = Environment.GetEnvironmentVariable("REXPLAYER_EFFECT_EXTREMES") == "1";
        var tile = (Environment.GetEnvironmentVariable("REXPLAYER_EFFECT_TILE") ?? "480x270").Split('x').Select(int.Parse).ToArray();
        var report = new StringBuilder();
        foreach (var source in sources.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Parse))
        {
            var frames = Consecutive(source, 3);
            try
            {
                var items = extremes ? ExtremeItems(only) : DefaultItems(only);
                Draw(folder, source, frames, items, tile[0], tile[1], extremes ? "extremes" : "all");
                report.AppendLine(Steadiness(source, frames, items));
            }
            finally
            {
                frames.ForEach(frame => frame.Dispose());
            }
        }

        File.WriteAllText(Path.Combine(folder, extremes ? "steadiness-extremes.txt" : "steadiness.txt"), report.ToString());
    }

    private static Source Parse(string entry)
    {
        var parts = entry.Split('|');
        var bars = parts.Skip(3).Contains("bars");
        (double, double)? crop = parts.Skip(3).FirstOrDefault(part => part.StartsWith("crop=", StringComparison.Ordinal)) is { } c
            ? (double.Parse(c[5..].Split(',')[0], CultureInfo.InvariantCulture), double.Parse(c[5..].Split(',')[1], CultureInfo.InvariantCulture))
            : null;
        return new Source(parts[0], parts[1], double.Parse(parts[2], CultureInfo.InvariantCulture), bars, crop);
    }

    private static List<Item> DefaultItems(HashSet<VideoEffect> only)
    {
        var items = new List<Item>();
        foreach (var effect in Enum.GetValues<VideoEffect>().Where(effect => only.Count == 0 || only.Contains(effect)))
        {
            items.Add(new Item(VideoEffects.Name(effect), effect, VideoStyleOptions.Controls(effect, null, VideoStyleOptions.SharedStrengthDefault)));
        }

        if (only.Count == 0)
        {
            foreach (var look in Enum.GetValues<VideoLook>().Where(look => look != VideoLook.Original))
            {
                items.Add(new Item("Look: " + VideoLooks.Name(look), VideoEffect.Off, default, look));
            }
        }

        return items;
    }

    /// <summary>Each effect at its defaults, then at full strength, then at each end of every other control.</summary>
    private static List<Item> ExtremeItems(HashSet<VideoEffect> only)
    {
        var items = new List<Item>();
        foreach (var effect in Enum.GetValues<VideoEffect>().Where(effect => effect != VideoEffect.Off && (only.Count == 0 || only.Contains(effect))))
        {
            var options = VideoStyleOptions.ForEffect(effect);
            var defaults = VideoStyleOptions.Controls(effect, null, VideoStyleOptions.SharedStrengthDefault);
            items.Add(new Item(VideoEffects.Name(effect), effect, defaults));
            items.Add(new Item("Intensity 100", effect, defaults with { Intensity = 100 }));
            items.Add(new Item($"Detail {options[1].Min}", effect, defaults with { Detail = options[1].Min }));
            items.Add(new Item($"Detail {options[1].Max}", effect, defaults with { Detail = options[1].Max }));
            for (var index = 2; index < options.Count; index++)
            {
                var option = options[index];
                var values = option.Key.EndsWith(".mode", StringComparison.Ordinal)
                    ? Enumerable.Range(option.Min, option.Max - option.Min + 1)
                    : [option.Min, option.Max];
                foreach (var value in values)
                {
                    var label = option.Key.EndsWith(".mode", StringComparison.Ordinal)
                        ? VideoStyleOptions.ModeNames(effect)[value]
                        : $"{option.Label.Split(' ')[0]} {value}";
                    items.Add(new Item(label, effect, index == 2 ? defaults with { Third = value } : defaults with { Fourth = value }));
                }
            }
        }

        return items;
    }

    private static void Draw(string folder, Source source, List<VideoFrame> frames, List<Item> items, int tileWidth, int tileHeight, string kind)
    {
        // Drawn larger and averaged down (REXPLAYER_EFFECT_SCALE), a tile shows what a full-screen
        // picture looks like shrunk, rather than the effect drawn into a small window.
        var scale = source.Crop is null && int.TryParse(Environment.GetEnvironmentVariable("REXPLAYER_EFFECT_SCALE"), out var s) ? Math.Max(1, s) : 1;
        var (renderWidth, renderHeight) = source.Crop is null ? (tileWidth * scale, tileHeight * scale) : (frames[0].Width, frames[0].Height);
        using var presenter = D3D11Presenter.Offscreen(renderWidth, renderHeight, software: false);
        var perSheet = Columns * 3;
        for (var start = 0; start < items.Count; start += perSheet)
        {
            var chunk = items.Skip(start).Take(perSheet).ToList();
            var rows = (chunk.Count + Columns - 1) / Columns;
            using var sheet = VideoFrame.Rent(PixelFormat.Bgra32, Columns * tileWidth, rows * (tileHeight + LabelHeight));
            sheet.Plane(0).Clear();
            for (var index = 0; index < chunk.Count; index++)
            {
                var item = chunk[index];
                using var drawn = Render(presenter, frames[0], frames[1], item);
                var (left, top) = (index % Columns * tileWidth, index / Columns * (tileHeight + LabelHeight));
                var (cropLeft, cropTop) = source.Crop is { } crop
                    ? (Math.Clamp((int)(crop.X * drawn.Width) - (tileWidth / 2), 0, drawn.Width - tileWidth),
                        Math.Clamp((int)(crop.Y * drawn.Height) - (tileHeight / 2), 0, drawn.Height - tileHeight))
                    : (0, 0);
                for (var y = 0; y < tileHeight; y++)
                {
                    var target = sheet.Row(0, top + LabelHeight + y)[(left * 4)..];
                    for (var x = 0; x < tileWidth * 4; x++)
                    {
                        var sum = 0;
                        for (var dy = 0; dy < scale; dy++)
                        {
                            var row = drawn.Row(0, cropTop + (y * scale) + dy);
                            for (var dx = 0; dx < scale; dx++)
                            {
                                sum += row[((cropLeft + ((x / 4) * scale) + dx) * 4) + (x % 4)];
                            }
                        }

                        target[x] = (byte)(sum / (scale * scale));
                    }
                }

                Label(sheet, left + 4, top + 4, item.Label);
            }

            using var file = File.Create(Path.Combine(folder, $"{source.Label}-{kind}-{(start / perSheet) + 1}.png"));
            PngWriter.Write(file, sheet);
        }
    }

    private static VideoFrame Render(D3D11Presenter presenter, VideoFrame previous, VideoFrame current, Item item)
    {
        presenter.SetLook(item.Look);
        presenter.SetEffect(item.Effect, item.Controls.Intensity, item.Controls.Detail);
        presenter.SetEffectExtras(item.Controls.Third, item.Controls.Fourth);
        presenter.SetPointer(0.5f, 0.45f, VideoEffects.UsesPointer(item.Effect));
        presenter.Present(previous);
        presenter.Present(current);
        return presenter.ReadBack();
    }

    /// <summary>
    /// For each item, how much its drawing changes between two consecutive pictures, over how much
    /// the plain pictures change: near 1 follows the video, far above 1 flickers.
    /// </summary>
    private static string Steadiness(Source source, List<VideoFrame> frames, List<Item> items)
    {
        using var presenter = D3D11Presenter.Offscreen(960, 540, software: false);
        using var plainFirst = Render(presenter, frames[0], frames[1], new Item("", VideoEffect.Off, default));
        using var plainSecond = Render(presenter, frames[1], frames[2], new Item("", VideoEffect.Off, default));
        var plain = MeanDifference(plainFirst, plainSecond);
        var text = new StringBuilder($"{source.Label}: the plain pictures differ by {plain:F2} levels on average\n");
        foreach (var item in items)
        {
            using var first = Render(presenter, frames[0], frames[1], item);
            using var second = Render(presenter, frames[1], frames[2], item);
            var changed = MeanDifference(first, second);
            text.AppendLine(CultureInfo.InvariantCulture, $"  {item.Label,-32} {changed,7:F2} levels  {changed / Math.Max(plain, 0.01),6:F2}x");
        }

        return text.ToString();
    }

    private static double MeanDifference(VideoFrame a, VideoFrame b)
    {
        long sum = 0;
        for (var y = 0; y < a.Height; y++)
        {
            var rowA = a.Row(0, y);
            var rowB = b.Row(0, y);
            for (var x = 0; x < a.Width * 4; x++)
            {
                sum += x % 4 == 3 ? 0 : Math.Abs(rowA[x] - rowB[x]);
            }
        }

        return sum / (double)(a.Width * a.Height * 3);
    }

    /// <summary>The first <paramref name="count"/> pictures at or after the source's moment, decoded by Windows.</summary>
    private static List<VideoFrame> Consecutive(Source source, int count)
    {
        using var file = new FileByteSource(source.File);
        using var demuxer = MediaRegistries.Demuxers().Probe(file, CancellationToken.None)!.Open(file, CancellationToken.None);
        var track = demuxer.Info.FirstTrack(MediaKind.Video)!;
        var opened = MediaRegistries.Decoders(new MfDecoderFactory()).CreateVideo(track);
        using var decoder = opened.Decoder ?? throw new NotSupportedException(opened.Reason);
        var at = MediaTime.FromSeconds(source.Seconds);
        demuxer.Seek(at, CancellationToken.None);
        var kept = new List<VideoFrame>();
        var decoded = new List<VideoFrame>();
        while (kept.Count < count && demuxer.ReadPacket(CancellationToken.None) is { } packet)
        {
            using (packet)
            {
                if (packet.TrackId == track.Id)
                {
                    decoder.Decode(packet, decoded);
                }
            }

            foreach (var frame in decoded)
            {
                if (kept.Count < count && frame.Pts >= at)
                {
                    frame.Generation = 1;
                    kept.Add(source.Bars ? WithBars(frame) : frame);
                }
                else
                {
                    frame.Dispose();
                }
            }

            decoded.Clear();
        }

        return kept;
    }

    /// <summary>Paints black over the top and bottom eighth of a planar picture, as a letterboxed recording has.</summary>
    private static VideoFrame WithBars(VideoFrame frame)
    {
        var bar = frame.Height / 8 & ~1;
        var wide = frame.Format == PixelFormat.P010;
        for (var y = 0; y < frame.Height; y++)
        {
            if (y < bar || y >= frame.Height - bar)
            {
                var row = frame.Row(0, y);
                for (var x = 0; x < frame.Width; x++)
                {
                    if (wide)
                    {
                        row[x * 2] = 0;
                        row[(x * 2) + 1] = 0x10; // 64 << 6, ten-bit black in the top bits
                    }
                    else
                    {
                        row[x] = 16;
                    }
                }
            }
        }

        for (var y = 0; y < (frame.Height + 1) / 2; y++)
        {
            if (y < bar / 2 || y >= (frame.Height - bar) / 2)
            {
                var row = frame.Row(1, y);
                for (var x = 0; x < (frame.Width + 1) / 2 * 2; x++)
                {
                    if (wide)
                    {
                        row[x * 2] = 0;
                        row[(x * 2) + 1] = 128;
                    }
                    else
                    {
                        row[x] = 128;
                    }
                }
            }
        }

        return frame;
    }

    private static void Label(VideoFrame sheet, int left, int top, string text)
    {
        var x = left;
        foreach (var character in text.ToUpperInvariant())
        {
            if (Font.TryGetValue(character, out var glyph))
            {
                var rows = glyph.Split('|');
                for (var row = 0; row < 7; row++)
                {
                    for (var column = 0; column < 5; column++)
                    {
                        if (rows[row][column] == '#')
                        {
                            for (var dy = 0; dy < 2; dy++)
                            {
                                var line = sheet.Row(0, top + (row * 2) + dy);
                                for (var dx = 0; dx < 2; dx++)
                                {
                                    var at = (x + (column * 2) + dx) * 4;
                                    if (at + 3 < line.Length)
                                    {
                                        line[at] = line[at + 1] = line[at + 2] = 235;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            x += 12;
        }
    }

    private static readonly Dictionary<char, string> Font = new()
    {
        ['A'] = ".###.|#...#|#...#|#####|#...#|#...#|#...#",
        ['B'] = "####.|#...#|#...#|####.|#...#|#...#|####.",
        ['C'] = ".###.|#...#|#....|#....|#....|#...#|.###.",
        ['D'] = "####.|#...#|#...#|#...#|#...#|#...#|####.",
        ['E'] = "#####|#....|#....|####.|#....|#....|#####",
        ['F'] = "#####|#....|#....|####.|#....|#....|#....",
        ['G'] = ".###.|#...#|#....|#.###|#...#|#...#|.####",
        ['H'] = "#...#|#...#|#...#|#####|#...#|#...#|#...#",
        ['I'] = ".###.|..#..|..#..|..#..|..#..|..#..|.###.",
        ['J'] = "..###|...#.|...#.|...#.|...#.|#..#.|.##..",
        ['K'] = "#...#|#..#.|#.#..|##...|#.#..|#..#.|#...#",
        ['L'] = "#....|#....|#....|#....|#....|#....|#####",
        ['M'] = "#...#|##.##|#.#.#|#.#.#|#...#|#...#|#...#",
        ['N'] = "#...#|#...#|##..#|#.#.#|#..##|#...#|#...#",
        ['O'] = ".###.|#...#|#...#|#...#|#...#|#...#|.###.",
        ['P'] = "####.|#...#|#...#|####.|#....|#....|#....",
        ['Q'] = ".###.|#...#|#...#|#...#|#.#.#|#..#.|.##.#",
        ['R'] = "####.|#...#|#...#|####.|#.#..|#..#.|#...#",
        ['S'] = ".####|#....|#....|.###.|....#|....#|####.",
        ['T'] = "#####|..#..|..#..|..#..|..#..|..#..|..#..",
        ['U'] = "#...#|#...#|#...#|#...#|#...#|#...#|.###.",
        ['V'] = "#...#|#...#|#...#|#...#|#...#|.#.#.|..#..",
        ['W'] = "#...#|#...#|#...#|#.#.#|#.#.#|#.#.#|.#.#.",
        ['X'] = "#...#|#...#|.#.#.|..#..|.#.#.|#...#|#...#",
        ['Y'] = "#...#|#...#|.#.#.|..#..|..#..|..#..|..#..",
        ['Z'] = "#####|....#|...#.|..#..|.#...|#....|#####",
        ['0'] = ".###.|#...#|#..##|#.#.#|##..#|#...#|.###.",
        ['1'] = "..#..|.##..|..#..|..#..|..#..|..#..|.###.",
        ['2'] = ".###.|#...#|....#|...#.|..#..|.#...|#####",
        ['3'] = "#####|...#.|..#..|...#.|....#|#...#|.###.",
        ['4'] = "...#.|..##.|.#.#.|#..#.|#####|...#.|...#.",
        ['5'] = "#####|#....|####.|....#|....#|#...#|.###.",
        ['6'] = "..##.|.#...|#....|####.|#...#|#...#|.###.",
        ['7'] = "#####|....#|...#.|..#..|.#...|.#...|.#...",
        ['8'] = ".###.|#...#|#...#|.###.|#...#|#...#|.###.",
        ['9'] = ".###.|#...#|#...#|.####|....#|...#.|.##..",
        ['-'] = ".....|.....|.....|#####|.....|.....|.....",
        ['&'] = ".##..|#..#.|#.#..|.#...|#.#.#|#..#.|.##.#",
        ['('] = "...#.|..#..|.#...|.#...|.#...|..#..|...#.",
        [')'] = ".#...|..#..|...#.|...#.|...#.|..#..|.#...",
        ['%'] = "##...|##..#|...#.|..#..|.#...|#..##|...##",
        ['.'] = ".....|.....|.....|.....|.....|.##..|.##..",
        [','] = ".....|.....|.....|.....|.##..|..#..|.#...",
        [':'] = ".....|.##..|.##..|.....|.##..|.##..|.....",
        ['/'] = ".....|....#|...#.|..#..|.#...|#....|.....",
        ['+'] = ".....|..#..|..#..|#####|..#..|..#..|.....",
    };
}
