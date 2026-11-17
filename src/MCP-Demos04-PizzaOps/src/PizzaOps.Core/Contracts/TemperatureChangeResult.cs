namespace PizzaOps.Core.Contracts;

/// <summary>Describes the requested and observed state for a temperature command.</summary>
public sealed record TemperatureChangeResult(
    string OvenId,
    int RequestedTemperatureC,
    int PreviousTemperatureC,
    int CurrentTemperatureC,
    string Health,
    DateTimeOffset UpdatedAtUtc,
    bool StateChanged);
