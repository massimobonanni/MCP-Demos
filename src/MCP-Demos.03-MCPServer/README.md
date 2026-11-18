# Demo 03 — Stage Director

Stage Director is a local .NET 10 MCP demo. Copilot selects a predefined virtual-stage scene through MCP; a browser dashboard displays the in-memory state.

## Run the demo

Prerequisite: the .NET 10 SDK, VS Code with GitHub Copilot, and a local checkout of this repository.

1. From the repository root, build the server:

   ```powershell
   dotnet build .\src\MCP-Demos.03-MCPServer\StageDirector.csproj -c Release
   ```

2. The repository-root [`.mcp.json`](../../.mcp.json) configures the already-built `StageDirector.dll` over stdio; it does not use `dotnet run`. VS Code's current Copilot workspace configuration uses the portable `mcpServers` schema in `.mcp.json`, not the legacy `.vscode/mcp.json` `servers` schema. Enable the experimental `chat.mcp.workspaceRootConfig.enabled` setting if needed, open this repository as the workspace, select the Copilot session target, and connect/start **stage-director** from **Chat: Open Customizations > MCP Servers**. Review and approve starting this local server. Confirm Copilot can call `get_stage_status`.
3. **After MCP is connected**, open <http://127.0.0.1:5187/> in a browser. Keep the VS Code-managed server running while presenting.
4. When finished, use the server's **Stop** action in VS Code (or close VS Code) to shut down the process and release the port.

The server defaults to port `5187`. To use another available port, replace the `stage-director` entry in `.mcp.json` with this entry, changing the example port if needed:

```json
"stage-director": {
  "type": "stdio",
  "command": "dotnet",
  "args": [
    "src/MCP-Demos.03-MCPServer/bin/Release/net10.0/StageDirector.dll"
  ],
  "env": {
    "STAGE_DIRECTOR_PORT": "5188"
  }
}
```

Use a decimal port from `1024` through `65535`, then reconnect/restart the MCP server and open `http://127.0.0.1:5188/`. Keep the dashboard URL and configured port in sync.

## Presenter notes

| MCP tool | What it does |
|---|---|
| `get_stage_status` | Reads the active scene, its visual settings, revision, and available scene IDs. |
| `activate_scene` | Selects one of the four predefined scenes: `welcome`, `focus`, `energy`, or `celebration`. |
| `reset_stage` | Returns to `welcome`; repeating an unchanged activation or reset does not create a new revision. |

| Scene | On-screen treatment |
|---|---|
| `welcome` | Blue, softly animated welcome screen. |
| `focus` | Minimal dark screen with subdued motion. |
| `energy` | Purple/orange screen with moderate pulsing. |
| `celebration` | Thank-you screen with a brief, approximately four-second confetti effect. |

**Two separate channels:** Copilot starts the C# process and exchanges MCP messages over stdin/stdout (stdio); diagnostics go to stderr. The browser talks to that same process over loopback HTTP, fetching the page assets and read-only `GET /api/stage` snapshot. The dashboard does not invoke MCP or provide scene-changing controls. The model chooses a tool, the server validates and updates state, and the browser reflects the result.

**Safe demo boundaries:** the server binds only to `127.0.0.1`. Tools accept only the fixed scene IDs; HTTP serves a fixed read-only allowlist. There is no cloud connection, physical lighting or hardware control, arbitrary file or command access, database, or persistence. State exists only in process memory and returns to `welcome` on restart.

### Natural-language prompts

- “Check the current Stage Director status.”
- “Set the stage to focus for a technical talk with fewer distractions.”
- “Bring up the energy for the audience.”
- “Switch to celebration for the finale.”
- “Reset the stage to welcome.”

### Troubleshooting

- **MCP config/server is missing:** open the repository root as the VS Code workspace and enable `chat.mcp.workspaceRootConfig.enabled` if available. Check the Copilot session target and the MCP server output in VS Code.
- **Server fails to start:** build the `Release` target first, confirm the .NET 10 runtime is installed, and check that the configured DLL path matches the workspace-root-relative path in `.mcp.json`.
- **Port conflict:** startup fails with a diagnostic if another process (including another Stage Director instance) owns the port. Set `STAGE_DIRECTOR_PORT` to a free valid port, restart the server, and use that port in the browser URL.
- **Dashboard says `DISCONNECTED` / “Stage Director unreachable”:** connect or restart the MCP server first, check that its selected port matches the URL, then reload the page. A stopped server has no dashboard listener.

For current setup details, see the official [VS Code MCP configuration reference](https://code.visualstudio.com/docs/agents/reference/mcp-configuration) and [Add MCP servers in VS Code](https://code.visualstudio.com/docs/agent-customization/mcp-servers).
