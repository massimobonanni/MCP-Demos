using PizzaOps.Core.Domain;

namespace PizzaOps.Storage.Persistence;

/// <summary>Explicit operator-only table setup and partition-scoped reset operations.</summary>
public interface IKitchenTableMaintenance
{
    /// <summary>Creates the configured tables when explicitly requested by an operator.</summary>
    Task EnsureTablesAsync(CancellationToken cancellationToken);

    /// <summary>Deletes rows in the configured kitchen partition from both PizzaOps tables.</summary>
    Task DeleteKitchenDataAsync(CancellationToken cancellationToken);

    /// <summary>Creates or replaces one oven row for deterministic operator reset.</summary>
    Task<Oven> CreateOrReplaceOvenAsync(
        string ovenId,
        string displayName,
        int temperatureC,
        DateTimeOffset updatedAtUtc,
        string? lastAction,
        CancellationToken cancellationToken);
}
