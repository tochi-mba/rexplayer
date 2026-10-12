using System.Net;
using System.Security.Cryptography;
using Rex.Media.Vision;

namespace Rex.Media.Tests.Vision;

/// <summary>A folder under the system's temporary folder, deleted when the test ends.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rexplayer-vision-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

/// <summary>An HTTP server in memory: each request is answered by the function given.</summary>
internal sealed class FakeServer(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(request);
        return Task.FromResult(answer(request));
    }

    /// <summary>Serves files by URL, honouring a byte range the way the authors' hosts do.</summary>
    public static FakeServer Serving(IReadOnlyDictionary<Uri, byte[]> files) => new(request =>
    {
        if (!files.TryGetValue(request.RequestUri!, out var bytes))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        if (request.Headers.Range?.Ranges.Single().From is { } from)
        {
            return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(bytes[(int)from..]) };
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    });
}

/// <summary>Small models whose bytes and hashes the tests know.</summary>
internal static class TestModels
{
    public static byte[] Bytes(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    public static ModelFile File(string name, byte[] bytes) =>
        new(name, new Uri("https://models.example/" + name), bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)));

    public static VisionModel Model(string id, params ModelFile[] files) => Model(id, "MIT", files);

    public static VisionModel Model(string id, string licence, params ModelFile[] files) =>
        new(id, ModelPurpose.Inpainting, id.ToUpperInvariant(), "Tests", licence, new Uri("https://models.example/LICENSE"), "bytes", files);

    /// <summary>Writes the files of <paramref name="model"/> into the store as if installed.</summary>
    public static void Install(ModelStore store, VisionModel model, IReadOnlyDictionary<string, byte[]> contents)
    {
        Directory.CreateDirectory(store.Folder(model));
        foreach (var file in model.Files)
        {
            System.IO.File.WriteAllBytes(store.PathOf(model, file), contents[file.Name]);
        }

        store.MarkInstalled(model);
    }
}
