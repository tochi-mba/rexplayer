using System.IO.Compression;
using System.Text;

namespace Rex.Media.AppCore.Player;

/// <summary>
/// Where each episode's analysis is kept between runs: one small compressed file per episode in a
/// folder of its own, known by a hash of the episode's location, never inside the player's store.
/// An analysis holds hundreds of thousands of landmarks; kept as JSON in the store, which is read
/// whole at every launch, each one cost about 12 MB. Here one costs about a tenth of that, is read
/// only when its season is analysed, and is checked against the file's signature, so a changed
/// recording or a newer analysis is never trusted. The least recently used files beyond
/// <see cref="Limit"/> are removed. Without a folder, analyses are kept in memory only.
/// </summary>
public sealed class SectionAnalysisCache
{
    /// <summary>How many episodes are kept: a few seasons, about 200 MB at the most.</summary>
    public const int Limit = 160;

    private const int Magic = 0x41535852; // "RXSA"
    private const string Extension = ".sections";
    private readonly Dictionary<string, EpisodeAnalysis> _memory = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <param name="folder">The folder the files live in, or null to keep analyses in memory only.</param>
    public SectionAnalysisCache(string? folder = null) => Folder = folder;

    public string? Folder { get; }

    /// <summary>The analysis of <paramref name="path"/> made from the file as it is now, or null.</summary>
    public EpisodeAnalysis? Get(string path, string signature)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(signature);
        var key = PlayerMemory.KeyFor(path);
        if (Folder is null)
        {
            lock (_gate)
            {
                return _memory.TryGetValue(key, out var kept) && kept.Signature == signature ? kept with { Path = path } : null;
            }
        }

        var file = Path.Combine(Folder, key + Extension);
        try
        {
            using var stream = File.OpenRead(file);
            var analysis = Read(stream, path);
            if (analysis?.Signature != signature)
            {
                return null;
            }

            // Recently used: the last to be removed.
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow);
            return analysis;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            // Missing, damaged or from an older rexplayer: analysed again.
            return null;
        }
    }

    /// <summary>Keeps <paramref name="analysis"/>; a cache that cannot be written is only slower.</summary>
    public void Set(EpisodeAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        var key = PlayerMemory.KeyFor(analysis.Path);
        if (Folder is null)
        {
            lock (_gate)
            {
                _memory[key] = analysis;
            }

            return;
        }

        try
        {
            Directory.CreateDirectory(Folder);
            var file = Path.Combine(Folder, key + Extension);
            var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var stream = File.Create(temporary))
            {
                Write(stream, analysis);
            }

            File.Move(temporary, file, overwrite: true);
            Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Read-only or full disk: the next run analyses again.
        }
    }

    private void Trim()
    {
        var files = new DirectoryInfo(Folder!).GetFiles("*" + Extension);
        foreach (var old in files.OrderByDescending(file => file.LastWriteTimeUtc).Skip(Limit))
        {
            old.Delete();
        }
    }

    /// <summary>The format: a header, then landmarks as hash and frame step, and picture samples, compressed.</summary>
    internal static void Write(Stream output, EpisodeAnalysis analysis)
    {
        using var compressed = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true);
        using var writer = new BinaryWriter(compressed, Encoding.UTF8);
        writer.Write(Magic);
        writer.Write(SectionAnalysis.FeatureVersion);
        writer.Write(analysis.Signature);
        writer.Write(analysis.Duration.Ticks);
        Landmarks(writer, analysis.Opening);
        Landmarks(writer, analysis.Closing);
        writer.Write(analysis.Frames.Count);
        foreach (var frame in analysis.Frames)
        {
            writer.Write(frame.At.Ticks);
            writer.Write(frame.MeanLuma);
            writer.Write(frame.DarkFraction);
            writer.Write(frame.BrightFraction);
            writer.Write(frame.Strokes);
            writer.Write7BitEncodedInt(frame.Rows.Count);
            foreach (var row in frame.Rows)
            {
                writer.Write(row);
            }
        }
    }

    /// <summary>The analysis in <paramref name="input"/>, or null when it is from another version.</summary>
    internal static EpisodeAnalysis? Read(Stream input, string path)
    {
        using var compressed = new ZLibStream(input, CompressionMode.Decompress, leaveOpen: true);
        using var reader = new BinaryReader(compressed, Encoding.UTF8);
        if (reader.ReadInt32() != Magic || reader.ReadInt32() != SectionAnalysis.FeatureVersion)
        {
            return null;
        }

        var signature = reader.ReadString();
        var duration = TimeSpan.FromTicks(reader.ReadInt64());
        var opening = Landmarks(reader);
        var closing = Landmarks(reader);
        var frames = new CreditFrameSample[Count(reader.ReadInt32())];
        for (var i = 0; i < frames.Length; i++)
        {
            var at = TimeSpan.FromTicks(reader.ReadInt64());
            var (mean, dark, bright, strokes) = (reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble());
            var rows = new float[Count(reader.Read7BitEncodedInt())];
            for (var row = 0; row < rows.Length; row++)
            {
                rows[row] = reader.ReadSingle();
            }

            frames[i] = new CreditFrameSample(at, mean, dark, bright, rows, strokes);
        }

        return new EpisodeAnalysis(path, signature, duration, opening, closing, frames);
    }

    private static void Landmarks(BinaryWriter writer, IReadOnlyList<AudioLandmark> landmarks)
    {
        writer.Write(landmarks.Count);
        var previous = 0;
        foreach (var landmark in landmarks)
        {
            writer.Write(landmark.Hash);
            // Landmarks come in time order, so the step from the last one is small and never negative.
            writer.Write7BitEncodedInt(landmark.Frame - previous);
            previous = landmark.Frame;
        }
    }

    private static AudioLandmark[] Landmarks(BinaryReader reader)
    {
        var landmarks = new AudioLandmark[Count(reader.ReadInt32())];
        var frame = 0;
        for (var i = 0; i < landmarks.Length; i++)
        {
            var hash = reader.ReadUInt32();
            frame += reader.Read7BitEncodedInt();
            landmarks[i] = new AudioLandmark(hash, frame);
        }

        return landmarks;
    }

    /// <summary>A count read from a file; a damaged one must not allocate gigabytes.</summary>
    private static int Count(int value) =>
        value is >= 0 and <= 4_000_000 ? value : throw new InvalidDataException("The cached analysis is damaged.");
}
