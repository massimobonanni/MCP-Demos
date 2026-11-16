# PizzaOps

PizzaOps is a scoped .NET 10 solution for a simulated pizzeria demonstration.
The solution currently contains the Core domain and contracts plus the
Azure Table Storage adapter and shared read projection. MCP tools, operator
reset, and dashboard endpoints/components are separate later tasks.

## Projects

| Project | Responsibility |
| --- | --- |
| `PizzaOps.Core` | Domain and application contracts; no infrastructure references |
| `PizzaOps.Storage` | Storage adapter boundary |
| `PizzaOps.Functions` | Azure Functions isolated-worker MCP host |
| `PizzaOps.Dashboard` | Blazor Web App |
| `PizzaOps.DemoAdmin` | Local operator command-line app |
| `PizzaOps.UnitTests` | Core unit-test project |
| `PizzaOps.IntegrationTests` | Storage and Functions integration-test project |

`Core` has no project or package dependencies. `Storage` depends on `Core` and
uses Azure Table Storage with managed identity in Azure or explicitly selected
Azurite in Development.
Functions, Dashboard, and DemoAdmin may depend on Core and Storage; Dashboard
and DemoAdmin do not reference Functions.

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
explicitly enabled. The CI workflow restores, builds, and checks formatting
without Azure login, provisioning, or deployment.

The test projects use xUnit 2 with the VSTest runner (`Microsoft.NET.Test.Sdk`
and `xunit.runner.visualstudio`); the command above is the solution-level test
invocation.
