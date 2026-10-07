using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Rex.Media.Settings;

namespace Rex.Media.AppCore.Updates;

/// <summary>A newer version and where its installer and checksum are.</summary>
public sealed record UpdateOffer(Version Version, Uri Installer, Uri Checksum, Uri ReleasePage, string InstallerName);

/// <summary>
/// The updater's decisions (TOOL-09), apart from the downloading: whether a check is due, what the
/// latest release offers, and whether a downloaded installer is the one the release published. The
/// app does the network and process work; nothing here touches either.
/// </summary>
public static class UpdateCheck
{
    /// <summary>The release the updater asks about: this repository's latest, never a pre-release.</summary>
    public static readonly Uri LatestRelease = new("https://api.github.com/repos/tochi-mba/rexplayer/releases/latest");

    /// <summary>Whether a check is due under <paramref name="cadence"/>, given the last one.</summary>
    public static bool IsDue(UpdateCadence cadence, DateTimeOffset? lastCheck, DateTimeOffset now) => cadence switch
    {
        UpdateCadence.Daily => lastCheck is not { } last || now - last >= TimeSpan.FromDays(1),
        UpdateCadence.Weekly => lastCheck is not { } last || now - last >= TimeSpan.FromDays(7),
        _ => false,
    };

    /// <summary>
    /// The offer in the GitHub API's description of the latest release, when it is newer than
    /// <paramref name="current"/> and carries an installer with its checksum; null otherwise, and
    /// for anything that is not such a description.
    /// </summary>
    public static UpdateOffer? Offer(string releaseJson, string current)
    {
        ArgumentNullException.ThrowIfNull(releaseJson);
        if (!TryVersion(current, out var running))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(releaseJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String
                || !TryVersion(tag.GetString()!.TrimStart('v'), out var latest) || latest <= running
                || (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True)
                || !root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var installerName = $"rexplayer-Setup-{latest}.exe";
            Uri? installer = null, checksum = null;
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.TryGetProperty("name", out var name) && asset.TryGetProperty("browser_download_url", out var url)
                    && Uri.TryCreate(url.GetString(), UriKind.Absolute, out var link) && link.Scheme == Uri.UriSchemeHttps)
                {
                    installer = name.GetString() == installerName ? link : installer;
                    checksum = name.GetString() == installerName + ".sha256" ? link : checksum;
                }
            }

            var page = root.TryGetProperty("html_url", out var html) && Uri.TryCreate(html.GetString(), UriKind.Absolute, out var htmlUri)
                ? htmlUri
                : new Uri("https://github.com/tochi-mba/rexplayer/releases/latest");
            return installer is null || checksum is null ? null : new UpdateOffer(latest, installer, checksum, page, installerName);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            // A property of the wrong kind (a number where a name belongs): not a release we can trust.
            return null;
        }
    }

    /// <summary>
    /// Whether <paramref name="installer"/> matches the checksum file's hash for
    /// <paramref name="installerName"/> ("hash  name", or the hash alone), ignoring case.
    /// </summary>
    public static bool Verify(Stream installer, string checksumFile, string installerName)
    {
        ArgumentNullException.ThrowIfNull(installer);
        ArgumentNullException.ThrowIfNull(checksumFile);
        var expected = checksumFile.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 1 || parts[1].TrimStart('*') == installerName)
            .Select(parts => parts[0])
            .FirstOrDefault();
        if (expected is not { Length: 64 })
        {
            return false;
        }

        return string.Equals(Convert.ToHexString(SHA256.HashData(installer)), expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryVersion(string? text, out Version version)
    {
        version = new Version();
        var parts = text?.Split('.') ?? [];
        if (parts.Length != 3 || !parts.All(part => int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            return false;
        }

        version = new Version(int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture), int.Parse(parts[2], CultureInfo.InvariantCulture));
        return true;
    }
}
