using PizzaOps.Core.Contracts;
using PizzaOps.Core.Domain;

namespace PizzaOps.Core.Application;

/// <summary>Provides validated access to the immutable recipe catalog.</summary>
public sealed class RecipeReadService(IRecipeCatalog catalog)
{
    /// <summary>Looks up a normalized pizza type and reports invalid or unknown values.</summary>
    public OperationResult<Recipe> GetRecipe(string? pizzaType)
    {
        if (string.IsNullOrWhiteSpace(pizzaType))
        {
            return OperationResultFactory.Failed<Recipe>(ResultCodes.InvalidArgument, "A pizza type is required.");
        }

        var recipe = catalog.FindByType(pizzaType);
        return recipe is null
            ? OperationResultFactory.Failed<Recipe>(ResultCodes.RecipeNotFound, "The pizza recipe was not found.")
            : OperationResultFactory.Completed(ResultCodes.Ok, $"Recipe found for {recipe.DisplayName}.", recipe);
    }
}
