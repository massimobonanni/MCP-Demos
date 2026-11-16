using PizzaOps.Core.Application;
using PizzaOps.Core.Domain;
using PizzaOps.Core.Interfaces;
using PizzaOps.Storage.Persistence;

namespace PizzaOps.DemoAdmin;

/// <summary>Performs an explicit, convergent reset of the configured kitchen partition.</summary>
public sealed class KitchenResetService : IKitchenResetService
{
    private static readonly (string OvenId, string DisplayName, int TemperatureC)[] CanonicalOvens =
    [
        ("oven-01", "Oven 01", 320),
        ("oven-02", "Oven 02", 450),
        ("oven-03", "Oven 03", 300)
    ];

    private readonly IOvenRepository ovenRepository;
    private readonly IKitchenEventRepository eventRepository;
    private readonly IKitchenTableMaintenance maintenance;
    private readonly TimeProvider timeProvider;

    /// <summary>Creates the reset operation using the shared storage adapter.</summary>
    public KitchenResetService(
        IOvenRepository ovenRepository,
        IKitchenEventRepository eventRepository,
        IKitchenTableMaintenance maintenance,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(ovenRepository);
        ArgumentNullException.ThrowIfNull(eventRepository);
        ArgumentNullException.ThrowIfNull(maintenance);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.ovenRepository = ovenRepository;
        this.eventRepository = eventRepository;
        this.maintenance = maintenance;
        this.timeProvider = timeProvider;
    }

    /// <summary>Replaces only configured kitchen rows and succeeds only after read-back verification.</summary>
    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await ExecuteStepAsync("table preparation", () => maintenance.EnsureTablesAsync(cancellationToken));
        await ExecuteStepAsync("partition cleanup", () => maintenance.DeleteKitchenDataAsync(cancellationToken));

        foreach (var oven in CanonicalOvens)
        {
            await ExecuteStepAsync(
                $"restoring {oven.OvenId}",
                () => maintenance.CreateOrReplaceOvenAsync(
                    oven.OvenId,
                    oven.DisplayName,
                    oven.TemperatureC,
                    timeProvider.GetUtcNow(),
                    "RESET",
                    cancellationToken));
        }

        var eventId = Guid.NewGuid().ToString("N");
        var resetEvent = new KitchenEvent(
            eventId,
            timeProvider.GetUtcNow(),
            KitchenEventType.Reset,
            null,
            "Kitchen reset to canonical oven temperatures.",
            KitchenEventOutcome.Success,
            eventId);
        await ExecuteStepAsync("RESET event append", () => eventRepository.AppendAsync(resetEvent, cancellationToken));
        await VerifyResetAsync(eventId, cancellationToken);
    }

    private async Task VerifyResetAsync(string expectedEventId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Oven> ovens;
        IReadOnlyList<KitchenEvent> events;

        try
        {
            ovens = await ovenRepository.GetAllAsync(cancellationToken);
        }
        catch (KitchenStorageException exception)
        {
            throw new KitchenResetException("oven verification", exception);
        }
        catch (OperationCanceledException exception)
        {
            throw new KitchenResetException("oven verification", exception);
        }

        var expectedOvens = CanonicalOvens.ToDictionary(oven => oven.OvenId, StringComparer.Ordinal);
        if (ovens.Count != expectedOvens.Count ||
            ovens.Any(oven =>
                !expectedOvens.TryGetValue(oven.OvenId, out var expected) ||
                oven.DisplayName != expected.DisplayName ||
                oven.TemperatureC != expected.TemperatureC ||
                oven.LastAction != "RESET"))
        {
            throw new KitchenResetException(
                "oven verification",
                new InvalidOperationException("Read-back did not match the three canonical oven rows."));
        }

        try
        {
            events = await eventRepository.GetLatestAsync(20, cancellationToken);
        }
        catch (KitchenStorageException exception)
        {
            throw new KitchenResetException("event verification", exception);
        }
        catch (OperationCanceledException exception)
        {
            throw new KitchenResetException("event verification", exception);
        }

        if (events.Count != 1 ||
            events[0].EventId != expectedEventId ||
            events[0].EventType != KitchenEventType.Reset ||
            events[0].Outcome != KitchenEventOutcome.Success)
        {
            throw new KitchenResetException(
                "event verification",
                new InvalidOperationException("Read-back did not contain exactly the current RESET event."));
        }
    }

    private static async Task ExecuteStepAsync(string stage, Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (KitchenStorageException exception)
        {
            throw new KitchenResetException(stage, exception);
        }
        catch (OperationCanceledException exception)
        {
            throw new KitchenResetException(stage, exception);
        }
    }

    private static async Task ExecuteStepAsync<T>(string stage, Func<Task<T>> operation)
    {
        try
        {
            await operation();
        }
        catch (KitchenStorageException exception)
        {
            throw new KitchenResetException(stage, exception);
        }
        catch (OperationCanceledException exception)
        {
            throw new KitchenResetException(stage, exception);
        }
    }
}
