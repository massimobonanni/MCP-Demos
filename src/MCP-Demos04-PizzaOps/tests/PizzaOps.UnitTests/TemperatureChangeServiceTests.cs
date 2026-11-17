using PizzaOps.Core.Application;
using PizzaOps.Core.Contracts;
using PizzaOps.Core.Domain;

namespace PizzaOps.UnitTests;

public sealed class TemperatureChangeServiceTests
{
    private static readonly DateTimeOffset UpdatedAtUtc = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SetTemperatureConditionallyUpdatesAndAppendsASafeChangeEvent()
    {
        var (service, ovens, events) = CreateService(450);

        var result = await service.SetTemperatureAsync(
            " OVEN-02 ",
            320,
            "  operator repair  ",
            "correlation-1",
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(ResultCodes.TemperatureUpdated, result.Code);
        Assert.Equal("oven-02", result.Data?.OvenId);
        Assert.Equal(450, result.Data?.PreviousTemperatureC);
        Assert.Equal(320, result.Data?.CurrentTemperatureC);
        Assert.Equal("HEALTHY", result.Data?.Health);
        Assert.Equal(UpdatedAtUtc, result.Data?.UpdatedAtUtc);
        Assert.True(result.Data?.StateChanged);
        Assert.Equal(true, result.EventRecorded);
        Assert.Equal("operator repair", ovens.Current.LastAction);
        Assert.Equal(new ConcurrencyToken("initial-etag"), ovens.ExpectedVersion);
        Assert.Equal(1, ovens.UpdateCalls);

        var kitchenEvent = Assert.Single(events.Events);
        Assert.Equal(KitchenEventType.TemperatureChanged, kitchenEvent.EventType);
        Assert.Equal(KitchenEventOutcome.Success, kitchenEvent.Outcome);
        Assert.Equal("oven-02", kitchenEvent.OvenId);
        Assert.Equal("Temperature changed from 450 C to 320 C.", kitchenEvent.Summary);
        Assert.DoesNotContain("operator repair", kitchenEvent.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnsafeTemperatureFailsDomainPolicyWithoutChangingAnyOvenField()
    {
        var (service, ovens, events) = CreateService(450);
        var original = ovens.Current;

        var result = await service.SetTemperatureAsync(
            "oven-02",
            600,
            "do not record this reason",
            null,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ResultCodes.TemperatureOutOfRange, result.Code);
        Assert.Equal(600, result.Data?.RequestedTemperatureC);
        Assert.Equal(450, result.Data?.PreviousTemperatureC);
        Assert.Equal(450, result.Data?.CurrentTemperatureC);
        Assert.Equal("CRITICAL", result.Data?.Health);
        Assert.False(result.Data?.StateChanged);
        Assert.Equal(true, result.EventRecorded);
        Assert.Equal(original, ovens.Current);
        Assert.Equal(0, ovens.UpdateCalls);

        var rejection = Assert.Single(events.Events);
        Assert.Equal(KitchenEventType.TemperatureRejected, rejection.EventType);
        Assert.Equal(KitchenEventOutcome.Rejected, rejection.Outcome);
        Assert.Equal("Temperature command rejected by policy.", rejection.Summary);
        Assert.DoesNotContain("600", rejection.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("do not record", rejection.Summary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(199)]
    [InlineData(351)]
    [InlineData(600)]
    public async Task OutOfRangeCommandsAppendRejectionWithoutAttemptingAWrite(int targetTemperatureC)
    {
        var (service, ovens, events) = CreateService(450);
        var original = ovens.Current;

        var result = await service.SetTemperatureAsync(
            "oven-02",
            targetTemperatureC,
            null,
            null,
            CancellationToken.None);

        Assert.Equal(ResultCodes.TemperatureOutOfRange, result.Code);
        Assert.Equal(original, ovens.Current);
        Assert.Equal(0, ovens.UpdateCalls);
        Assert.Equal(KitchenEventType.TemperatureRejected, Assert.Single(events.Events).EventType);
    }

    [Fact]
    public async Task RepeatingTheCurrentTemperatureReturnsNoChangeWithoutAnEvent()
    {
        var (service, ovens, events) = CreateService(320);
        var original = ovens.Current;

        var result = await service.SetTemperatureAsync(
            "oven-02",
            320,
            null,
            null,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(ResultCodes.NoChange, result.Code);
        Assert.False(result.Data?.StateChanged);
        Assert.Null(result.EventRecorded);
        Assert.Equal(original, ovens.Current);
        Assert.Equal(0, ovens.UpdateCalls);
        Assert.Empty(events.Events);
    }

    [Fact]
    public async Task ConcurrentCommandsUsingTheSameObservedVersionCannotBothCommit()
    {
        var ovens = new InMemoryOvenRepository(CreateOven(450), synchronizeReads: true);
        var events = new InMemoryEventRepository();
        var service = new TemperatureChangeService(ovens, events, new FixedTimeProvider(UpdatedAtUtc));

        var results = await Task.WhenAll(
            service.SetTemperatureAsync("oven-02", 320, null, null, CancellationToken.None),
            service.SetTemperatureAsync("oven-02", 330, null, null, CancellationToken.None));

        Assert.Single(results, result => result.Code == ResultCodes.TemperatureUpdated);
        Assert.Single(results, result => result.Code == ResultCodes.ConcurrentUpdate);
        Assert.Equal(1, ovens.SuccessfulWrites);
        Assert.Single(events.Events, item => item.EventType == KitchenEventType.TemperatureChanged);
        Assert.Single(events.Events, item => item.EventType == KitchenEventType.TemperatureRejected);
    }

    [Fact]
    public async Task FailedEventAppendReportsCommittedTemperatureAndWarningWithoutRetry()
    {
        var (service, ovens, events) = CreateService(450);
        events.ExceptionToThrow = new KitchenStorageException(
            KitchenStorageFailureKind.Unavailable,
            "storage details are not public");

        var result = await service.SetTemperatureAsync(
            "oven-02",
            320,
            null,
            null,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(ResultCodes.TemperatureUpdated, result.Code);
        Assert.Equal(320, result.Data?.CurrentTemperatureC);
        Assert.False(result.EventRecorded);
        Assert.Contains(result.Warnings, warning => warning.StartsWith("EVENT_WRITE_FAILED:", StringComparison.Ordinal));
        Assert.Equal(320, ovens.Current.TemperatureC);
        Assert.Equal(1, ovens.UpdateCalls);
        Assert.Equal(1, events.AppendCalls);
    }

    [Fact]
    public async Task AmbiguousTemperatureWriteIsReportedWithoutRetryOrEventAppend()
    {
        var (service, ovens, events) = CreateService(450);
        ovens.ExceptionToThrow = new KitchenStorageException(
            KitchenStorageFailureKind.OutcomeUnknown,
            "storage details are not public");

        var result = await service.SetTemperatureAsync(
            "oven-02",
            320,
            null,
            null,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ResultCodes.StorageOutcomeUnknown, result.Code);
        Assert.Contains("Do not retry automatically", result.Message, StringComparison.Ordinal);
        Assert.Equal(1, ovens.UpdateCalls);
        Assert.Equal(450, ovens.Current.TemperatureC);
        Assert.Empty(events.Events);
    }

    [Fact]
    public async Task AmbiguousEventWriteIsDistinctFromKnownEventFailure()
    {
        var (service, ovens, events) = CreateService(450);
        events.ExceptionToThrow = new KitchenStorageException(
            KitchenStorageFailureKind.OutcomeUnknown,
            "storage details are not public");

        var result = await service.SetTemperatureAsync(
            "oven-02",
            320,
            null,
            null,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(320, ovens.Current.TemperatureC);
        Assert.False(result.EventRecorded);
        Assert.Contains(
            result.Warnings,
            warning => warning.StartsWith("EVENT_WRITE_OUTCOME_UNKNOWN:", StringComparison.Ordinal));
        Assert.Equal(1, ovens.UpdateCalls);
        Assert.Equal(1, events.AppendCalls);
    }

    [Fact]
    public async Task CancellationDuringEventAppendKeepsCommittedStateAndReportsUnknownEventOutcome()
    {
        var (service, ovens, events) = CreateService(450);
        events.ExceptionToThrow = new OperationCanceledException();

        var result = await service.SetTemperatureAsync(
            "oven-02",
            320,
            null,
            null,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(320, ovens.Current.TemperatureC);
        Assert.False(result.EventRecorded);
        Assert.Contains(
            result.Warnings,
            warning => warning.StartsWith("EVENT_WRITE_OUTCOME_UNKNOWN:", StringComparison.Ordinal));
        Assert.Equal(1, ovens.UpdateCalls);
        Assert.Equal(1, events.AppendCalls);
    }

    [Fact]
    public async Task InvalidIdentifierAndExcessiveReasonFailBeforeLookup()
    {
        var (service, ovens, events) = CreateService(450);

        var missingId = await service.SetTemperatureAsync(" ", 320, null, null, CancellationToken.None);
        var excessiveReason = await service.SetTemperatureAsync(
            "oven-02",
            320,
            new string('x', 121),
            null,
            CancellationToken.None);

        Assert.Equal(ResultCodes.InvalidArgument, missingId.Code);
        Assert.Equal(ResultCodes.InvalidArgument, excessiveReason.Code);
        Assert.Equal(0, ovens.ReadCalls);
        Assert.Equal(0, ovens.UpdateCalls);
        Assert.Empty(events.Events);
    }

    [Fact]
    public async Task CancellationDuringConditionalWriteReturnsUnknownOutcomeAndDoesNotRetry()
    {
        var (service, ovens, events) = CreateService(450);
        ovens.ExceptionToThrow = new OperationCanceledException();

        var result = await service.SetTemperatureAsync(
            "oven-02",
            320,
            null,
            null,
            CancellationToken.None);

        Assert.Equal(ResultCodes.StorageOutcomeUnknown, result.Code);
        Assert.Equal(1, ovens.UpdateCalls);
        Assert.Empty(events.Events);
    }

    [Fact]
    public async Task CancellationDuringLookupDoesNotClaimAnAmbiguousWrite()
    {
        var (service, ovens, events) = CreateService(450);
        ovens.ReadException = new OperationCanceledException();

        var result = await service.SetTemperatureAsync("oven-02", 320, null, "trace", CancellationToken.None);

        Assert.Equal(ResultCodes.Cancelled, result.Code);
        Assert.Equal("trace", result.CorrelationId);
        Assert.Equal(0, ovens.UpdateCalls);
        Assert.Empty(events.Events);
    }

    [Fact]
    public async Task CancellationBeforeLookupDoesNotReadOrWriteStorage()
    {
        var (service, ovens, events) = CreateService(450);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.SetTemperatureAsync(
                "oven-02",
                320,
                null,
                null,
                cancellation.Token));

        Assert.Equal(0, ovens.ReadCalls);
        Assert.Equal(0, ovens.UpdateCalls);
        Assert.Empty(events.Events);
    }

    private static (TemperatureChangeService Service, InMemoryOvenRepository Ovens, InMemoryEventRepository Events)
        CreateService(int temperatureC)
    {
        var ovens = new InMemoryOvenRepository(CreateOven(temperatureC));
        var events = new InMemoryEventRepository();
        var service = new TemperatureChangeService(ovens, events, new FixedTimeProvider(UpdatedAtUtc));
        return (service, ovens, events);
    }

    private static Oven CreateOven(int temperatureC) =>
        new(
            "oven-02",
            "Oven 02",
            temperatureC,
            DateTimeOffset.UnixEpoch,
            "original action",
            new ConcurrencyToken("initial-etag"));

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class InMemoryOvenRepository(Oven initialOven, bool synchronizeReads = false) : IOvenRepository
    {
        private readonly object sync = new();
        private readonly TaskCompletionSource allReaders = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int readCalls;
        private int version;

        public Oven Current { get; private set; } = initialOven;

        public int ReadCalls => readCalls;

        public int UpdateCalls { get; private set; }

        public int SuccessfulWrites { get; private set; }

        public ConcurrencyToken? ExpectedVersion { get; private set; }

        public Exception? ExceptionToThrow { get; set; }

        public Exception? ReadException { get; set; }

        public Task<IReadOnlyList<Oven>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Oven>>([Current]);

        public async Task<Oven?> GetByIdAsync(string normalizedOvenId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref readCalls);
            if (ReadException is not null)
            {
                throw ReadException;
            }
            var observed = Current;

            if (synchronizeReads)
            {
                if (Volatile.Read(ref readCalls) == 2)
                {
                    allReaders.TrySetResult();
                }

                await allReaders.Task.WaitAsync(cancellationToken);
            }

            return string.Equals(normalizedOvenId, observed.OvenId, StringComparison.Ordinal)
                ? observed
                : null;
        }

        public Task<OvenUpdateResult> TrySetTemperatureAsync(
            string normalizedOvenId,
            int targetTemperatureC,
            string? lastAction,
            DateTimeOffset updatedAtUtc,
            ConcurrencyToken expectedVersion,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (sync)
            {
                UpdateCalls++;
                ExpectedVersion = expectedVersion;

                if (ExceptionToThrow is not null)
                {
                    throw ExceptionToThrow;
                }

                if (expectedVersion != Current.Version)
                {
                    return Task.FromResult(new OvenUpdateResult(OvenUpdateStatus.Conflict, null));
                }

                Current = new Oven(
                    Current.OvenId,
                    Current.DisplayName,
                    targetTemperatureC,
                    updatedAtUtc,
                    lastAction,
                    new ConcurrencyToken($"etag-{++version}"));
                SuccessfulWrites++;
                return Task.FromResult(new OvenUpdateResult(OvenUpdateStatus.Updated, Current));
            }
        }
    }

    private sealed class InMemoryEventRepository : IKitchenEventRepository
    {
        private readonly List<KitchenEvent> events = [];
        private readonly object sync = new();

        public IReadOnlyList<KitchenEvent> Events
        {
            get
            {
                lock (sync)
                {
                    return events.ToArray();
                }
            }
        }

        public int AppendCalls { get; private set; }

        public Exception? ExceptionToThrow { get; set; }

        public Task AppendAsync(KitchenEvent kitchenEvent, CancellationToken cancellationToken)
        {
            lock (sync)
            {
                AppendCalls++;

                if (ExceptionToThrow is not null)
                {
                    throw ExceptionToThrow;
                }

                events.Add(kitchenEvent);
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<KitchenEvent>> GetLatestAsync(int count, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<KitchenEvent>>(Events.Take(count).ToArray());
    }
}
