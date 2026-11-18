using System.Collections.Immutable;

namespace StageDirector.Domain;

internal sealed record StageState(
    string Scene,
    long Revision,
    string Title,
    string Subtitle,
    StageTheme Theme,
    string Effect,
    int EffectDurationMs,
    DateTime ActivatedAtUtc,
    ImmutableArray<string> AvailableScenes);
