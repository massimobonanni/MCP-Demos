using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using PizzaOps.Telemetry;

namespace PizzaOps.UnitTests;

public sealed class HostTelemetryTests
{
    [Fact]
    public void SuccessfulPollsAreFilteredButFailedRequestsAndWarningsArePreservedWithoutQueryStrings()
    {
        var sink = new RecordingProcessor();
        var processor = new SafeTelemetryProcessor(sink);
        var success = new RequestTelemetry
        {
            Url = new Uri("https://localhost/api/kitchen/state?key=secret"),
            Success = true,
            ResponseCode = "200"
        };
        var failure = new RequestTelemetry
        {
            Url = success.Url,
            Success = false,
            ResponseCode = "503"
        };
        var warning = new TraceTelemetry("Event feed unavailable.", SeverityLevel.Warning);
        processor.Process(success);
        processor.Process(failure);
        processor.Process(warning);

        Assert.Equal<ITelemetry>([failure, warning], sink.Items);
        Assert.Empty(failure.Url.Query);
    }

    [Fact]
    public void SuccessfulRefreshSpansAreFilteredWithoutDroppingDegradedOrCancelledReads()
    {
        var sink = new RecordingProcessor();
        var processor = new SafeTelemetryProcessor(sink);
        var successful = new DependencyTelemetry
        {
            Name = "PizzaOps.dashboard.refresh",
            Success = true,
            ResultCode = "OK"
        };
        var degraded = new DependencyTelemetry
        {
            Name = successful.Name,
            Success = false,
            ResultCode = "EVENT_FEED_UNAVAILABLE"
        };
        var cancelled = new DependencyTelemetry
        {
            Name = successful.Name,
            Success = false,
            ResultCode = "CANCELLED"
        };

        processor.Process(successful);
        processor.Process(degraded);
        processor.Process(cancelled);

        Assert.Equal<ITelemetry>([degraded, cancelled], sink.Items);
    }

    [Fact]
    public void RolesRemainDistinctWithoutChangingTraceCorrelationAndDependencyQueriesAreRemoved()
    {
        var dependency = new DependencyTelemetry { Data = "https://localhost/table?sig=secret" };
        dependency.Context.Operation.Id = "originating-trace";
        new RoleNameInitializer("PizzaOps.Functions").Initialize(dependency);
        var request = new RequestTelemetry();
        new RoleNameInitializer("PizzaOps.Dashboard").Initialize(request);
        var sink = new RecordingProcessor();
        new SafeTelemetryProcessor(sink).Process(dependency);

        Assert.Equal("PizzaOps.Functions", dependency.Context.Cloud.RoleName);
        Assert.Equal("PizzaOps.Dashboard", request.Context.Cloud.RoleName);
        Assert.Equal("originating-trace", dependency.Context.Operation.Id);
        Assert.Equal("https://localhost/table", dependency.Data);
        Assert.Same(dependency, Assert.Single(sink.Items));
    }

    private sealed class RecordingProcessor : ITelemetryProcessor
    {
        public List<ITelemetry> Items { get; } = [];
        public void Process(ITelemetry item) => Items.Add(item);
    }
}
