using System.Globalization;
using System.Security.Cryptography;

namespace Rex.Media.Vision;

/// <summary>
/// Where downloaded models live: <c>%LocalAppData%\REX\rexplayer\models\&lt;model id&gt;\</c>, one folder
/// per model, with an <c>installed.txt</c> manifest listing the SHA-256 of each file that was checked
/// when it was installed. A file still downloading carries a <c>.part</c> suffix and is never loaded.
/// </summary>
public sealed class ModelStore
{
    /// <summary>The manifest written once every file of a model has matched its pinned hash.</summary>
    public const string ManifestName = "installed.txt";

    /// <summary>The suffix of a file that has not finished downloading.</summary>
    public const string PartialSuffix = ".part";

    public ModelStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
    }

    /// <summary>The folder holding every model's folder.</summary>
    public string Root { get; }

    /// <summary>The store under a local application data folder, as the app and the tests find it.</summary>
    public static string DefaultRoot(string localApplicationData) =>
        Path.Combine(localApplicationData, "REX", "rexplayer", "models");

    public string Folder(VisionModel model) => Path.Combine(Root, model.Id);

    public string PathOf(VisionModel model, ModelFile file) => Path.Combine(Folder(model), file.Name);

    public string PartialPathOf(VisionModel model, ModelFile file) => PathOf(model, file) + PartialSuffix;

    /// <summary>
    /// What is on disk for <paramref name="model"/>: installed when the manifest pins exactly the
    /// catalogue's hashes and every file has its pinned length; an update when a manifest exists for
    /// other hashes (an older catalogue) and its files are all present; otherwise nothing usable.
    /// </summary>
    public InstallPhase Inspect(VisionModel model)
    {
        var recorded = ReadManifest(model);
        if (recorded.Count == 0)
        {
            return InstallPhase.NotInstalled;
        }

        var current = model.Files.All(file => recorded.TryGetValue(file.Name, out var hash) && hash == file.Sha256);
        if (current)
        {
            return model.Files.All(file => LengthOf(PathOf(model, file)) == file.Bytes) ? InstallPhase.Installed : InstallPhase.NotInstalled;
        }

        return recorded.Keys.All(name => File.Exists(Path.Combine(Folder(model), name))) ? InstallPhase.UpdateAvailable : InstallPhase.NotInstalled;
    }

    /// <summary>Whether this file is already in place with its pinned length and hash, so it need not be fetched again.</summary>
    public bool HasVerified(VisionModel model, ModelFile file)
    {
        var path = PathOf(model, file);
        return LengthOf(path) == file.Bytes && HashOf(path) == file.Sha256;
    }

    /// <summary>Bytes of this file already downloaded towards a resume.</summary>
    public long PartialBytes(VisionModel model, ModelFile file) => Math.Max(0, LengthOf(PartialPathOf(model, file)));

    /// <summary>Bytes still to download for <paramref name="model"/>, counting finished and partial files.</summary>
    public long Remaining(VisionModel model) =>
        model.Files.Sum(file => LengthOf(PathOf(model, file)) == file.Bytes ? 0 : file.Bytes - Math.Min(file.Bytes, PartialBytes(model, file)));

    /// <summary>Records that every file matched its hash: the model is installed from now on.</summary>
    public void MarkInstalled(VisionModel model)
    {
        Directory.CreateDirectory(Folder(model));
        var manifest = Path.Combine(Folder(model), ManifestName);
        var temporary = manifest + ".new";
        File.WriteAllLines(temporary, model.Files.Select(file => $"{file.Sha256} {file.Name}"));
        File.Move(temporary, manifest, overwrite: true);
    }

    /// <summary>Deletes the model's folder: its files, any partial download and its manifest.</summary>
    public void Remove(VisionModel model)
    {
        var folder = Folder(model);
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>The SHA-256 of a file as lower-case hexadecimal.</summary>
    public static string HashOf(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private Dictionary<string, string> ReadManifest(VisionModel model)
    {
        var manifest = Path.Combine(Folder(model), ManifestName);
        var recorded = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(manifest))
        {
            return recorded;
        }

        foreach (var line in File.ReadAllLines(manifest))
        {
            var parts = line.Split(' ', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && parts[0].Length == 64 && parts[1].Length > 0)
            {
                recorded[parts[1]] = parts[0].ToLower(CultureInfo.InvariantCulture);
            }
        }

        return recorded;
    }

    private static long LengthOf(string path) => File.Exists(path) ? new FileInfo(path).Length : -1;
}
