using PizzaOps.Core.Domain;

namespace PizzaOps.Core.Application;

/// <summary>Reads persisted oven state.</summary>
public interface IOvenReader
{
    /// <summary>Reads all persisted ovens.</summary>
    Task<IReadOnlyList<Oven>> GetAllAsync(CancellationToken cancellationToken);
}
