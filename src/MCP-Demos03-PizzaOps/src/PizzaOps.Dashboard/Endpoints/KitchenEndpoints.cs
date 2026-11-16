using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using PizzaOps.Core.Application;
using PizzaOps.Core.Contracts;
using PizzaOps.Core.Domain;

namespace PizzaOps.Dashboard.Endpoints;

internal static partial class KitchenEndpoints
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);

    public static void MapKitchenEndpoints(this WebApplication app)
    {
        app.MapGet("/api/kitchen/state", GetStateAsync)
            .WithName("GetKitchenState")
            .WithSummary("Read the current kitchen state")
            .Produces<KitchenStateResponse>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        app.MapGet("/health/live", () => TypedResults.Ok(new HealthResponse("live")))
            .WithName("GetLiveness")
            .WithSummary("Check application liveness");
        app.MapGet("/health/ready", GetReadinessAsync)
            .WithName("GetReadiness")
            .WithSummary("Check storage and kitchen readiness")
            .Produces<HealthResponse>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<Results<Ok<KitchenStateResponse>, ProblemHttpResult>> GetStateAsync(
        IKitchenReadService kitchen,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var result = await ReadAsync(kitchen, loggerFactory.CreateLogger("KitchenState"), cancellationToken);
        if (result?.Success != true || result.Data is null)
        {
            return Unavailable(result?.Code);
        }

        var snapshot = result.Data;
        if (!snapshot.EventFeedAvailable)
        {
            LogEventFeedUnavailable(loggerFactory.CreateLogger("KitchenState"));
        }

        return TypedResults.Ok(new KitchenStateResponse(
            snapshot.RetrievedAtUtc,
            snapshot.Ovens.Select(oven => new OvenResponse(
                oven.OvenId, oven.DisplayName, oven.TemperatureC,
                oven.Health.ToString().ToUpperInvariant(), oven.UpdatedAtUtc)).ToArray(),
            snapshot.RecentEvents.Select(item => new KitchenEventResponse(
                item.TimestampUtc, JsonNamingPolicy.SnakeCaseUpper.ConvertName(item.EventType.ToString()), item.OvenId,
                item.Summary, item.Outcome.ToString().ToUpperInvariant())).ToArray(),
            snapshot.EventFeedAvailable,
            snapshot.EventFeedAvailable ? null : "Recent events are unavailable."));
    }

    private static async Task<Results<Ok<HealthResponse>, ProblemHttpResult>> GetReadinessAsync(
        IKitchenReadService kitchen,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("KitchenReadiness");
        var result = await ReadAsync(kitchen, logger, cancellationToken);
        if (result?.Success != true || result.Data is null || !result.Data.EventFeedAvailable)
        {
            LogReadinessFailed(logger);
            return Unavailable(result?.Code);
        }

        return TypedResults.Ok(new HealthResponse("ready"));
    }

    private static async Task<OperationResult<KitchenSnapshot>?> ReadAsync(
        IKitchenReadService kitchen,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReadTimeout);
        try
        {
            var result = await kitchen.GetStatusAsync(timeout.Token).WaitAsync(timeout.Token);
            if (!result.Success)
            {
                LogReadFailed(logger, result.Code);
            }
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogReadTimeout(logger);
            return null;
        }
    }

    private static ProblemHttpResult Unavailable(string? code) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Kitchen state is unavailable.",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code == ResultCodes.KitchenStateInvalid
                    ? ResultCodes.KitchenStateInvalid
                    : ResultCodes.StorageUnavailable
            });

    [LoggerMessage(Level = LogLevel.Warning, Message = "The recent kitchen event feed is unavailable.")]
    private static partial void LogEventFeedUnavailable(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Kitchen readiness check failed.")]
    private static partial void LogReadinessFailed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Kitchen read failed with code {Code}.")]
    private static partial void LogReadFailed(ILogger logger, string code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Kitchen read timed out.")]
    private static partial void LogReadTimeout(ILogger logger);
}

/// <summary>Public, read-only kitchen snapshot.</summary>
internal sealed record KitchenStateResponse(
    DateTimeOffset RetrievedAtUtc,
    IReadOnlyList<OvenResponse> Ovens,
    IReadOnlyList<KitchenEventResponse> Events,
    bool EventFeedAvailable,
    string? EventFeedWarning);

/// <summary>Public oven state without persistence versions or user-supplied reasons.</summary>
internal sealed record OvenResponse(
    string OvenId, string DisplayName, int TemperatureC, string Health, DateTimeOffset UpdatedAtUtc);

/// <summary>Safe operational event displayed in the recent feed.</summary>
internal sealed record KitchenEventResponse(
    DateTimeOffset TimestampUtc, string EventType, string? OvenId, string Summary, string Outcome);

/// <summary>Public application health status.</summary>
internal sealed record HealthResponse(string Status);
