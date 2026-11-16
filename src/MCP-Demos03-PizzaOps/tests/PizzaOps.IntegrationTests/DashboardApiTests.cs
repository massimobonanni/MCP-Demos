using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PizzaOps.Core.Application;
using PizzaOps.Core.Domain;
using PizzaOps.Storage.Persistence;

namespace PizzaOps.IntegrationTests;

public sealed class DashboardApiTests
{
    [Fact]
    public void ProductionReadRegistrationDoesNotExposeWriteCapabilities()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var services = factory.Services;
        Assert.NotNull(services.GetRequiredService<IKitchenReadService>());
        Assert.False(services.GetRequiredService<IOvenReader>() is IOvenRepository);
        Assert.False(services.GetRequiredService<IKitchenEventReader>() is IKitchenEventRepository);
        Assert.Null(services.GetService<IOvenRepository>());
        Assert.Null(services.GetService<IKitchenEventRepository>());
        Assert.Null(services.GetService<IKitchenTableMaintenance>());
        Assert.Null(services.GetService<TableKitchenRepository>());
    }

    [Fact]
    public async Task StateUsesSharedProjectionAndDoesNotExposePrivateFields()
    {
        var storage = new FakeStorage();
        using var factory = CreateFactory(storage);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/kitchen/state");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoStore(response);
        using var json = await ReadJsonAsync(response);
        var root = json.RootElement;
        Assert.True(root.GetProperty("event_feed_available").GetBoolean());
        Assert.Equal(JsonValueKind.String, root.GetProperty("retrieved_at_utc").ValueKind);
        var ovens = root.GetProperty("ovens").EnumerateArray().ToArray();
        Assert.Equal(["oven-01", "oven-02", "oven-03"], ovens.Select(o => o.GetProperty("oven_id").GetString()));
        Assert.Equal(["HEALTHY", "CRITICAL", "HEALTHY"], ovens.Select(o => o.GetProperty("health").GetString()));
        Assert.Equal(450, ovens[1].GetProperty("temperature_c").GetInt32());
        var events = root.GetProperty("events").EnumerateArray().ToArray();
        Assert.Equal(["newest", "older"], events.Select(e => e.GetProperty("summary").GetString()));
        Assert.Equal(["RESET", "TEST_BAKE"], events.Select(e => e.GetProperty("event_type").GetString()));
        Assert.Equal(["SUCCESS", "PASS"], events.Select(e => e.GetProperty("outcome").GetString()));
        Assert.Equal(20, storage.RequestedEvents);
        var body = root.GetRawText();
        Assert.DoesNotContain("private-reason", body);
        Assert.DoesNotContain("private-etag", body);
        Assert.DoesNotContain("private-correlation", body);

        Assert.Null(factory.Services.GetService<IOvenRepository>());
        Assert.Null(factory.Services.GetService<IKitchenEventRepository>());
        Assert.Null(factory.Services.GetService<IKitchenTableMaintenance>());
        Assert.Null(factory.Services.GetService<TableKitchenRepository>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnavailableOrIncompleteOvensFailStateAndReadiness(bool unavailable)
    {
        var storage = new FakeStorage { OvenUnavailable = unavailable, Incomplete = !unavailable };
        using var factory = CreateFactory(storage);
        using var client = factory.CreateClient();
        foreach (var path in new[] { "/api/kitchen/state", "/health/ready" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            AssertNoStore(response);
            using var json = await ReadJsonAsync(response);
            Assert.Equal(unavailable ? "STORAGE_UNAVAILABLE" : "KITCHEN_STATE_INVALID",
                json.RootElement.GetProperty("code").GetString());
            Assert.DoesNotContain("secret-internal-error", json.RootElement.GetRawText());
        }
    }

    [Fact]
    public async Task EventFailureIsExplicitAndReadinessFails()
    {
        using var factory = CreateFactory(new FakeStorage { EventsUnavailable = true });
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/kitchen/state");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoStore(response);
        using var json = await ReadJsonAsync(response);
        Assert.False(json.RootElement.GetProperty("event_feed_available").GetBoolean());
        Assert.NotEmpty(json.RootElement.GetProperty("event_feed_warning").GetString()!);
        Assert.Empty(json.RootElement.GetProperty("events").EnumerateArray());
        Assert.Equal(3, json.RootElement.GetProperty("ovens").GetArrayLength());
        using var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        AssertNoStore(ready);
    }

    [Fact]
    public async Task MalformedStorageStateReturnsInvalidKitchenProblem()
    {
        using var factory = CreateFactory(new FakeStorage { InvalidData = true });
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/kitchen/state");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AssertNoStore(response);
        using var json = await ReadJsonAsync(response);
        Assert.Equal("KITCHEN_STATE_INVALID", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task LiveDoesNotReadStorageAndReadyDoes()
    {
        var storage = new FakeStorage();
        using var factory = CreateFactory(storage);
        using var client = factory.CreateClient();
        using var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        AssertNoStore(live);
        Assert.Equal(0, storage.OvenReads);
        using var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        AssertNoStore(ready);
        Assert.Equal(1, storage.OvenReads);
    }

    [Theory]
    [InlineData("POST", "/api/kitchen/state")]
    [InlineData("PATCH", "/api/kitchen/state")]
    [InlineData("DELETE", "/api/kitchen/state")]
    [InlineData("POST", "/api/kitchen/reset")]
    [InlineData("POST", "/health/ready")]
    public async Task MutationRoutesAreNotAvailable(string method, string path)
    {
        var storage = new FakeStorage();
        using var factory = CreateFactory(storage);
        using var client = factory.CreateClient();
        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));
        Assert.True(response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound);
        AssertNoStore(response);
        Assert.Equal(0, storage.OvenReads);
    }

    [Theory]
    [InlineData("/api/kitchen/state")]
    [InlineData("/health/ready")]
    public async Task ReadsAreBoundedAndCancelStorage(string path)
    {
        var storage = new FakeStorage { Block = true };
        using var factory = CreateFactory(storage);
        using var client = factory.CreateClient();
        var stopwatch = Stopwatch.StartNew();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AssertNoStore(response);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(10));
        await storage.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ClientCancellationReachesStorage()
    {
        var storage = new FakeStorage { Block = true };
        using var factory = CreateFactory(storage);
        using var client = factory.CreateClient();
        using var cancellation = new CancellationTokenSource();
        var request = client.GetAsync("/api/kitchen/state", cancellation.Token);
        await storage.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await storage.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task UnexpectedErrorsDoNotExposeDetails()
    {
        using var factory = CreateFactory(new FakeStorage { UnexpectedFailure = true });
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/kitchen/state");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        AssertNoStore(response);
        Assert.DoesNotContain("secret-internal-error", await response.Content.ReadAsStringAsync());
    }

    private static WebApplicationFactory<Program> CreateFactory(FakeStorage? storage = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PizzaOps:TableStorage:Mode"] = "Azurite",
                ["PizzaOps:TableStorage:AzuriteConnectionString"] = "UseDevelopmentStorage=true"
            }));
            builder.ConfigureServices(services =>
            {
                if (storage is not null)
                {
                    services.RemoveAll<IOvenReader>();
                    services.RemoveAll<IKitchenEventReader>();
                    services.AddSingleton<IOvenReader>(storage);
                    services.AddSingleton<IKitchenEventReader>(storage);
                }
            });
        });

    private static void AssertNoStore(HttpResponseMessage response) =>
        Assert.True(response.Headers.CacheControl?.NoStore);

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonDocument>())!;

    private sealed class FakeStorage : IOvenReader, IKitchenEventReader
    {
        private static readonly DateTimeOffset Now = new(2026, 11, 14, 17, 0, 0, TimeSpan.Zero);
        public bool OvenUnavailable { get; init; }
        public bool EventsUnavailable { get; init; }
        public bool Incomplete { get; init; }
        public bool InvalidData { get; init; }
        public bool UnexpectedFailure { get; init; }
        public bool Block { get; init; }
        public int OvenReads { get; private set; }
        public int RequestedEvents { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<Oven>> GetAllAsync(CancellationToken cancellationToken)
        {
            OvenReads++;
            if (Block)
            {
                Started.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                finally
                {
                    Cancelled.TrySetResult();
                }
            }
            if (UnexpectedFailure)
            {
                throw new InvalidOperationException("secret-internal-error");
            }
            if (OvenUnavailable)
            {
                throw new KitchenStorageException(KitchenStorageFailureKind.Unavailable, "secret-internal-error");
            }
            if (InvalidData)
            {
                throw new KitchenStorageException(KitchenStorageFailureKind.InvalidData, "secret-internal-error");
            }
            var ovens = new[]
            {
                CreateOven("oven-03", 300), CreateOven("oven-01", 320), CreateOven("oven-02", 450)
            };
            return Incomplete ? ovens[..2] : ovens;
        }

        public Task<IReadOnlyList<KitchenEvent>> GetLatestAsync(int count, CancellationToken cancellationToken)
        {
            RequestedEvents = count;
            if (EventsUnavailable)
            {
                throw new KitchenStorageException(KitchenStorageFailureKind.Unavailable, "secret-internal-error");
            }
            IReadOnlyList<KitchenEvent> events =
            [
                new("event1", Now, KitchenEventType.Reset, null, "newest", KitchenEventOutcome.Success, "private-correlation"),
                new("event2", Now.AddMinutes(-1), KitchenEventType.TestBake, "oven-01", "older", KitchenEventOutcome.Pass, null)
            ];
            return Task.FromResult(events);
        }

        private static Oven CreateOven(string id, int temperature) =>
            new(id, id, temperature, Now, "private-reason", new ConcurrencyToken("private-etag"));
    }
}
