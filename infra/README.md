# AZD Infrastructure

This folder contains the Bicep infrastructure used by Azure Developer CLI
(AZD), configured through `azure.yaml` (`infra.path: ./infra`, module `main`).
The subscription-scoped entry point provisions Demo 02 and PizzaOps resources
into the environment's resource group; it does not deploy or start either
.NET application. The root [azure.yaml](../azure.yaml) separately defines
exactly two deployable services: `pizzaops-functions` (`function`) and
`pizzaops-dashboard` (`appservice`). DemoAdmin and storage are not services.

## Chat with tools (Demo 02)

AZD starts a subscription-scoped Bicep deployment. The template creates a
resource group whose name includes the environment (`rg-mcp-demos-<environment>`),
then deploys a Microsoft Foundry account, a project, and the `gpt-6-luna` model
deployment with the `GlobalStandard` SKU into it.

```mermaid
flowchart TD
    S["Azure subscription<br/>(AZURE_SUBSCRIPTION_ID)"]
    RG["Resource group<br/>rg-mcp-demos-&lt;environment&gt;"]
    FA["Microsoft Foundry account<br/>SKU S0"]
    P["Foundry project<br/>demo02"]
    D["Model deployment<br/>gpt-6-luna<br/>GlobalStandard"]

    S --> RG
    RG --> FA
    FA --> P
    FA --> D
```

The template exposes the resource group name, Foundry account name and
endpoint, project name, and deployment name as AZD outputs. It does not expose
keys or other secrets.

## PizzaOps resources (Demo 03)

The root `main.bicep` also invokes `modules/pizzaops.bicep` in that same
resource group. It adds one Linux Flex Consumption Functions app and plan, one
Linux App Service dashboard and plan, one shared StorageV2 account, the
`PizzaOpsOvens` and `PizzaOpsEvents` tables, a private Flex deployment
container, Application Insights, and its Log Analytics workspace. Every
PizzaOps resource is tagged `demo: '03'`; existing Demo 02 names, tags,
outputs remain unchanged. The root `postprovision` hook retains its local
behavior, with an explicit CI-only skip described below.

```mermaid
flowchart TD
    RG["Shared resource group<br/>rg-mcp-demos-&lt;environment&gt;"]
    Presenter["Presenter / Copilot"]
    Functions["PizzaOps MCP Function App<br/>Linux Flex Consumption<br/>.NET 10 isolated"]
    Dashboard["PizzaOps dashboard<br/>Linux App Service<br/>.NET 10 / Blazor"]
    Storage["Shared StorageV2 account<br/>Shared-key access disabled"]
    Ovens[("PizzaOpsOvens table")]
    Events[("PizzaOpsEvents table")]
    Deploy["Private Flex deployment container"]
    Insights["Application Insights"]
    Workspace["Log Analytics workspace"]

    RG --> Functions
    RG --> Dashboard
    RG --> Storage
    RG --> Insights
    Insights --> Workspace
    Presenter -->|"HTTPS / MCP"| Functions
    Functions -->|"Table Contributor"| Ovens
    Functions -->|"Table Contributor"| Events
    Dashboard -->|"Table Reader"| Ovens
    Dashboard -->|"Table Reader"| Events
    Functions -.->|"System identity / host-storage RBAC"| Storage
    Functions -.->|"Deployment Blob Contributor"| Deploy
    Storage --> Ovens
    Storage --> Events
    Storage --> Deploy
    Functions -.-> Insights
    Dashboard -.-> Insights
```

The PizzaOps app and dashboard have system-assigned identities. The dashboard
gets only `Storage Table Data Reader` on each application table. Functions
gets `Storage Table Data Contributor` on both tables, and the host-storage
permissions required by identity-based Functions storage: `Storage Blob Data
Owner`, `Storage Queue Data Contributor`, and `Storage Table Data Contributor`
at the account scope. Its Flex deployment identity also receives
`Storage Blob Data Contributor` on the deployment container. The account
disallows shared-key access; its HTTPS endpoints remain enabled with public
network access, so data-plane access is enforced by Entra ID/RBAC rather than
network isolation. Both app identities receive `Monitoring Metrics Publisher`
on Application Insights, which has local authentication disabled.

The optional `PIZZAOPS_OPERATOR_PRINCIPAL_ID` setting grants the supplied
principal `Storage Table Data Contributor` on only the two PizzaOps tables.
Leave it unset unless the authorized reset operator and deployment target are
known.

### PizzaOps environment settings

`main.bicepparam` defaults PizzaOps to the existing `AZURE_LOCATION`, the
`dev` profile, and zero Flex always-ready instances. Set AZD environment values
before a future, approved deployment:

| AZD value | Default | Effect |
| --- | --- | --- |
| `PIZZAOPS_PROFILE` | `dev` | `dev` uses a Linux B1 dashboard plan with Always On disabled; `demo` uses S1 with Always On enabled. |
| `PIZZAOPS_FUNCTIONS_ALWAYS_READY_HTTP_INSTANCES` | `0` | Restricted to 0 or 1. Keep at 0 until the selected region, actual MCP trigger scale group, and incremental cost are checked and approved. |
| `PIZZAOPS_OPERATOR_PRINCIPAL_ID` | Empty | Optional user, group, or service-principal object ID for reset-table access. |
| `PIZZAOPS_OPERATOR_PRINCIPAL_TYPE` | `User` | Set to `Group` or `ServicePrincipal` when the optional operator ID has that type. |

The Flex app uses runtime `dotnet-isolated` version `10`, Functions v4, a
2-GiB instance size, and a maximum of 40 instances. Its deployment package is
configured for the system-assigned identity, while `AzureWebJobsStorage` uses
the identity-based `accountName`/`credential=managedidentity` settings.
App Service uses the built-in `DOTNETCORE|10.0` stack, HTTPS-only, TLS 1.2,
WebSockets, and `/health/ready`. Both apps receive the Table endpoint and
validated table/partition names through app settings. No MCP key is emitted
as an output or configured by this infrastructure.

On Flex Consumption, the runtime name and version are configured only through
`functionAppConfig.runtime`. Do not add `FUNCTIONS_WORKER_RUNTIME` or
`FUNCTIONS_WORKER_RUNTIME_VERSION` app settings; `FUNCTIONS_EXTENSION_VERSION`
is managed by the platform and must also be omitted. These legacy settings
can cause `azd provision` to fail with `BadRequest` even after the Function
App has been created. After correcting the template, rerun `azd provision`
against the same environment; this error does not require deleting resources.
See the [Flex Consumption app-setting deprecations](https://learn.microsoft.com/azure/azure-functions/functions-app-settings#flex-consumption-plan-deprecations).

The pre-existing `AZURE_LOCATION` allowlist remains the Foundry policy and
still determines the resource-group location. It is not a statement that
Flex .NET 10, App Service .NET 10, or the MCP extension is available there.
The AZD parameter file deliberately supplies **the same `AZURE_LOCATION` to
both demos**; there is no `PIZZAOPS_LOCATION` environment override.
Choose a region satisfying both Foundry and PizzaOps runtime/service
requirements before requesting target approval. The lower-level Bicep
`pizzaOpsLocation` parameter remains for direct template callers, but the
root AZD workflow and CI use the shared region.

For development, use a named environment such as `mcp-demos-dev` and keep
`PIZZAOPS_PROFILE=dev`. For a demo environment, explicitly set
`PIZZAOPS_PROFILE=demo`; this selects the Standard dashboard plan and Always
On but leaves Flex always-ready disabled. Verify the supported Flex scale
group and obtain explicit cost approval before setting
`PIZZAOPS_FUNCTIONS_ALWAYS_READY_HTTP_INSTANCES=1`. B1/S1 plan charges,
workspace ingestion/retention, storage, and Flex execution/always-ready
charges are separate cost considerations; estimate them for the selected
region and expected usage before provisioning.

## Prerequisites and parameters

- Azure CLI and Azure Developer CLI (`azd`) 1.38.0 or later installed.
- PowerShell 7 (`pwsh`) on the `PATH`, including on Linux/macOS, and a .NET SDK
  compatible with the `net10.0` project. The `postprovision` hook and the
  `azd hooks run postprovision` command and packaging have been verified with
  stable AZD 1.38.0. AZD still labels `package` Beta in its command help.
- Access to the subscription and permissions to create a resource group and
  Microsoft Foundry resources.
- The Azure CLI identity must have the
  `Microsoft.CognitiveServices/accounts/listKeys/action` permission on the
  Foundry account. AZD login alone does not necessarily authenticate Azure CLI.
- Logged in with `az login` and `azd auth login`.
- A non-empty AZD environment name (up to 77 characters).
- `AZURE_SUBSCRIPTION_ID`: ID of the target subscription.
- `AZURE_LOCATION`: region for the resource group, Foundry account, and
  deployment. The template accepts only regions where `gpt-6-luna` with the
  Global Standard SKU is available:
  `australiaeast`, `brazilsouth`, `canadacentral`, `canadaeast`, `centralus`,
  `eastus`, `eastus2`, `francecentral`, `germanywestcentral`, `italynorth`,
  `japaneast`, `koreacentral`, `northcentralus`, `norwayeast`,
  `polandcentral`, `southafricanorth`, `southcentralus`, `southindia`,
  `southeastasia`, `spaincentral`, `swedencentral`, `switzerlandnorth`,
  `switzerlandwest`, `uaenorth`, `uksouth`, `westeurope`, `westus`, `westus3`.

The environment name is the one provided to `azd env new`; AZD passes it as
`AZURE_ENV_NAME`. The `main.bicepparam` file reads the existing three required
variables and the optional PizzaOps settings with
`readEnvironmentVariable`. The template does not pin the model version or
capacity; these are left to the provider defaults.

## Create the environment and provision

From the repository root:

```powershell
az login
azd auth login

azd env new demo02-dev
azd env set AZURE_SUBSCRIPTION_ID <subscription-id> -e demo02-dev
azd env set AZURE_LOCATION eastus2 -e demo02-dev
azd env set PIZZAOPS_PROFILE dev -e demo02-dev

azd provision --cwd .\src\MCP-Demos03-PizzaOps -e demo02-dev
```

Replace `demo02-dev`, the ID, and `eastus2` with the values you want. The
region must be one of those listed above for Demo 02 and also support PizzaOps.
If the environment already exists, select it with `azd env select demo02-dev`
and update the parameters with `azd env set` if needed. Do not run
`azd provision`, `azd up`, or an Azure what-if until the subscription,
environment, tenant, shared region, optional operator principal, and intended
resource changes have been reviewed and explicitly approved.

### Package, provision, deploy, and reset are different operations

Run these examples from the repository root, only against a reviewed target:

```powershell
# Local build/package only; no Azure login or resource changes.
azd package --cwd .\src\MCP-Demos03-PizzaOps --all -e demo02-dev --no-prompt
# Shared infrastructure, tables and RBAC; local Demo 02 postprovision hook runs.
azd provision --cwd .\src\MCP-Demos03-PizzaOps -e demo02-dev
# Application code only; preserves kitchen data and does not run postprovision.
azd deploy --cwd .\src\MCP-Demos03-PizzaOps --all -e demo02-dev
# Combined package/provision/deploy lifecycle, not a kitchen reset.
azd up --cwd .\src\MCP-Demos03-PizzaOps -e demo02-dev
```

**Keep the explicit `--cwd` on commands that build code.** AZD finds the root
manifest by walking up from the PizzaOps solution. In AZD 1.38.0 a bare
root `azd package` runs .NET from the root, not the service directory:
verification found SDK 11 preview despite the scoped `global.json`.
With the shown option, both actual publish builds used SDK **10.0.404**.
There is no root SDK pin or second/custom publishing implementation.
Use absolute `--output-path` paths when retaining packages, because relative
paths are resolved against the selected working directory.

The Function App and dashboard carry matching `azd-service-name` tags.
AZD discovers the shared resource group through its existing `azd-env-name`
tag; the existing camel-case Demo 02 and PizzaOps name/ID/endpoint outputs
remain unchanged and nonsecret. No duplicate uppercase output is required.
Discovery uses the tags, not hard-coded app names. The two
deployable projects have stable `UserSecretsId` values so AZD initialization
does not edit project files. AZD may materialize **nonsecret infrastructure
outputs** in their local user-secret stores; no MCP key is an output.

The Function App uses Flex (`FC1`/`FlexConsumption`), not classic Consumption.
AZD 1.38.0's [function target](https://github.com/Azure/azure-dev/blob/azure-dev-cli_1.38.0/cli/azd/pkg/project/service_target_functionapp.go)
reads the actual plan and chooses its Flex deployment API rather than the
regular ZipDeploy path. Flex uses
[One Deploy](https://learn.microsoft.com/azure/azure-functions/functions-deployment-technologies#one-deploy).
`remoteBuild: false` sends the locally published ZIP as-is. The Bicep
`functionAppConfig.deployment.storage` configures the private blob container
and system identity; do not add classic `WEBSITE_RUN_FROM_PACKAGE`,
storage keys, Oryx settings, or a parallel `func publish` pipeline.
ZIP checks cover the .NET 10 runtime, host/extension files, and the SDK 1.0.0
`worker.config.json` worker-indexing contract (no legacy `functions.metadata`
file is required). This is local/package and implementation evidence, **not**
proof of runtime availability or a successful Azure deployment.

Provisioning creates empty tables. Deployment never seeds/resets them or
retrieves the MCP key. After deployment, `/health/live` should respond; an
empty or incomplete kitchen correctly makes `/health/ready` return 503.
Use the separately authorized [operator procedure](../src/MCP-Demos03-PizzaOps/README.md#operator-reset-and-status)
to pause/drain calls, explicitly reset, and verify status before resuming.
Then verify readiness, dashboard state, real managed-identity/RBAC behavior,
and authenticated MCP discovery in Task 13. A failed provision/deploy does
not trigger automatic reset, rollback, or deletion.

### Protected manual GitHub deployment

The [CI workflow](../.github/workflows/pizzaops-ci.yml) runs without Azure
credentials on pushes/PRs affecting PizzaOps, infrastructure, AZD scripts or
either workflow. It retains the existing build/test/format/dependency audit
and isolated local E2E suite, and adds Bicep/parameter/discovery validation,
offline secret-hook regressions, and root-invoked SDK/ZIP assertions.
It needs package/tool download access, but no Azure login or resource access.
Actions are commit-pinned; CI uses AZD 1.38.0, Bicep 0.49.2 and the scoped SDK.

The [deployment workflow](../.github/workflows/pizzaops-deploy.yml) is
`workflow_dispatch` only and reuses that no-cloud validation before deployment.
It allows the default branch only, requires an explicit environment plus
matching confirmation, and serializes deployments per environment without
canceling an in-progress resource update. `deploy` is the code-only default;
`provision-and-deploy` explicitly includes shared Demo 02 infrastructure.
No PR, push, or completed-CI event deploys automatically.

Repository administrators must configure these prerequisites **before** dispatch:

1. Create a GitHub Environment named exactly like the lowercase AZD environment.
   Require reviewers, prevent self-review, restrict deployment branches to the
   protected default branch, and disable approval bypass where available.
   The workflow checks the existing environment's reviewer and branch policies
   through the GitHub API and fails closed if they are missing/unreadable.
   YAML does not create these protection settings. The GitHub plan must support
   environment approvals for this repository.
2. Configure an Entra application's federated credential with issuer
   `https://token.actions.githubusercontent.com`, audience
   `api://AzureADTokenExchange`, and subject
   `repo:<owner>/<repository>:environment:<environment>`.
   Do not use a client secret or a branch-wide OIDC subject.
3. Set protected Environment **variables** `AZURE_ENV_NAME`, `AZURE_CLIENT_ID`,
   `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_LOCATION`,
   `PIZZAOPS_PROFILE`, `PIZZAOPS_FUNCTIONS_ALWAYS_READY_HTTP_INSTANCES`,
   and `PIZZAOPS_OPERATOR_PRINCIPAL_TYPE`. Set optional
   `PIZZAOPS_OPERATOR_PRINCIPAL_ID` deliberately (empty means no operator
   grant). `AZURE_LOCATION` is shared by both demos. These are nonsecret
   configuration; no keys are needed.
4. Grant the federated identity only the target's required management rights.
   Code-only deployment needs app deployment, discovery and deployment-output
   read access. Root provisioning additionally creates the resource group at
   subscription scope, manages Foundry/PizzaOps resources, and assigns RBAC.
   Have an administrator review scoped/custom roles rather than blindly
   granting subscription Owner. The pipeline is not the reset operator and
   does not need application Table data access or MCP/Foundry key retrieval.
5. Review the Task 11 what-if, region/runtime availability and costs before
   approving provisioning. This workflow does not waive those still-open gates.

The protected job signs in with `azd auth login
--federated-credential-provider github`, initializes fresh local AZD state for
the explicit subscription/environment, and provisions or refreshes existing
outputs. Protected variables are the reproducible parameter source; the runner
does not inherit a developer's `.azure` directory. Every environment operation
is explicit and noninteractive. Packages are built once, retained with SHA-256
checksums for 14 days, and passed to native `azd deploy --from-package`.
Only ZIPs/checksums are uploaded, never `.azure`, logs, profiles, or keys.
The final smoke check is dashboard liveness only; it deliberately does not
mask a missing kitchen by seeding data.

For code rollback, download the **pair** of known-good packages for a reviewed
commit, verify their recorded SHA-256 hashes, refresh the intended environment,
and deploy each package with the same native path:

```powershell
azd env refresh -e demo02-dev
azd deploy pizzaops-functions --cwd .\src\MCP-Demos03-PizzaOps -e demo02-dev --from-package <absolute-functions-zip>
azd deploy pizzaops-dashboard --cwd .\src\MCP-Demos03-PizzaOps -e demo02-dev --from-package <absolute-dashboard-zip>
```

Obtain target approval first; confirm runtime/configuration compatibility and
recheck liveness/readiness afterward. Deploying the pair is not atomic; pause
calls while recovering a partial rollout. Code rollback does not roll back
Bicep, table data, identities or keys, and is never an excuse to run `azd down`.

## Automatic local configuration for Demo 02

After successful provisioning, the root `postprovision` hook in `azure.yaml`
runs `scripts/azd/initialize-demo02-secrets.ps1`. It uses the non-secret
outputs `resourceGroupName`, `foundryAccountName`, and `foundryEndpoint`, along
with the `AZURE_SUBSCRIPTION_ID` value from the selected environment. Azure CLI
retrieves `key1` from the account in that subscription; the key is held in
memory and passed as JSON through stdin to `dotnet user-secrets set --project`,
together with the endpoint.

The `src/MCP-Demos.02-MCPChat` project already has a `UserSecretsId`; there is
no need to run `dotnet user-secrets init`. The settings written are
`Foundry:Endpoint` and `Foundry:ApiKey`, the same names read by the
application. The model remains the `gpt-6-luna` deployment expected by the
project.

The hook does not print the values or place the key in process arguments,
temporary files, Bicep outputs, or the `.azure` environment. It also
suppresses the output of commands that handle the key, including error output,
and instead reports the failed phase and checks to perform. An error causes
the hook to exit with a non-zero code and fails the AZD command; resources
already created are not automatically deleted. Do not run setup with tracing,
PowerShell transcripts, or debug logging enabled for commands that handle
credentials.

User secrets are a local **unencrypted** store outside the repository,
intended for development only. Run provisioning/the hook and the application
as the same operating system user. The hook updates only these two settings
and preserves other user secrets; changing the AZD environment overwrites the
Demo 02 values for the current user. Do not use this store to distribute
production credentials.

In GitHub Actions only, the deployment job sets
`DEMO02_SKIP_LOCAL_SECRETS=true`. The hook requires **both** that exact opt-in
and `GITHUB_ACTIONS=true`; otherwise an attempted skip fails visibly.
Without the opt-in, even CI retains the original fail-fast behavior.
The skip occurs before checking outputs or invoking either CLI, logs that no
key was retrieved, and neither lists Foundry keys nor writes local Demo 02
secrets. This is a workflow guard, not an authentication boundary. Never set
the flag locally to hide a broken hook. It does not skip infrastructure or
change the local hook path, and no separate deployment implementation exists.

To repeat only the local configuration without provisioning again:

```powershell
# If the local outputs are out of date, synchronize them from Azure:
azd env refresh -e demo02-dev
azd hooks run postprovision -e demo02-dev --no-prompt

dotnet run --project .\src\MCP-Demos.02-MCPChat
```

Refreshing and retrieving the key require access to Azure, but the
`hooks run` command does not create resources. If the key is rotated, run the
hook again. Do not use `dotnet user-secrets list` for a shared verification:
it would also display the key.

### Local verification without deployment

From the repository root:

```powershell
pwsh -NoProfile -File .\scripts\azd\tests\Test-PizzaOpsInfrastructure.ps1
pwsh -NoProfile -File .\scripts\azd\tests\Test-Demo02Secrets.ps1
pwsh -NoProfile -File .\scripts\azd\tests\Test-PizzaOpsPackage.ps1
```

The test uses a fake key and an isolated temporary profile. It verifies the
actual import through .NET, value replacement, preservation of unrelated
settings, and the cases of missing outputs, invalid endpoint, empty/invalid
key, Azure CLI error, and write error. It checks that values do not appear in
the output. It also runs the actual `azd hooks run postprovision` against a
temporary copy of `azure.yaml`, with an intentionally invalid endpoint,
verifying environment loading and error propagation before any request to
Azure. It does not provision resources, retrieve real keys, or modify the
repository's selected AZD environment, and it removes the temporary profile
when finished.

The expanded hook suite also checks invalid/local skip attempts, explicit CI
skip without either CLI call, default CI behavior, and the real AZD hook skip
without changing existing secrets. All **13 checks passed** on Windows with
AZD 1.38.0. The packaging test copies only nonignored source inputs into a
temporary repository-root layout, isolates AZD/profile state, reuses the NuGet
cache, instruments the actual MSBuild publish SDK, validates both ZIPs, and
cleans up. It never selects or refreshes the developer's active environment.
Both packages passed with SDK 10.0.404 even with SDK 11 preview installed.
Bicep/discovery and both dev/demo parameter profiles passed locally with
Bicep 0.49.2. GitHub workflow syntax was checked with actionlint 1.7.7;
the protected cloud workflow was **not run**.

### Linux compatibility

The same PowerShell hook runs on Windows and Linux: `shell: pwsh` is
explicit, with no `windows` restriction in `azure.yaml`. Linux requires
native PowerShell 7, .NET SDK, Azure CLI, and AZD on `PATH`; Bash alone is
not sufficient. In WSL, do not use Windows Azure CLI or Windows .NET for
this workflow.

All nine offline checks passed on Ubuntu 24.04.4 (WSL), PowerShell 7.5.4,
.NET SDK 10.0.404, and AZD 1.37.3, including actual imports into the Linux
user secrets store and hook execution through native Linux AZD. The same
suite also passed again on Windows. Azure CLI key retrieval is simulated in
these tests; they do not verify access to a real Azure account.

To repeat the tests from Bash at the repository root:

```bash
pwsh -NoProfile -File ./scripts/azd/tests/Test-Demo02Secrets.ps1
```

On Linux, the test unsets `APPDATA` and isolates `HOME`, checking the
`.microsoft/usersecrets` store inside the temporary profile. The hook itself
does not set these variables: normal execution uses the current user's
profile.

## Verification and cleanup

Read only the nonsecret outputs needed for verification, rather than dumping
the entire AZD environment (which may contain unrelated local values):

```powershell
azd env get-value pizzaOpsDashboardUrl -e demo02-dev
azd env get-value pizzaOpsFunctionAppName -e demo02-dev
```

To delete the resources created for the environment (including both demos):

```powershell
azd down -e demo02-dev
```

**Shared-resource destruction:** root `azd down` deletes the shared environment,
including Demo 02 Foundry resources and PizzaOps storage/data, plans, apps and
telemetry. It is not a PizzaOps reset or routine recovery step. Obtain explicit
approval from both demos' owners, confirm the environment/subscription and
resource inventory, and back up needed data first. Never run cleanup
automatically after a failed deployment. Prefer the explicit kitchen reset for
rehearsal; if only PizzaOps must be removed, plan a separately reviewed,
resource-scoped removal rather than deleting the shared group.
