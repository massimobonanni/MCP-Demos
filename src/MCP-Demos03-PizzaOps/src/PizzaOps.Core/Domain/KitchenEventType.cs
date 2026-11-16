namespace PizzaOps.Core.Domain;

/// <summary>Supported kitchen event categories.</summary>
public enum KitchenEventType
{
    TemperatureChanged,
    TemperatureRejected,
    TestBake,
    Reset
}
