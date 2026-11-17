using Azure.Core.Pipeline;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using PizzaOps.Core.Application;
using PizzaOps.Storage;
using PizzaOps.Storage.Persistence;

namespace PizzaOps.UnitTests;

public sealed class TableReadFailureTests
{
    [Fact]
    public async Task ExhaustedConnectionRetriesAreReportedAsUnavailableForAllReadPaths()
    {
        using var httpClient = new HttpClient(new FailingHandler());
        var clientOptions = new TableClientOptions
        {
            Transport = new HttpClientTransport(httpClient)
        };
        clientOptions.Retry.MaxRetries = 2;
        clientOptions.Retry.Delay = TimeSpan.Zero;
        clientOptions.Retry.MaxDelay = TimeSpan.Zero;
        var client = new TableServiceClient(
            new Uri("http://127.0.0.1:10002/devstoreaccount1"),
            new TableSharedKeyCredential("devstoreaccount1", "dGVzdA=="),
            clientOptions);
        var repository = new TableKitchenRepository(client, Options.Create(new PizzaOpsTableOptions()));

        var ovensError = await Assert.ThrowsAsync<KitchenStorageException>(
            () => repository.GetAllAsync(CancellationToken.None));
        var eventsError = await Assert.ThrowsAsync<KitchenStorageException>(
            () => repository.GetLatestAsync(20, CancellationToken.None));
        var ovenError = await Assert.ThrowsAsync<KitchenStorageException>(
            () => repository.GetByIdAsync("oven-02", CancellationToken.None));
        var preWriteReadError = await Assert.ThrowsAsync<KitchenStorageException>(
            () => repository.TrySetTemperatureAsync("oven-02", 320, null, DateTimeOffset.UtcNow,
                new PizzaOps.Core.Domain.ConcurrencyToken("observed-etag"), CancellationToken.None));

        Assert.Equal(KitchenStorageFailureKind.Unavailable, ovensError.FailureKind);
        Assert.Equal(KitchenStorageFailureKind.Unavailable, eventsError.FailureKind);
        Assert.Equal(KitchenStorageFailureKind.Unavailable, ovenError.FailureKind);
        Assert.Equal(KitchenStorageFailureKind.Unavailable, preWriteReadError.FailureKind);
        Assert.IsType<AggregateException>(ovensError.InnerException);
        Assert.IsType<AggregateException>(eventsError.InnerException);
        Assert.IsType<AggregateException>(ovenError.InnerException);
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("Simulated connection failure."));
    }
}
