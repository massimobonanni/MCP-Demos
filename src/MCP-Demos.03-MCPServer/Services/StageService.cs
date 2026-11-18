using StageDirector.Domain;

namespace StageDirector.Services;

internal sealed class StageService
{
    private readonly Lock stateLock = new();
    private StageState state = CreateSnapshot(StageSceneCatalog.Welcome, revision: 1);

    internal StageState GetSnapshot()
    {
        lock (stateLock)
        {
            return state;
        }
    }

    internal StageChangeResult ActivateScene(string? scene) =>
        TransitionTo(StageSceneCatalog.GetRequiredScene(scene));

    internal StageChangeResult Reset() => TransitionTo(StageSceneCatalog.Welcome);

    private StageChangeResult TransitionTo(StageScene preset)
    {
        lock (stateLock)
        {
            string previousScene = state.Scene;
            bool changed = previousScene != preset.Id;
            if (changed)
            {
                state = CreateSnapshot(preset, checked(state.Revision + 1));
            }

            return new(changed, previousScene, state);
        }
    }

    private static StageState CreateSnapshot(StageScene preset, long revision) =>
        new(preset.Id, revision, preset.Title, preset.Subtitle, preset.Theme, preset.Effect,
            preset.EffectDurationMs, DateTime.UtcNow, StageSceneCatalog.AvailableScenes);
}

internal sealed record StageChangeResult(bool Changed, string PreviousScene, StageState Stage)
{
    public bool Success { get; } = true;
    public string CurrentScene => Stage.Scene;
    public long Revision => Stage.Revision;
}
