using PizzaOps.Core.Contracts;

namespace PizzaOps.Core.Application;

/// <summary>Read-only application projection shared by status consumers and the dashboard.</summary>
public interface IKitchenReadService
{
    /// <summary>Returns authoritative oven state and independently reported event-feed status.</summary>
    Task<OperationResult<KitchenSnapshot>> GetStatusAsync(CancellationToken cancellationToken);
}
