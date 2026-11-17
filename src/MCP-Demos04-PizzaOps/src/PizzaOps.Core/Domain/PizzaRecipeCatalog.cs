using System.Collections.ObjectModel;

namespace PizzaOps.Core.Domain;

/// <summary>The fixed three-recipe catalog used by deterministic test bakes.</summary>
public sealed class PizzaRecipeCatalog : IRecipeCatalog
{
    private static readonly ReadOnlyCollection<Recipe> CatalogRecipes = Array.AsReadOnly(
    [
        new Recipe("margherita", "Margherita", 320, 15),
        new Recipe("marinara", "Marinara", 300, 15),
        new Recipe("diavola", "Diavola", 310, 15)
    ]);

    private static readonly ReadOnlyDictionary<string, Recipe> RecipesByType =
        new ReadOnlyDictionary<string, Recipe>(
            CatalogRecipes.ToDictionary(recipe => recipe.PizzaType, StringComparer.Ordinal));

    /// <summary>Gets the shared immutable catalog instance.</summary>
    public static PizzaRecipeCatalog Instance { get; } = new();

    /// <summary>The catalog's recipes in stable order.</summary>
    public static IReadOnlyList<Recipe> Recipes => CatalogRecipes;

    /// <inheritdoc />
    public Recipe? FindByType(string? pizzaType) =>
        IdentifierNormalization.TryNormalize(pizzaType, out var normalizedPizzaType) &&
        RecipesByType.TryGetValue(normalizedPizzaType, out var recipe)
            ? recipe
            : null;
}
