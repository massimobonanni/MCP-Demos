using PizzaOps.Core.Contracts;

namespace PizzaOps.Core.Application;

/// <summary>Runs immediate test bakes without modifying ovens.</summary>
public interface IBakeTestService
{
    /// <summary>Observes the persisted oven, evaluates an immutable recipe, and records a test event.</summary>
    Task<OperationResult<BakeResult>> BakeAsync(
        string? ovenId,
        string? pizzaType,
        string? correlationId,
        CancellationToken cancellationToken);
}
