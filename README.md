# MCP-Demos

Four hands-on demos for learning the Model Context Protocol (MCP) with C# and .NET 10. Start by calling an existing MCP tool directly, let a language model call tools in a chat, then build your own MCP server locally or host it on Azure.

[Demos and projects](#demos-and-projects) | [Repository structure](#repository-structure) | [Quick start](#quick-start)

## Demos and projects

| Project | What it demonstrates |
| --- | --- |
| [01 - MCP Client](src/MCP-Demos.01-MCPClient/readme.md) | A console client that discovers tools on the remote Microsoft Learn MCP server and calls its documentation search tool directly, without a language model. |
| [02 - MCP Chat](src/MCP-Demos.02-MCPChat/readme.md) | A console chat backed by Microsoft Foundry. The model can call Microsoft Learn tools and use the results in its answers. |
| [03 - Stage Director](src/MCP-Demos.03-MCPServer/README.md) | A local MCP server over standard input/output (`stdio`). GitHub Copilot can switch scenes on a virtual stage, and a browser dashboard reflects the changes. See also [how to customize the scene markup](src/MCP-Demos.03-MCPServer/Web/README.md). |
| [04 - PizzaOps](src/MCP-Demos04-PizzaOps/README.md) | An Azure Functions MCP server over Streamable HTTP for a simulated pizzeria. Tools read recipes and oven status, change oven temperatures, and simulate test bakes. A separate Blazor dashboard displays the results. |
| [MCP-Demos.Core](src/MCP-Demos.Core/) | A shared library used by Demos 01 and 02 for console output, result parsing, and MCP error handling. It is not a standalone demo. |

### Inside PizzaOps

| Project | Responsibility |
| --- | --- |
| [PizzaOps.Core](src/MCP-Demos04-PizzaOps/src/PizzaOps.Core/README.md) | Pizzeria domain model and application contracts. |
| [PizzaOps.Storage](src/MCP-Demos04-PizzaOps/src/PizzaOps.Storage/README.md) | Persistence using Azure Table Storage or the local Azurite emulator. |
| [PizzaOps.Functions](src/MCP-Demos04-PizzaOps/src/PizzaOps.Functions/README.md) | Azure Functions host that exposes the MCP tools. |
| [PizzaOps.Dashboard](src/MCP-Demos04-PizzaOps/src/PizzaOps.Dashboard/README.md) | Read-only Blazor dashboard for oven status and kitchen events. |
| [PizzaOps.DemoAdmin](src/MCP-Demos04-PizzaOps/src/PizzaOps.DemoAdmin/README.md) | Separate operator CLI for checking or resetting the demo state. |
| [PizzaOps.UnitTests](src/MCP-Demos04-PizzaOps/tests/PizzaOps.UnitTests/) | Tests for the core domain behavior. |
| [PizzaOps.IntegrationTests](src/MCP-Demos04-PizzaOps/tests/PizzaOps.IntegrationTests/) | Tests for storage and the Functions MCP interface. |

## Repository structure

| Folder | Contents |
| --- | --- |
| [`.github/`](.github/) | Copilot agent definitions, repository instructions, and reusable skills. |
| [`docs/`](docs/) | [Demo descriptions](docs/descriptions/) and [implementation plans](docs/plans/). |
| [`infra/`](infra/README.md) | Bicep templates for Microsoft Foundry (Demo 02) and the PizzaOps Azure resources (Demo 04). |
| [`scripts/`](scripts/) | PowerShell automation for Azure Developer CLI (`azd`), including the Demo 02 post-provision hook and checks for Azure setup and packaging. |
| [`src/`](src/) | The four demos, the shared .NET library, and the PizzaOps solution and tests. |

[`.mcp.json`](.mcp.json) configures the local Stage Director MCP server, [`azure.yaml`](azure.yaml) defines Azure provisioning and the deployable PizzaOps services, and [`global.json`](global.json) specifies the .NET SDK. The local `.azure/` directory, when present, stores `azd` environment settings and is not tracked.

## Quick start

[Demo 01](src/MCP-Demos.01-MCPClient/readme.md) is the simplest starting point. With the .NET 10 SDK and internet access, run this command from the repository root:

```powershell
dotnet run --project .\src\MCP-Demos.01-MCPClient\MCP-Demos.01-MCPClient.csproj
```

Once the tool list appears, enter a topic such as `Azure Functions`. No Azure subscription or API key is required.

> [!NOTE]
> The other demos have different prerequisites. Use their linked guides for Foundry configuration, Stage Director setup in VS Code, or running PizzaOps locally with Azurite.