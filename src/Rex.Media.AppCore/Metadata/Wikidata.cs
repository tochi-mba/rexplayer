using System.Globalization;
using System.Text.Json;

namespace Rex.Media.AppCore.Metadata;

/// <summary>One item a Wikidata search offered for a name, in the order the search ranked it.</summary>
public sealed record WikidataHit(string Id, string Label, string? Description);

/// <summary>One value a statement or qualifier holds: another item, a string, a point in time or a quantity.</summary>
public sealed record WikidataValue(string? Item = null, string? Text = null, string? Time = null, decimal? Amount = null, string? Unit = null);

/// <summary>One statement about an item: its value and the qualifiers that refine it ("as the character Neo").</summary>
public sealed record WikidataClaim(WikidataValue Value, IReadOnlyDictionary<string, IReadOnlyList<WikidataValue>> Qualifiers)
{
    /// <summary>The first value of one qualifier, or null when the statement has none.</summary>
    public WikidataValue? Qualifier(string property) =>
        Qualifiers.TryGetValue(property, out var values) && values.Count > 0 ? values[0] : null;
}

/// <summary>One item with its statements (deprecated ones already dropped) and its English Wikipedia article.</summary>
public sealed record WikidataEntity(string Id, string? Label, string? Description, IReadOnlyDictionary<string, IReadOnlyList<WikidataClaim>> Claims, string? EnwikiTitle)
{
    /// <summary>The statements for one property, preferred rank first; empty when there are none.</summary>
    public IReadOnlyList<WikidataClaim> Statements(string property) =>
        Claims.TryGetValue(property, out var claims) ? claims : [];
}

/// <summary>What the query service says about an item another item names: its English label, and a picture when it has one.</summary>
public sealed record WikidataLabel(string Label, string? Image);

/// <summary>
/// Wikidata (www.wikidata.org and query.wikidata.org), free with no key and no sign-up: where to
/// ask and how to read the answers. Searches and items come from the Wikibase API; the English
/// names and pictures of the people, genres and companies an item points at come from one query
/// to the query service. Every request carries rexplayer's User-Agent with the repository as the
/// contact, as Wikimedia's User-Agent policy asks, and the window's fetch delegate keeps to one
/// request at a time per host. Only a title ever leaves the computer.
/// </summary>
public static class Wikidata
{
    /// <summary>The most items one entities request or one label query asks about, the API's own cap.</summary>
    public const int BatchLimit = 50;

    /// <summary>Searching items by English label or alias.</summary>
    public static Uri SearchUri(string name) =>
        new("https://www.wikidata.org/w/api.php?action=wbsearchentities&format=json&language=en&uselang=en&type=item&limit=10&search="
            + Uri.EscapeDataString(name ?? throw new ArgumentNullException(nameof(name))));

    /// <summary>The statements, English label and English Wikipedia article of up to <see cref="BatchLimit"/> items.</summary>
    public static Uri EntitiesUri(IReadOnlyList<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count is 0 or > BatchLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(ids));
        }

        return new Uri("https://www.wikidata.org/w/api.php?action=wbgetentities&format=json&languages=en&sitefilter=enwiki"
            + "&props=labels%7Cdescriptions%7Cclaims%7Csitelinks&ids=" + string.Join("%7C", ids.Select(Checked)));
    }

    /// <summary>One query for the English labels and pictures (P18) of up to <see cref="BatchLimit"/> items.</summary>
    public static Uri LabelsUri(IReadOnlyList<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count is 0 or > BatchLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(ids));
        }

        var query = "SELECT ?item ?itemLabel ?image WHERE { VALUES ?item { " + string.Join(" ", ids.Select(id => "wd:" + Checked(id)))
            + " } OPTIONAL { ?item wdt:P18 ?image } SERVICE wikibase:label { bd:serviceParam wikibase:language \"en\". } }";
        return new Uri("https://query.wikidata.org/sparql?format=json&query=" + Uri.EscapeDataString(query));
    }

    /// <summary>A Wikimedia Commons file (a P18 picture) scaled to <paramref name="width"/> pixels, through Commons' own redirect.</summary>
    public static string CommonsUrl(string file, int width)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        return "https://commons.wikimedia.org/wiki/Special:FilePath/" + Uri.EscapeDataString(file.Replace(' ', '_'))
            + "?width=" + width.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The items a search answered with, best first; malformed JSON throws <see cref="JsonException"/>.</summary>
    public static IReadOnlyList<WikidataHit> ParseSearch(string json)
    {
        using var doc = JsonDocument.Parse(json ?? throw new ArgumentNullException(nameof(json)));
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("search", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var hits = new List<WikidataHit>();
        foreach (var result in results.EnumerateArray())
        {
            if (result.ValueKind == JsonValueKind.Object && Text(result, "id") is { Length: > 0 } id && Text(result, "label") is { Length: > 0 } label)
            {
                hits.Add(new WikidataHit(id, label, Text(result, "description")));
            }
        }

        return hits;
    }

    /// <summary>The items an entities answer holds, in the order asked; malformed JSON throws <see cref="JsonException"/>.</summary>
    public static IReadOnlyList<WikidataEntity> ParseEntities(string json)
    {
        using var doc = JsonDocument.Parse(json ?? throw new ArgumentNullException(nameof(json)));
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("entities", out var entities) || entities.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var items = new List<WikidataEntity>();
        foreach (var property in entities.EnumerateObject())
        {
            var entity = property.Value;
            if (entity.ValueKind != JsonValueKind.Object || Text(entity, "id") is not { Length: > 0 } id)
            {
                // A "missing" placeholder for an id that names nothing.
                continue;
            }

            var claims = new Dictionary<string, IReadOnlyList<WikidataClaim>>(StringComparer.Ordinal);
            if (entity.TryGetProperty("claims", out var all) && all.ValueKind == JsonValueKind.Object)
            {
                foreach (var statements in all.EnumerateObject())
                {
                    if (Statements(statements.Value) is { Count: > 0 } kept)
                    {
                        claims[statements.Name] = kept;
                    }
                }
            }

            string? article = entity.TryGetProperty("sitelinks", out var links) && links.ValueKind == JsonValueKind.Object
                && links.TryGetProperty("enwiki", out var enwiki) && enwiki.ValueKind == JsonValueKind.Object ? Text(enwiki, "title") : null;
            items.Add(new WikidataEntity(id, English(entity, "labels"), English(entity, "descriptions"), claims, string.IsNullOrEmpty(article) ? null : article));
        }

        return items;
    }

    /// <summary>
    /// The labels and pictures a query answered with, by item id. An item with no English label
    /// comes back labelled with its own id, which is no name to show, so it is left out.
    /// </summary>
    public static IReadOnlyDictionary<string, WikidataLabel> ParseLabels(string json)
    {
        using var doc = JsonDocument.Parse(json ?? throw new ArgumentNullException(nameof(json)));
        var labels = new Dictionary<string, WikidataLabel>(StringComparer.Ordinal);
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Object
            || !results.TryGetProperty("bindings", out var bindings) || bindings.ValueKind != JsonValueKind.Array)
        {
            return labels;
        }

        foreach (var row in bindings.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object || Binding(row, "item") is not { } uri || Binding(row, "itemLabel") is not { Length: > 0 } label)
            {
                continue;
            }

            var id = uri[(uri.LastIndexOf('/') + 1)..];
            if (label == id)
            {
                continue;
            }

            var image = Binding(row, "image") is { Length: > 0 } picture ? Https(picture) : null;
            // An item with two pictures comes back as two rows; the first picture stands.
            if (!labels.TryGetValue(id, out var known) || known.Image is null)
            {
                labels[id] = new WikidataLabel(label, image ?? known?.Image);
            }
        }

        return labels;
    }

    /// <summary>The numeric part of an item id ("Q83495" is 83495), or 0 for anything that is not one.</summary>
    public static int Number(string id) =>
        id is ['Q', .. var digits] && int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0;

    private static string Checked(string id) =>
        Number(id) > 0 ? id : throw new ArgumentException("Not a Wikidata item id: " + id, nameof(id));

    private static List<WikidataClaim> Statements(JsonElement statements)
    {
        var preferred = new List<WikidataClaim>();
        var normal = new List<WikidataClaim>();
        if (statements.ValueKind != JsonValueKind.Array)
        {
            return preferred;
        }

        foreach (var statement in statements.EnumerateArray())
        {
            if (statement.ValueKind != JsonValueKind.Object || !statement.TryGetProperty("mainsnak", out var snak)
                || Value(snak) is not { } value)
            {
                continue;
            }

            var rank = Text(statement, "rank");
            if (rank == "deprecated")
            {
                continue;
            }

            var qualifiers = new Dictionary<string, IReadOnlyList<WikidataValue>>(StringComparer.Ordinal);
            if (statement.TryGetProperty("qualifiers", out var all) && all.ValueKind == JsonValueKind.Object)
            {
                foreach (var qualifier in all.EnumerateObject())
                {
                    if (qualifier.Value.ValueKind == JsonValueKind.Array
                        && qualifier.Value.EnumerateArray().Select(Value).Where(found => found is not null).ToList() is { Count: > 0 } values)
                    {
                        qualifiers[qualifier.Name] = values!;
                    }
                }
            }

            (rank == "preferred" ? preferred : normal).Add(new WikidataClaim(value, qualifiers));
        }

        preferred.AddRange(normal);
        return preferred;
    }

    private static WikidataValue? Value(JsonElement snak)
    {
        if (snak.ValueKind != JsonValueKind.Object || Text(snak, "snaktype") != "value"
            || !snak.TryGetProperty("datavalue", out var data) || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("value", out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() is { Length: > 0 } text ? new WikidataValue(Text: text) : null;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (Text(value, "id") is { Length: > 0 } item)
        {
            return new WikidataValue(Item: item);
        }

        if (Text(value, "time") is { Length: > 0 } time)
        {
            return new WikidataValue(Time: time);
        }

        return Text(value, "amount") is { } amount
            && decimal.TryParse(amount, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var quantity)
            ? new WikidataValue(Amount: quantity, Unit: Text(value, "unit") is { } unit ? unit[(unit.LastIndexOf('/') + 1)..] : null)
            : null;
    }

    private static string? English(JsonElement entity, string name) =>
        entity.TryGetProperty(name, out var languages) && languages.ValueKind == JsonValueKind.Object
            && languages.TryGetProperty("en", out var english) && english.ValueKind == JsonValueKind.Object
            && Text(english, "value") is { Length: > 0 } text ? text : null;

    private static string? Binding(JsonElement row, string name) =>
        row.TryGetProperty(name, out var cell) && cell.ValueKind == JsonValueKind.Object ? Text(cell, "value") : null;

    private static string Https(string url) =>
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "https://" + url[7..] : url;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
