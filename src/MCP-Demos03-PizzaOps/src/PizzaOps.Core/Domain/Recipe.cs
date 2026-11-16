namespace PizzaOps.Core.Domain;

/// <summary>An immutable PizzaOps test recipe and its inclusive passing temperature interval.</summary>
public sealed class Recipe
{
    /// <summary>Creates a recipe with a normalized type and non-overflowing tolerance bounds.</summary>
    public Recipe(string pizzaType, string displayName, int targetTemperatureC, int toleranceC)
    {
        if (!IdentifierNormalization.TryNormalize(pizzaType, out var normalizedPizzaType))
        {
            throw new ArgumentException("A pizza type is required.", nameof(pizzaType));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (toleranceC < 0 ||
            targetTemperatureC < int.MinValue + toleranceC ||
            targetTemperatureC > int.MaxValue - toleranceC)
        {
            throw new ArgumentOutOfRangeException(nameof(toleranceC), "The tolerance must produce valid integer bounds.");
        }

        PizzaType = normalizedPizzaType;
        DisplayName = displayName;
        TargetTemperatureC = targetTemperatureC;
        ToleranceC = toleranceC;
    }

    /// <summary>The canonical recipe identifier.</summary>
    public string PizzaType { get; }

    /// <summary>The display name.</summary>
    public string DisplayName { get; }

    /// <summary>The ideal oven temperature in Celsius.</summary>
    public int TargetTemperatureC { get; }

    /// <summary>The inclusive temperature tolerance in Celsius.</summary>
    public int ToleranceC { get; }

    /// <summary>The lowest temperature that passes.</summary>
    public int MinimumPassingTemperatureC => TargetTemperatureC - ToleranceC;

    /// <summary>The highest temperature that passes.</summary>
    public int MaximumPassingTemperatureC => TargetTemperatureC + ToleranceC;
}
