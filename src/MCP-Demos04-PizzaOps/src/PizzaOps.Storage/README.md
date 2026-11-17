# PizzaOps Table Storage

The adapter uses the `PizzaOps:TableStorage` options section. It never creates a
table as part of dependency injection, reads, or writes. Only the explicit
`IKitchenTableMaintenance.EnsureTablesAsync` operator operation creates tables.

## Local Azurite

Start the Azurite Table service in a separate terminal:

```powershell
azurite --inMemoryPersistence --tableHost 127.0.0.1 --tablePort 10002
```

In the local app configuration, explicitly set:

```json
{
  "PizzaOps": {
    "TableStorage": {
      "Mode": "Azurite",
      "AzuriteConnectionString": "UseDevelopmentStorage=true",
      "OvensTableName": "PizzaOpsOvens",
      "EventsTableName": "PizzaOpsEvents",
      "PartitionKey": "main-kitchen"
    }
  }
}
```

Azurite mode is accepted only in the `Development` environment and only with
the explicit development connection string above. This profile must not be
copied into a deployed environment.

The real-emulator integration tests are opt-in. With Azurite running, set the
following process environment variables and run
`dotnet test .\tests\PizzaOps.IntegrationTests\PizzaOps.IntegrationTests.csproj`:

```powershell
$env:PIZZAOPS_AZURITE_TESTS = "true"
$env:PIZZAOPS_AZURITE_CONNECTION_STRING = "UseDevelopmentStorage=true"
```

Each test creates uniquely named tables and deletes them after execution.

## Azure managed identity

In a non-Development environment, configure `Mode` as `Azure` and provide an
absolute HTTPS `TableEndpoint`, such as
`https://<storage-account>.table.core.windows.net`. An optional
`ManagedIdentityClientId` selects a user-assigned managed identity; when
omitted, the system-assigned managed identity is used. Connection strings and
account keys are rejected in Azure mode. Grant the hosting identity the
required Table data-plane role outside the application.

## Storage shape

The adapter uses the configured kitchen partition in both tables. Oven keys
are canonical oven IDs. Event keys begin with fixed-width reverse UTC ticks,
so ascending Table ordering returns the newest events first; the repository
reads no more than 20. The Core read projection requires exactly the three
known oven IDs, derives health in Core, and reports event-feed failures
separately from oven-state failures.
