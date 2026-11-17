using PizzaOps.Core.Domain;

namespace PizzaOps.Core.Application;

/// <summary>The result of a conditional oven update.</summary>
public sealed record OvenUpdateResult(OvenUpdateStatus Status, Oven? Oven);

/// <summary>Whether a conditional oven update committed or conflicted.</summary>
public enum OvenUpdateStatus
{
    Updated,
    Conflict
}
