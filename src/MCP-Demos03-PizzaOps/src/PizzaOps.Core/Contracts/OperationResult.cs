using System.Text.Json.Serialization;

namespace PizzaOps.Core.Contracts;

/// <summary>
/// Application outcome envelope. For bakes, Success describes execution; the bake's
/// Pass/Undercooked/Burnt business outcome is carried separately in Data.
/// </summary>
public sealed class OperationResult<T>
{
    private OperationResult(
        bool success,
        string code,
        string message,
        T? data,
        string? correlationId,
        bool? eventRecorded,
        IEnumerable<string>? warnings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Success = success;
        Code = code;
        Message = message;
        Data = data;
        CorrelationId = correlationId;
        EventRecorded = eventRecorded;
        Warnings = Array.AsReadOnly(warnings?.ToArray() ?? []);
    }

    /// <summary>Whether the requested application operation completed.</summary>
    [JsonPropertyName("success")]
    public bool Success { get; }

    /// <summary>The stable machine-readable result code.</summary>
    [JsonPropertyName("code")]
    public string Code { get; }

    /// <summary>A safe human-readable outcome message.</summary>
    [JsonPropertyName("message")]
    public string Message { get; }

    /// <summary>Operation data, absent when execution or validation failed.</summary>
    [JsonPropertyName("data")]
    public T? Data { get; }

    /// <summary>The correlation identifier, if one was assigned.</summary>
    [JsonPropertyName("correlation_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CorrelationId { get; }

    /// <summary>
    /// Whether the associated event was appended. Null means no event was attempted;
    /// false means the primary operation completed or was rejected but the event failed.
    /// </summary>
    [JsonPropertyName("event_recorded")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? EventRecorded { get; }

    /// <summary>Non-fatal warnings, such as a failed best-effort event append.</summary>
    [JsonPropertyName("warnings")]
    public IReadOnlyList<string> Warnings { get; }

    internal static OperationResult<T> Create(
        bool success,
        string code,
        string message,
        T? data,
        string? correlationId,
        bool? eventRecorded,
        IEnumerable<string>? warnings) =>
        new(success, code, message, data, correlationId, eventRecorded, warnings);
}

/// <summary>Creates typed application results without placing factories on a generic type.</summary>
public static class OperationResultFactory
{
    /// <summary>Creates a completed operation result, optionally recording event status.</summary>
    public static OperationResult<T> Completed<T>(
        string code,
        string message,
        T data,
        string? correlationId = null,
        bool? eventRecorded = null,
        IEnumerable<string>? warnings = null) =>
        OperationResult<T>.Create(true, code, message, data, correlationId, eventRecorded, warnings);

    /// <summary>Creates a failed operation result without success-shaped data.</summary>
    public static OperationResult<T> Failed<T>(
        string code,
        string message,
        string? correlationId = null,
        bool? eventRecorded = null,
        IEnumerable<string>? warnings = null) =>
        OperationResult<T>.Create(false, code, message, default, correlationId, eventRecorded, warnings);
}
