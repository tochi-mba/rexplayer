using System.Xml.Linq;
using Rex.Media.AppCore.Player;
using Rex.Media.Library.Playlists;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Repository;

/// <summary>
/// Finder offers rexplayer for every file the player accepts. The explicit extensions matter for
/// formats such as Matroska and raw elementary streams that do not consistently conform to
/// macOS's broad public.movie or public.audio types.
/// </summary>
public sealed class MacAssociationTests
{
    private static readonly (string Kind, IReadOnlyList<string> Extensions)[] Kinds =
    [
        ("Video", MediaFiles.Video),
        ("Audio", MediaFiles.Audio),
        ("Picture", MediaFiles.Pictures),
        ("Playlist", PlaylistFiles.Extensions),
    ];

    [Fact]
    [Capability("WIN-02", CapabilityPlatform.MacOS)]
    public void TheMacBundleOffersRexplayerForEveryFileItPlays()
    {
        var document = XDocument.Load(RepoPaths.Combine("src", "Rex.Media.App.Mac", "Info.plist"));
        var documentTypes = ValueAfterKey(document.Root!.Element("dict")!, "CFBundleDocumentTypes");
        var declared = documentTypes.Elements("dict")
            .Where(type => type.Elements("key").Any(key => key.Value == "CFBundleTypeExtensions"))
            .ToDictionary(
            type => ValueAfterKey(type, "CFBundleTypeName").Value,
            type => ValueAfterKey(type, "CFBundleTypeExtensions").Elements("string")
                .Select(extension => "." + extension.Value).ToArray(),
            StringComparer.Ordinal);

        foreach (var (kind, extensions) in Kinds)
        {
            Assert.True(declared.TryGetValue(kind, out var actual), $"Info.plist does not declare the {kind} document type.");
            Assert.Equal(extensions.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
        }

        var all = Kinds.SelectMany(kind => kind.Extensions).ToArray();
        Assert.Equal(all.Length, declared.Where(pair => pair.Key != "Folder").SelectMany(pair => pair.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    private static XElement ValueAfterKey(XElement dictionary, string name)
    {
        var key = dictionary.Elements("key").Single(element => element.Value == name);
        return key.ElementsAfterSelf().First();
    }
}
