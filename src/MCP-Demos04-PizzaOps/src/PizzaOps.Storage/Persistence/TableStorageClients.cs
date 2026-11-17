using Azure.Data.Tables;

namespace PizzaOps.Storage.Persistence;

/// <summary>Separate bounded-read and no-retry-write Table clients.</summary>
public sealed record TableStorageClients(
    TableServiceClient ReadClient,
    TableServiceClient WriteClient);
