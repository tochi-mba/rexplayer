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

        foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
        {
            if (line.Length == 0)
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
            Append(key, value);
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

            Append(key, null);
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

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
            AtomicFile.Write(Path, Encoding.UTF8.GetBytes(text.ToString()));
            _lines = _values.Count;
            Skipped = 0;
        }
    }

    private void Append(string key, string? value)
    {
        if (Path is null)
        {
            return;
        }

        // Rewritten once the log is mostly lines that no longer count.
        if (_lines >= 64 && _lines > _values.Count * 4)
        {
            Compact();
            return;
        }

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
        using var stream = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
        var bytes = Encoding.UTF8.GetBytes(Line(key, value) + "\n");
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
        _lines++;
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

    private static (string Key, string? Value)? Parse(string line)
    {
        if (line.Length < 10 || line[8] != ' ' || !uint.TryParse(line.AsSpan(0, 8), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var checksum))
        {
            return null;
        }

        var json = Encoding.UTF8.GetBytes(line[9..]);
        if (Crc.Crc32.Compute(json) != checksum)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("k", out var key) && key.ValueKind == JsonValueKind.String
                ? (key.GetString()!, root.TryGetProperty("v", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
