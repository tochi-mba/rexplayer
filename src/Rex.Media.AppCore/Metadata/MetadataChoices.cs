using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rex.Media.AppCore.Metadata;

/// <summary>
/// The user's own match corrections ("Wrong match...", "Find the film..."): which catalogue entry each name means,
/// kept as one small JSON file beside the downloads (<see cref="MetadataCache.ChoicesPath"/>), so
/// a rescan or a cleared cache never undoes a pick. Not thread-safe; callers serialize access.
/// </summary>
public sealed class MetadataChoices
{
    private readonly Dictionary<string, int> _chosen = new(StringComparer.Ordinal);

    public int Count => _chosen.Count;

    /// <summary>The choices read back from <paramref name="json"/>; missing or damaged text is an empty set, never an error.</summary>
    public static MetadataChoices Load(string? json)
    {
        var choices = new MetadataChoices();
        if (string.IsNullOrWhiteSpace(json))
        {
            return choices;
        }

        try
        {
            foreach (var (key, value) in JsonSerializer.Deserialize(json, MetadataChoicesJson.Default.DictionaryStringInt32) ?? [])
            {
                choices._chosen[key] = value;
            }
        }
        catch (JsonException)
        {
            // A damaged file loses the corrections but never the player; the user can pick again.
        }

        return choices;
    }

    /// <summary>The file's next contents.</summary>
    public string Save() => JsonSerializer.Serialize(_chosen, MetadataChoicesJson.Default.DictionaryStringInt32);

    /// <summary>The catalogue entry the user picked for this show, or null when they never corrected it.</summary>
    public int? ChosenShow(string show) => _chosen.TryGetValue(Key(show), out var id) ? id : null;

    /// <summary>Remembers that <paramref name="show"/> means the catalogue's entry <paramref name="id"/>.</summary>
    public void ChooseShow(string show, int id) => _chosen[Key(show)] = id;

    /// <summary>Forgets the pick, so the next fetch matches by name again.</summary>
    public void ForgetShow(string show) => _chosen.Remove(Key(show));

    /// <summary>The Wikidata item number the user picked for this film's title and year, or null when they never picked one.</summary>
    public int? ChosenMovie(string title, int? year) => _chosen.TryGetValue(MovieKey(title, year), out var id) ? id : null;

    /// <summary>Remembers that the film named <paramref name="title"/> (<paramref name="year"/>) is Wikidata's item <paramref name="id"/>.</summary>
    public void ChooseMovie(string title, int? year, int id) => _chosen[MovieKey(title, year)] = id;

    private static string Key(string show) => "tv/" + MetadataMatch.Normalize(show);

    private static string MovieKey(string title, int? year) => "film/" + MetadataMatch.MovieKey(title, year);
}

[JsonSerializable(typeof(Dictionary<string, int>))]
internal sealed partial class MetadataChoicesJson : JsonSerializerContext;
