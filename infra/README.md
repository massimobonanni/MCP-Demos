# AZD Infrastructure

This folder contains the Bicep infrastructure used by Azure Developer CLI
(AZD), configured through `azure.yaml` (`infra.path: ./infra`, module `main`).
The subscription-scoped entry point provisions Demo 02 and PizzaOps resources
into the environment's resource group; it does not deploy or start either
.NET application.

## Demo 02

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
outputs, and the root `postprovision` hook remain separate and unchanged.

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
| `PIZZAOPS_LOCATION` | Empty | Optional PizzaOps-only region override; the resource-group and Demo 02 Foundry region policy is not changed. |
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

The pre-existing `AZURE_LOCATION` allowlist remains the Foundry policy and
still determines the resource-group location. It is not a statement that
Flex .NET 10, App Service .NET 10, or the MCP extension is available there.
Check actual runtime and service availability for the selected PizzaOps
region before requesting target approval. If another supported region is
needed, set `PIZZAOPS_LOCATION` without changing the existing Foundry
allowlist.

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

- Azure CLI and Azure Developer CLI (`azd`) installed.
- PowerShell 7 (`pwsh`) on the `PATH`, including on Linux/macOS, and a .NET SDK
  compatible with the `net10.0` project. The `postprovision` hook and the
  `azd hooks run postprovision` command have been verified with stable AZD
  1.37.3, without preview features.
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
azd env set AZURE_SUBSCRIPTION_ID <subscription-id>
azd env set AZURE_LOCATION eastus2
azd env set PIZZAOPS_PROFILE dev

azd provision -e demo02-dev
```

Replace `demo02-dev`, the ID, and `eastus2` with the values you want. The
region must be one of those listed above for Demo 02. An optional
`PIZZAOPS_LOCATION` can target a separately validated region for PizzaOps.
If the environment already exists, select it with `azd env select demo02-dev`
and update the parameters with `azd env set` if needed. Do not run
`azd provision`, `azd up`, or an Azure what-if until the subscription,
environment, tenant, both regions, optional operator principal, and intended
resource changes have been reviewed and explicitly approved.

Alternatively, `azd up -e demo02-dev` provisions the infrastructure and then
attempts the application's deployment phases defined by the project. Use
`azd provision` to deploy infrastructure resources only.

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
az bicep build --file .\infra\main.bicep --stdout | Out-Null
pwsh -NoProfile -File .\scripts\azd\tests\Test-Demo02Secrets.ps1
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

To view the nonsecret provisioning outputs:

```powershell
azd env get-values -e demo02-dev
```

To delete the resources created for the environment (including both demos):

```powershell
azd down -e demo02-dev
```

This is destructive and deletes the resources in the environment's resource
group.
