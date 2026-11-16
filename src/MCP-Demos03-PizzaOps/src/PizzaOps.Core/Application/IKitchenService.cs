using PizzaOps.Core.Contracts;
using PizzaOps.Core.Domain;

namespace PizzaOps.Core.Application;

/// <summary>Application operations for recipe lookup, temperature changes, and test bakes.</summary>
public interface IKitchenService
{
    /// <summary>
    /// Looks up a recipe after trimming and invariant-lowercasing the type. Null/whitespace
    /// returns INVALID_ARGUMENT; an unknown non-empty type returns RECIPE_NOT_FOUND.
    /// </summary>
    OperationResult<Recipe> GetRecipe(string? pizzaType, string? correlationId = null);

    /// <summary>
    /// Trims and invariant-lowercases the oven ID, rejects blank IDs, returns OVEN_NOT_FOUND
    /// for unknown IDs, validates the 200–350 inclusive command range, and trims optional
    /// reasons. Reasons longer than 120 characters after trimming return INVALID_ARGUMENT.
    /// </summary>
    Task<OperationResult<Oven>> SetTemperatureAsync(
        string? ovenId,
        int targetTemperatureC,
        string? reason,
        string? correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Executes an immediate test bake. A completed BURNT or UNDERCOOKED result is still a
    /// successful operation; only failure to execute or validate is represented as failure.
    /// Oven IDs and recipe types are trimmed and invariant-lowercased. Blank values return
    /// INVALID_ARGUMENT; unknown non-empty IDs/types return OVEN_NOT_FOUND/RECIPE_NOT_FOUND.
    /// </summary>
    Task<OperationResult<BakeResult>> BakeTestPizzaAsync(
        string? ovenId,
        string? pizzaType,
        string? correlationId,
        CancellationToken cancellationToken);
}
