namespace PizzaOps.Core.Interfaces;

/// <summary>Restores and verifies the configured kitchen during a maintenance window.</summary>
public interface IKitchenResetService
{
    /// <summary>Resets the configured kitchen.</summary>
    Task ResetAsync(CancellationToken cancellationToken);
}
