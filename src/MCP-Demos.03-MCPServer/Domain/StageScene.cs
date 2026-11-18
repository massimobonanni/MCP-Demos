using System.Collections.Immutable;

namespace StageDirector.Domain;

internal sealed record StageScene(
    string Id,
    string Title,
    string Subtitle,
    StageTheme Theme,
    string Effect,
    int EffectDurationMs);

internal sealed record StageTheme(
    string Background,
    string Accent,
    string Intensity,
    ImmutableArray<string> EffectColors);
