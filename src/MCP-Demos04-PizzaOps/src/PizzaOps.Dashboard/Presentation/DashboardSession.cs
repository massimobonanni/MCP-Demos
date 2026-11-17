using System.Threading.Channels;
using PizzaOps.Core.Application;
using PizzaOps.Core.Contracts;

namespace PizzaOps.Dashboard.Presentation;

/// <summary>Serializes read-only refreshes for one interactive dashboard.</summary>
public sealed partial class DashboardSession(
    IKitchenReadService kitchen,
    TimeProvider clock,
    ILogger<DashboardSession> logger) : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly Channel<bool> requests = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private CancellationTokenSource? activeRead;
    private Task? worker;
    private Task? ticker;
    private bool disposed;
    private DashboardView view = new(null, false, false, false, false, clock.GetUtcNow());

    /// <summary>Signals a presentation update; subscribers must dispatch to their renderer.</summary>
    public event Action? Changed;

    /// <summary>The latest immutable presentation state.</summary>
    public DashboardView View
    {
        get { lock (gate) { return view; } }
    }

    /// <summary>Starts a single refresh worker after browser visibility is known.</summary>
    public void Start()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (worker is not null)
            {
                return;
            }
            requests.Writer.TryWrite(true);
            worker = RunAsync();
            ticker = TickAsync();
        }
    }

    /// <summary>Pauses and cancels reads while hidden; returning queues an immediate read.</summary>
    public void SetVisible(bool visible) => SetAvailability(visible, null);

    /// <summary>Cancels disconnected reads and refreshes immediately after circuit recovery.</summary>
    public void SetConnected(bool connected) => SetAvailability(null, connected);

    private void SetAvailability(bool? visible, bool? connected)
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            view = view with
            {
                IsVisible = visible ?? view.IsVisible,
                IsConnected = connected ?? view.IsConnected,
                IsCurrent = false
            };
            if (!view.IsVisible || !view.IsConnected)
            {
                activeRead?.Cancel();
            }
        }
        Changed?.Invoke();
        requests.Writer.TryWrite(true);
    }

    private async Task TickAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
        try
        {
            while (await timer.WaitForNextTickAsync(lifetime.Token))
            {
                lock (gate)
                {
                    view = view with { Now = clock.GetUtcNow() };
                }
                Changed?.Invoke();
                requests.Writer.TryWrite(true);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            // Disposal stops the timer and the read worker together.
        }
    }

    private async Task RunAsync()
    {
        try
        {
            await foreach (var _ in requests.Reader.ReadAllAsync(lifetime.Token))
            {
                CancellationTokenSource read;
                lock (gate)
                {
                    if (disposed || !view.IsVisible || !view.IsConnected)
                    {
                        continue;
                    }
                    read = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    activeRead = read;
                }

                using (read)
                using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5), clock))
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(read.Token, deadline.Token))
                {
                    try
                    {
                        // Await actual completion even on cancellation: never overlap storage reads.
                        var result = await kitchen.GetStatusAsync(timeout.Token);
                        lock (gate)
                        {
                            if (!read.IsCancellationRequested && !timeout.IsCancellationRequested)
                            {
                                view = view with
                                {
                                    Snapshot = result.Success && result.Data is not null ? result.Data : view.Snapshot,
                                    HasFailed = !result.Success || result.Data is null,
                                    IsCurrent = result.Success && result.Data is not null,
                                    Now = clock.GetUtcNow()
                                };
                                if (view.HasFailed)
                                {
                                    LogReadFailed(logger, result.Code);
                                }
                                else if (result.Data?.EventFeedAvailable == false)
                                {
                                    LogFeedDegraded(logger);
                                }
                            }
                            else if (!read.IsCancellationRequested)
                            {
                                MarkFailed();
                            }
                        }
                    }
                    catch (OperationCanceledException) when (read.IsCancellationRequested)
                    {
                        // Hidden tabs, lost circuits and disposal are deliberate cancellations.
                    }
                    catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                    {
                        lock (gate) { MarkFailed(); }
                    }
                    catch (Exception exception)
                    {
                        LogUnexpectedFailure(logger, exception.GetType().Name);
                        lock (gate)
                        {
                            if (!read.IsCancellationRequested)
                            {
                                view = view with { HasFailed = true, IsCurrent = false };
                            }
                        }
                    }
                    finally
                    {
                        lock (gate) { activeRead = null; }
                    }
                }
                if (!lifetime.IsCancellationRequested)
                {
                    Changed?.Invoke();
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            // Expected when the component is disposed.
        }
    }

    private void MarkFailed()
    {
        view = view with { HasFailed = true, IsCurrent = false, Now = clock.GetUtcNow() };
        LogReadFailed(logger, "READ_TIMEOUT");
    }

    /// <summary>Cancels pending work and waits until no refresh can publish another snapshot.</summary>
    public async ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            lifetime.Cancel();
            activeRead?.Cancel();
        }
        await Task.WhenAll(worker ?? Task.CompletedTask, ticker ?? Task.CompletedTask);
        lifetime.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dashboard refresh failed with code {Code}.")]
    private static partial void LogReadFailed(ILogger logger, string code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dashboard event feed is degraded.")]
    private static partial void LogFeedDegraded(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected dashboard read failure: {ExceptionType}.")]
    private static partial void LogUnexpectedFailure(ILogger logger, string exceptionType);
}

/// <summary>UI freshness is independent of backend-computed oven health.</summary>
public sealed record DashboardView(
    KitchenSnapshot? Snapshot,
    bool IsVisible,
    bool IsConnected,
    bool IsCurrent,
    bool HasFailed,
    DateTimeOffset Now)
{
    /// <summary>Age of the last successful authoritative read, not the age of an oven update.</summary>
    public TimeSpan? Age => Snapshot is null ? null : Now - Snapshot.RetrievedAtUtc;

    /// <summary>Never labels an old, paused, disconnected or failed snapshot as live.</summary>
    public bool IsLive => IsVisible && IsConnected && IsCurrent && !HasFailed && Age <= TimeSpan.FromSeconds(3);
}
