using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Rex.Media.Video;

/// <summary>
/// Keeps compiled shaders on disk between runs. Compiling the picture shaders takes seconds, and
/// they change only when rexplayer does, so each is filed under a hash of its source, entry point
/// and profile: a new version files its shaders anew and never reads an old one. A file that is
/// not a whole compiled shader (cut short by a crash, or damaged) is compiled again and replaced.
/// </summary>
public sealed class CompiledShaderCache
{
    private readonly string _folder;
    private readonly Func<string, byte[]?> _read;
    private readonly Action<string, byte[]> _write;

    /// <param name="folder">Where the compiled shaders are kept.</param>
    /// <param name="read">Reads a file, or null when it is missing or unreadable; by default from disk.</param>
    /// <param name="write">Writes a file whole; by default to disk, through a temporary file renamed into place.</param>
    public CompiledShaderCache(string folder, Func<string, byte[]?>? read = null, Action<string, byte[]>? write = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        _folder = folder;
        _read = read ?? ReadFile;
        _write = write ?? WriteFile;
    }

    /// <summary>The name a compiled shader is filed under.</summary>
    public static string Key(string source, string entry, string profile)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(profile);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile + "\n" + entry + "\n" + source))).ToLowerInvariant();
    }

    /// <summary>
    /// Whether <paramref name="code"/> is a whole compiled shader: a DXBC container whose header
    /// gives its own length.
    /// </summary>
    public static bool IsWhole(ReadOnlySpan<byte> code) =>
        code.Length >= 32 && code[..4].SequenceEqual("DXBC"u8) && BinaryPrimitives.ReadUInt32LittleEndian(code[24..]) == code.Length;

    /// <summary>The shader compiled before, or <paramref name="compile"/>'s, which is then kept; failing to keep it costs only the next run's time.</summary>
    public byte[] GetOrCompile(string source, string entry, string profile, Func<byte[]> compile)
    {
        ArgumentNullException.ThrowIfNull(compile);
        var path = Path.Combine(_folder, Key(source, entry, profile) + ".dxbc");
        if (_read(path) is { } kept && IsWhole(kept))
        {
            return kept;
        }

        var code = compile();
        try
        {
            _write(path, code);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Compiled again next time.
        }

        return code;
    }

    private static byte[]? ReadFile(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WriteFile(string path, byte[] code)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(temporary, code);
        File.Move(temporary, path, overwrite: true);
    }
}
