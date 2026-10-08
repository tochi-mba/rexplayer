// Spec: Windows Media metafile (ASX, WAX, WVX) elements: ASX, ENTRY, REF HREF, TITLE, AUTHOR and ENTRYREF HREF, with names in any case.
using System.Net;
using System.Text.RegularExpressions;

namespace Rex.Media.Library.Playlists;

/// <summary>
/// ASX, WAX and WVX playlists (PLF-05). Read leniently, by their elements rather than as XML:
/// names come in any case, and files in the wild are often not well-formed.
/// </summary>
internal static partial class Asx
{
    public static IReadOnlyList<PlaylistEntry> Parse(string text, string folder)
    {
        if (!Root().IsMatch(text))
        {
            throw new FormatException("This is not an ASX playlist.");
        }

        var entries = new List<PlaylistEntry>();
        foreach (Match match in EntryOrReference().Matches(text))
        {
            // An ENTRY plays its first REF; an ENTRYREF names another playlist to play.
            var body = match.Groups["body"];
            var href = body.Success ? Reference().Match(body.Value) is { Success: true } reference ? reference.Groups["href"].Value : null : match.Groups["entryref"].Value;
            if (PlaylistPaths.Resolve(href is null ? null : WebUtility.HtmlDecode(href), folder) is not { } location)
            {
                continue;
            }

            entries.Add(new PlaylistEntry(location)
            {
                Title = body.Success ? Text(Title(), body.Value) : null,
                Artist = body.Success ? Text(Author(), body.Value) : null,
            });
        }

        return entries;
    }

    private static string? Text(Regex element, string body) =>
        element.Match(body) is { Success: true } match && WebUtility.HtmlDecode(match.Groups["text"].Value).Trim() is { Length: > 0 } text ? text : null;

    [GeneratedRegex(@"<asx\b", RegexOptions.IgnoreCase)]
    private static partial Regex Root();

    [GeneratedRegex(@"<entry\b[^>]*>(?<body>.*?)</entry\s*>|<entryref\b[^>]*\bhref\s*=\s*[""'](?<entryref>[^""']*)[""']", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex EntryOrReference();

    [GeneratedRegex(@"<ref\b[^>]*\bhref\s*=\s*[""'](?<href>[^""']*)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex Reference();

    [GeneratedRegex(@"<title\b[^>]*>(?<text>.*?)</title\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Title();

    [GeneratedRegex(@"<author\b[^>]*>(?<text>.*?)</author\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Author();
}
