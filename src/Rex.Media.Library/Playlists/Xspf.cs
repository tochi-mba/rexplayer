// Spec: XSPF Version 1 (xspf.org/spec) sections 4.1.1 (playlist, title, trackList) and 4.1.1.2.14 (track: location, title, creator, duration in milliseconds, extension).
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Rex.Media.Library.Playlists;

/// <summary>XSPF playlists (PLF-03), keeping each track's extension data from other programs.</summary>
internal static class Xspf
{
    private static readonly XNamespace Ns = "http://xspf.org/ns/0/";

    public static IReadOnlyList<PlaylistEntry> Parse(string text, string folder)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(text);
        }
        catch (XmlException ex)
        {
            throw new FormatException("The XSPF playlist is not well-formed XML: " + ex.Message, ex);
        }

        var entries = new List<PlaylistEntry>();
        foreach (var track in document.Descendants().Where(element => element.Name.LocalName == "track"))
        {
            var reference = Child(track, "location");
            if (PlaylistPaths.Resolve(reference is null ? null : Uri.UnescapeDataString(reference), folder) is not { } location)
            {
                continue;
            }

            var extensions = track.Elements()
                .Where(element => element.Name.LocalName == "extension" && element.Attribute("application") is not null)
                .GroupBy(element => element.Attribute("application")!.Value, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => string.Concat(group.First().Nodes().Select(node => node.ToString(SaveOptions.DisableFormatting))), StringComparer.Ordinal);
            entries.Add(new PlaylistEntry(location)
            {
                Title = Child(track, "title"),
                Artist = Child(track, "creator"),
                Duration = long.TryParse(Child(track, "duration"), NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds) ? TimeSpan.FromMilliseconds(milliseconds) : null,
                Extensions = extensions,
            });
        }

        return entries;
    }

    public static string Write(IReadOnlyList<PlaylistEntry> entries, string folder, string? title)
    {
        var playlist = new XElement(Ns + "playlist", new XAttribute("version", "1"));
        if (title is not null)
        {
            playlist.Add(new XElement(Ns + "title", title));
        }

        playlist.Add(new XElement(
            Ns + "trackList",
            entries.Select(entry => new XElement(
                Ns + "track",
                new XElement(Ns + "location", LocationUri(entry.Location, folder)),
                entry.Title is null ? null : new XElement(Ns + "title", entry.Title),
                entry.Artist is null ? null : new XElement(Ns + "creator", entry.Artist),
                entry.Duration is { } duration ? new XElement(Ns + "duration", (long)duration.TotalMilliseconds) : null,
                entry.Extensions.Select(extension => new XElement(
                    Ns + "extension",
                    new XAttribute("application", extension.Key),
                    XElement.Parse($"<x xmlns=\"{Ns}\">{extension.Value}</x>").Nodes()))))));
        return new XDocument(new XDeclaration("1.0", "UTF-8", null), playlist).Declaration + "\n" + playlist + "\n";
    }

    private static string? Child(XElement parent, string name) =>
        parent.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value.Trim() is { Length: > 0 } value ? value : null;

    /// <summary>A location as XSPF writes it: a URI, relative to the playlist when the media is beside it.</summary>
    private static string LocationUri(string location, string folder)
    {
        if (PlaylistPaths.IsUrl(location))
        {
            return location;
        }

        var relative = PlaylistPaths.Relative(location, folder);

        // Each part escaped, except a drive's colon.
        var escaped = string.Join('/', relative.Split('\\', '/').Select((part, i) => i == 0 && part.Length == 2 && part[1] == ':' ? part : System.Uri.EscapeDataString(part)));
        if (relative != location)
        {
            return escaped;
        }

        return location.StartsWith(@"\\", StringComparison.Ordinal) ? "file:" + escaped
            : location.StartsWith('/') ? "file://" + escaped
            : "file:///" + escaped;
    }
}
