using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rex.Media.AppCore.Transcription;

/// <summary>
/// Who a media file is, for its transcript (SUB-20): its size and when it last changed, not its
/// name, so a file moved or renamed is still known and a changed one is not. The library knows
/// both for every file without touching the disk.
/// </summary>
public readonly record struct TranscriptSignature(long Size, long ModifiedTicks)
{
    public static TranscriptSignature Of(long size, DateTime modifiedUtc) => new(size, modifiedUtc.Ticks);

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Size}-{ModifiedTicks}");
}

/// <summary>Where a file's transcript stands.</summary>
public enum TranscriptState
{
    /// <summary>Some of it is heard; hearing goes on where it stopped.</summary>
    Partial,

    /// <summary>All of it is heard (possibly no words, when nobody speaks).</summary>
    Done,

    /// <summary>Hearing it failed; <see cref="TranscriptRecord.NextTry"/> says when it is tried again, if ever.</summary>
    Failed,

    /// <summary>It has no sound to hear, so it is never tried.</summary>
    NoSound,
}

/// <summary>What is known of one file's transcript.</summary>
public sealed record TranscriptRecord
{
    /// <summary>Where the file was last seen.</summary>
    public required string Path { get; init; }

    public required TranscriptSignature Signature { get; init; }

    public required string Model { get; init; }

    public required TranscriptState State { get; init; }

    /// <summary>How many words were heard.</summary>
    public int Words { get; init; }

    /// <summary>How much of it is heard, 0 to 1.</summary>
    public double Share { get; init; }

    /// <summary>Why it failed, in words a person reads.</summary>
    public string? Reason { get; init; }

    /// <summary>How many times in a row it failed.</summary>
    public int Attempts { get; init; }

    /// <summary>When a failed file is tried again; null when it is not, until the user asks.</summary>
    public DateTimeOffset? NextTry { get; init; }
}

/// <summary>
/// What is known of every transcript made (SUB-20): one record a file signature, kept as one JSON
/// file written whole or not at all. Read from disk the first time it is asked, never at start-up.
/// Safe to use from the worker and the window at once.
/// </summary>
public sealed class TranscriptionLedger
{
    private readonly object _gate = new();
    private readonly object _writing = new();
    private readonly string _path;
    private long _version;
    private long _written;
    private Dictionary<TranscriptSignature, TranscriptRecord>? _records;

    public TranscriptionLedger(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    /// <summary>The ledger's usual place under the player's data folder.</summary>
    public static string DefaultPath(string dataRoot)
    {
        ArgumentNullException.ThrowIfNull(dataRoot);
        return System.IO.Path.Combine(dataRoot, "cache", "transcription.json");
    }

    /// <summary>Every record, in no order.</summary>
    public IReadOnlyList<TranscriptRecord> All
    {
        get
        {
            lock (_gate)
            {
                return [.. Records.Values];
            }
        }
    }

    public TranscriptRecord? Find(TranscriptSignature signature)
    {
        lock (_gate)
        {
            return Records.GetValueOrDefault(signature);
        }
    }

    /// <summary>Keeps <paramref name="record"/> in place of any for its signature, and writes the ledger.</summary>
    public void Set(TranscriptRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        string json;
        long version;
        lock (_gate)
        {
            Records[record.Signature] = record;
            (json, version) = Snapshot();
        }

        Write(json, version);
    }

    /// <summary>Forgets the records <paramref name="forget"/> picks; gives them.</summary>
    public IReadOnlyList<TranscriptRecord> Remove(Func<TranscriptRecord, bool> forget)
    {
        ArgumentNullException.ThrowIfNull(forget);
        List<TranscriptRecord> gone;
        string json;
        long version;
        lock (_gate)
        {
            gone = [.. Records.Values.Where(forget)];
            foreach (var record in gone)
            {
                Records.Remove(record.Signature);
            }

            (json, version) = Snapshot();
        }

        if (gone.Count > 0)
        {
            Write(json, version);
        }

        return gone;
    }

    /// <summary>The records, read the first time they are needed; a missing or damaged file is an empty ledger. Called holding the gate.</summary>
    private Dictionary<TranscriptSignature, TranscriptRecord> Records => _records ??= Read();

    private Dictionary<TranscriptSignature, TranscriptRecord> Read()
    {
        try
        {
            var records = JsonSerializer.Deserialize(File.ReadAllText(_path), LedgerJson.Default.ListTranscriptRecord) ?? [];
            return records.Where(record => record is { Path: not null, Model: not null }).DistinctBy(record => record.Signature).ToDictionary(record => record.Signature);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The ledger as text, numbered so an older write never lands after a newer one. Called holding the gate.</summary>
    private (string Json, long Version) Snapshot() => (JsonSerializer.Serialize([.. Records.Values], LedgerJson.Default.ListTranscriptRecord), ++_version);

    /// <summary>Writes the ledger outside the gate, so a reader is never held up by the disk.</summary>
    private void Write(string json, long version)
    {
        lock (_writing)
        {
            if (version > _written)
            {
                _written = version;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_path))!);
                File.WriteAllText(_path + ".part", json);
                File.Move(_path + ".part", _path, overwrite: true);
            }
        }
    }
}

[JsonSerializable(typeof(List<TranscriptRecord>), TypeInfoPropertyName = "ListTranscriptRecord")]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, UseStringEnumConverter = true)]
internal sealed partial class LedgerJson : JsonSerializerContext;
