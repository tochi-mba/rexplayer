using System.Globalization;
using System.Security.Cryptography;
using Rex.Media.Settings;

namespace Rex.Media.AppCore.Captions;

/// <summary>A speech model the user may download (SUB-19, ADR-0020): where it comes from, how big it is, its hash, and its licence.</summary>
public sealed record SpeechModel(string Id, string Name, string FileName, Uri Url, long Bytes, string Sha256, string Licence, string Speed)
{
    /// <summary>The size as people read it.</summary>
    public string Size => string.Create(CultureInfo.InvariantCulture, $"{Bytes / 1_000_000.0:0} MB");
}

/// <summary>
/// The speech models rexplayer knows (SUB-19): the open Whisper models in the ggml form the
/// whisper.cpp project publishes (MIT), English only, at a fixed revision of its model page, each
/// pinned by size and SHA-256. None is bundled; each is downloaded only when the user asks.
/// </summary>
public static class SpeechModels
{
    private const string Repository = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";

    public static SpeechModel TinyEnglish { get; } = new(
        "tiny.en",
        "Tiny (English)",
        "ggml-tiny.en.bin",
        new Uri(Repository + "ggml-tiny.en.bin"),
        77_704_715,
        "921e4cf8686fdd993dcd081a5da5b6c365bfde1162e72b08d75ac75289920b1f",
        "MIT",
        "Hears a minute of speech in a few seconds; misses some words.");

    public static SpeechModel BaseEnglish { get; } = new(
        "base.en",
        "Base (English)",
        "ggml-base.en.bin",
        new Uri(Repository + "ggml-base.en.bin"),
        147_964_211,
        "a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002",
        "MIT",
        "Hears a minute of speech in about ten seconds; good for clear talk.");

    public static SpeechModel SmallEnglish { get; } = new(
        "small.en",
        "Small (English)",
        "ggml-small.en.bin",
        new Uri(Repository + "ggml-small.en.bin"),
        487_614_201,
        "c6138d6d58ecc8322097e0f987c32f1be8bb0a18532a3f88f734d1bbf9c41e5d",
        "MIT",
        "Hears a minute of speech in half a minute; hears best.");

    public static IReadOnlyList<SpeechModel> All { get; } = [TinyEnglish, BaseEnglish, SmallEnglish];

    /// <summary>The model for a setting.</summary>
    public static SpeechModel For(TranscriptModelChoice choice) => choice switch
    {
        TranscriptModelChoice.Tiny => TinyEnglish,
        TranscriptModelChoice.Small => SmallEnglish,
        _ => BaseEnglish,
    };

    /// <summary>The model with this id, or null.</summary>
    public static SpeechModel? Find(string id) => All.FirstOrDefault(model => string.Equals(model.Id, id, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Where speech models live on this computer (<c>models/speech</c> under the player's data
/// folder), which are there, and their download: a byte range resumes a part-done file, the file
/// is written beside its final name as <c>.part</c> and moved into place only when its SHA-256
/// matches; anything else is deleted and reported.
/// </summary>
public sealed class SpeechModelStore
{
    public const string PartialSuffix = ".part";

    private readonly Func<HttpMessageHandler> _handler;

    public SpeechModelStore(string root, Func<HttpMessageHandler>? handler = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = root;
        _handler = handler ?? (() => new HttpClientHandler());
    }

    public string Root { get; }

    /// <summary>The store's usual place under the player's data folder.</summary>
    public static string DefaultRoot(string dataRoot)
    {
        ArgumentNullException.ThrowIfNull(dataRoot);
        return Path.Combine(dataRoot, "models", "speech");
    }

    public string PathOf(SpeechModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Path.Combine(Root, model.FileName);
    }

    /// <summary>Whether the model is here, whole.</summary>
    public bool IsInstalled(SpeechModel model) => new FileInfo(PathOf(model)) is { Exists: true } info && info.Length == model.Bytes;

    /// <summary>How many bytes of a download begun before are here.</summary>
    public long PartialBytes(SpeechModel model) => new FileInfo(PathOf(model) + PartialSuffix) is { Exists: true } info ? info.Length : 0;

    public void Remove(SpeechModel model)
    {
        foreach (var path in new[] { PathOf(model), PathOf(model) + PartialSuffix })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>
    /// Downloads <paramref name="model"/>, going on from a part left before, reporting progress 0 to
    /// 1; the file is kept only when its hash is the one the catalogue names.
    /// </summary>
    public async Task DownloadAsync(SpeechModel model, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (IsInstalled(model))
        {
            progress?.Report(1);
            return;
        }

        Directory.CreateDirectory(Root);
        var partial = PathOf(model) + PartialSuffix;
        var have = PartialBytes(model);
        if (have >= model.Bytes)
        {
            File.Delete(partial);
            have = 0;
        }

        using var client = new HttpClient(_handler(), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        using var request = new HttpRequestMessage(HttpMethod.Get, model.Url);
        if (have > 0)
        {
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(have, null);
        }

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"The model {model.Name} could not be downloaded: the server said {(int)response.StatusCode}.");
        }

        var resumed = have > 0 && response.StatusCode == System.Net.HttpStatusCode.PartialContent;
        if (!resumed)
        {
            have = 0;
        }

        await using (var file = new FileStream(partial, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None))
        await using (var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        {
            var buffer = new byte[1 << 16];
            int read;
            while ((read = await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                have += read;
                progress?.Report(Math.Min(0.999, (double)have / model.Bytes));
            }
        }

        var hash = Convert.ToHexStringLower(await HashAsync(partial, cancellationToken).ConfigureAwait(false));
        if (have != model.Bytes || !string.Equals(hash, model.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(partial);
            throw new InvalidDataException($"The model {model.Name} downloaded was not the one expected (size {have}, hash {hash[..12]}...), so it was deleted.");
        }

        File.Move(partial, PathOf(model), overwrite: true);
        progress?.Report(1);
    }

    private static async Task<byte[]> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
    }
}
