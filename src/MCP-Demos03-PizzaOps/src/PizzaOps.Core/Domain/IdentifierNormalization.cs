namespace PizzaOps.Core.Domain;

/// <summary>Canonicalizes caller-provided identifiers before catalog or repository lookup.</summary>
public static class IdentifierNormalization
{
    /// <summary>Trims and invariant-lowercases an identifier, or rejects a missing value.</summary>
    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized.Length > 0;
    }
}
