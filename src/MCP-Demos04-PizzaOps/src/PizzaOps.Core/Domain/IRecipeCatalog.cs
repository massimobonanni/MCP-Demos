namespace PizzaOps.Core.Domain;

/// <summary>Provides lookup of the immutable, canonical PizzaOps recipe catalog.</summary>
public interface IRecipeCatalog
{
    /// <summary>
    /// Finds a recipe after trim-and-invariant-case normalization; returns null for a missing
    /// or unknown value. The application service distinguishes blank INVALID_ARGUMENT from
    /// a non-empty unknown recipe's RECIPE_NOT_FOUND.
    /// </summary>
    Recipe? FindByType(string? pizzaType);
}
