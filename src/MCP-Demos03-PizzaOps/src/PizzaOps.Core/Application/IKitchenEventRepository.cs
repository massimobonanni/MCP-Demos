using PizzaOps.Core.Domain;

namespace PizzaOps.Core.Application;

/// <summary>Append-only event persistence and bounded recent-event reads.</summary>
public interface IKitchenEventRepository : IKitchenEventReader
{
    /// <summary>Appends one kitchen event.</summary>
    Task AppendAsync(KitchenEvent kitchenEvent, CancellationToken cancellationToken);

}
