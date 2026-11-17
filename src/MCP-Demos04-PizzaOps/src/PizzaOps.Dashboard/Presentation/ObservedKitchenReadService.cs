using System.Diagnostics;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using PizzaOps.Core.Application;
using PizzaOps.Core.Contracts;

namespace PizzaOps.Dashboard.Presentation;

internal sealed partial class ObservedKitchenReadService(
    IKitchenReadService inner,
    ILogger<ObservedKitchenReadService> logger,
    TelemetryClient? telemetry) : IKitchenReadService
{
    public async Task<OperationResult<KitchenSnapshot>> GetStatusAsync(CancellationToken cancellationToken)
    {
        using var operation = telemetry?.StartOperation<DependencyTelemetry>("PizzaOps.dashboard.refresh");
        using var localActivity = operation is null ? new Activity("PizzaOps.dashboard.refresh").Start() : null;
        var activity = Activity.Current!;
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = activity.TraceId.ToHexString()
        });
        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await inner.GetStatusAsync(cancellationToken);
            var degraded = result.Data?.EventFeedAvailable == false;
            activity.SetTag("pizzaops.code", result.Code);
            activity.SetStatus(result.Success && !degraded ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
            if (operation is not null)
            {
                operation.Telemetry.Type = "PizzaOps";
                operation.Telemetry.ResultCode = degraded ? "EVENT_FEED_UNAVAILABLE" : result.Code;
                operation.Telemetry.Success = result.Success && !degraded;
            }
            var level = result.Success && !degraded ? LogLevel.Debug : LogLevel.Warning;
            if (logger.IsEnabled(level))
            {
                var durationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                LogRead(logger, level, result.Code, degraded, durationMs);
            }
            return OperationResultFactory.WithCorrelation(result, activity.TraceId.ToHexString());
        }
        catch (OperationCanceledException)
        {
            if (operation is not null)
            {
                operation.Telemetry.Success = false;
                operation.Telemetry.ResultCode = ResultCodes.Cancelled;
            }
            if (logger.IsEnabled(LogLevel.Debug))
            {
                var durationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                LogRead(logger, LogLevel.Debug, ResultCodes.Cancelled, false, durationMs);
            }
            throw;
        }
        catch (Exception exception)
        {
            activity.SetStatus(ActivityStatusCode.Error);
            if (operation is not null)
            {
                operation.Telemetry.Success = false;
                operation.Telemetry.ResultCode = ResultCodes.InternalError;
            }
            LogRequestFailed(logger, exception.GetType().Name, activity.TraceId.ToHexString());
            var durationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            LogRead(logger, LogLevel.Error, ResultCodes.InternalError, false, durationMs);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Dashboard request failed with {ExceptionType}; correlation_id={CorrelationId}.")]
    internal static partial void LogRequestFailed(ILogger logger, string exceptionType, string? correlationId);

    [LoggerMessage(Message = "Dashboard read completed with {Code}; event_feed_degraded={Degraded}, duration_ms={DurationMs}.")]
    private static partial void LogRead(ILogger logger, LogLevel level, string code, bool degraded, double durationMs);
}
