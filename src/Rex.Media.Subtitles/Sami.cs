// Spec: Microsoft "Understanding SAMI 1.0": SYNC elements with a Start time in milliseconds whose P text shows until the next SYNC; a non-breaking space alone clears the screen; BR breaks a line.
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Rex.Media.Subtitles;

/// <summary>SAMI: each SYNC starts a cue that lasts until the next one.</summary>
internal static partial class Sami
{
    public static IEnumerable<SubtitleCue> Parse(string text)
    {
        var syncs = Sync().Matches(text).Select(match => (Start: long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), Body: match.Groups[2].Value)).ToList();
        for (var i = 0; i < syncs.Count; i++)
        {
            var end = i + 1 < syncs.Count ? syncs[i + 1].Start : syncs[i].Start + 3000;
            var body = Paragraph().Match(syncs[i].Body) is { Success: true } p ? p.Groups[1].Value : syncs[i].Body;
            var lines = Break().Replace(body.Replace("\r", "", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal), "\n");
            var plain = WebUtility.HtmlDecode(lines).Trim();
            if (plain.Replace(NoBreakSpace, "", StringComparison.Ordinal).Trim().Length > 0
                && SubtitleFile.Cue(TimeSpan.FromMilliseconds(syncs[i].Start), TimeSpan.FromMilliseconds(end), plain) is { } cue)
            {
                yield return cue;
            }
        }
    }

    private static readonly string NoBreakSpace = ((char)0xA0).ToString();

    [GeneratedRegex("<SYNC[^>]*Start\\s*=\\s*\"?(\\d+)\"?[^>]*>(.*?)(?=<SYNC|</BODY|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Sync();

    [GeneratedRegex("<P[^>]*>(.*?)(?=<P[\\s>]|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Paragraph();

    [GeneratedRegex("<BR\\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex Break();
}
