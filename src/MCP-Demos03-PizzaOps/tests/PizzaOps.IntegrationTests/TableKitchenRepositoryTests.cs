using Azure;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using Microsoft.Extensions.Options;
using PizzaOps.Core.Application;
using PizzaOps.Core.Contracts;
using PizzaOps.Core.Domain;
using PizzaOps.DemoAdmin;
using PizzaOps.Storage;
using PizzaOps.Storage.Persistence;

namespace PizzaOps.IntegrationTests;

public sealed class TableKitchenRepositoryTests : IAsyncLifetime
{
    private static readonly int[] CanonicalTemperatures = [320, 450, 300];
    private readonly TableServiceClient tableServiceClient;
    private readonly TableClient ovensTable;
    private readonly TableClient eventsTable;
    private readonly TableKitchenRepository repository;
    private readonly TableKitchenRepository maintenance;
    private readonly PizzaOpsTableOptions options;

    public TableKitchenRepositoryTests()
    {
        var connectionString = Environment.GetEnvironmentVariable("PIZZAOPS_AZURITE_CONNECTION_STRING");
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var suffix = Guid.NewGuid().ToString("N")[..10];
        options = new PizzaOpsTableOptions
        {
            Mode = "Azurite",
            AzuriteConnectionString = connectionString,
            OvensTableName = $"PizzaOvens{suffix}",
            EventsTableName = $"PizzaEvents{suffix}",
            PartitionKey = "main-kitchen"
        };
        tableServiceClient = new TableServiceClient(connectionString);
        ovensTable = tableServiceClient.GetTableClient(options.OvensTableName);
        eventsTable = tableServiceClient.GetTableClient(options.EventsTableName);
        repository = new TableKitchenRepository(tableServiceClient, Options.Create(options));
        maintenance = repository;
    }

    public async Task InitializeAsync()
    {
        await maintenance.EnsureTablesAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await DeleteTableIfPresentAsync(ovensTable);
        await DeleteTableIfPresentAsync(eventsTable);
    }

    [AzuriteFact]
    public async Task ConditionalUpdateRejectsAStaleEtagWithoutLosingTheFirstWrite()
    {
        var original = await AddOvenAsync("oven-02", 450);
        var firstRead = await repository.GetByIdAsync("oven-02", CancellationToken.None);
        var secondRead = await repository.GetByIdAsync("oven-02", CancellationToken.None);

        Assert.NotNull(firstRead);
        Assert.NotNull(secondRead);
        Assert.Equal(original.Version, firstRead.Version);

        var committed = await repository.TrySetTemperatureAsync(
            "oven-02",
            320,
            "operator repair",
            DateTimeOffset.UtcNow,
            firstRead.Version,
            CancellationToken.None);
        var conflict = await repository.TrySetTemperatureAsync(
            "oven-02",
            330,
            "stale update",
            DateTimeOffset.UtcNow,
            secondRead.Version,
            CancellationToken.None);

        Assert.Equal(OvenUpdateStatus.Updated, committed.Status);
        Assert.Equal(OvenUpdateStatus.Conflict, conflict.Status);
        Assert.NotNull(committed.Oven);
        Assert.Equal(nameof(ConcurrencyToken), committed.Oven.Version.ToString());
        Assert.Equal(320, (await repository.GetByIdAsync("oven-02", CancellationToken.None))?.TemperatureC);
    }

    [AzuriteFact]
    public async Task LatestEventsArePartitionScopedNewestFirstAndCappedAtTwentyAcrossPages()
    {
        var otherPartitionOptions = new PizzaOpsTableOptions
        {
            Mode = "Azurite",
            AzuriteConnectionString = options.AzuriteConnectionString,
            OvensTableName = options.OvensTableName,
            EventsTableName = options.EventsTableName,
            PartitionKey = "other-kitchen"
        };
        var otherPartitionRepository = new TableKitchenRepository(
            tableServiceClient,
            Options.Create(otherPartitionOptions));
        var baseline = DateTimeOffset.UtcNow.AddMinutes(-1);

        for (var index = 0; index < 27; index++)
        {
            await repository.AppendAsync(
                CreateEvent($"main-{index:D2}", baseline.AddSeconds(index)),
                CancellationToken.None);
        }

        await otherPartitionRepository.AppendAsync(
            CreateEvent("other-01", baseline.AddMinutes(2)),
            CancellationToken.None);
        await otherPartitionRepository.AppendAsync(
            CreateEvent("other-02", baseline.AddMinutes(3)),
            CancellationToken.None);

        var latest = await repository.GetLatestAsync(100, CancellationToken.None);

        Assert.Equal(20, latest.Count);
        Assert.Equal("main-26", latest[0].EventId);
        Assert.Equal("main-07", latest[^1].EventId);
        Assert.All(latest, item => Assert.StartsWith("main-", item.EventId, StringComparison.Ordinal));
        Assert.Equal(
            latest.Select(item => item.TimestampUtc).OrderDescending(),
            latest.Select(item => item.TimestampUtc));
    }

    [AzuriteFact]
    public async Task MissingOvensMapToAnInvalidKitchenState()
    {
        var readService = CreateReadService();

        var result = await readService.GetStatusAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ResultCodes.KitchenStateInvalid, result.Code);
        Assert.Null(result.Data);
    }

    [AzuriteFact]
    public async Task CorruptOvenRowsMapToAnInvalidKitchenState()
    {
        await AddOvenAsync("oven-01", 320);
        await AddOvenAsync("oven-03", 300);
        await ovensTable.AddEntityAsync(new TableEntity(options.PartitionKey, "oven-02")
        {
            [nameof(Oven.DisplayName)] = "Oven 02",
            [nameof(Oven.TemperatureC)] = "not-an-integer",
            [nameof(Oven.UpdatedAtUtc)] = DateTimeOffset.UtcNow,
            [nameof(Oven.LastAction)] = "corrupt row"
        });

        var result = await CreateReadService().GetStatusAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ResultCodes.KitchenStateInvalid, result.Code);
        Assert.Null(result.Data);
    }

    [AzuriteFact]
    public async Task MissingTableMapsToStorageUnavailableAndDoesNotCreateItOnRead()
    {
        await ovensTable.DeleteAsync();

        var result = await CreateReadService().GetStatusAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ResultCodes.StorageUnavailable, result.Code);
        Assert.False(await TableExistsAsync(options.OvensTableName));
    }

    [AzuriteFact]
    public async Task MissingEventTableDegradesOnlyTheEventFeed()
    {
        await AddCanonicalOvensAsync();
        await eventsTable.DeleteAsync();

        var result = await CreateReadService().GetStatusAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(ResultCodes.Ok, result.Code);
        Assert.NotNull(result.Data);
        Assert.False(result.Data.EventFeedAvailable);
        Assert.Equal(3, result.Data.Ovens.Count);
        Assert.False(await TableExistsAsync(options.EventsTableName));
    }

    [AzuriteFact]
    public async Task ResetRepeatsRecoversAfterPartialFailureAndLeavesOtherPartitionUntouched()
    {
        var otherPartitionOptions = new PizzaOpsTableOptions
        {
            Mode = "Azurite",
            AzuriteConnectionString = options.AzuriteConnectionString,
            OvensTableName = options.OvensTableName,
            EventsTableName = options.EventsTableName,
            PartitionKey = "other-kitchen"
        };
        var otherRepository = new TableKitchenRepository(
            tableServiceClient,
            Options.Create(otherPartitionOptions));
        await AddCanonicalOvensAsync();
        await repository.AppendAsync(
            CreateEvent("old-target-event", DateTimeOffset.UtcNow),
            CancellationToken.None);
        await otherRepository.CreateOrReplaceOvenAsync(
            "oven-01",
            "Other oven",
            275,
            DateTimeOffset.UtcNow,
            "untouched",
            CancellationToken.None);
        await otherRepository.AppendAsync(
            CreateEvent("other-partition-event", DateTimeOffset.UtcNow.AddSeconds(1)),
            CancellationToken.None);

        var reset = new KitchenResetService(
            repository,
            repository,
            new FailOnceAfterDeleteMaintenance(maintenance),
            TimeProvider.System);

        var failure = await Assert.ThrowsAsync<KitchenResetException>(
            () => reset.ResetAsync(CancellationToken.None));

        Assert.Equal("partition cleanup", failure.Stage);
        Assert.Empty(await repository.GetAllAsync(CancellationToken.None));
        Assert.Equal(275, (await otherRepository.GetByIdAsync("oven-01", CancellationToken.None))?.TemperatureC);
        Assert.Equal(
            "other-partition-event",
            (await otherRepository.GetLatestAsync(20, CancellationToken.None)).Single().EventId);

        await reset.ResetAsync(CancellationToken.None);
        await AssertCanonicalResetAsync();
        await reset.ResetAsync(CancellationToken.None);
        await AssertCanonicalResetAsync();

        Assert.Equal(
            275,
            (await otherRepository.GetByIdAsync("oven-01", CancellationToken.None))?.TemperatureC);
        Assert.Equal(
            "other-partition-event",
            (await otherRepository.GetLatestAsync(20, CancellationToken.None)).Single().EventId);
    }

    private KitchenReadService CreateReadService() =>
        new(repository, repository, TimeProvider.System);

    private async Task<Oven> AddOvenAsync(string ovenId, int temperatureC) =>
        await maintenance.CreateOrReplaceOvenAsync(
            ovenId,
            $"Oven {ovenId[^2..]}",
            temperatureC,
            DateTimeOffset.UtcNow,
            null,
            CancellationToken.None);

    private async Task AddCanonicalOvensAsync()
    {
        await AddOvenAsync("oven-01", 320);
        await AddOvenAsync("oven-02", 450);
        await AddOvenAsync("oven-03", 300);
    }

    private async Task AssertCanonicalResetAsync()
    {
        var ovens = await repository.GetAllAsync(CancellationToken.None);
        Assert.Equal(3, ovens.Count);
        Assert.Equal(
            CanonicalTemperatures,
            ovens.OrderBy(oven => oven.OvenId, StringComparer.Ordinal).Select(oven => oven.TemperatureC));
        Assert.All(ovens, oven => Assert.Equal("RESET", oven.LastAction));

        var events = await repository.GetLatestAsync(20, CancellationToken.None);
        Assert.Single(events);
        Assert.Equal(KitchenEventType.Reset, events[0].EventType);
        Assert.Equal(KitchenEventOutcome.Success, events[0].Outcome);
    }

    private static KitchenEvent CreateEvent(string eventId, DateTimeOffset timestampUtc) =>
        new(
            eventId,
            timestampUtc,
            KitchenEventType.TestBake,
            "oven-02",
            $"Test event {eventId}",
            KitchenEventOutcome.Success,
            "integration-test");

    private static async Task DeleteTableIfPresentAsync(TableClient table)
    {
        try
        {
            await table.DeleteAsync();
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
        }
    }

    private async Task<bool> TableExistsAsync(string tableName)
    {
        await foreach (var item in tableServiceClient.QueryAsync(
            table => table.Name == tableName))
        {
            return true;
        }

        return false;
    }

    private sealed class FailOnceAfterDeleteMaintenance(IKitchenTableMaintenance inner)
        : IKitchenTableMaintenance
    {
        private bool hasFailed;

        public Task EnsureTablesAsync(CancellationToken cancellationToken) =>
            inner.EnsureTablesAsync(cancellationToken);

        public async Task DeleteKitchenDataAsync(CancellationToken cancellationToken)
        {
            await inner.DeleteKitchenDataAsync(cancellationToken);
            if (!hasFailed)
            {
                hasFailed = true;
                throw new KitchenStorageException(
                    KitchenStorageFailureKind.Unavailable,
                    "Injected failure after partition cleanup.");
            }
        }

        public Task<Oven> CreateOrReplaceOvenAsync(
            string ovenId,
            string displayName,
            int temperatureC,
            DateTimeOffset updatedAtUtc,
            string? lastAction,
            CancellationToken cancellationToken) =>
            inner.CreateOrReplaceOvenAsync(
                ovenId,
                displayName,
                temperatureC,
                updatedAtUtc,
                lastAction,
                cancellationToken);
    }
}
