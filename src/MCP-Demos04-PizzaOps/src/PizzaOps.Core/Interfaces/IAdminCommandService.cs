namespace PizzaOps.Core.Interfaces;

/// <summary>Executes operator commands against an explicitly selected environment.</summary>
public interface IAdminCommandService
{
    /// <summary>Reads the kitchen status.</summary>
    Task<int> StatusAsync(string environmentName, CancellationToken cancellationToken);

    /// <summary>Confirms the target and resets the kitchen.</summary>
    Task<int> ResetAsync(string environmentName, CancellationToken cancellationToken);
}
