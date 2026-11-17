using PizzaOps.Core.Application;
using PizzaOps.Core.Contracts;
using PizzaOps.Core.Domain;

namespace PizzaOps.UnitTests;

public sealed class BakeTestServiceTests
{
    private static readonly DateTimeOffset ObservedAtUtc = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("margherita", 304, "UNDERCOOKED")]
    [InlineData("margherita", 305, "PASS")]
    [InlineData("margherita", 335, "PASS")]
    [InlineData("margherita", 336, "BURNT")]
    [InlineData("margherita", 450, "BURNT")]
    [InlineData("marinara", 300, "PASS")]
    [InlineData("diavola", 310, "PASS")]
    public async Task BakeUsesObservedTemperatureAndImmutableRecipeWithoutWritingTheOven(
        string pizzaType,
        int temperatureC,
        string expectedResult)
    {
        var (service, ovens, events) = CreateService(temperatureC);
        var original = ovens.Current;
        var recipe = PizzaRecipeCatalog.Instance.FindByType(pizzaType)!;

        var result = await service.BakeAsync(" OVEN-02 ", $" {pizzaType.ToUpperInvariant()} ", "trace-1", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(ResultCodes.TestCompleted, result.Code);
        Assert.Equal("trace-1", result.CorrelationId);
        Assert.True(result.EventRecorded);
        Assert.Empty(result.Warnings);
        var data = Assert.IsType<BakeResult>(result.Data);
        Assert.Equal("oven-02", data.OvenId);
        Assert.Equal(pizzaType, data.PizzaType);
        Assert.Equal(temperatureC, data.OvenTemperatureC);
        Assert.Equal(recipe.TargetTemperatureC, data.TargetTemperatureC);
        Assert.Equal(recipe.ToleranceC, data.ToleranceC);
        Assert.Equal(recipe.MinimumPassingTemperatureC, data.MinimumPassingTemperatureC);
        Assert.Equal(recipe.MaximumPassingTemperatureC, data.MaximumPassingTemperatureC);
        Assert.Equal(expectedResult, data.Result);
        Assert.Same(original, ovens.Current);
        Assert.Equal(1, ovens.ReadCalls);

        var recorded = Assert.Single(events.Events);
        Assert.Equal(KitchenEventType.TestBake, recorded.EventType);
        Assert.Equal(expectedResult == "PASS" ? KitchenEventOutcome.Pass : KitchenEventOutcome.Fail, recorded.Outcome);
        Assert.Equal(ObservedAtUtc, recorded.TimestampUtc);
        Assert.Equal("oven-02", recorded.OvenId);
        Assert.Equal("trace-1", recorded.CorrelationId);
        Assert.Contains($"{temperatureC} C", recorded.Summary, StringComparison.Ordinal);
        Assert.Contains($"{recipe.MinimumPassingTemperatureC}-{recipe.MaximumPassingTemperatureC} C", recorded.Summary, StringComparison.Ordinal);
        Assert.Contains(expectedResult, recorded.Summary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "margherita", ResultCodes.InvalidArgument)]
    [InlineData("", "margherita", ResultCodes.InvalidArgument)]
    [InlineData(" ", "margherita", ResultCodes.InvalidArgument)]
    [InlineData("oven-02", null, ResultCodes.InvalidArgument)]
    [InlineData("oven-02", " ", ResultCodes.InvalidArgument)]
    [InlineData("oven-02", "unknown", ResultCodes.RecipeNotFound)]
    [InlineData("oven-99", "margherita", ResultCodes.OvenNotFound)]
    public async Task MissingOrUnknownInputsFailWithoutAnEvent(string? ovenId, string? pizzaType, string expectedCode)
    {
        var (service, ovens, events) = CreateService(320);

        var result = await service.BakeAsync(ovenId, pizzaType, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(expectedCode, result.Code);
        Assert.Null(result.Data);
        Assert.Null(result.EventRecorded);
        Assert.Empty(events.Events);
        Assert.Equal(expectedCode == ResultCodes.OvenNotFound ? 1 : 0, ovens.ReadCalls);
    }

    [Fact]
    public async Task RepeatedBakesAppendDistinctEventsAndLaterChangesDoNotRewriteObservations()
    {
        var (service, ovens, events) = CreateService(450);
        var first = await service.BakeAsync("oven-02", "margherita", null, CancellationToken.None);
        var second = await service.BakeAsync("oven-02", "margherita", null, CancellationToken.None);
        ovens.Current = CreateOven(320);
        var repaired = await service.BakeAsync("oven-02", "margherita", null, CancellationToken.None);

        Assert.Equal("BURNT", first.Data?.Result);
        Assert.Equal(first.Data?.Result, second.Data?.Result);
        Assert.Equal(450, first.Data?.OvenTemperatureC);
        Assert.Equal("PASS", repaired.Data?.Result);
        Assert.Equal(320, repaired.Data?.OvenTemperatureC);
        Assert.Equal(3, events.Events.Count);
        Assert.Equal(3, events.Events.Select(item => item.EventId).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("450 C", events.Events[0].Summary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(KitchenStorageFailureKind.Unavailable, "EVENT_WRITE_FAILED:")]
    [InlineData(KitchenStorageFailureKind.OutcomeUnknown, "EVENT_WRITE_OUTCOME_UNKNOWN:")]
    public async Task EventWriteFailurePreservesCompletedBakeWithoutRetry(
        KitchenStorageFailureKind failureKind,
        string warningPrefix)
    {
        var (service, ovens, events) = CreateService(450);
        var original = ovens.Current;
        events.Exception = new KitchenStorageException(failureKind, "private storage details");

        var result = await service.BakeAsync("oven-02", "margherita", null, CancellationToken.None);

        AssertCompletedWithEventWarning(result, warningPrefix);
        Assert.Same(original, ovens.Current);
        Assert.Equal(1, ovens.ReadCalls);
        Assert.Equal(1, events.AppendCalls);
        Assert.Empty(events.Events);
    }

    [Fact]
    public async Task CancellationDuringEventAppendPreservesCompletedBakeWithUnknownEventOutcome()
    {
        var (service, _, events) = CreateService(450);
        events.Exception = new OperationCanceledException();

        var result = await service.BakeAsync("oven-02", "margherita", null, CancellationToken.None);

        AssertCompletedWithEventWarning(result, "EVENT_WRITE_OUTCOME_UNKNOWN:");
        Assert.Equal(1, events.AppendCalls);
    }

    [Theory]
    [InlineData(KitchenStorageFailureKind.Unavailable, ResultCodes.StorageUnavailable)]
    [InlineData(KitchenStorageFailureKind.InvalidData, ResultCodes.KitchenStateInvalid)]
    public async Task OvenReadFailureDoesNotFabricateABakeOrAppendAnEvent(
        KitchenStorageFailureKind failureKind,
        string expectedCode)
    {
        var (service, ovens, events) = CreateService(450);
        ovens.Exception = new KitchenStorageException(failureKind, "private storage details");

        var result = await service.BakeAsync("oven-02", "margherita", null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(expectedCode, result.Code);
        Assert.Null(result.Data);
        Assert.Null(result.EventRecorded);
        Assert.Empty(events.Events);
        Assert.DoesNotContain("private storage details", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellationBeforeObservationDoesNotReadOrRecordATest()
    {
        var (service, ovens, events) = CreateService(320);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.BakeAsync("oven-02", "margherita", null, cancellation.Token));

        Assert.Equal(0, ovens.ReadCalls);
        Assert.Empty(events.Events);
    }

    private static void AssertCompletedWithEventWarning(OperationResult<BakeResult> result, string warningPrefix)
    {
        Assert.True(result.Success);
        Assert.Equal(ResultCodes.TestCompleted, result.Code);
        Assert.Equal("BURNT", result.Data?.Result);
        Assert.False(result.EventRecorded);
        Assert.StartsWith(warningPrefix, Assert.Single(result.Warnings), StringComparison.Ordinal);
        Assert.DoesNotContain("private storage details", result.Warnings[0], StringComparison.Ordinal);
    }

    private static (BakeTestService Service, OvenRepository Ovens, EventRepository Events) CreateService(int temperatureC)
    {
        var ovens = new OvenRepository { Current = CreateOven(temperatureC) };
        var events = new EventRepository();
        return (new BakeTestService(ovens, events, PizzaRecipeCatalog.Instance, new FixedTimeProvider()), ovens, events);
    }

    private static Oven CreateOven(int temperatureC) =>
        new("oven-02", "Oven 02", temperatureC, DateTimeOffset.UnixEpoch, "original action", new ConcurrencyToken("original-etag"));

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ObservedAtUtc;
    }

    private sealed class OvenRepository : IOvenRepository
    {
        public required Oven Current { get; set; }
        public int ReadCalls { get; private set; }
        public Exception? Exception { get; set; }

        public Task<Oven?> GetByIdAsync(string normalizedOvenId, CancellationToken cancellationToken)
        {
            ReadCalls++;
            return Exception is null
                ? Task.FromResult(normalizedOvenId == Current.OvenId ? Current : null)
                : Task.FromException<Oven?>(Exception);
        }

        public Task<IReadOnlyList<Oven>> GetAllAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A bake reads only its requested oven.");

        public Task<OvenUpdateResult> TrySetTemperatureAsync(
            string normalizedOvenId,
            int targetTemperatureC,
            string? lastAction,
            DateTimeOffset updatedAtUtc,
            ConcurrencyToken expectedVersion,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A bake must never write an oven.");
    }

    private sealed class EventRepository : IKitchenEventRepository
    {
        public List<KitchenEvent> Events { get; } = [];
        public Exception? Exception { get; set; }
        public int AppendCalls { get; private set; }

        public Task AppendAsync(KitchenEvent kitchenEvent, CancellationToken cancellationToken)
        {
            AppendCalls++;
            if (Exception is not null)
            {
                return Task.FromException(Exception);
            }

            Events.Add(kitchenEvent);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<KitchenEvent>> GetLatestAsync(int count, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A bake does not read the event feed.");
    }
}
