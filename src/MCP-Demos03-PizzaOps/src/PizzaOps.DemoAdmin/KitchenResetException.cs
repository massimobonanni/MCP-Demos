namespace PizzaOps.DemoAdmin;

/// <summary>Reports the reset stage that failed, leaving the target explicitly unready.</summary>
public sealed class KitchenResetException(string stage, Exception innerException)
    : Exception($"The operation could not be confirmed: {innerException.Message}", innerException)
{
    /// <summary>The last reset stage attempted.</summary>
    public string Stage { get; } = stage;
}
