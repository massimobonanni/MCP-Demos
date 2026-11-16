namespace PizzaOps.Core.Contracts;

/// <summary>Stable application result codes shared by tools and read surfaces.</summary>
public static class ResultCodes
{
    /// <summary>Operation completed successfully.</summary>
    public const string Ok = "OK";

    /// <summary>Operation completed, but the requested value failed domain validation.</summary>
    public const string InvalidArgument = "INVALID_ARGUMENT";

    /// <summary>The requested oven does not exist.</summary>
    public const string OvenNotFound = "OVEN_NOT_FOUND";

    /// <summary>The requested recipe is not in the fixed catalog.</summary>
    public const string RecipeNotFound = "RECIPE_NOT_FOUND";

    /// <summary>The requested command is outside the permitted temperature range.</summary>
    public const string TemperatureOutOfRange = "TEMPERATURE_OUT_OF_RANGE";

    /// <summary>The oven changed since the caller observed its version.</summary>
    public const string ConcurrentUpdate = "CONCURRENT_UPDATE";

    /// <summary>Persistence was unavailable before an operation could complete.</summary>
    public const string StorageUnavailable = "STORAGE_UNAVAILABLE";

    /// <summary>A write may have committed, but its outcome could not be confirmed.</summary>
    public const string StorageOutcomeUnknown = "STORAGE_OUTCOME_UNKNOWN";

    /// <summary>Persisted kitchen state is missing, incomplete, or malformed.</summary>
    public const string KitchenStateInvalid = "KITCHEN_STATE_INVALID";

    /// <summary>The temperature changed successfully.</summary>
    public const string TemperatureUpdated = "TEMPERATURE_UPDATED";

    /// <summary>The requested temperature was already set.</summary>
    public const string NoChange = "NO_CHANGE";

    /// <summary>A test bake completed, regardless of its business outcome.</summary>
    public const string TestCompleted = "TEST_COMPLETED";
}
