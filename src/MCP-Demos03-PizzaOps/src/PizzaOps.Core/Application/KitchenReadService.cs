using PizzaOps.Core.Contracts;
using PizzaOps.Core.Domain;

namespace PizzaOps.Core.Application;

/// <summary>Builds the shared, policy-consistent status projection for server-side consumers.</summary>
public sealed class KitchenReadService : IKitchenReadService
{
    private static readonly HashSet<string> ExpectedOvenIds = new(StringComparer.Ordinal)
    {
        "oven-01",
        "oven-02",
        "oven-03"
    };

    private readonly IOvenRepository ovenRepository;
    private readonly IKitchenEventRepository eventRepository;
    private readonly TimeProvider timeProvider;

    /// <summary>Creates the shared status projection.</summary>
    public KitchenReadService(
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
    public async Task<OperationResult<KitchenSnapshot>> GetStatusAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Oven> ovens;

        try
        {
            ovens = await ovenRepository.GetAllAsync(cancellationToken);
        }
        catch (KitchenStorageException exception)
        {
            return exception.FailureKind == KitchenStorageFailureKind.InvalidData
                ? OperationResultFactory.Failed<KitchenSnapshot>(
                    ResultCodes.KitchenStateInvalid,
                    "Kitchen state is incomplete or invalid.")
                : OperationResultFactory.Failed<KitchenSnapshot>(
                    ResultCodes.StorageUnavailable,
                    "Kitchen state could not be read from storage.");
        }

        if (ovens.Count != ExpectedOvenIds.Count ||
            ovens.Select(oven => oven.OvenId).Distinct(StringComparer.Ordinal).Count() != ExpectedOvenIds.Count ||
            ovens.Any(oven => !ExpectedOvenIds.Contains(oven.OvenId)))
        {
            return OperationResultFactory.Failed<KitchenSnapshot>(
                ResultCodes.KitchenStateInvalid,
                "Kitchen state is incomplete or invalid.");
        }

        IReadOnlyList<KitchenEvent> recentEvents;
        var eventFeedAvailable = true;
        string? eventFeedWarning = null;

        try
        {
            recentEvents = await eventRepository.GetLatestAsync(20, cancellationToken);
        }
        catch (KitchenStorageException)
        {
            recentEvents = [];
            eventFeedAvailable = false;
            eventFeedWarning = "Recent events are unavailable.";
        }

        var snapshot = new KitchenSnapshot(
            timeProvider.GetUtcNow(),
            ovens,
            recentEvents,
            eventFeedAvailable,
            eventFeedWarning);

        return OperationResultFactory.Completed(
            ResultCodes.Ok,
            eventFeedAvailable
                ? "Kitchen status loaded."
                : "Kitchen status loaded; the recent event feed is degraded.",
            snapshot);
    }
}
