// Spec: Windows Media Player playlists (WPL, and ZPL alike): SMIL 2.0 documents whose body/seq holds one media element per entry, its src attribute the location.
using System.Xml;
using System.Xml.Linq;

namespace Rex.Media.Library.Playlists;

/// <summary>WPL and ZPL playlists (PLF-06).</summary>
internal static class Wpl
{
    public static IReadOnlyList<PlaylistEntry> Parse(string text, string folder)
    {
        XDocument document;
        try
        {
            // Files start with a <?wpl version="1.0"?> instruction, which XML takes as a processing instruction.
            document = XDocument.Parse(text);
        }
        catch (XmlException ex)
        {
            throw new FormatException("The playlist is not well-formed XML: " + ex.Message, ex);
        }

        return
        [
            .. document.Descendants()
                .Where(element => element.Name.LocalName.Equals("media", StringComparison.OrdinalIgnoreCase))
                .Select(element => PlaylistPaths.Resolve(element.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals("src", StringComparison.OrdinalIgnoreCase))?.Value, folder))
                .OfType<string>()
                .Select(location => new PlaylistEntry(location)),
        ];
    }
}
