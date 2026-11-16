using PizzaOps.Core.Domain;

namespace PizzaOps.Core.Application;

/// <summary>Append-only event persistence and bounded recent-event reads.</summary>
public interface IKitchenEventRepository
{
    /// <summary>Appends one kitchen event.</summary>
    Task AppendAsync(KitchenEvent kitchenEvent, CancellationToken cancellationToken);

    /// <summary>Reads at most the requested number of newest events.</summary>
    Task<IReadOnlyList<KitchenEvent>> GetLatestAsync(int count, CancellationToken cancellationToken);
}
