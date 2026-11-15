# Plan: Demo 02 — Chat Console with an LLM and MCP Tools

## Reference and objective

Functional description: `docs/descriptions/demo02.md`.

Build `src/MCP-Demos.02-MCPChat`, a .NET 10 console application for chatting with a Microsoft Foundry model and using tools from the Microsoft Learn MCP server. The AZD infrastructure will reuse the existing root `azure.yaml` and Bicep provider configuration, adding the required provisioning and .NET user secrets initialization.

The plan covers the application, infrastructure, and demo documentation. The conversation must preserve context between turns, end on `/quit`, and display user messages, model responses, MCP tool calls, and their results for educational purposes. Credentials must not be written to logs or displayed in output.

## Task progress

| Task number | Title | Progress |
|---:|---|---|
| 1 | Create the console project | Done |
| 2 | Implement the Foundry chat | Done |
| 3 | Integrate Microsoft Learn MCP tools | Done |
| 4 | Provision Foundry and the model with Bicep/AZD | Done |
| 5 | Configure AZD and .NET user secrets | Done |
| 6 | Document the demo | Done |
| 7 | Validate the build, configuration, and end-to-end flow | Done |

### Validation results

- Passed `dotnet restore` and `dotnet build --no-restore` for `src/MCP-Demos.02-MCPChat/MCP-Demos.02-MCPChat.csproj` using the installed .NET 10 SDK 10.0.404.
- Passed `az bicep build --file ./infra/main.bicep --stdout`.
- Passed all nine offline checks in `pwsh -NoProfile -File ./scripts/azd/tests/Test-Demo02Secrets.ps1`, including execution of the root AZD `postprovision` hook with installed AZD 1.37.3, without provisioning resources.
- With isolated temporary user-secrets profiles, confirmed missing settings produce a safe diagnostic and exit code 1; confirmed `/quit` exits with code 0 after Microsoft Learn MCP initialization and tool discovery. The latter used dummy Foundry settings and did not send a model request.
- Live Foundry connectivity was not tested because no explicitly authorized target Azure environment was identified. No Azure resources were created or changed.

## Tasks and suggested prompts

### 1. Create the console project

**Objective:** Add the .NET 10 project `src/MCP-Demos.02-MCPChat`, configure it with nullable reference types and the dependencies required for `Microsoft.Extensions.AI`, Foundry inference, and the C# MCP client. Reuse `MCP-Demos.Core` utilities where appropriate and follow the repository's C# and MCP instructions.

**Suggested prompt**

> Create `src/MCP-Demos.02-MCPChat`, a C# console project targeting .NET 10. First inspect the existing projects and C# and MCP instructions, then follow relevant conventions and reuse shared utilities. Add only the packages needed for Microsoft.Extensions.AI, Microsoft Foundry model access, and the C# MCP client. Verify that the project restores and builds; do not put credentials or secret values in code.

### 2. Implement the Foundry chat

**Objective:** Create the chat client using an endpoint and API key loaded from .NET user secrets, define a system message, and preserve the conversation history between turns. Read input until `/quit` and display user messages and model responses.

**Suggested prompt**

> Implement the Microsoft Foundry chat in `MCP-Demos.02-MCPChat` using Microsoft.Extensions.AI. Load the endpoint and API key from local configuration through .NET user secrets (never from constants, committed files, or output); if either is missing, clearly explain how to configure it. Define a system message, preserve the full conversation history between turns, handle `/quit`, and display user messages and model responses in the console. Follow the repository's C# guidance and shared utilities, and handle errors explicitly.

### 3. Integrate Microsoft Learn MCP tools

**Objective:** Connect to the Microsoft Learn server using the C# MCP client, make its tools available to the model during the chat loop, and display the complete educational flow, including tool calls and results.

**Suggested prompt**

> Integrate the official Microsoft Learn MCP server into the chat using the C# MCP client and the documented transport for the remote server. Before choosing the endpoint, transport, or APIs, verify the official documentation and examples compatible with the project's package versions. Make the tools discovered from the server available to the model and implement the tool invocation loop required by Microsoft.Extensions.AI while preserving conversation history. Clearly distinguish user messages, model responses, MCP tool calls, and MCP results in the console. Explicitly handle connection, invocation, and response errors; do not fabricate results when an operation fails.

### 4. Provision Foundry and the model with Bicep/AZD

**Objective:** Extend `infra` with Bicep modules for the resource group, Microsoft Foundry resource, project, and `gpt-6-luna` deployment, using the default deployment settings supported in the selected region. Use `AZURE_ENV_NAME`, `AZURE_SUBSCRIPTION_ID`, and `AZURE_LOCATION`, and include the environment name in the resource group name.

**Suggested prompt**

> Extend the Bicep infrastructure already connected to AZD through `azure.yaml` for Demo 02. Provision a resource group, Microsoft Foundry resource, Foundry project, and `gpt-6-luna` model deployment using the default deployment settings supported in the selected region. Use `AZURE_ENV_NAME`, `AZURE_SUBSCRIPTION_ID`, and `AZURE_LOCATION` as standard AZD inputs, and include the environment name in the resource group name. Verify current Bicep schemas and APIs as well as regional model limitations; expose only non-secret outputs needed by subsequent tasks. Do not change existing demo configuration unless necessary.

### 5. Configure AZD and .NET user secrets

**Objective:** Ensure AZD configures the project's .NET user secrets with the Foundry endpoint and API key after provisioning, without exposing the key in logs or normal output.

**Suggested prompt**

> Configure the Demo 02 AZD workflow so that after provisioning it initializes the .NET user secrets for `src/MCP-Demos.02-MCPChat` with the Microsoft Foundry model endpoint and corresponding API key. Reuse the existing Bicep structure and `azure.yaml`, and choose AZD hooks or mechanisms compatible with the installed tool version. Handle secret values without printing them, saving them in committed files, or including them in public outputs; report errors clearly. Document any local prerequisites and verify the configuration without running a real deployment if subscription access or authorization is unavailable.

### 6. Document the demo

**Objective:** Add `src/MCP-Demos.02-MCPChat/readme.md` with a description, project structure, class diagram, and instructions for local setup, execution, and AZD.

**Suggested prompt**

> Write `readme.md` for `MCP-Demos.02-MCPChat` based on the code and infrastructure actually implemented. Describe the chat flow, Microsoft Learn MCP tool integration, project components, and Foundry/AZD infrastructure. Include a Mermaid class diagram, prerequisites, secure .NET user secrets configuration, and commands for provisioning, local execution, and exiting with `/quit`. Do not document unverified behavior or commands.

### 7. Validate the build, configuration, and end-to-end flow

**Objective:** Verify the .NET build, infrastructure and AZD configuration, configuration handling, and interactive behavior. Run live tests against Azure only if credentials and explicit authorization are available, without creating resources unexpectedly.

**Suggested prompt**

> Validate Demo 02 without changing unrelated components: restore and build the .NET 10 project, validate the Bicep templates, and check the AZD configuration. Verify that missing configuration produces a clear error without revealing secrets and that `/quit` ends the chat. If an explicitly authorized Azure environment is available, also verify connectivity to the model and MCP server; do not start provisioning or create resources without authorization. Report the commands run, results, and any verification limitations.

## Dependencies and notes

- Tasks 2 and 3 depend on Task 1; Task 3 integrates with the chat loop implemented in Task 2.
- Task 4 depends on Task 1 only for the project name and path; Task 5 depends on Tasks 1 and 4.
- Task 6 depends on Tasks 1–5 so the documentation reflects the actual implementation.
- Task 7 verifies the results of the preceding tasks.
- The required model is `gpt-6-luna`; availability, region, deployment schema, and default settings must be verified before implementing the infrastructure.
- Azure deployment requires explicit authorization and is not an automatic local validation step.
