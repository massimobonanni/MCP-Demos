namespace PizzaOps.Core.Domain;

/// <summary>
/// An opaque version token used only to conditionally update the oven version that was read.
/// Consumers must round-trip the value without parsing or interpreting its contents.
/// </summary>
public sealed class ConcurrencyToken : IEquatable<ConcurrencyToken>
{
    /// <summary>Creates a token around a non-empty persistence version value.</summary>
    public ConcurrencyToken(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    /// <summary>The opaque value required by the persistence adapter for a conditional write.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public bool Equals(ConcurrencyToken? other) =>
        other is not null && StringComparer.Ordinal.Equals(Value, other.Value);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ConcurrencyToken other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    /// <inheritdoc />
    public override string ToString() => nameof(ConcurrencyToken);
}
