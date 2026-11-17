using PizzaOps.Core.Domain;

namespace PizzaOps.Core.Application;

/// <summary>Purpose-specific persistence operations for existing ovens.</summary>
public interface IOvenRepository : IOvenReader
{
    /// <summary>
    /// Reads an oven by normalized identifier and returns null when it is unknown.
    /// Caller input is trimmed and invariant-lowercased before lookup; a blank caller value
    /// is INVALID_ARGUMENT, while an unknown non-empty identifier is OVEN_NOT_FOUND.
    /// The repository must not create unknown ovens.
    /// </summary>
    Task<Oven?> GetByIdAsync(string normalizedOvenId, CancellationToken cancellationToken);

    /// <summary>
    /// Attempts a conditional temperature update using the previously observed opaque version.
    /// Storage must not treat a conflict as a successful update. The application validates
    /// the inclusive 200–350 command range and trims an optional reason (maximum 120 chars)
    /// before calling this method.
    /// </summary>
    Task<OvenUpdateResult> TrySetTemperatureAsync(
        string normalizedOvenId,
        int targetTemperatureC,
        string? lastAction,
        DateTimeOffset updatedAtUtc,
        ConcurrencyToken expectedVersion,
        CancellationToken cancellationToken);
}
