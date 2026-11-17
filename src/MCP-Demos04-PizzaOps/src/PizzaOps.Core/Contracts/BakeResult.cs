using System.Text.Json.Serialization;
using PizzaOps.Core.Domain;

namespace PizzaOps.Core.Contracts;

/// <summary>Business data for a successfully executed deterministic test bake.</summary>
public sealed class BakeResult
{
    /// <summary>Creates the immutable observation from one completed bake.</summary>
    public BakeResult(
        string ovenId,
        string pizzaType,
        int ovenTemperatureC,
        int targetTemperatureC,
        int toleranceC,
        BakeOutcome outcome)
    {
        OvenId = ovenId;
        PizzaType = pizzaType;
        OvenTemperatureC = ovenTemperatureC;
        TargetTemperatureC = targetTemperatureC;
        ToleranceC = toleranceC;
        Outcome = outcome;
    }

    /// <summary>The canonical oven identifier.</summary>
    [JsonPropertyName("oven_id")]
    public string OvenId { get; }

    /// <summary>The canonical recipe identifier.</summary>
    [JsonPropertyName("pizza_type")]
    public string PizzaType { get; }

    /// <summary>The oven temperature observed by this bake.</summary>
    [JsonPropertyName("oven_temperature_c")]
    public int OvenTemperatureC { get; }

    /// <summary>The recipe's target temperature.</summary>
    [JsonPropertyName("target_temperature_c")]
    public int TargetTemperatureC { get; }

    /// <summary>The recipe's inclusive tolerance.</summary>
    [JsonPropertyName("tolerance_c")]
    public int ToleranceC { get; }

    /// <summary>The inclusive lower bound used for this observation.</summary>
    [JsonPropertyName("minimum_passing_temperature_c")]
    public int MinimumPassingTemperatureC => TargetTemperatureC - ToleranceC;

    /// <summary>The inclusive upper bound used for this observation.</summary>
    [JsonPropertyName("maximum_passing_temperature_c")]
    public int MaximumPassingTemperatureC => TargetTemperatureC + ToleranceC;

    /// <summary>The typed bake outcome, separate from operation execution success.</summary>
    [JsonIgnore]
    public BakeOutcome Outcome { get; }

    /// <summary>The stable uppercase bake outcome used in serialized results.</summary>
    [JsonPropertyName("result")]
    public string Result => Outcome switch
    {
        BakeOutcome.Pass => "PASS",
        BakeOutcome.Undercooked => "UNDERCOOKED",
        BakeOutcome.Burnt => "BURNT",
        _ => throw new InvalidOperationException($"Unsupported bake outcome: {Outcome}.")
    };
}
