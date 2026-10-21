using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.Library;

/// <summary>
/// The player's own small store of what it remembers (LIB-11): resume points, bookmarks, recent
/// media, quick slots and the queue, as text keys and values. Each change is one line appended to
/// a log and flushed to the disk at once, with a checksum, so a crash or power cut mid-write can
/// lose at most that last change: a line that does not check out is skipped when the log is read.
/// Once the log holds many more lines than live values it is rewritten in one atomic step.
/// </summary>
public sealed class RexStore
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private int _lines;

    private RexStore(string? path) => Path = path;

    /// <summary>The log on disk, or null for a store kept only in memory.</summary>
    public string? Path { get; }

    /// <summary>Lines of the log that were damaged and skipped when it was read.</summary>
    public int Skipped { get; private set; }

    /// <summary>
    /// Why the last change could not reach the disk (a full disk, the log locked by another
    /// window), or null once it could. Changes are kept in memory meanwhile, and the next change
    /// that can be written rewrites the whole log with them.
    /// </summary>
    public string? WriteProblem { get; private set; }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _values.Count;
            }
        }
    }

    /// <summary>A store that forgets everything when the player closes, for tests and for when there is nowhere to keep it.</summary>
    public static RexStore InMemory() => new(null);

    /// <summary>Reads the store at <paramref name="path"/>; a missing file is an empty store, made on the first change.</summary>
    public static RexStore Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var store = new RexStore(path);
        if (!File.Exists(path))
        {
            return store;
        }

        // Read as bytes and checked line by line where they lie: a store can hold records of many
        // megabytes, and decoding each line to text only to encode it again for its checksum
        // doubled the work of every launch.
        ReadOnlySpan<byte> rest = File.ReadAllBytes(path);
        rest = rest.StartsWith(Utf8Bom) ? rest[Utf8Bom.Length..] : rest;
        while (!rest.IsEmpty)
        {
            var end = rest.IndexOf((byte)'\n');
            var line = end < 0 ? rest : rest[..end];
            rest = end < 0 ? [] : rest[(end + 1)..];
            line = line.EndsWith((byte)'\r') ? line[..^1] : line;
            if (line.IsEmpty)
            {
                continue;
            }

            if (Parse(line) is not { } record)
            {
                store.Skipped++;
                continue;
            }

            store._lines++;
            if (record.Value is null)
            {
                store._values.Remove(record.Key);
            }
            else
            {
                store._values[record.Key] = record.Value;
            }
        }

        return store;
    }

    public string? Get(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (_gate)
        {
            return _values.GetValueOrDefault(key);
        }
    }

    /// <summary>Every value whose key starts with <paramref name="prefix"/>, in key order.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> WithPrefix(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        lock (_gate)
        {
            return [.. _values.Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal)).OrderBy(pair => pair.Key, StringComparer.Ordinal)];
        }
    }

    public void Set(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        lock (_gate)
        {
            if (_values.TryGetValue(key, out var old) && old == value)
            {
                return;
            }

            _values[key] = value;
            Append([(key, value)]);
        }
    }

    /// <summary>
    /// Makes several changes in one write to the disk (a value of null forgets its key), for the
    /// library's thousands of files. Each change is still its own checked line, so a crash part-way
    /// keeps every change before the one it cut.
    /// </summary>
    public void SetMany(IReadOnlyCollection<KeyValuePair<string, string?>> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        lock (_gate)
        {
            var lines = new List<(string Key, string? Value)>();
            foreach (var (key, value) in changes)
            {
                ArgumentNullException.ThrowIfNull(key);
                if (value is null ? _values.Remove(key) : !(_values.TryGetValue(key, out var old) && old == value))
                {
                    if (value is not null)
                    {
                        _values[key] = value;
                    }

                    lines.Add((key, value));
                }
            }

            Append(lines);
        }
    }

    /// <summary>Forgets <paramref name="key"/>; false when it was not kept.</summary>
    public bool Remove(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (_gate)
        {
            if (!_values.Remove(key))
            {
                return false;
            }

            Append([(key, null)]);
            return true;
        }
    }

    /// <summary>Forgets every key starting with <paramref name="prefix"/>, and says how many there were.</summary>
    public int RemovePrefix(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        lock (_gate)
        {
            var keys = _values.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            foreach (var key in keys)
            {
                _values.Remove(key);
            }

            if (keys.Count > 0)
            {
                Compact();
            }

            return keys.Count;
        }
    }

    /// <summary>Rewrites the log as just the live values, replacing the old one in one step.</summary>
    public void Compact()
    {
        lock (_gate)
        {
            if (Path is null)
            {
                return;
            }

            var text = new StringBuilder();
            foreach (var (key, value) in _values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                text.Append(Line(key, value)).Append('\n');
            }

            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
                AtomicFile.Write(Path, Encoding.UTF8.GetBytes(text.ToString()));
                _lines = _values.Count;
                Skipped = 0;
                WriteProblem = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                WriteProblem = ex.Message;
            }
        }
    }

    private void Append(IReadOnlyList<(string Key, string? Value)> changes)
    {
        if (Path is null || changes.Count == 0)
        {
            return;
        }

        // Rewritten once the log is mostly lines that no longer count, or when earlier changes
        // could not be written: the rewrite holds every live value, theirs included.
        if (WriteProblem is not null || (_lines >= 64 && _lines > _values.Count * 4))
        {
            Compact();
            return;
        }

        var text = new StringBuilder();
        foreach (var (key, value) in changes)
        {
            text.Append(Line(key, value)).Append('\n');
        }

        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
            using var stream = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
            stream.Write(Encoding.UTF8.GetBytes(text.ToString()));
            stream.Flush(flushToDisk: true);
            _lines += changes.Count;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A full disk or a log another window holds must not fail what the player was doing:
            // the change stays in memory and goes to the disk with the next one that can.
            WriteProblem = ex.Message;
        }
    }

    /// <summary>"checksum json": the CRC-32 of the JSON in hexadecimal, a space, then the record.</summary>
    private static string Line(string key, string? value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("k", key);
            if (value is not null)
            {
                json.WriteString("v", value);
            }

            json.WriteEndObject();
        }

        return Crc.Crc32.Compute(buffer.WrittenSpan).ToString("x8", CultureInfo.InvariantCulture) + " " + Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// One line's record: its key and value (null forgets the key); null when the line is damaged
    /// or is not a record. The JSON is read in one pass, without building a document of it.
    /// </summary>
    private static (string Key, string? Value)? Parse(ReadOnlySpan<byte> line)
    {
        if (line.Length < 10 || line[8] != (byte)' ' || !uint.TryParse(line[..8], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var checksum))
        {
            return null;
        }

        var json = line[9..];
        if (Crc.Crc32.Compute(json) != checksum)
        {
            return null;
        }

        try
        {
            return ReadRecord(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>An object with a text "k" and, unless it forgets the key, a text "v"; anything else in it is ignored.</summary>
    private static (string Key, string? Value)? ReadRecord(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(json);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return null;
        }

        string? key = null;
        string? value = null;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var name = reader.ValueTextEquals("k"u8) ? 'k' : reader.ValueTextEquals("v"u8) ? 'v' : ' ';
            reader.Read();
            var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            (key, value) = name switch { 'k' => (text, value), 'v' => (key, text), _ => (key, value) };
            reader.Skip();
        }

        // Nothing may follow the object: the reader throws on anything after it.
        _ = reader.Read();
        return key is null ? null : (key, value);
    }
}
