using PizzaOps.Storage;

namespace PizzaOps.DemoAdmin;

/// <summary>Validated, non-secret storage scope shown before an operator reset.</summary>
public sealed record AdminTarget(
    string EnvironmentName,
    string Mode,
    string DisplayEndpoint,
    string OvensTableName,
    string EventsTableName,
    string PartitionKey,
    string? DashboardUrl)
{
    /// <summary>Creates a confirmation phrase that identifies the full configured scope.</summary>
    public string ConfirmationText =>
        $"RESET {EnvironmentName} {DisplayEndpoint} {OvensTableName} {EventsTableName} {PartitionKey}";

    /// <summary>Builds a display target without exposing the Azurite connection string.</summary>
    public static AdminTarget Create(
        string environmentName,
        PizzaOpsTableOptions tableOptions,
        string? dashboardUrl)
    {
        ArgumentNullException.ThrowIfNull(tableOptions);

        var displayEndpoint = string.Equals(
            tableOptions.Mode,
            "Azurite",
            StringComparison.OrdinalIgnoreCase)
            ? "Azurite@127.0.0.1:10002"
            : new Uri(tableOptions.TableEndpoint!, UriKind.Absolute).GetLeftPart(UriPartial.Authority);

        return new AdminTarget(
            environmentName,
            tableOptions.Mode,
            displayEndpoint,
            tableOptions.OvensTableName,
            tableOptions.EventsTableName,
            tableOptions.PartitionKey,
            dashboardUrl);
    }
}
