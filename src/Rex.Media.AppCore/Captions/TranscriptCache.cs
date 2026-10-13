using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Rex.Media.AppCore.Captions;

/// <summary>
/// Transcripts kept on disk (SUB-19), one JSON file a media file, so speech is heard once. A
/// transcript is keyed by the media's path, size and last change, and by the model that heard it;
/// a changed file or another model is heard again.
/// </summary>
public sealed class TranscriptCache
{
    /// <summary>The cache's layout; a change here forgets every transcript made before.</summary>
    public const int Version = 1;

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
        var identity = string.Create(CultureInfo.InvariantCulture, $"v{Version}|{model}|{Path.GetFullPath(media)}|{(info.Exists ? info.Length : 0)}|{(info.Exists ? info.LastWriteTimeUtc.Ticks : 0)}");
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..32];
        return Path.Combine(Root, hash + ".json");
    }

    /// <summary>The transcript kept for <paramref name="media"/> by <paramref name="model"/>, or null when there is none worth reading.</summary>
    public Transcript? Load(string media, string model)
    {
        var path = PathFor(media, model);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return Transcript.FromJson(File.ReadAllText(path));
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Keeps <paramref name="transcript"/> for <paramref name="media"/>; the file lands whole or not at all.</summary>
    public void Save(string media, Transcript transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        var path = PathFor(media, transcript.Model);
        Directory.CreateDirectory(Root);
        var partial = path + ".part";
        File.WriteAllText(partial, transcript.ToJson());
        File.Move(partial, path, overwrite: true);
    }

    /// <summary>Forgets the transcript of <paramref name="media"/> by <paramref name="model"/>, if there is one.</summary>
    public void Forget(string media, string model)
    {
        var path = PathFor(media, model);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
