namespace PizzaOps.Core.Domain;

/// <summary>Coarse event outcome categories used by the event feed.</summary>
public enum KitchenEventOutcome
{
    Success,
    Rejected,
    Pass,
    Fail
}
