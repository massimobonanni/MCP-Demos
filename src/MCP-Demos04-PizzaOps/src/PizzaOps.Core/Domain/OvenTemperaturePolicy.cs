namespace PizzaOps.Core.Domain;

/// <summary>Deterministic policy for commanded temperatures and derived oven health.</summary>
public static class OvenTemperaturePolicy
{
    /// <summary>The minimum allowed commanded temperature, in Celsius.</summary>
    public const int MinimumCommandTemperatureC = 200;

    /// <summary>The maximum allowed commanded temperature, in Celsius.</summary>
    public const int MaximumCommandTemperatureC = 350;

    /// <summary>Checks whether a requested command is in the inclusive permitted range.</summary>
    public static bool IsCommandAllowed(int temperatureC) =>
        temperatureC is >= MinimumCommandTemperatureC and <= MaximumCommandTemperatureC;

    /// <summary>Derives oven health without restricting representable incident temperatures.</summary>
    public static OvenHealth GetHealth(int temperatureC) =>
        IsCommandAllowed(temperatureC) ? OvenHealth.Healthy : OvenHealth.Critical;
}
