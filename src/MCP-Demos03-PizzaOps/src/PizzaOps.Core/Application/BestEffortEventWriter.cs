using PizzaOps.Core.Domain;

namespace PizzaOps.Core.Application;

internal static class BestEffortEventWriter
{
    internal static async Task<EventWriteResult> AppendAsync(
        IKitchenEventRepository repository,
        KitchenEvent kitchenEvent,
        CancellationToken cancellationToken)
    {
        try
        {
            await repository.AppendAsync(kitchenEvent, cancellationToken);
            return new EventWriteResult(true, []);
        }
        catch (OperationCanceledException)
        {
            return new EventWriteResult(
                false,
                ["EVENT_WRITE_OUTCOME_UNKNOWN: Event append was cancelled; whether it was recorded is unknown."]);
        }
        catch (KitchenStorageException exception)
        {
            return exception.FailureKind == KitchenStorageFailureKind.OutcomeUnknown
                ? new EventWriteResult(
                    false,
                    ["EVENT_WRITE_OUTCOME_UNKNOWN: Whether the event was recorded is unknown."])
                : new EventWriteResult(
                    false,
                    ["EVENT_WRITE_FAILED: The event could not be recorded."]);
        }
    }
}

internal sealed record EventWriteResult(bool Recorded, IReadOnlyList<string> Warnings);
