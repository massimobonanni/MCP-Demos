# PizzaOps

PizzaOps is a scoped .NET 10 solution for a simulated pizzeria demonstration.
It contains the Core domain and contracts, Azure Table Storage adapter, shared
read projection, and a separately authorized operator reset/status utility.
MCP tools and dashboard endpoints/components are implemented as separate
projects.

## Projects

| Project | Responsibility |
| --- | --- |
| `PizzaOps.Core` | Domain and application contracts; no infrastructure references |
| `PizzaOps.Storage` | Storage adapter boundary |
| `PizzaOps.Functions` | Azure Functions isolated-worker MCP host |
| `PizzaOps.Dashboard` | Blazor Web App |
| `PizzaOps.DemoAdmin` | Azure-authorized/local-emulator operator command-line app |
| `PizzaOps.UnitTests` | Core unit-test project |
| `PizzaOps.IntegrationTests` | Storage and Functions integration-test project |

`Core` has no project or package dependencies. `Storage` depends on `Core` and
uses Azure Table Storage with managed identity in Azure or explicitly selected
Azurite in Development.
Functions, Dashboard, and DemoAdmin may depend on Core and Storage; Dashboard
and DemoAdmin do not reference Functions.

## Local MCP tools

The Functions app exposes exactly `get_ovens_status`, `get_pizza_recipe`,
`set_oven_temperature`, and `bake_test_pizza` using the official MCP extension.
There are no MCP resources, prompts, or admin tools. The presenter compatibility gate remains open; see
[`docs\compatibility.md`](docs/compatibility.md) before relying on this slice.
The deployed webhook is configured with System authorization. Core Tools'
local behavior is not a test of the Azure system key.

Start Azurite as described below, then use the explicit operator reset to seed
the kitchen. From this solution directory, start the host with its local
Development launch profile:

```powershell
dotnet run --project .\src\PizzaOps.Functions
```

The profile configures the isolated worker and Azurite for local development;
it contains no Azure credentials and is not used for deployment. To supply
different settings, use `dotnet run --no-launch-profile` and configure the
environment explicitly. Alternatively, in a separate PowerShell session,
configure and start Core Tools directly:

```powershell
cd .\src\PizzaOps.Functions
$env:AZURE_FUNCTIONS_ENVIRONMENT = "Development"
$env:DOTNET_ENVIRONMENT = "Development"
$env:FUNCTIONS_WORKER_RUNTIME = "dotnet-isolated"
$env:AzureWebJobsStorage = "UseDevelopmentStorage=true"
$env:PizzaOps__TableStorage__Mode = "Azurite"
$env:PizzaOps__TableStorage__AzuriteConnectionString = "UseDevelopmentStorage=true"
func start
```

The Streamable HTTP endpoint is
`http://127.0.0.1:7071/runtime/webhooks/mcp`. With the host running and the
canonical kitchen freshly reset, run the opt-in C# wire-contract tests from the
solution directory:

```powershell
$env:PIZZAOPS_MCP_TESTS = "true"
dotnet test .\tests\PizzaOps.IntegrationTests\PizzaOps.IntegrationTests.csproj --filter "FullyQualifiedName~Mcp"
```

These tests target that exact local endpoint. The temperature/bake tests reset
the local Azurite kitchen before and after their mutations. The bake test also
temporarily deletes the local events table to verify event-write failure, then
restores it through reset. Run them only against a disposable local emulator
with no other callers. MCP tests are skipped unless explicitly enabled.

`bake_test_pizza` requires string `oven_id` and `pizza_type` arguments and rejects
extra fields. It reads the current persisted oven once and uses the immutable
recipe bounds, without delays, oven writes, or ETag changes. The response includes
the observed `oven_temperature_c`, recipe target/tolerance, inclusive minimum
and maximum passing temperatures, and `result`. Both a burnt pizza and an
undercooked pizza are completed tests: `success: true`, `code: TEST_COMPLETED`,
with `result: BURNT` or `UNDERCOOKED`; only temperatures inside the inclusive
bounds yield `PASS`. Invalid/missing inputs or unreadable ovens do not fabricate
a bake result.

Each completed test attempts one `TEST_BAKE` event containing the observed
temperature, recipe target/bounds, and result. Repeated bakes can record separate
events but never alter the oven. Event append failure preserves `TEST_COMPLETED`
and its observation, with `event_recorded: false` and an `EVENT_WRITE_FAILED`
warning (or `EVENT_WRITE_OUTCOME_UNKNOWN` when persistence is uncertain).

## Dashboard read API

Run the dashboard in Development with the same Azurite settings as the Functions
host, after the operator has reset the kitchen:

```powershell
$env:PizzaOps__TableStorage__Mode = "Azurite"
$env:PizzaOps__TableStorage__AzuriteConnectionString = "UseDevelopmentStorage=true"
dotnet run --project .\src\PizzaOps.Dashboard
```

`GET /api/kitchen/state` returns the shared oven projection and recent events.
It reports an unavailable event feed explicitly; missing or invalid ovens and
storage failures return HTTP 503. `GET /health/live` checks the process, while
`GET /health/ready` requires both a complete kitchen and a readable event feed.
Storage-backed requests time out after five seconds. All three responses use
`Cache-Control: no-store`. The dashboard never initializes tables or connects
to MCP; in Azure its managed identity needs only Table data read access.

## Prerequisites

- .NET 10 SDK. The scoped `global.json` selects SDK `10.0.404` and permits a
  later .NET 10 feature band where that exact SDK is unavailable.
- Azure Functions Core Tools 4.x (4.0.7030 or later) only to run the Functions
  host locally. Building the solution does not require Core Tools.
- Azure credentials or an Azure subscription are not required for the local
  build and unit tests. Real storage integration tests require Azurite; see
  [`src\PizzaOps.Storage\README.md`](src/PizzaOps.Storage/README.md).

The Azurite endpoint, configuration, and emulator test instructions are in
[`src\PizzaOps.Storage\README.md`](src/PizzaOps.Storage/README.md).

## Operator reset and status

`PizzaOps.DemoAdmin` is an explicit operator console only. It targets Azure
through the signed-in operator identity or local Azurite through an explicit
development profile. Reset is not exposed through MCP, HTTP, or the dashboard,
and is never run during application startup or deployment.

The required `--environment` selects exactly one `appsettings.<environment>.json`
configuration file and one environment-variable prefix. No implicit default
environment or shared appsettings file is loaded. Supply the `PizzaOps` options
for that environment:

```json
{
  "PizzaOps": {
    "TableStorage": {
      "Mode": "Azurite",
      "AzuriteConnectionString": "UseDevelopmentStorage=true",
      "OvensTableName": "PizzaOpsOvens",
      "EventsTableName": "PizzaOpsEvents",
      "PartitionKey": "main-kitchen"
    },
    "Admin": {
      "DashboardUrl": "http://localhost:5000"
    }
  }
}
```

Save that local-only example as `appsettings.Development.json`; Azurite is
accepted only for the explicit `Development` environment. For Azure, use a
separate file such as `appsettings.demo.json` with `Mode` set to `Azure`, the
HTTPS `TableEndpoint`, table names, partition, and optional dashboard URL.
Azure mode uses the signed-in Azure CLI operator identity (`az login`) and
requires Table Storage data-plane authorization. `ManagedIdentityClientId`
and connection strings are not accepted as Azure operator credentials. Instead
of a JSON file, settings can be supplied with the environment-specific prefix,
for example:

```powershell
$env:PIZZAOPS_DEVELOPMENT_PizzaOps__TableStorage__Mode = "Azurite"
$env:PIZZAOPS_DEVELOPMENT_PizzaOps__TableStorage__AzuriteConnectionString = "UseDevelopmentStorage=true"
$env:PIZZAOPS_DEMO_PizzaOps__TableStorage__Mode = "Azure"
$env:PIZZAOPS_DEMO_PizzaOps__TableStorage__TableEndpoint = "https://<storage-account>.table.core.windows.net"
```

Run commands from this directory. Reset requires both the explicit quiescence
acknowledgement and typing the complete non-secret target scope printed by the
utility. Pause tool activity and drain in-flight calls before proceeding:

```powershell
dotnet run --project .\src\PizzaOps.DemoAdmin -- reset --environment Development --quiesced
dotnet run --project .\src\PizzaOps.DemoAdmin -- status --environment Development
dotnet run --project .\src\PizzaOps.DemoAdmin -- reset --help
```

The CLI uses `System.CommandLine`; use `--help` to display the available
commands and options. `-e` is an alias for `--environment` and `-q` is an
alias for `--quiesced`.

Reset removes rows only in the configured kitchen partition, restores ovens at
320/450/300 C, appends a single `RESET` event, then reads both tables back
before reporting `READY`. A partial failure reports the failing stage and
`NOT READY`; rerun reset after resolving the cause to converge. Status is
read-only and reports readiness, temperatures, event availability, and the
configured dashboard URL without displaying credentials.

## Build and validate

Run commands from this directory so `global.json` selects the scoped .NET SDK:

```powershell
dotnet --version
dotnet restore .\PizzaOps.sln
dotnet build .\PizzaOps.sln --no-restore
dotnet test .\PizzaOps.sln --no-build
dotnet format .\PizzaOps.sln --verify-no-changes --no-restore
```

The SDK pin is scoped to this solution; existing repository demos continue to
use the repository-level SDK. Future AZD service entries should point to the
PizzaOps project directories under this folder so their build processes find
this `global.json` by walking up from the service directory. The repository's
current root `azure.yaml` has no PizzaOps services, so AZD packaging is not part
of this scaffold task.

The integration tests are opt-in and use a running Azurite Table service when
explicitly enabled. The reset integration test covers repeatability, recovery
after a failure immediately following partition cleanup, and isolation from
another kitchen partition. The CI workflow restores, builds, and checks
formatting without Azure login, provisioning, or deployment.

The test projects use xUnit 2 with the VSTest runner (`Microsoft.NET.Test.Sdk`
and `xunit.runner.visualstudio`); the command above is the solution-level test
invocation.
