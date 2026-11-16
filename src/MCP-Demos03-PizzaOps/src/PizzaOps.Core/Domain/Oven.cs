namespace PizzaOps.Core.Domain;

/// <summary>An oven state; out-of-range temperatures are valid recorded incident state.</summary>
public sealed class Oven
{
    /// <summary>Creates an oven record, normalizing its identifier but not its temperature.</summary>
    public Oven(
        string ovenId,
        string displayName,
        int temperatureC,
        DateTimeOffset updatedAtUtc,
        string? lastAction,
        ConcurrencyToken version)
    {
        if (!IdentifierNormalization.TryNormalize(ovenId, out var normalizedOvenId))
        {
            throw new ArgumentException("An oven identifier is required.", nameof(ovenId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(version);

        if (updatedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The update timestamp must be UTC.", nameof(updatedAtUtc));
        }

        OvenId = normalizedOvenId;
        DisplayName = displayName;
        TemperatureC = temperatureC;
        UpdatedAtUtc = updatedAtUtc;
        LastAction = lastAction;
        Version = version;
    }

    /// <summary>The canonical oven identifier.</summary>
    public string OvenId { get; }

    /// <summary>The audience-facing oven name.</summary>
    public string DisplayName { get; }

    /// <summary>The currently recorded temperature, including an incident value such as 450.</summary>
    public int TemperatureC { get; }

    /// <summary>The UTC time at which this oven state was recorded.</summary>
    public DateTimeOffset UpdatedAtUtc { get; }

    /// <summary>The latest short action description, if available.</summary>
    public string? LastAction { get; }

    /// <summary>The opaque persistence version used for optimistic concurrency.</summary>
    public ConcurrencyToken Version { get; }

    /// <summary>Health derived from the current temperature.</summary>
    public OvenHealth Health => OvenTemperaturePolicy.GetHealth(TemperatureC);
}
