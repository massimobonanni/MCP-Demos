using System.Collections.Immutable;

namespace StageDirector.Domain;

internal static class StageSceneCatalog
{
    internal static StageScene Welcome { get; } = new(
        "welcome", "WELCOME", "The show is about to begin",
        new("#101B39", "#83BAFF", "soft", []), "ambient", 0);

    private static readonly ImmutableArray<StageScene> scenes =
    [
        Welcome,
        new("focus", "FOCUS MODE", "Let's dive into the details",
            new("#242424", "#FFFFFF", "minimal", []), "none", 0),
        new("energy", "ENERGY MODE", "Let's wake up the room!",
            new("#481D59", "#FFD34C", "dynamic", ["#FF8C42"]), "pulse", 0),
        new("celebration", "THANK YOU!", "What a great session!",
            new("#14532D", "#FFD700", "festive", ["#FF4D8D", "#45D9FF", "#A78BFF"]),
            "confetti", 4_000)
    ];

    internal static ImmutableArray<string> AvailableScenes { get; } =
        scenes.Select(scene => scene.Id).ToImmutableArray();

    internal static StageScene GetRequiredScene(string? scene)
    {
        foreach (StageScene preset in scenes)
        {
            if (string.Equals(preset.Id, scene, StringComparison.Ordinal))
            {
                return preset;
            }
        }

        throw new ArgumentException(
            $"INVALID_SCENE: Choose one of: {string.Join(", ", AvailableScenes)}.", nameof(scene));
    }
}
