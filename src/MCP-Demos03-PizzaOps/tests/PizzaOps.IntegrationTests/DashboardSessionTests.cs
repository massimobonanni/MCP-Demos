using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaOps.Core.Application;
using PizzaOps.Core.Contracts;
using PizzaOps.Core.Domain;
using PizzaOps.Dashboard.Presentation;

namespace PizzaOps.IntegrationTests;

public sealed class DashboardSessionTests
{
    [Fact]
    public async Task SuccessfulReadsWaitOneSecondAndHiddenTabsPauseUntilImmediateRecovery()
    {
        var clock = new ManualClock();
        var reader = new ControlledReader();
        await using var session = Create(reader, clock);
        Start(session);
        (await reader.NextAsync()).Complete(Snapshot(clock));
        await UntilAsync(session, view => view.IsLive);
        clock.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Equal(1, reader.Calls);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        (await reader.NextAsync()).Complete(Snapshot(clock));
        await UntilAsync(session, view => view.Snapshot?.RetrievedAtUtc == clock.GetUtcNow());
        session.SetVisible(false);
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(2, reader.Calls);
        Assert.False(session.View.IsLive);
        session.SetVisible(true);
        var resumed = await reader.NextAsync();
        Assert.False(session.View.IsLive);
        resumed.Complete(Snapshot(clock));
        await UntilAsync(session, view => view.IsLive);
    }

    [Fact]
    public async Task OneSecondTicksNeverOverlapSlowReads()
    {
        var clock = new ManualClock();
        var reader = new ControlledReader();
        await using var session = Create(reader, clock);
        Start(session);
        var first = await reader.NextAsync();
        clock.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Equal(1, reader.Calls);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(1, reader.Calls);
        first.Complete(Snapshot(clock));
        var second = await reader.NextAsync();
        Assert.Equal(2, reader.Calls);
        Assert.False(first.Token.IsCancellationRequested);
        second.Complete(Snapshot(clock));
        await UntilAsync(session, view => view.IsLive);
    }

    [Fact]
    public async Task FirstFailureHasNoDataAndLaterFailureRetainsLastSnapshotUntilSuccess()
    {
        var clock = new ManualClock();
        var reader = new ControlledReader();
        await using var session = Create(reader, clock);
        Start(session);
        Assert.Null(session.View.Snapshot);
        (await reader.NextAsync()).Fail();
        await UntilAsync(session, view => view.HasFailed);
        Assert.Null(session.View.Snapshot);
        Assert.False(session.View.IsLive);

        clock.Advance(TimeSpan.FromSeconds(1));
        var good = Snapshot(clock);
        (await reader.NextAsync()).Complete(good);
        await UntilAsync(session, view => view.IsLive);

        clock.Advance(TimeSpan.FromSeconds(1));
        (await reader.NextAsync()).Fail();
        await UntilAsync(session, view => view.HasFailed);
        Assert.Same(good, session.View.Snapshot);
        Assert.False(session.View.IsLive);

        clock.Advance(TimeSpan.FromSeconds(1));
        var recovery = await reader.NextAsync();
        Assert.False(session.View.IsLive);
        recovery.Complete(Snapshot(clock, eventFeedAvailable: false));
        await UntilAsync(session, view => view.IsLive);
        Assert.False(session.View.HasFailed);
        Assert.False(session.View.Snapshot!.EventFeedAvailable);
        Assert.Empty(session.View.Snapshot.RecentEvents);
    }

    [Fact]
    public async Task HiddenTabCancelsReadAndVisibleRecoveryIsImmediateButSerialized()
    {
        var clock = new ManualClock();
        var reader = new ControlledReader(ignoreCancellation: true);
        await using var session = Create(reader, clock);
        Start(session);
        var pending = await reader.NextAsync();
        session.SetVisible(false);
        Assert.True(pending.Token.IsCancellationRequested);
        clock.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(1, reader.Calls);
        session.SetVisible(true);
        Assert.False(session.View.IsLive);
        Assert.Equal(1, reader.Calls);
        // Even a cancellation-ignoring provider cannot publish its late result or overlap a read.
        pending.Complete(Snapshot(clock));
        var resumed = await reader.NextAsync();
        Assert.Null(session.View.Snapshot);
        resumed.Complete(Snapshot(clock));
        await UntilAsync(session, view => view.IsLive);
    }

    [Fact]
    public async Task CircuitLossCancelsReadsAndReconnectRequiresFreshSuccess()
    {
        var clock = new ManualClock();
        var reader = new ControlledReader(ignoreCancellation: true);
        await using var session = Create(reader, clock);
        Start(session);
        var snapshot = Snapshot(clock);
        (await reader.NextAsync()).Complete(snapshot);
        await UntilAsync(session, view => view.IsLive);
        clock.Advance(TimeSpan.FromSeconds(1));
        var pending = await reader.NextAsync();
        session.SetConnected(false);
        Assert.True(pending.Token.IsCancellationRequested);
        Assert.False(session.View.IsLive);
        pending.Complete(Snapshot(clock));
        session.SetConnected(true);
        var recovery = await reader.NextAsync();
        Assert.Same(snapshot, session.View.Snapshot);
        Assert.False(session.View.IsLive);
        recovery.Complete(Snapshot(clock));
        await UntilAsync(session, view => view.IsLive);
    }

    [Fact]
    public async Task TimeoutCancelsStorageAndOldSnapshotsLoseLiveQualification()
    {
        var clock = new ManualClock();
        var reader = new ControlledReader();
        await using var session = Create(reader, clock);
        Start(session);
        (await reader.NextAsync()).Complete(Snapshot(clock));
        await UntilAsync(session, view => view.IsLive);
        clock.Advance(TimeSpan.FromSeconds(1));
        var pending = await reader.NextAsync();
        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.False(session.View.IsLive);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(pending.Token.IsCancellationRequested);
        await UntilAsync(session, view => view.HasFailed);
        Assert.NotNull(session.View.Snapshot);
        Assert.False(session.View.IsLive);
    }

    [Fact]
    public async Task DisposalCancelsAndDrainsReadWithoutPublishingLateDataOrRestarting()
    {
        var clock = new ManualClock();
        var reader = new ControlledReader(ignoreCancellation: true);
        var session = Create(reader, clock);
        Start(session);
        var pending = await reader.NextAsync();
        var disposal = session.DisposeAsync().AsTask();
        Assert.True(pending.Token.IsCancellationRequested);
        Assert.False(disposal.IsCompleted);
        pending.Complete(Snapshot(clock));
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(10));
        session.SetVisible(true);
        session.SetConnected(true);
        Assert.Null(session.View.Snapshot);
        Assert.Equal(1, reader.Calls);
        Assert.Throws<ObjectDisposedException>(session.Start);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task HiddenStartupDoesNotReadAndUnexpectedFailureIsSafe()
    {
        var clock = new ManualClock();
        var reader = new ControlledReader();
        await using var session = Create(reader, clock);
        session.SetConnected(true);
        session.Start();
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(0, reader.Calls);
        session.SetVisible(true);
        var pending = await reader.NextAsync();
        pending.Result.SetException(new InvalidOperationException("private-storage-details"));
        await UntilAsync(session, view => view.HasFailed);
        Assert.Null(session.View.Snapshot);
        Assert.False(session.View.IsLive);
    }

    private static DashboardSession Create(ControlledReader reader, ManualClock clock) =>
        new(reader, clock, NullLogger<DashboardSession>.Instance);

    private static void Start(DashboardSession session)
    {
        session.SetVisible(true);
        session.SetConnected(true);
        session.Start();
    }

    private static KitchenSnapshot Snapshot(ManualClock clock, bool eventFeedAvailable = true) =>
        new(clock.GetUtcNow(), [], [], eventFeedAvailable, eventFeedAvailable ? null : "Recent events are unavailable.");

    private static async Task UntilAsync(DashboardSession session, Func<DashboardView, bool> predicate)
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Check()
        {
            if (predicate(session.View)) { signal.TrySetResult(); }
        }
        session.Changed += Check;
        try
        {
            Check();
            await signal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { session.Changed -= Check; }
    }

    private sealed class ControlledReader(bool ignoreCancellation = false) : IKitchenReadService
    {
        private readonly Channel<ReadCall> reads = Channel.CreateUnbounded<ReadCall>();
        private int calls;
        public int Calls => Volatile.Read(ref calls);

        public Task<OperationResult<KitchenSnapshot>> GetStatusAsync(CancellationToken cancellationToken)
        {
            var read = new ReadCall(cancellationToken);
            Interlocked.Increment(ref calls);
            reads.Writer.TryWrite(read);
            return ignoreCancellation ? read.Result.Task : read.Result.Task.WaitAsync(cancellationToken);
        }

        public async Task<ReadCall> NextAsync() =>
            await reads.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class ReadCall(CancellationToken token)
    {
        public CancellationToken Token { get; } = token;
        public TaskCompletionSource<OperationResult<KitchenSnapshot>> Result { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete(KitchenSnapshot snapshot) =>
            Result.SetResult(OperationResultFactory.Completed(ResultCodes.Ok, "Loaded.", snapshot));
        public void Fail() => Result.SetResult(OperationResultFactory.Failed<KitchenSnapshot>(
            ResultCodes.StorageUnavailable, "Kitchen state is unavailable."));
    }

    private sealed class ManualClock : TimeProvider
    {
        private readonly List<ManualTimer> timers = [];
        private DateTimeOffset now = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() { lock (timers) { return now; } }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (timers)
            {
                var timer = new ManualTimer(this, callback, state);
                timers.Add(timer);
                timer.Change(dueTime, period);
                return timer;
            }
        }

        public void Advance(TimeSpan amount)
        {
            List<ManualTimer> due;
            lock (timers)
            {
                now += amount;
                due = timers.Where(timer => timer.Due <= now).ToList();
                foreach (var timer in due)
                {
                    timer.Due = timer.Period == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : now + timer.Period;
                }
            }
            foreach (var timer in due) { timer.Fire(); }
        }

        private sealed class ManualTimer(ManualClock owner, TimerCallback callback, object? state) : ITimer
        {
            public DateTimeOffset Due { get; set; }
            public TimeSpan Period { get; private set; }
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner.timers)
                {
                    Due = dueTime == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : owner.now + dueTime;
                    Period = period;
                    return true;
                }
            }
            public void Fire() => callback(state);
            public void Dispose() { lock (owner.timers) { owner.timers.Remove(this); } }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
