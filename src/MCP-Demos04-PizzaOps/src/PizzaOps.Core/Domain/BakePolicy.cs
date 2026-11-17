namespace PizzaOps.Core.Domain;

/// <summary>Evaluates a completed bake against its recipe's inclusive tolerance interval.</summary>
public static class BakePolicy
{
    /// <summary>Returns the bake outcome without changing oven state or implying execution failure.</summary>
    public static BakeOutcome Evaluate(Recipe recipe, int ovenTemperatureC)
    {
        ArgumentNullException.ThrowIfNull(recipe);

        if (ovenTemperatureC < recipe.MinimumPassingTemperatureC)
        {
            return BakeOutcome.Undercooked;
        }

        return ovenTemperatureC > recipe.MaximumPassingTemperatureC
            ? BakeOutcome.Burnt
            : BakeOutcome.Pass;
    }
}
