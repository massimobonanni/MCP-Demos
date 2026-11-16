namespace PizzaOps.Core.Domain;

/// <summary>The deterministic business outcome of a completed test bake.</summary>
public enum BakeOutcome
{
    Pass,
    Undercooked,
    Burnt
}
