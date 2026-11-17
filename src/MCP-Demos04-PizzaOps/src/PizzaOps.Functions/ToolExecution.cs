using System.Diagnostics;
using System.Text.Json;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.Logging;
using PizzaOps.Core.Application;
using PizzaOps.Core.Contracts;

namespace PizzaOps.Functions;

/// <summary>Correlates tool outcomes and contains unexpected failures without exposing arguments.</summary>
public sealed partial class ToolExecution(ILogger<ToolExecution> logger, TelemetryClient? telemetry = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>Executes one tool in the existing worker trace; uncertain commands are never replayed.</summary>
    public async Task<string> RunAsync<T>(
        string tool,
        bool mutation,
        Func<string, Task<OperationResult<T>>> execute,
        CancellationToken cancellationToken)
    {
        using var operation = telemetry?.StartOperation<DependencyTelemetry>($"PizzaOps.{tool}");
        using var localActivity = operation is null ? new Activity($"PizzaOps.{tool}").Start() : null;
        var activity = Activity.Current!;
        var correlationId = activity.TraceId.ToHexString();
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["Tool"] = tool
        });
        var started = Stopwatch.GetTimestamp();
        OperationResult<T> result;
        var entered = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            entered = true;
            result = await execute(correlationId);
        }
        catch (OperationCanceledException)
        {
            result = OperationResultFactory.Failed<T>(
                mutation && entered ? ResultCodes.StorageOutcomeUnknown : ResultCodes.Cancelled,
                mutation && entered
                    ? "The operation was interrupted; refresh state before deciding whether to retry."
                    : "The operation was cancelled.",
                correlationId);
        }
        catch (KitchenStorageException exception)
        {
            result = OperationResultFactory.Failed<T>(
                exception.FailureKind switch
                {
                    KitchenStorageFailureKind.OutcomeUnknown => ResultCodes.StorageOutcomeUnknown,
                    KitchenStorageFailureKind.InvalidData => ResultCodes.KitchenStateInvalid,
                    _ => ResultCodes.StorageUnavailable
                },
                "Storage could not confirm the operation. Refresh state before retrying.",
                correlationId);
        }
        catch (Exception exception)
        {
            // Exception messages can contain storage URLs, arguments or credentials.
            LogUnexpected(logger, tool, exception.GetType().Name);
            result = OperationResultFactory.Failed<T>(
                mutation && entered ? ResultCodes.StorageOutcomeUnknown : ResultCodes.InternalError,
                mutation && entered
                    ? "The operation could not be confirmed. Refresh state before deciding whether to retry."
                    : "The operation could not be completed.",
                correlationId);
        }

        activity.SetTag("pizzaops.code", result.Code);
        activity.SetStatus(result.Success && result.Warnings.Count == 0 ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
        if (operation is not null)
        {
            operation.Telemetry.Type = "PizzaOps";
            operation.Telemetry.ResultCode = result.Code;
            operation.Telemetry.Success = result.Success && result.Warnings.Count == 0;
        }
        var level = !result.Success || result.Warnings.Count > 0 ? LogLevel.Warning
            : mutation || result.EventRecorded.HasValue ? LogLevel.Information : LogLevel.Debug;
        if (logger.IsEnabled(level))
        {
            var warningCodes = string.Join(",", result.Warnings.Select(warning => warning.StartsWith("EVENT_WRITE_OUTCOME_UNKNOWN:", StringComparison.Ordinal)
                ? "EVENT_WRITE_OUTCOME_UNKNOWN" : warning.StartsWith("EVENT_WRITE_FAILED:", StringComparison.Ordinal)
                    ? "EVENT_WRITE_FAILED" : "EVENT_FEED_UNAVAILABLE"));
            var durationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            LogOutcome(logger, level, tool, result.Code, result.Success, result.EventRecorded,
                warningCodes, durationMs, correlationId);
        }
        return JsonSerializer.Serialize(OperationResultFactory.WithCorrelation(result, correlationId), JsonOptions);
    }

    [LoggerMessage(Message = "Tool {Tool} completed with {Code}; success={Success}, event_recorded={EventRecorded}, warnings={WarningCodes}, duration_ms={DurationMs}, correlation_id={CorrelationId}.")]
    private static partial void LogOutcome(ILogger logger, LogLevel level, string tool, string code,
        bool success, bool? eventRecorded, string warningCodes, double durationMs, string correlationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Tool {Tool} failed unexpectedly with {ExceptionType}.")]
    private static partial void LogUnexpected(ILogger logger, string tool, string exceptionType);
}
