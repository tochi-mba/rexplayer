namespace Rex.Media.AppCore.Metadata;

/// <summary>One person on a title's card: who they are, who they play, and a photo to fetch lazily.</summary>
public sealed record CastMember(string Name, string? Character, string? PhotoUrl);

/// <summary>Another title a catalogue suggests beside this one; shown only when the library holds it.</summary>
public sealed record TitleRef(string Title, int? Year);

/// <summary>One season as a catalogue knows it: its number, a plain-text overview and a poster to fetch lazily.</summary>
public sealed record SeasonDetails(int Number, string? Summary, string? PosterUrl);
