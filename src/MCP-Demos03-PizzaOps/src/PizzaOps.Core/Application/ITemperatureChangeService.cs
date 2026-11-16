using PizzaOps.Core.Contracts;

namespace PizzaOps.Core.Application;

/// <summary>Applies validated temperature commands to existing ovens.</summary>
public interface ITemperatureChangeService
{
    /// <summary>Changes an oven temperature using the version observed during lookup.</summary>
    Task<OperationResult<TemperatureChangeResult>> SetTemperatureAsync(
        string? ovenId,
        int targetTemperatureC,
        string? reason,
        string? correlationId,
        CancellationToken cancellationToken);
}
