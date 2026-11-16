using PizzaOps.Core.Contracts;
using PizzaOps.Core.Domain;

namespace PizzaOps.Core.Application;

/// <summary>Validates and conditionally updates oven temperatures, then appends safe events.</summary>
public sealed class TemperatureChangeService : ITemperatureChangeService
{
    private readonly IOvenRepository ovenRepository;
    private readonly IKitchenEventRepository eventRepository;
    private readonly TimeProvider timeProvider;

    /// <summary>Creates the temperature command application service.</summary>
    public TemperatureChangeService(
        IOvenRepository ovenRepository,
        IKitchenEventRepository eventRepository,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(ovenRepository);
        ArgumentNullException.ThrowIfNull(eventRepository);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.ovenRepository = ovenRepository;
        this.eventRepository = eventRepository;
        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<OperationResult<TemperatureChangeResult>> SetTemperatureAsync(
        string? ovenId,
        int targetTemperatureC,
        string? reason,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IdentifierNormalization.TryNormalize(ovenId, out var normalizedOvenId))
        {
            return OperationResultFactory.Failed<TemperatureChangeResult>(
                ResultCodes.InvalidArgument,
                "An oven ID is required.",
                correlationId);
        }

        if (!ReasonValidation.TryNormalize(reason, out var normalizedReason))
        {
            return OperationResultFactory.Failed<TemperatureChangeResult>(
                ResultCodes.InvalidArgument,
                $"The reason must be no longer than {ReasonValidation.MaximumLength} characters.",
                correlationId);
        }

        Oven? oven;
        try
        {
            oven = await ovenRepository.GetByIdAsync(normalizedOvenId, cancellationToken);
        }
        catch (KitchenStorageException exception)
        {
            return MapReadFailure(exception, correlationId);
        }

        if (oven is null)
        {
            return OperationResultFactory.Failed<TemperatureChangeResult>(
                ResultCodes.OvenNotFound,
                "The requested oven was not found.",
                correlationId);
        }

        if (!OvenTemperaturePolicy.IsCommandAllowed(targetTemperatureC))
        {
            var eventWrite = await AppendEventAsync(
                CreateEvent(
                    timeProvider.GetUtcNow(),
                    KitchenEventType.TemperatureRejected,
                    oven.OvenId,
                    "Temperature command rejected by policy.",
                    KitchenEventOutcome.Rejected,
                    correlationId),
                cancellationToken);
            var rejected = CreateResult(oven, targetTemperatureC, stateChanged: false);
            return OperationResultFactory.FailedWithData(
                ResultCodes.TemperatureOutOfRange,
                "Commanded temperatures must be between 200°C and 350°C.",
                rejected,
                correlationId,
                eventWrite.Recorded,
                eventWrite.Warnings);
        }

        if (oven.TemperatureC == targetTemperatureC)
        {
            return OperationResultFactory.Completed(
                ResultCodes.NoChange,
                $"{oven.DisplayName} is already at {targetTemperatureC}°C.",
                CreateResult(oven, targetTemperatureC, stateChanged: false),
                correlationId);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var updatedAtUtc = timeProvider.GetUtcNow();
        var lastAction = normalizedReason ?? $"Temperature set to {targetTemperatureC} C.";
        OvenUpdateResult update;

        try
        {
            update = await ovenRepository.TrySetTemperatureAsync(
                oven.OvenId,
                targetTemperatureC,
                lastAction,
                updatedAtUtc,
                oven.Version,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return OperationResultFactory.Failed<TemperatureChangeResult>(
                ResultCodes.StorageOutcomeUnknown,
                "The temperature write was cancelled after it started; its outcome is unknown. Do not retry automatically.",
                correlationId);
        }
        catch (KitchenStorageException exception)
        {
            return MapWriteFailure(exception, correlationId);
        }

        if (update.Status == OvenUpdateStatus.Conflict)
        {
            var eventWrite = await AppendEventAsync(
                CreateEvent(
                    timeProvider.GetUtcNow(),
                    KitchenEventType.TemperatureRejected,
                    oven.OvenId,
                    "Temperature change was not applied because the oven changed concurrently.",
                    KitchenEventOutcome.Rejected,
                    correlationId),
                cancellationToken);
            return OperationResultFactory.Failed<TemperatureChangeResult>(
                ResultCodes.ConcurrentUpdate,
                "The oven changed after it was read; no temperature change was applied.",
                correlationId,
                eventWrite.Recorded,
                eventWrite.Warnings);
        }

        if (update.Status != OvenUpdateStatus.Updated || update.Oven is null)
        {
            return OperationResultFactory.Failed<TemperatureChangeResult>(
                ResultCodes.StorageOutcomeUnknown,
                "The conditional temperature write returned an incomplete result; its outcome is unknown. Do not retry automatically.",
                correlationId);
        }

        var updatedOven = update.Oven;
        var changeEvent = CreateEvent(
            updatedAtUtc,
            KitchenEventType.TemperatureChanged,
            updatedOven.OvenId,
            $"Temperature changed from {oven.TemperatureC} C to {updatedOven.TemperatureC} C.",
            KitchenEventOutcome.Success,
            correlationId);
        var changeEventWrite = await AppendEventAsync(changeEvent, cancellationToken);
        var result = CreateResult(oven, updatedOven, targetTemperatureC);

        return OperationResultFactory.Completed(
            ResultCodes.TemperatureUpdated,
            $"{updatedOven.DisplayName} changed from {oven.TemperatureC}°C to {updatedOven.TemperatureC}°C; status is {updatedOven.Health.ToString().ToUpperInvariant()}.",
            result,
            correlationId,
            changeEventWrite.Recorded,
            changeEventWrite.Warnings);
    }

    private Task<EventWriteResult> AppendEventAsync(
        KitchenEvent kitchenEvent,
        CancellationToken cancellationToken) =>
        BestEffortEventWriter.AppendAsync(eventRepository, kitchenEvent, cancellationToken);

    private static KitchenEvent CreateEvent(
        DateTimeOffset timestampUtc,
        KitchenEventType eventType,
        string ovenId,
        string summary,
        KitchenEventOutcome outcome,
        string? correlationId) =>
        new(Guid.NewGuid().ToString("N"), timestampUtc, eventType, ovenId, summary, outcome, correlationId);

    private static TemperatureChangeResult CreateResult(
        Oven oven,
        int requestedTemperatureC,
        bool stateChanged) =>
        new(
            oven.OvenId,
            requestedTemperatureC,
            oven.TemperatureC,
            oven.TemperatureC,
            oven.Health.ToString().ToUpperInvariant(),
            oven.UpdatedAtUtc,
            stateChanged);

    private static TemperatureChangeResult CreateResult(
        Oven previous,
        Oven current,
        int requestedTemperatureC) =>
        new(
            current.OvenId,
            requestedTemperatureC,
            previous.TemperatureC,
            current.TemperatureC,
            current.Health.ToString().ToUpperInvariant(),
            current.UpdatedAtUtc,
            StateChanged: true);

    private static OperationResult<TemperatureChangeResult> MapReadFailure(
        KitchenStorageException exception,
        string? correlationId) =>
        OperationResultFactory.Failed<TemperatureChangeResult>(
            exception.FailureKind == KitchenStorageFailureKind.InvalidData
                ? ResultCodes.KitchenStateInvalid
                : ResultCodes.StorageUnavailable,
            exception.FailureKind == KitchenStorageFailureKind.InvalidData
                ? "The requested oven state is invalid."
                : "The requested oven could not be read from storage.",
            correlationId);

    private static OperationResult<TemperatureChangeResult> MapWriteFailure(
        KitchenStorageException exception,
        string? correlationId) =>
        exception.FailureKind switch
        {
            KitchenStorageFailureKind.InvalidData => OperationResultFactory.Failed<TemperatureChangeResult>(
                ResultCodes.KitchenStateInvalid,
                "The conditional temperature write could not be validated.",
                correlationId),
            KitchenStorageFailureKind.OutcomeUnknown => OperationResultFactory.Failed<TemperatureChangeResult>(
                ResultCodes.StorageOutcomeUnknown,
                "The temperature write may have completed, but its outcome could not be confirmed. Do not retry automatically.",
                correlationId),
            _ => OperationResultFactory.Failed<TemperatureChangeResult>(
                ResultCodes.StorageUnavailable,
                "The temperature could not be written to storage.",
                correlationId)
        };
}
