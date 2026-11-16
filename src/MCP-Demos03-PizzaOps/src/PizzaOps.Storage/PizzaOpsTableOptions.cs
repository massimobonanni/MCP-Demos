namespace PizzaOps.Storage;

/// <summary>Validated connection and table settings for the PizzaOps Table Storage adapter.</summary>
public sealed class PizzaOpsTableOptions
{
    /// <summary>Configuration section bound by <see cref="DependencyInjection.StorageServiceCollectionExtensions"/>.</summary>
    public const string SectionName = "PizzaOps:TableStorage";

    /// <summary>Authentication mode: Azure managed identity or the explicit local Azurite profile.</summary>
    public string Mode { get; set; } = string.Empty;

    /// <summary>Azure Table endpoint. Required in Azure and forbidden for the Azurite profile.</summary>
    public string? TableEndpoint { get; set; }

    /// <summary>
    /// Local emulator connection string. Only the explicit Azurite development connection
    /// string is accepted, and only in the Development environment.
    /// </summary>
    public string? AzuriteConnectionString { get; set; }

    /// <summary>Optional client ID for a user-assigned managed identity.</summary>
    public string? ManagedIdentityClientId { get; set; }

    /// <summary>Ovens table name.</summary>
    public string OvensTableName { get; set; } = "PizzaOpsOvens";

    /// <summary>Events table name.</summary>
    public string EventsTableName { get; set; } = "PizzaOpsEvents";

    /// <summary>Partition containing this deployment's kitchen.</summary>
    public string PartitionKey { get; set; } = "main-kitchen";
}
