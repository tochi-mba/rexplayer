using System.Text.Json;

namespace Rex.Media.AppCore.Metadata;

/// <summary>A picture an article offers, with its size in pixels so its shape can be judged.</summary>
public sealed record ArticleImage(string Url, int Width, int Height)
{
    /// <summary>Wider than it is tall by a clear margin: a still or a banner, not a poster.</summary>
    public bool Landscape => Width > Height * 1.2;
}

/// <summary>An article's summary: the opening paragraph as plain text, the article's address for the credit, and its lead picture.</summary>
public sealed record ArticleSummary(string Title, string? Extract, string? PageUrl, ArticleImage? Thumbnail, ArticleImage? Original);

/// <summary>Which of an article's pictures serves as a card's poster, and which as a wide backdrop.</summary>
public sealed record ArticleArt(string? PosterUrl, string? BackdropUrl);

/// <summary>
/// English Wikipedia's page summaries (en.wikipedia.org/api/rest_v1, free with no key): the
/// synopsis and the lead picture, usually the infobox poster, of the article an item's sitelink
/// names. The text is CC BY-SA, so wherever it is shown it is credited "Description from
/// Wikipedia" with a link to <see cref="ArticleSummary.PageUrl"/>.
/// </summary>
public static class Wikipedia
{
    /// <summary>The credit shown wherever a synopsis from Wikipedia appears, beside a link to the article.</summary>
    public const string DescriptionCredit = "Description from Wikipedia";

    /// <summary>The widest original picture taken as a card's poster; a larger one uses the summary's own thumbnail.</summary>
    public const int PosterMaxWidth = 1000;

    /// <summary>The summary of the article titled <paramref name="title"/>, as a sitelink spells it.</summary>
    public static Uri SummaryUri(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        return new Uri("https://en.wikipedia.org/api/rest_v1/page/summary/" + Uri.EscapeDataString(title.Replace(' ', '_')));
    }

    /// <summary>
    /// The summary an answer holds, or null for one that is not an article's (a disambiguation
    /// page lists other articles and describes nothing); malformed JSON throws <see cref="JsonException"/>.
    /// </summary>
    public static ArticleSummary? ParseSummary(string json)
    {
        using var doc = JsonDocument.Parse(json ?? throw new ArgumentNullException(nameof(json)));
        var page = doc.RootElement;
        if (page.ValueKind != JsonValueKind.Object || Text(page, "type") is "disambiguation" || Text(page, "title") is not { Length: > 0 } title)
        {
            return null;
        }

        string? url = page.TryGetProperty("content_urls", out var urls) && urls.ValueKind == JsonValueKind.Object
            && urls.TryGetProperty("desktop", out var desktop) && desktop.ValueKind == JsonValueKind.Object ? Text(desktop, "page") : null;
        return new ArticleSummary(title, NullIfBlank(Text(page, "extract")), NullIfBlank(url), Image(page, "thumbnail"), Image(page, "originalimage"));
    }

    /// <summary>
    /// The article's lead picture as a poster when it is poster-shaped (the original when it is
    /// not huge, the summary's thumbnail otherwise), or as a backdrop when it is wide; the item's
    /// own Commons picture, when it has one, is the backdrop otherwise.
    /// </summary>
    public static ArticleArt ChooseArt(ArticleSummary? summary, string? commonsBackdrop)
    {
        var lead = summary?.Original ?? summary?.Thumbnail;
        if (lead is null)
        {
            return new ArticleArt(null, commonsBackdrop);
        }

        if (lead.Landscape)
        {
            return new ArticleArt(null, lead.Url);
        }

        var poster = summary!.Original is { } original && original.Width <= PosterMaxWidth ? original : summary.Thumbnail ?? lead;
        return new ArticleArt(poster.Url, commonsBackdrop);
    }

    private static ArticleImage? Image(JsonElement page, string name) =>
        page.TryGetProperty(name, out var image) && image.ValueKind == JsonValueKind.Object && Text(image, "source") is { Length: > 0 } source
            && image.TryGetProperty("width", out var width) && width.ValueKind == JsonValueKind.Number
            && image.TryGetProperty("height", out var height) && height.ValueKind == JsonValueKind.Number
            ? new ArticleImage(source, width.GetInt32(), height.GetInt32()) : null;

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
