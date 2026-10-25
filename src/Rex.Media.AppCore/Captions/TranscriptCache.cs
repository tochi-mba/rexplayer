using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Rex.Media.AppCore.Transcription;

namespace Rex.Media.AppCore.Captions;

/// <summary>
/// Transcripts kept on disk (SUB-19, SUB-20), one JSON file a media file, so speech is heard once.
/// A transcript is keyed by the media's signature (its size and last change, not its name), and by
/// the model that heard it: a file moved or renamed keeps its transcript, a changed file or
/// another model is heard again. A transcript still being heard is kept beside it as
/// <c>.partial.json</c>, so hearing goes on where it stopped. Nothing here is read until asked.
/// </summary>
public sealed partial class TranscriptCache
{
    /// <summary>The cache's layout; a change here forgets every transcript made before.</summary>
    public const int Version = 2;

    private const string PartialSuffix = ".partial.json";

    public TranscriptCache(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = root;
    }

    public string Root { get; }

    /// <summary>The cache's usual place under the player's data folder.</summary>
    public static string DefaultRoot(string dataRoot)
    {
        ArgumentNullException.ThrowIfNull(dataRoot);
        return Path.Combine(dataRoot, "cache", "transcripts");
    }

    /// <summary>Where the transcript of <paramref name="media"/> by <paramref name="model"/> lives, whether or not it exists.</summary>
    public string PathFor(string media, string model)
    {
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(model);
        var info = new FileInfo(media);

        // A file that is not there has no signature; its name stands in.
        return info.Exists ? PathFor(TranscriptSignature.Of(info.Length, info.LastWriteTimeUtc), model) : Place($"v{Version}|{model}|{Path.GetFullPath(media)}");
    }

    /// <summary>Where the transcript of the file with <paramref name="signature"/> by <paramref name="model"/> lives.</summary>
    public string PathFor(TranscriptSignature signature, string model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Place(string.Create(CultureInfo.InvariantCulture, $"v{Version}|{model}|{signature}"));
    }

    /// <summary>The transcript kept for <paramref name="media"/> by <paramref name="model"/>, or null when there is none worth reading.</summary>
    public Transcript? Load(string media, string model)
    {
        var path = PathFor(media, model);
        if (Read(path) is { } kept)
        {
            return kept;
        }

        // Kept by name before version 2: moved to its signature the first time it is read.
        var info = new FileInfo(media);
        var legacy = Place(string.Create(CultureInfo.InvariantCulture, $"v1|{model}|{Path.GetFullPath(media)}|{(info.Exists ? info.Length : 0)}|{(info.Exists ? info.LastWriteTimeUtc.Ticks : 0)}"));
        if (Read(legacy) is not { } old)
        {
            return null;
        }

        Save(media, old);
        File.Delete(legacy);
        return old;
    }

    /// <summary>Keeps <paramref name="transcript"/> for <paramref name="media"/>; the file lands whole or not at all.</summary>
    public void Save(string media, Transcript transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        Write(PathFor(media, transcript.Model), transcript.ToJson());
    }

    /// <summary>Forgets the transcript of <paramref name="media"/> by <paramref name="model"/>, if there is one, and any part heard.</summary>
    public void Forget(string media, string model)
    {
        var path = PathFor(media, model);
        Delete(path);
        Delete(Partial(path));
    }

    /// <summary>The part of a transcript heard so far for <paramref name="media"/> by <paramref name="model"/>, or null.</summary>
    public ProgressiveTranscript? LoadPartial(string media, string model)
    {
        var path = Partial(PathFor(media, model));
        return File.Exists(path) && Text(path) is { } json ? ProgressiveTranscript.FromJson(json) : null;
    }

    /// <summary>Keeps the part of a transcript heard so far, to go on with later.</summary>
    public void SavePartial(string media, ProgressiveTranscript transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        Write(Partial(PathFor(media, transcript.Model)), transcript.ToJson());
    }

    /// <summary>Forgets the part heard of <paramref name="media"/> by <paramref name="model"/>, once the whole is kept.</summary>
    public void ForgetPartial(string media, string model) => Delete(Partial(PathFor(media, model)));

    /// <summary>Forgets what was kept for the file with <paramref name="signature"/> by <paramref name="model"/>.</summary>
    public void Forget(TranscriptSignature signature, string model)
    {
        var path = PathFor(signature, model);
        Delete(path);
        Delete(Partial(path));
    }

    /// <summary>How many transcripts are kept and how many bytes they take.</summary>
    public (int Files, long Bytes) Usage()
    {
        if (!Directory.Exists(Root))
        {
            return (0, 0);
        }

        var files = new DirectoryInfo(Root).GetFiles("*.json");
        return (files.Length, files.Sum(file => file.Length));
    }

    /// <summary>Forgets every transcript kept; gives how many bytes that freed.</summary>
    public long Clear()
    {
        var (_, bytes) = Usage();
        if (Directory.Exists(Root))
        {
            foreach (var file in Directory.GetFiles(Root, "*.json").Concat(Directory.GetFiles(Root, "*.part")))
            {
                File.Delete(file);
            }
        }

        return bytes;
    }

    private string Place(string identity) => Path.Combine(Root, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..32] + ".json");

    private static string Partial(string path) => path[..^".json".Length] + PartialSuffix;

    private static Transcript? Read(string path) => File.Exists(path) && Text(path) is { } json ? Transcript.FromJson(json) : null;

    private static string? Text(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private void Write(string path, string text)
    {
        Directory.CreateDirectory(Root);
        var partial = path + ".part";
        File.WriteAllText(partial, text);
        File.Move(partial, path, overwrite: true);
    }

    private static void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
