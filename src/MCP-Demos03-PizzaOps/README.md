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

## Live audience dashboard

The root page is a single-screen Interactive Server Blazor dashboard. It reads
`IKitchenReadService` directly, just like the API, rather than calling loopback
HTTP or MCP. Health labels and safe event summaries come from Core; the page
does not implement temperature or bake policy. Template counter/weather pages
and navigation are removed. All scripts, styles and fonts are local/system
assets, with light/dark colors and reduced-motion support.

One serialized worker consumes coalesced one-second refresh requests. A read
has a five-second cancellation deadline; no second read starts before the first
actually completes. Hidden tabs and disconnected circuits cancel the current
read and pause further storage reads. Visibility and circuit recovery request
an immediate fresh read, including visibility resynchronization after reconnect.
Disposal cancels and drains the worker and timer and removes browser listeners.

The display distinguishes:

- **Loading / unavailable:** no fabricated oven cards or temperatures.
- **Live / connected:** a successful snapshot no more than three seconds old.
- **Stale / not live:** the last successful snapshot remains visible, labeled
  stale, until another successful read. Oven health is not connection health.
- **Connected / degraded:** authoritative ovens were read, but recent events
  could not be read; no empty-success feed or inferred bake result is shown.
- **Paused / connection lost:** hidden-tab messaging or the native Blazor
  reconnect dialog prevents a disconnected screen from claiming to be live.

Recent events are newest first, bounded to 20 by the existing read service,
rendered as encoded text, and scrollable by keyboard within the event panel.
Times are explicitly UTC. There are no kitchen-mutating controls; reconnect
buttons only restore the browser connection.

Run the focused xUnit/VSTest lifecycle and HTTP regression checks:

```powershell
dotnet test .\tests\PizzaOps.IntegrationTests\PizzaOps.IntegrationTests.csproj --filter "FullyQualifiedName~Dashboard"
```

Local Task 09 verification (2026-11-17): 24 tests passed, including virtual-time
coverage of one-second cadence, slow reads/no overlap, timeouts, failed first
load, retained stale data, event degradation, hidden-tab/circuit recovery,
late canceled results and disposal. Browser inspection at 1920x1080 used real,
isolated Azurite tables: reset 320/450/300, repair to 320, PASS, and rejection
were displayed. The three story events took 953-979 ms from their persisted
event timestamps to DOM observation in one local run (not an Azure/stage
latency guarantee). Missing tables demonstrated unavailable, stale and
event-only degraded states; stopping the host showed the native connection-loss
dialog. Page Visibility/reconnect-event emulation verified paused reads and an
immediate fresh recovery. Light/dark screenshots, a 20-row feed, literal markup
as text, zero external asset requests, zero editable controls, and zero active
animations under reduced motion were checked with no document overflow.
Live Azure/MCP end-to-end and stage-network gates remain later plan tasks.

## Telemetry and safe failures

The executable roots register **one** Application Insights SDK pipeline only when
`APPLICATIONINSIGHTS_CONNECTION_STRING` is configured. Local verification leaves
it unset and requires no Azure credentials or telemetry endpoint:

- Functions uses `Microsoft.ApplicationInsights.WorkerService` 2.23.0 with
  `Microsoft.Azure.Functions.Worker.ApplicationInsights` 2.50.0 and
  `AddApplicationInsightsTelemetryWorkerService` /
  `ConfigureFunctionsApplicationInsights`. The official worker integration
  preserves host/worker Activity correlation and disables relaying worker logs
  through the host. There is no additional OpenTelemetry/Azure Monitor exporter.
- Dashboard uses `Microsoft.ApplicationInsights.AspNetCore` 2.23.0 and
  `AddApplicationInsightsTelemetry`. Do not enable App Service codeless
  instrumentation alongside this SDK or add an additional exporter.
- Cloud roles are `PizzaOps.Functions` and `PizzaOps.Dashboard`. The initializer
  covers worker-originated telemetry; configure `WEBSITE_CLOUD_ROLENAME` as
  `PizzaOps.Functions` for the separate Functions **host** pipeline when deploying.
  Host dependency collection is disabled; worker SDK dependencies remain enabled.
  Host/worker sampling is disabled so rejected/uncertain operations are retained.

These are the compatible classic SDK integrations documented by the
[Functions worker package](https://www.nuget.org/packages/Microsoft.Azure.Functions.Worker.ApplicationInsights/2.50.0)
and [ASP.NET Core SDK](https://learn.microsoft.com/en-us/azure/azure-monitor/app/asp-net-core).
A future migration to OpenTelemetry must replace, not supplement, this pipeline.

Every tool creates a child Activity without replacing the worker's parent.
When export is configured, `TelemetryClient.StartOperation<DependencyTelemetry>`
exports that application span through the same SDK pipeline; it is not a second
exporter. Local-only runs use Activity correlation without an exporter.
Its trace ID is returned as `correlation_id`, passed through application services
into persisted event rows, and included in structured `ILogger` scopes/outcomes.
Dashboard API snapshots have their own trace ID; persisted event
`correlation_id` values identify the originating tool, not the later poll.
Event correlation IDs stay in storage/telemetry, not the public event projection.
Interactive refreshes also create independent correlated read scopes.

JSON console logs include tool name, outcome code, elapsed milliseconds, success,
`event_recorded`, and allowlisted warning codes. They distinguish policy
rejection, ETag conflict, unavailable storage, a successful command with a failed
event append, and an uncertain write. Successful reads are Debug-level and only
successful polling request telemetry is filtered; failure/warning telemetry is
not sampled away. Successful dashboard refresh spans are also filtered.
Request/dependency URL query strings are removed before export.
No application logger records arguments, reasons, prompts, keys, authorization
headers, or raw exception messages. Do not enable HTTP body/header diagnostic
capture when investigating the demo.

The MCP boundary contains unexpected exceptions using safe messages. Unexpected
command failures after entry conservatively return `STORAGE_OUTCOME_UNKNOWN`
with no invented unchanged-state data; refresh before deciding whether to retry.
Pre-entry cancellation and cancellation during the command's lookup return
`CANCELLED`; interrupted writes remain uncertain. Confirmed oven commits keep
their successful result when only event persistence fails, with
`EVENT_WRITE_FAILED` or `EVENT_WRITE_OUTCOME_UNKNOWN`. No write is replayed.
Malformed binding/transport errors can still be rejected by the Functions host
before a tool runs; those are not fabricated application successes.

Dashboard API exceptions return a generic problem with a trace identifier, and
interactive failures retain the explicitly stale last snapshot. Read retries are
bounded (two retries, 200 ms initial delay, one-second maximum delay, two-second
per-network-attempt timeout); writes have zero retries. The five-second dashboard
read deadline remains independent.

## Repeatable local reliability verification

On **Windows**, install .NET 10, Node.js, Microsoft Edge stable, Azurite 3.35.0
and Functions Core Tools 4.16.0. The latter two can be installed with
`npm install --global azurite@3.35.0 azure-functions-core-tools@4.16.0`.
No Azure login, subscription, deployment, connection secret or downloaded
Playwright browser is required. The pinned Playwright .NET client drives the
installed Edge channel, using the supported `PLAYWRIGHT_NODEJS_PATH` override
for Node rather than modifying the driver's files.

From the solution directory:

```powershell
dotnet restore .\PizzaOps.sln
dotnet build .\PizzaOps.sln --no-restore
dotnet test .\PizzaOps.sln --no-build --no-restore
dotnet format .\PizzaOps.sln --verify-no-changes --no-restore
.\scripts\Test-Local.ps1
```

`Test-Local.ps1` also works when invoked from another directory: it selects the
scoped SDK, restores the caller's directory/environment on exit, builds before
starting services, and fails on missing prerequisites. It enables only the named
`PizzaOps-Local-E2E` destructive environment. The test settings reject anything
other than development storage, loopback HTTP and run-specific table names.

The start helper refuses occupied emulator ports 10000-10002 instead of reusing
or stopping an existing emulator. It starts an **in-memory** loopback-only
Azurite instance and two real applications, with random application ports,
unique tables and a unique kitchen partition. Readiness is bounded; Functions
must expose its actual MCP route and Dashboard must pass liveness. A reset is
still explicit C# operator/test logic, never application startup logic.
Inherited cloud credentials and telemetry-export settings are cleared for these
processes. Test diagnostics contain only synthetic data and operational outcomes.

The test helper always stops its owned process trees and removes their run
directory, including on failure. Ownership manifests verify PIDs/start times;
unrelated services and storage are not stopped or deleted. To start the same
isolated stack manually after building, use:

```powershell
$run = .\scripts\Start-Local.ps1
# Follow the printed Development-only DemoAdmin configuration/reset instructions.
# The returned object includes DashboardUrl and McpEndpoint.
.\scripts\Stop-Local.ps1 -RunId $run.RunId
```

The real SDK MCP client performs reset -> exact four-tool discovery -> status ->
recipe -> 450-to-320 repair -> PASS -> rejected 600 -> unchanged oven fields/ETag,
**twice**, with a fresh reset and fresh DOM reset assertions between stories.
The read-only API and actual Interactive Server Blazor DOM at 1920x1080 are
checked; an old event from the previous story cannot satisfy the next story.
Tool response trace IDs must match stored events and structured Functions logs.

The same helper runs the real Table integration suite: observed-ETag conflict,
a competing writer mapped to `CONCURRENT_UPDATE`, partition isolation, reset
failure/recovery, malformed data, bounded event queries, and response loss or
cancellation **after a real Table commit**. The last two faults are injected at
the SDK HTTP transport boundary: actual persisted state changes, the application
returns `STORAGE_OUTCOME_UNKNOWN` with no false unchanged-state claim, and the
write is not retried. These focused fault cases call the shared application
service/adapter; the two main stories use the real remote MCP endpoint.

Deleting only the owned event table proves a repair can commit with an explicit
failed-event warning while the UI shows **CONNECTED / DEGRADED** and the new
temperature. Removing only the owned oven table proves MCP/API storage failure,
retained **STALE / NOT LIVE** oven cards, and recovery after reset. Invalid MCP
arguments preserve the persisted entity; an untrusted marker must not appear
in captured application logs. No failure is replaced by mock kitchen state.

**Local evidence (2026-11-17):** 123 unit tests and 24 offline dashboard tests
passed; the helper passed all 11 real integration tests (including both stories).
Build completed with zero warnings/errors; solution formatting and the transitive
dependency vulnerability gate passed. Invocation-start-to-visible-DOM upper
bounds, including the actual MCP call, were:

| Story | Repair | PASS bake | Rejection |
| --- | --- | --- | --- |
| First reset/run | 1,438 ms | 429 ms | 1,162 ms |
| Second reset/run | 971 ms | 881 ms | 1,369 ms |

Event degradation appeared in 889 ms; stale storage state appeared in 833 ms.
Every measured display change was below the three-poll / 3,000 ms threshold.
Separate immediate API observations took 4-10 ms; those are **not** UI latency
measurements. The helper cleaned all owned processes/artifacts and released
emulator ports. These are local results, not Azure ingestion, deployed-key/RBAC,
presenter Copilot compatibility, or stage-network evidence.

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
another kitchen partition. The Windows CI workflow restores, builds, runs offline tests, checks formatting,
fails on vulnerable dependencies/audit errors, and runs the isolated real
MCP/Table/Edge tests with pinned emulator/Core Tools versions. It performs no
Azure login, provisioning, or deployment. The three older hard-coded-port MCP
tests remain separately opt-in; the new isolated suite does not enable them or
reuse another running host.

The test projects use xUnit 2 with the VSTest runner (`Microsoft.NET.Test.Sdk`
and `xunit.runner.visualstudio`); the command above is the solution-level test
invocation.
