using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using PizzaOps.Core.Application;
using PizzaOps.Core.Domain;

namespace PizzaOps.Storage.Persistence;

/// <summary>Purpose-specific Table Storage adapter for PizzaOps ovens and kitchen events.</summary>
public sealed class TableKitchenRepository :
    IOvenRepository,
    IKitchenEventRepository,
    IKitchenTableMaintenance
{
    private const int ExpectedOvenCount = 3;
    private const int MaximumRecentEvents = 20;

    private readonly TableClient ovensReadTable;
    private readonly TableClient ovensWriteTable;
    private readonly TableClient eventsReadTable;
    private readonly TableClient eventsWriteTable;
    private readonly string partitionKey;

    /// <summary>Creates clients for the configured tables without creating them.</summary>
    public TableKitchenRepository(
        TableServiceClient tableServiceClient,
        IOptions<PizzaOpsTableOptions> options)
        : this(tableServiceClient, tableServiceClient, options)
    {
    }

    /// <summary>Creates clients with distinct read and write retry policies.</summary>
    public TableKitchenRepository(
        TableStorageClients tableStorageClients,
        IOptions<PizzaOpsTableOptions> options)
        : this(tableStorageClients.ReadClient, tableStorageClients.WriteClient, options)
    {
    }

    private TableKitchenRepository(
        TableServiceClient readClient,
        TableServiceClient writeClient,
        IOptions<PizzaOpsTableOptions> options)
    {
        ArgumentNullException.ThrowIfNull(readClient);
        ArgumentNullException.ThrowIfNull(writeClient);
        ArgumentNullException.ThrowIfNull(options);

        var value = options.Value;
        partitionKey = value.PartitionKey;
        ovensReadTable = readClient.GetTableClient(value.OvensTableName);
        ovensWriteTable = writeClient.GetTableClient(value.OvensTableName);
        eventsReadTable = readClient.GetTableClient(value.EventsTableName);
        eventsWriteTable = writeClient.GetTableClient(value.EventsTableName);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Oven>> GetAllAsync(CancellationToken cancellationToken)
    {
        var ovens = new List<Oven>(ExpectedOvenCount);

        try
        {
            await foreach (var entity in ovensReadTable.QueryAsync<TableEntity>(
                item => item.PartitionKey == partitionKey,
                maxPerPage: ExpectedOvenCount + 1,
                cancellationToken: cancellationToken))
            {
                if (ovens.Count == ExpectedOvenCount)
                {
                    throw InvalidData("The kitchen partition contains more than three ovens.");
                }

                ovens.Add(MapOven(entity));
            }
        }
        catch (RequestFailedException exception)
        {
            throw Translate(exception, isWrite: false);
        }
        catch (AggregateException exception) when (IsReadRetryFailure(exception))
        {
            throw TranslateReadRetryFailure(exception);
        }

        return ovens;
    }

    /// <inheritdoc />
    public async Task<Oven?> GetByIdAsync(string normalizedOvenId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedOvenId);

        try
        {
            var response = await ovensReadTable.GetEntityAsync<TableEntity>(
                partitionKey,
                normalizedOvenId,
                cancellationToken: cancellationToken);
            return MapOven(response.Value);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
        catch (RequestFailedException exception)
        {
            throw Translate(exception, isWrite: false);
        }
        catch (AggregateException exception) when (IsReadRetryFailure(exception))
        {
            throw TranslateReadRetryFailure(exception);
        }
    }

    /// <inheritdoc />
    public async Task<OvenUpdateResult> TrySetTemperatureAsync(
        string normalizedOvenId,
        int targetTemperatureC,
        string? lastAction,
        DateTimeOffset updatedAtUtc,
        ConcurrencyToken expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedOvenId);
        ArgumentNullException.ThrowIfNull(expectedVersion);

        if (updatedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The update timestamp must be UTC.", nameof(updatedAtUtc));
        }

        TableEntity existing;

        try
        {
            var response = await ovensReadTable.GetEntityAsync<TableEntity>(
                partitionKey,
                normalizedOvenId,
                cancellationToken: cancellationToken);
            existing = response.Value;
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return new OvenUpdateResult(OvenUpdateStatus.Conflict, null);
        }
        catch (RequestFailedException exception)
        {
            throw Translate(exception, isWrite: false);
        }

        var current = MapOven(existing);
        var updatedEntity = new TableEntity(partitionKey, normalizedOvenId)
        {
            [nameof(Oven.DisplayName)] = current.DisplayName,
            [nameof(Oven.TemperatureC)] = targetTemperatureC,
            [nameof(Oven.UpdatedAtUtc)] = updatedAtUtc,
            [nameof(Oven.LastAction)] = lastAction
        };

        try
        {
            var response = await ovensWriteTable.UpdateEntityAsync(
                updatedEntity,
                new ETag(expectedVersion.Value),
                TableUpdateMode.Replace,
                cancellationToken);
            var updatedOven = new Oven(
                current.OvenId,
                current.DisplayName,
                targetTemperatureC,
                updatedAtUtc,
                lastAction,
                new ConcurrencyToken(GetResponseEtag(response, "The updated oven ETag was not returned.")));
            return new OvenUpdateResult(OvenUpdateStatus.Updated, updatedOven);
        }
        catch (RequestFailedException exception) when (exception.Status is 404 or 412)
        {
            return new OvenUpdateResult(OvenUpdateStatus.Conflict, null);
        }
        catch (RequestFailedException exception)
        {
            throw Translate(exception, isWrite: true);
        }
    }

    /// <inheritdoc />
    public async Task AppendAsync(KitchenEvent kitchenEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(kitchenEvent);

        var entity = new TableEntity(partitionKey, CreateEventRowKey(kitchenEvent.TimestampUtc))
        {
            [nameof(KitchenEvent.EventId)] = kitchenEvent.EventId,
            [nameof(KitchenEvent.TimestampUtc)] = kitchenEvent.TimestampUtc,
            [nameof(KitchenEvent.EventType)] = kitchenEvent.EventType.ToString(),
            [nameof(KitchenEvent.OvenId)] = kitchenEvent.OvenId,
            [nameof(KitchenEvent.Summary)] = kitchenEvent.Summary,
            [nameof(KitchenEvent.Outcome)] = kitchenEvent.Outcome.ToString(),
            [nameof(KitchenEvent.CorrelationId)] = kitchenEvent.CorrelationId
        };

        try
        {
            await eventsWriteTable.AddEntityAsync(entity, cancellationToken);
        }
        catch (RequestFailedException exception)
        {
            throw Translate(exception, isWrite: true);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KitchenEvent>> GetLatestAsync(
        int count,
        CancellationToken cancellationToken)
    {
        if (count <= 0)
        {
            return [];
        }

        var requestedCount = Math.Min(count, MaximumRecentEvents);
        var events = new List<KitchenEvent>(requestedCount);

        try
        {
            await foreach (var entity in eventsReadTable.QueryAsync<TableEntity>(
                item => item.PartitionKey == partitionKey,
                maxPerPage: requestedCount,
                cancellationToken: cancellationToken))
            {
                events.Add(MapEvent(entity));

                if (events.Count == requestedCount)
                {
                    break;
                }
            }
        }
        catch (RequestFailedException exception)
        {
            throw Translate(exception, isWrite: false);
        }
        catch (AggregateException exception) when (IsReadRetryFailure(exception))
        {
            throw TranslateReadRetryFailure(exception);
        }

        return events;
    }

    /// <inheritdoc />
    public async Task EnsureTablesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ovensWriteTable.CreateIfNotExistsAsync(cancellationToken);
            await eventsWriteTable.CreateIfNotExistsAsync(cancellationToken);
        }
        catch (RequestFailedException exception)
        {
            throw Translate(exception, isWrite: true);
        }
    }

    /// <inheritdoc />
    public async Task DeleteKitchenDataAsync(CancellationToken cancellationToken)
    {
        await DeletePartitionRowsAsync(ovensReadTable, ovensWriteTable, cancellationToken);
        await DeletePartitionRowsAsync(eventsReadTable, eventsWriteTable, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Oven> CreateOrReplaceOvenAsync(
        string ovenId,
        string displayName,
        int temperatureC,
        DateTimeOffset updatedAtUtc,
        string? lastAction,
        CancellationToken cancellationToken)
    {
        if (!IdentifierNormalization.TryNormalize(ovenId, out var normalizedOvenId) ||
            !string.Equals(ovenId, normalizedOvenId, StringComparison.Ordinal))
        {
            throw new ArgumentException("An oven identifier must be canonical.", nameof(ovenId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (updatedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The update timestamp must be UTC.", nameof(updatedAtUtc));
        }

        var entity = new TableEntity(partitionKey, normalizedOvenId)
        {
            [nameof(Oven.DisplayName)] = displayName,
            [nameof(Oven.TemperatureC)] = temperatureC,
            [nameof(Oven.UpdatedAtUtc)] = updatedAtUtc,
            [nameof(Oven.LastAction)] = lastAction
        };

        try
        {
            var response = await ovensWriteTable.UpsertEntityAsync(
                entity,
                TableUpdateMode.Replace,
                cancellationToken);
            return new Oven(
                normalizedOvenId,
                displayName,
                temperatureC,
                updatedAtUtc,
                lastAction,
                new ConcurrencyToken(GetResponseEtag(response, "The reset oven ETag was not returned.")));
        }
        catch (RequestFailedException exception)
        {
            throw Translate(exception, isWrite: true);
        }
    }

    private async Task DeletePartitionRowsAsync(
        TableClient readTable,
        TableClient writeTable,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var entity in readTable.QueryAsync<TableEntity>(
                item => item.PartitionKey == partitionKey,
                cancellationToken: cancellationToken))
            {
                try
                {
                    await writeTable.DeleteEntityAsync(
                        entity.PartitionKey,
                        entity.RowKey,
                        ETag.All,
                        cancellationToken);
                }
                catch (RequestFailedException exception)
                {
                    throw Translate(exception, isWrite: true);
                }
            }
        }
        catch (RequestFailedException exception)
        {
            throw Translate(exception, isWrite: false);
        }
    }

    private static Oven MapOven(TableEntity entity)
    {
        var displayName = RequiredValue<string>(entity, nameof(Oven.DisplayName));
        var temperatureC = RequiredValue<int>(entity, nameof(Oven.TemperatureC));
        var updatedAtUtc = RequiredValue<DateTimeOffset>(entity, nameof(Oven.UpdatedAtUtc));
        var etag = GetEntityEtag(entity);
        var lastAction = OptionalString(entity, nameof(Oven.LastAction));

        if (!IdentifierNormalization.TryNormalize(entity.RowKey, out var ovenId) ||
            !string.Equals(ovenId, entity.RowKey, StringComparison.Ordinal) ||
            updatedAtUtc.Offset != TimeSpan.Zero)
        {
            throw InvalidData("An oven row contains an invalid key or timestamp.");
        }

        try
        {
            return new Oven(ovenId, displayName, temperatureC, updatedAtUtc, lastAction, new ConcurrencyToken(etag));
        }
        catch (ArgumentException exception)
        {
            throw InvalidData("An oven row contains invalid required data.", exception);
        }
    }

    private static KitchenEvent MapEvent(TableEntity entity)
    {
        var eventId = RequiredValue<string>(entity, nameof(KitchenEvent.EventId));
        var timestampUtc = RequiredValue<DateTimeOffset>(entity, nameof(KitchenEvent.TimestampUtc));
        var eventTypeValue = RequiredValue<string>(entity, nameof(KitchenEvent.EventType));
        var summary = RequiredValue<string>(entity, nameof(KitchenEvent.Summary));
        var outcomeValue = RequiredValue<string>(entity, nameof(KitchenEvent.Outcome));
        var ovenId = OptionalString(entity, nameof(KitchenEvent.OvenId));
        var correlationId = OptionalString(entity, nameof(KitchenEvent.CorrelationId));

        if (timestampUtc.Offset != TimeSpan.Zero ||
            !Enum.TryParse(eventTypeValue, ignoreCase: false, out KitchenEventType eventType) ||
            !Enum.IsDefined(eventType) ||
            !Enum.TryParse(outcomeValue, ignoreCase: false, out KitchenEventOutcome outcome) ||
            !Enum.IsDefined(outcome))
        {
            throw InvalidData("An event row contains an invalid timestamp or category.");
        }

        try
        {
            return new KitchenEvent(
                eventId,
                timestampUtc,
                eventType,
                ovenId,
                summary,
                outcome,
                correlationId);
        }
        catch (ArgumentException exception)
        {
            throw InvalidData("An event row contains invalid required data.", exception);
        }
    }

    private static T RequiredValue<T>(TableEntity entity, string propertyName)
    {
        if (!entity.TryGetValue(propertyName, out var value) || value is not T typedValue)
        {
            throw InvalidData($"A table row is missing or has an invalid {propertyName} value.");
        }

        return typedValue;
    }

    private static string? OptionalString(TableEntity entity, string propertyName)
    {
        if (!entity.TryGetValue(propertyName, out var value) || value is null)
        {
            return null;
        }

        if (value is not string stringValue)
        {
            throw InvalidData($"A table row has an invalid {propertyName} value.");
        }

        return stringValue;
    }

    private static string GetEntityEtag(TableEntity entity)
    {
        var value = entity.ETag.ToString();

        return string.IsNullOrWhiteSpace(value)
            ? throw InvalidData("A table row did not include its ETag.")
            : value;
    }

    private static string GetResponseEtag(Response response, string message)
    {
        var value = response.Headers.ETag.ToString();

        return string.IsNullOrWhiteSpace(value)
            ? throw new KitchenStorageException(KitchenStorageFailureKind.OutcomeUnknown, message)
            : value;
    }

    private static string CreateEventRowKey(DateTimeOffset timestampUtc)
    {
        var reverseTicks = DateTime.MaxValue.Ticks - timestampUtc.UtcDateTime.Ticks;
        return $"{reverseTicks:D19}-{Guid.NewGuid():N}";
    }

    private static KitchenStorageException Translate(RequestFailedException exception, bool isWrite)
    {
        var failureKind = isWrite && exception.Status is 0 or 408 or >= 500
            ? KitchenStorageFailureKind.OutcomeUnknown
            : KitchenStorageFailureKind.Unavailable;
        var message = failureKind == KitchenStorageFailureKind.OutcomeUnknown
            ? "The storage write may have completed, but its outcome could not be confirmed."
            : "The kitchen storage operation failed.";

        return new KitchenStorageException(failureKind, message, exception);
    }

    private static bool IsReadRetryFailure(AggregateException exception) =>
        exception.InnerExceptions.Count > 0 &&
        exception.InnerExceptions.All(inner => inner is RequestFailedException);

    private static KitchenStorageException TranslateReadRetryFailure(AggregateException exception) =>
        new(KitchenStorageFailureKind.Unavailable, "The kitchen storage operation failed.", exception);

    private static KitchenStorageException InvalidData(string message, Exception? innerException = null) =>
        new(KitchenStorageFailureKind.InvalidData, message, innerException);
}
