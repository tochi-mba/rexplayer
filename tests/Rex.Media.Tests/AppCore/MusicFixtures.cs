namespace Rex.Media.Tests.AppCore;

/// <summary>
/// MusicBrainz, Wikidata and Wikipedia answers about Asake, recorded on 2026-10-21 and trimmed to
/// the fields rexplayer reads (and a few it must ignore), plus a group shaped the way MusicBrainz
/// answers for one. No test reaches the network.
/// </summary>
internal static class MusicFixtures
{
    public const string AsakeId = "2b37ba5e-2162-46ca-bbad-f076f6a333bd";

    public const string LunguBoyGroup = "92f948c4-be65-4979-bea7-50887b29f7c0";

    public const string LunguBoyReleaseId = "a14df43d-c33c-49ed-a03e-1e1f7b485220";

    public const string ArtistSearch = """
        {"created":"2026-10-21T05:53:18.675Z","count":2,"offset":0,"artists":[
          {"id":"2b37ba5e-2162-46ca-bbad-f076f6a333bd","type":"Person","score":100,"name":"Asake","sort-name":"Asake","country":"NG",
           "area":{"type":"Country","name":"Nigeria"},"begin-area":{"type":"City","name":"Lagos"},"disambiguation":"Nigerian singer-songwriter",
           "life-span":{"begin":"1995-01-13","ended":null},"tags":[{"count":1,"name":"afrobeat"},{"count":1,"name":"afrobeats"}]},
          {"id":"429bb543-779c-4556-886b-e6af1f156e89","score":76,"name":"Wizkid & Asake","sort-name":"Wizkid & Asake","life-span":{"begin":"2026-01-23","ended":null}},
          {"id":"","name":"No id"},
          {"id":"x","score":"high"},
          7
        ]}
        """;

    public const string Artist = """
        {"id":"2b37ba5e-2162-46ca-bbad-f076f6a333bd","name":"Asake","sort-name":"Asake","type":"Person","gender":"Male","country":"NG",
         "area":{"name":"Nigeria","iso-3166-1-codes":["NG"]},"begin-area":{"name":"Lagos"},"end-area":null,
         "life-span":{"end":null,"begin":"1995-01-13","ended":false},
         "genres":[{"name":"afrobeat","count":1},{"name":"afrobeats","count":2},{"name":"","count":9},{"count":3}],
         "relations":[
           {"type":"discogs","direction":"forward","url":{"resource":"https://www.discogs.com/artist/12109804"}},
           {"type":"social network","direction":"forward","url":{"resource":"https://www.instagram.com/asakemusic/"}},
           {"type":"wikidata","direction":"forward","url":{"resource":"https://www.wikidata.org/wiki/Q112861798"}},
           {"type":"wikidata","direction":"forward","url":{"resource":"https://www.wikidata.org/wiki/Q1"}},
           {"type":"member of band","direction":"forward","artist":{"name":"Not a member, a band he joined"}}
         ]}
        """;

    /// <summary>A group as MusicBrainz answers for one: founded, disbanded, with current and former members.</summary>
    public const string Group = """
        {"id":"b0000000-0000-0000-0000-000000000001","name":"The Lagos Strings","type":"Group",
         "area":{"name":"Nigeria"},"begin-area":{"name":"Nigeria"},
         "life-span":{"begin":"2009-05","end":"2019","ended":true},
         "genres":[{"name":"highlife","count":4}],
         "relations":[
           {"type":"member of band","direction":"backward","ended":true,"artist":{"name":"Former Member"}},
           {"type":"member of band","direction":"backward","ended":false,"artist":{"name":"Ada Lead"}},
           {"type":"member of band","direction":"backward","ended":false,"artist":{"name":"ada lead"}},
           {"type":"member of band","direction":"backward","artist":{"name":""}},
           {"type":"wikidata","url":{"resource":"https://example.org/wiki/Q5"}},
           {"type":"wikidata","url":"not an object"},
           42
         ]}
        """;

    public const string LunguBoySearch = """
        {"created":"2026-10-21T05:53:23.680Z","count":2,"offset":0,"release-groups":[
          {"id":"0000","title":"Someone else's"},
          {"id":"92f948c4-be65-4979-bea7-50887b29f7c0","score":100,"count":1,"title":"Lungu Boy","first-release-date":"2024-08-08","primary-type":"Album",
           "artist-credit":[{"name":"Asake","artist":{"id":"2b37ba5e-2162-46ca-bbad-f076f6a333bd","name":"Asake"}}],
           "releases":[{"id":"bootleg-0","title":"Lungu Boy","status":"Bootleg"},{"id":"a14df43d-c33c-49ed-a03e-1e1f7b485220","title":"Lungu Boy","status":"Official"}],
           "tags":[{"count":1,"name":"afrobeats"},{"count":2,"name":"afro-fusion"}]}
        ]}
        """;

    public const string LunguBoyRelease = """
        {"id":"a14df43d-c33c-49ed-a03e-1e1f7b485220","title":"Lungu Boy","date":"2024-08-08","status":"Official",
         "label-info":[{"catalog-number":null,"label":{"id":"l1","name":"EMPIRE"}},{"catalog-number":null,"label":{"id":"l2","name":"YBNL Nation"}},
           {"label":{"name":"empire"}},{"label":{"name":"[no label]"}},{"label":null},{"catalog-number":"X1"}]}
        """;

    public const string AsakeEntity = """
        {"entities":{"Q112861798":{"type":"item","id":"Q112861798",
          "labels":{"en":{"language":"en","value":"Asake"}},
          "descriptions":{"en":{"language":"en","value":"Nigerian singer"}},
          "claims":{
            "P18":[{"mainsnak":{"snaktype":"value","property":"P18","datavalue":{"value":"Asake performing in a Toronto concert (2024) (cropped).png","type":"string"}},"type":"statement","rank":"normal"}],
            "P2031":[{"mainsnak":{"snaktype":"value","property":"P2031","datavalue":{"value":{"time":"+2018-00-00T00:00:00Z","precision":9},"type":"time"}},"type":"statement","rank":"normal"}]
          },
          "sitelinks":{"enwiki":{"site":"enwiki","title":"Asake","badges":[]}}}}}
        """;

    public const string AsakeSummary = """
        {"type":"standard","title":"Asake","extract":"Ahmed Ololade, known professionally as Asake, is a Nigerian singer, rapper, and songwriter.",
         "content_urls":{"desktop":{"page":"https://en.wikipedia.org/wiki/Asake"}}}
        """;
}
