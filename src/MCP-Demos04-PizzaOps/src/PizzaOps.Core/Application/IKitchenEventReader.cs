using PizzaOps.Core.Domain;

namespace PizzaOps.Core.Application;

/// <summary>Reads the bounded recent kitchen event feed.</summary>
public interface IKitchenEventReader
{
    /// <summary>Reads at most the requested number of newest events.</summary>
    Task<IReadOnlyList<KitchenEvent>> GetLatestAsync(int count, CancellationToken cancellationToken);
}
