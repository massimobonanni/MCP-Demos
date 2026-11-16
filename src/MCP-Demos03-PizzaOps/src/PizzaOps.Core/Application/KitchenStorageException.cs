namespace PizzaOps.Core.Application;

/// <summary>Identifies a storage failure without exposing an infrastructure SDK type.</summary>
public enum KitchenStorageFailureKind
{
    Unavailable,
    OutcomeUnknown,
    InvalidData
}

/// <summary>A persistence failure translated at the adapter boundary.</summary>
public sealed class KitchenStorageException : Exception
{
    /// <summary>Creates a translated storage failure.</summary>
    public KitchenStorageException(
        KitchenStorageFailureKind failureKind,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        FailureKind = failureKind;
    }

    /// <summary>The persistence failure category.</summary>
    public KitchenStorageFailureKind FailureKind { get; }
}
