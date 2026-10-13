using System.Buffers.Binary;
using Rex.Media.Video;

namespace Rex.Media.Tests.Video;

public sealed class CompiledShaderCacheTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rexplayer-shaders-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    /// <summary>A stand-in compiled shader: a DXBC header that gives its own length, then <paramref name="body"/>.</summary>
    private static byte[] Shader(byte body, int length = 40)
    {
        var code = new byte[length];
        "DXBC"u8.CopyTo(code);
        BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(24), (uint)length);
        code[^1] = body;
        return code;
    }

    [Fact]
    public void AShaderIsCompiledOnceAndThenReadBack()
    {
        var cache = new CompiledShaderCache(_folder);
        var compiled = 0;

        var first = cache.GetOrCompile("float4 main() : SV_Target { return 1; }", "yuv", "ps_4_0", () => { compiled++; return Shader(7); });
        var again = new CompiledShaderCache(_folder).GetOrCompile("float4 main() : SV_Target { return 1; }", "yuv", "ps_4_0", () => { compiled++; return Shader(8); });

        Assert.Equal(1, compiled);
        Assert.Equal(first, again);
        Assert.Single(Directory.GetFiles(_folder));
    }

    [Fact]
    public void ANewSourceEntryOrProfileIsFiledApart()
    {
        var key = CompiledShaderCache.Key("source", "yuv", "ps_4_0");

        Assert.Equal(64, key.Length);
        Assert.Equal(key, CompiledShaderCache.Key("source", "yuv", "ps_4_0"));
        Assert.NotEqual(key, CompiledShaderCache.Key("source 2", "yuv", "ps_4_0"));
        Assert.NotEqual(key, CompiledShaderCache.Key("source", "bgra", "ps_4_0"));
        Assert.NotEqual(key, CompiledShaderCache.Key("source", "yuv", "ps_5_0"));
        Assert.Throws<ArgumentNullException>(() => CompiledShaderCache.Key(null!, "yuv", "ps_4_0"));
        Assert.Throws<ArgumentNullException>(() => CompiledShaderCache.Key("source", null!, "ps_4_0"));
        Assert.Throws<ArgumentNullException>(() => CompiledShaderCache.Key("source", "yuv", null!));
    }

    [Fact]
    public void OnlyAWholeCompiledShaderIsTrusted()
    {
        Assert.True(CompiledShaderCache.IsWhole(Shader(1)));
        Assert.False(CompiledShaderCache.IsWhole(Shader(1)[..39]));
        Assert.False(CompiledShaderCache.IsWhole(new byte[40]));
        Assert.False(CompiledShaderCache.IsWhole("DXBC"u8));
    }

    [Fact]
    public void ADamagedFileIsCompiledAgainAndReplaced()
    {
        var written = new Dictionary<string, byte[]>();
        var cache = new CompiledShaderCache(_folder, path => written.GetValueOrDefault(path), (path, code) => written[path] = code);
        var path = Path.Combine(_folder, CompiledShaderCache.Key("s", "vs", "vs_4_0") + ".dxbc");
        written[path] = Shader(1)[..30];

        var code = cache.GetOrCompile("s", "vs", "vs_4_0", () => Shader(2));

        Assert.Equal(2, code[^1]);
        Assert.Equal(code, written[path]);
    }

    [Fact]
    public void AFolderThatCannotBeWrittenOnlyCostsTheNextRunItsTime()
    {
        var compiled = 0;
        var cache = new CompiledShaderCache(_folder, _ => null, (_, _) => throw new UnauthorizedAccessException());
        var locked = new CompiledShaderCache(_folder, _ => null, (_, _) => throw new IOException());

        Assert.Equal(3, cache.GetOrCompile("s", "vs", "vs_4_0", () => { compiled++; return Shader(3); })[^1]);
        Assert.Equal(3, locked.GetOrCompile("s", "vs", "vs_4_0", () => { compiled++; return Shader(3); })[^1]);
        Assert.Equal(2, compiled);
    }

    [Fact]
    public void AnUnreadableFileIsCompiledAgain()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, CompiledShaderCache.Key("s", "vs", "vs_4_0") + ".dxbc");
        Directory.CreateDirectory(path);

        var code = new CompiledShaderCache(_folder).GetOrCompile("s", "vs", "vs_4_0", () => Shader(4));

        Assert.Equal(4, code[^1]);
    }

    [Fact]
    public void ArgumentsAreRequired()
    {
        Assert.ThrowsAny<ArgumentException>(() => new CompiledShaderCache(""));
        Assert.Throws<ArgumentNullException>(() => new CompiledShaderCache(_folder).GetOrCompile("s", "vs", "vs_4_0", null!));
    }
}
