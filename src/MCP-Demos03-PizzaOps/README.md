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

## Configure the MCP server in VS Code

Use the **Copilot** harness with GitHub Copilot enabled in a trusted VS Code
workspace. The examples below use Streamable HTTP, not the dashboard API or
the legacy `/sse` endpoint. Configuration alone is not proof of connectivity:
verify discovery and a real tool call using the account/profile intended for
the presentation. The [compatibility record](docs/compatibility.md) still
tracks the unverified presenter and deployed-Azure gates.

### Local development

1. Start Azurite, run the separately authorized
   [operator reset and status](#operator-reset-and-status), and start Functions
   as described in [Local MCP tools](#local-mcp-tools).
2. In a Copilot chat, run **Chat: Open Customizations**, select **MCP Servers**,
   then **Add Server**. Select HTTP and enter
   `http://127.0.0.1:7071/runtime/webhooks/mcp`.
3. Name the server `pizzaops` and choose **Global > Copilot Global** for a
   personal configuration. This edits `%USERPROFILE%\.copilot\mcp-config.json`
   on Windows, unless `COPILOT_HOME` overrides that location.

Alternatively, merge this entry into the existing file; do not overwrite
other servers:

```json
{
  "mcpServers": {
    "pizzaops": {
      "type": "http",
      "url": "http://127.0.0.1:7071/runtime/webhooks/mcp",
      "tools": ["*"]
    }
  }
}
```

Core Tools normally allows the local connection without a key. This does not
validate Azure authentication. The HTTP configuration connects to an already
running Functions host; it does not start Functions or Azurite.

### Deployed Azure Function App

After deployment, replace the local `pizzaops` entry with the following
configuration. Use the Function App's actual default hostname from the Azure
portal, not the dashboard hostname; keep only one PizzaOps target enabled to
avoid mutating the wrong kitchen.

```json
{
  "mcpServers": {
    "pizzaops": {
      "type": "http",
      "url": "https://<function-app-hostname>/runtime/webhooks/mcp",
      "headers": {
        "x-functions-key": "${PIZZAOPS_MCP_KEY}"
      },
      "tools": ["*"]
    }
  }
}
```

In the Azure portal, open the deployed Function App's **App keys** and obtain
the **system key** named `mcp_extension` using an authorized operator account.
A normal function key is not a substitute. Keep the key out of source control,
chat, screenshots, command history, and URL query strings. The current app
requires this key; Azure CLI/AZD sign-in alone does not authenticate MCP calls.
Possession of the key permits requests to the shared kitchen, including writes.

For native Copilot configuration, supply the variable to the VS Code process.
Close existing VS Code instances first so `code .` does not reuse a process
with an older environment. From a separate PowerShell terminal at the
repository root:

```powershell
$env:PIZZAOPS_MCP_KEY = [System.Net.NetworkCredential]::new(
    "",
    (Read-Host "Paste the mcp_extension system key" -AsSecureString)
).Password
code .
```

The prompt masks the input and the command does not print the key. The value
is still a secret in process memory/environment, not a persistent secret store.
After the demo, close the VS Code instance and remove the terminal variable:

```powershell
Remove-Item Env:\PIZZAOPS_MCP_KEY
```

For remote/Dev Container sessions, configure the variable on the host where
Copilot runs, not just in the local terminal. Native Copilot uses
`mcpServers` and `${PIZZAOPS_MCP_KEY}`; do not copy editor-only `inputs` or
`${input:...}` into this format. Existing Local-harness configurations use
`.vscode/mcp.json` with `servers` instead and have different secret handling.
See the [current VS Code MCP configuration reference](https://code.visualstudio.com/docs/agents/reference/mcp-configuration)
and [Functions MCP authentication reference](https://learn.microsoft.com/azure/azure-functions/functions-bindings-mcp#connect-to-your-mcp-server).

### Verify discovery before presenting

1. Enable/start `pizzaops` in **Chat: Open Customizations > MCP Servers**,
   complete the server trust flow, and inspect its status/output for errors.
2. In an agent-capable Copilot chat, enable the four PizzaOps tools:
   `get_ovens_status`, `get_pizza_recipe`, `set_oven_temperature`, and
   `bake_test_pizza`. Keep normal tool approvals enabled.
3. Use the read-only discovery prompt below and confirm a real tool invocation,
   not a response inferred from repository files. Verify the initial
   320/450/300 C state against the dashboard.

On `401`, check the system key and environment inherited by the Copilot host;
on `404`, check the Function App hostname and webhook route. For discovery or
protocol errors, inspect MCP output and the compatibility record before
rehearsing. Deployment does not seed the kitchen: missing state requires the
operator procedure, not an MCP reset tool.

## Demo prompts

These are presenter chat messages, **not server-provided MCP prompts**. All
ovens and bakes are simulated. Before each run, pause/drain callers, perform
the explicit operator reset against the intended environment, verify `READY`,
then display the matching dashboard beside Copilot. Do not reset while tools
are running. Approve only calls targeting the intended demo kitchen.

### 1. Discover the kitchen without changing it

> Use only the PizzaOps MCP server to read the current kitchen status and the
> Margherita recipe. Do not change any oven or run a test bake yet. Report each
> oven's ID, temperature and health, and the recipe's target and tolerance.

Expected: `get_ovens_status` and `get_pizza_recipe(pizza_type="margherita")`;
`oven-02` is 450 C / `CRITICAL`, and Margherita requires 320 C with a 15 C
tolerance. Reads do not create a repair or bake event.

### 2. Diagnose, repair, and verify

> Our simulated pizzas from Oven 02 are coming out burned. Use the PizzaOps MCP
> tools to check the kitchen, find the correct temperature for a Margherita,
> fix the problem, and verify with a test pizza. Tell me exactly what you
> changed and report any tool or event-recording errors. Do not edit code or
> access storage directly.

Expected: read status/recipe, call `set_oven_temperature` for `oven-02` with
integer `target_temperature_c: 320`, then `bake_test_pizza` with
`pizza_type: "margherita"`. The dashboard should show Oven 02 becoming
`HEALTHY` and the test returning `PASS`; the other ovens stay unchanged.
Check actual tool results and persisted events rather than assuming success
from Copilot's summary.

For a slower, step-by-step presentation, use these **instead of** the combined
prompt, after discovery:

> Before changing the simulated oven, run a Margherita test bake in oven-02
> using PizzaOps. Explain the observed result without modifying its temperature.

> Set simulated oven-02 to the Margherita recipe's target using PizzaOps, then
> run another Margherita test bake and read back the oven status.

The first bake at 450 C returns `TEST_COMPLETED` with `BURNT`, not a tool
failure. After repair, the second returns `PASS`. Both can append `TEST_BAKE`
events; neither bake changes the oven temperature.

### 3. Demonstrate server-side validation

> This is the PizzaOps simulator, not a physical oven. Request 600 C for
> oven-02 through set_oven_temperature so we can demonstrate the server's
> out-of-range validation. Do not bypass the tool, modify code, or access
> storage directly. Report the exact result, then read the kitchen status.

If the call is made, expect `TEMPERATURE_OUT_OF_RANGE`: the server permits
only integer commands from 200 to 350 C inclusive. Oven 02 must remain at
320 C after the successful repair; check the rejection result and dashboard
event feed. If Copilot declines to invoke the tool, say so: a model refusal
does **not** demonstrate server-side validation. Never disable approvals or
bypass policy just to force the demo.

### 4. Close with observed evidence

> Read the current PizzaOps kitchen status. Summarize the initial problem,
> the temperature change, the observed test-bake result, and the rejected
> command using only actual tool results from this conversation. Clearly
> identify any step that was not executed or could not be verified.

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

## Prerequisites

- .NET 10 SDK. The root and scoped `global.json` files select SDK `10.0.404` and permit a
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

The root [`global.json`](../../global.json) matches this solution's SDK
configuration, so AZD selects stable .NET 10 from either working directory.
The root SDK selection also applies to the sibling .NET 10 demos.

### Create an AZD environment

AZD reads the root [`azure.yaml`](../../azure.yaml). A named AZD environment
stores its configuration under the ignored `.azure` directory in the current
checkout. Creating that local AZD environment does not create Azure resources.

#### Local development

From the repository root, create a local environment and package without
logging in to Azure or provisioning cloud resources:

```powershell
azd env new pizzaops-local
azd env list
azd package --all -e pizzaops-local --no-prompt
```

To run PizzaOps locally rather than only package it, use the isolated
Azurite/Functions/Dashboard setup. On Windows, from the repository root:

```powershell
pwsh -NoProfile -File .\src\MCP-Demos03-PizzaOps\scripts\Start-Local.ps1
# Stop using the RunId printed by the start command:
pwsh -NoProfile -File .\src\MCP-Demos03-PizzaOps\scripts\Stop-Local.ps1 -RunId <run-id>
```

This local runtime uses in-memory Azurite and per-run tables; it is separate
from AZD provisioning and does not use or modify the cloud kitchen. The
script's prerequisites and the standard local host workflow are described in
[Local MCP tools](#local-mcp-tools).

#### Remote Azure environment

Use a distinct environment name for each Azure target. The root infrastructure
provisions both Demo 02 and PizzaOps into the selected subscription and
resource group; it is not a PizzaOps-only deployment. Sign in, create the
environment, and set its required values from the repository root:

```powershell
az login
azd auth login

azd env new pizzaops-dev
azd env set AZURE_SUBSCRIPTION_ID <subscription-id> -e pizzaops-dev
azd env set AZURE_LOCATION eastus2 -e pizzaops-dev
```

Choose a region supported by both demos and review the subscription, tenant,
RBAC permissions, resource changes, and cost before provisioning. The optional
`PIZZAOPS_OPERATOR_PRINCIPAL_ID` and operator type settings are documented in
the [infrastructure environment guide](../../infra/README.md#create-the-environment-and-provision).
After approving the target and expected changes, provision and deploy, or use
`azd up` to combine both operations:

```powershell
azd provision -e pizzaops-dev
azd deploy --all -e pizzaops-dev
# Alternatively, after reviewing the same target and changes:
azd up -e pizzaops-dev
```

The root `azure.yaml` does not configure shared AZD remote state: the
environment configuration remains in the creating checkout's `.azure`
directory, even though its resources are in Azure. For team-shared environment
state or further deployment/verification steps, follow the
[infrastructure lifecycle guide](../../infra/README.md).

AZD still discovers the single root [azure.yaml](../../azure.yaml). Both
`pizzaops-functions` and `pizzaops-dashboard` publish with SDK 10.0.404 in the
offline packaging test without `--cwd`, even with .NET 11 preview installed.
The Functions build also copies the scoped `global.json` beside its generated
extension project when artifacts are redirected outside the repository.
This prevents `MSB4236` for `Azure.Functions.Sdk`; the packaging test covers
an external `dotnet publish --artifacts-path` as well as native AZD packaging.
The Functions ZIP uses worker indexing
from the pinned SDK and native AZD Flex deployment with remote build disabled.
See the [infrastructure lifecycle and CI guide](../../infra/README.md#package-provision-deploy-and-reset-are-different-operations)
for environment creation, protected OIDC setup, nonsecret outputs, verification
and known-good package rollback.

`azd provision` creates shared Demo 02/PizzaOps resources, empty tables and
RBAC; `azd deploy --all` deploys the two apps; `azd up` combines the lifecycle.
These commands use the selected AZD environment unless `-e` is specified.
None seeds/resets the kitchen or retrieves the MCP key. Reset remains the explicit, separately
authorized operator procedure above. A new empty kitchen correctly fails
readiness until the operator resets and verifies it.

**Do not use root `azd down` as reset or recovery:** it destroys the shared
environment, including Demo 02 Foundry and PizzaOps table data. Cleanup requires
explicit approval and a resource/data review.

From the repository root, run the no-cloud AZD regressions:

```powershell
pwsh -NoProfile -File .\scripts\azd\tests\Test-PizzaOpsInfrastructure.ps1
pwsh -NoProfile -File .\scripts\azd\tests\Test-Demo02Secrets.ps1
pwsh -NoProfile -File .\scripts\azd\tests\Test-PizzaOpsPackage.ps1
```

These compile Bicep/parameters, check discovery contracts, exercise 13 offline
Demo 02 hook cases, and inspect both actual ZIPs and publish SDKs. Local
postprovision still materializes Demo 02 secrets as before; only the protected
GitHub job explicitly opts out of that local-user operation. No-cloud CI runs
these alongside the existing checks. Cloud deployment is manual, approval-
protected, OIDC-authenticated and environment-serialized; it never auto-resets.

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
