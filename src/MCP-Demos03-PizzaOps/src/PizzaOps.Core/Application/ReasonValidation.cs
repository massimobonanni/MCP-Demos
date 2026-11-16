namespace PizzaOps.Core.Application;

/// <summary>Normalizes optional operator reasons and enforces the event-feed length limit.</summary>
public static class ReasonValidation
{
    /// <summary>The maximum normalized reason length.</summary>
    public const int MaximumLength = 120;

    /// <summary>Trims a reason, maps null/whitespace to null, and rejects more than 120 characters.</summary>
    public static bool TryNormalize(string? reason, out string? normalized)
    {
        normalized = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        if (normalized is { Length: > MaximumLength })
        {
            normalized = null;
            return false;
        }

        return true;
    }
}
