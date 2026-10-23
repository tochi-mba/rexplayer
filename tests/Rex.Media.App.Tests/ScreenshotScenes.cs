using System.IO;
using System.Text.Json;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>
/// The tracked contract for captures from the running Windows app. These records describe
/// expected observable controls, not approval of an image without a CI baseline.
/// </summary>
internal static class ScreenshotScenes
{
    private sealed record Inventory(int SchemaVersion, ScreenshotScene[] Scenes);

    internal sealed record ExpectedText(string AutomationId, string Name);

    internal sealed record ScreenshotScene(
        string Id,
        string Fixture,
        string Route,
        string State,
        string Dimensions,
        string Theme,
        string[] ExpectedAutomationIds,
        ExpectedText[] ExpectedVisibleData,
        string ReadinessSignal,
        string Owner,
        string Baseline)
    {
        public string ReadinessId => ReadinessSignal["uia:".Length..];
    }

    private static readonly Lazy<IReadOnlyDictionary<string, ScreenshotScene>> AllScenes = new(Load);

    internal static IReadOnlyCollection<ScreenshotScene> All => AllScenes.Value.Values.ToArray();

    internal static ScreenshotScene Find(string name)
    {
        if (!AllScenes.Value.TryGetValue(name, out var scene))
        {
            throw new InvalidDataException($"Screenshot {name} is not declared in tests/screenshot-scenes.json.");
        }

        return scene;
    }

    private static IReadOnlyDictionary<string, ScreenshotScene> Load()
    {
        var path = RepoPaths.Combine("tests", "screenshot-scenes.json");
        var inventory = JsonSerializer.Deserialize<Inventory>(
            File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (inventory is not { SchemaVersion: 1, Scenes.Length: > 0 })
        {
            throw new InvalidDataException("The screenshot inventory must have schemaVersion 1 and at least one scene.");
        }

        var result = new Dictionary<string, ScreenshotScene>(StringComparer.Ordinal);
        foreach (var scene in inventory.Scenes)
        {
            if (string.IsNullOrWhiteSpace(scene.Id) ||
                !System.Text.RegularExpressions.Regex.IsMatch(scene.Id, "^[a-z0-9-]+$") ||
                string.IsNullOrWhiteSpace(scene.Fixture) ||
                string.IsNullOrWhiteSpace(scene.Route) ||
                string.IsNullOrWhiteSpace(scene.State) ||
                string.IsNullOrWhiteSpace(scene.Dimensions) ||
                string.IsNullOrWhiteSpace(scene.Theme) ||
                string.IsNullOrWhiteSpace(scene.Owner) ||
                scene.Baseline is not ("ci-windows" or "unreviewed") ||
                scene.ExpectedAutomationIds is not { Length: > 0 } ||
                scene.ExpectedAutomationIds.Any(string.IsNullOrWhiteSpace) ||
                scene.ExpectedVisibleData is null ||
                scene.ExpectedVisibleData.Any(t => string.IsNullOrWhiteSpace(t.AutomationId) ||
                    string.IsNullOrWhiteSpace(t.Name)) ||
                !scene.ReadinessSignal.StartsWith("uia:", StringComparison.Ordinal) ||
                !scene.ExpectedAutomationIds.Contains(scene.ReadinessId, StringComparer.Ordinal) ||
                !result.TryAdd(scene.Id, scene))
            {
                throw new InvalidDataException($"Invalid or duplicate screenshot scene: {scene.Id}");
            }
        }

        return result;
    }
}
