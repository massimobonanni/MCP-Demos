using System.Diagnostics;
using System.Text.Json;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using PizzaOps.Core.Contracts;
using PizzaOps.Functions;

namespace PizzaOps.UnitTests;

public sealed class ToolExecutionTests
{
    [Fact]
    public async Task ConfiguredSdkExportsExactlyOneApplicationSpanInTheWorkerTrace()
    {
        using var parent = new Activity("worker").SetIdFormat(ActivityIdFormat.W3C).Start();
        using var configuration = TelemetryConfiguration.CreateDefault();
        using var channel = new RecordingChannel();
        configuration.TelemetryChannel = channel;
        configuration.ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000001";
        var execution = new ToolExecution(new RecordingLogger(), new TelemetryClient(configuration));

        using var result = JsonDocument.Parse(await execution.RunAsync("set_oven_temperature", true, id =>
            Task.FromResult(OperationResultFactory.Completed(ResultCodes.TemperatureUpdated, "Done.",
                new object(), id, eventRecorded: true)), CancellationToken.None));

        var span = Assert.Single(channel.Items.OfType<DependencyTelemetry>());
        Assert.Equal(parent.TraceId.ToHexString(), span.Context.Operation.Id);
        Assert.Equal(parent.SpanId.ToHexString(), span.Context.Operation.ParentId);
        Assert.Equal(span.Context.Operation.Id, result.RootElement.GetProperty("correlation_id").GetString());
        Assert.Equal(ResultCodes.TemperatureUpdated, span.ResultCode);
        Assert.True(span.Success);
        Assert.Equal("PizzaOps", span.Type);
        Assert.Same(parent, Activity.Current);
    }

    [Fact]
    public async Task CurrentWorkerTraceFlowsIntoServiceResultAndStructuredWarningWithoutArguments()
    {
        using var parent = new Activity("worker").SetIdFormat(ActivityIdFormat.W3C).Start();
        var logger = new RecordingLogger();
        var execution = new ToolExecution(logger);
        string? serviceCorrelationId = null;
        using var result = JsonDocument.Parse(await execution.RunAsync("set_oven_temperature", true, correlationId =>
        {
            serviceCorrelationId = correlationId;
            Assert.Equal(parent.TraceId, Activity.Current!.TraceId);
            return Task.FromResult(OperationResultFactory.Completed(
                ResultCodes.TemperatureUpdated, "Done.", new { StateChanged = true }, correlationId,
                eventRecorded: false, warnings: ["EVENT_WRITE_FAILED: sensitive details must not be logged"]));
        }, CancellationToken.None));

        Assert.Equal(parent.TraceId.ToHexString(), serviceCorrelationId);
        Assert.Equal(serviceCorrelationId, result.RootElement.GetProperty("correlation_id").GetString());
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(ResultCodes.TemperatureUpdated, entry.Properties["Code"]);
        Assert.Equal("EVENT_WRITE_FAILED", entry.Properties["WarningCodes"]);
        Assert.Equal(false, entry.Properties["EventRecorded"]);
        Assert.True(Assert.IsType<double>(entry.Properties["DurationMs"]) >= 0);
        Assert.DoesNotContain("sensitive", string.Join(" ", entry.Properties.Values), StringComparison.Ordinal);
        Assert.Same(parent, Activity.Current);
    }

    [Theory]
    [InlineData(true, "STORAGE_OUTCOME_UNKNOWN")]
    [InlineData(false, "INTERNAL_ERROR")]
    public async Task UnexpectedFailuresAreSafeAndNeverClaimUnchangedState(bool mutation, string code)
    {
        var logger = new RecordingLogger();
        var execution = new ToolExecution(logger);
        var calls = 0;
        using var result = JsonDocument.Parse(await execution.RunAsync<object>("test", mutation, _ =>
        {
            calls++;
            throw new InvalidOperationException("credential=secret; raw prompt");
        }, CancellationToken.None));

        Assert.Equal(1, calls);
        Assert.Equal(code, result.RootElement.GetProperty("code").GetString());
        Assert.False(result.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, result.RootElement.GetProperty("data").ValueKind);
        Assert.DoesNotContain("secret", result.RootElement.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(logger.Entries, entry =>
            string.Join(" ", entry.Properties.Values).Contains("secret", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Properties.TryGetValue("ExceptionType", out var value) &&
            Equals(value, nameof(InvalidOperationException)));
    }

    [Fact]
    public async Task PreCancelledCommandNeverEntersServiceWhileInterruptedCommandRequiresRefresh()
    {
        var execution = new ToolExecution(new RecordingLogger());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        using var before = JsonDocument.Parse(await execution.RunAsync<object>("test", true,
            _ => throw new InvalidOperationException("Must not enter."), cancelled.Token));
        using var during = JsonDocument.Parse(await execution.RunAsync<object>("test", true,
            _ => throw new OperationCanceledException(), CancellationToken.None));

        Assert.Equal(ResultCodes.Cancelled, before.RootElement.GetProperty("code").GetString());
        Assert.Equal(ResultCodes.StorageOutcomeUnknown, during.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("CONCURRENT_UPDATE", false, null, null, LogLevel.Warning)]
    [InlineData("TEMPERATURE_OUT_OF_RANGE", false, true, null, LogLevel.Warning)]
    [InlineData("STORAGE_OUTCOME_UNKNOWN", false, null, null, LogLevel.Warning)]
    [InlineData("TEMPERATURE_UPDATED", true, false, "EVENT_WRITE_OUTCOME_UNKNOWN: uncertain", LogLevel.Warning)]
    [InlineData("TEMPERATURE_UPDATED", true, true, null, LogLevel.Information)]
    public async Task OutcomeLoggingDistinguishesConflictsRejectionCommitAndAmbiguity(
        string code, bool success, bool? recorded, string? warning, LogLevel expectedLevel)
    {
        var logger = new RecordingLogger();
        var execution = new ToolExecution(logger);
        await execution.RunAsync("set_oven_temperature", true, id => Task.FromResult(success
            ? OperationResultFactory.Completed(code, "Safe.", new object(), id, recorded,
                warning is null ? [] : [warning])
            : OperationResultFactory.Failed<object>(code, "Safe.", id, recorded)), CancellationToken.None);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(expectedLevel, entry.Level);
        Assert.Equal(code, entry.Properties["Code"]);
        Assert.Equal(recorded, entry.Properties["EventRecorded"]);
        if (warning is not null)
        {
            Assert.Equal("EVENT_WRITE_OUTCOME_UNKNOWN", entry.Properties["WarningCodes"]);
        }
    }

    private sealed class RecordingLogger : ILogger<ToolExecution>
    {
        public List<(LogLevel Level, Dictionary<string, object?> Properties)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Assert.Null(exception);
            var properties = Assert.IsAssignableFrom<IEnumerable<KeyValuePair<string, object?>>>(state);
            Entries.Add((logLevel, properties.ToDictionary()));
        }
    }

    private sealed class RecordingChannel : ITelemetryChannel
    {
        public List<ITelemetry> Items { get; } = [];
        public bool? DeveloperMode { get; set; }
        public string EndpointAddress { get; set; } = "http://127.0.0.1";
        public void Send(ITelemetry item) => Items.Add(item);
        public void Flush() { }
        public void Dispose() { }
    }
}
