# Demo 03 Implementation Plan — Stage Director

## Problem and proposed approach

Implement Demo 03 as a self-contained, local, non-destructive Stage Director. The solution will be a single .NET 10 C# console project and process that hosts the official MCP SDK over stdio and a read-only HTTP server bound to `127.0.0.1`. A browser dashboard will render the shared in-memory state, while Copilot in VS Code invokes three constrained tools: `get_stage_status`, `activate_scene`, and `reset_stage`.

The plan preserves the specification's invariants: four predefined scenes, no device or file access, no mutable HTTP endpoints, no ASP.NET Core, no cloud or database dependencies, no Node.js runtime, and no additional application or test project. The implementation target is `src/MCP-Demos.03-MCPServer/`, which does not currently exist.

## Task summary

| # | Task | Status |
|---:|---|---|
| 1 | Scaffold the .NET 10 console project and MCP stdio host | ToDo |
| 2 | Implement the scene domain and thread-safe in-memory service | ToDo |
| 3 | Expose the three constrained MCP tools | ToDo |
| 4 | Add the same-process, read-only localhost HTTP server | ToDo |
| 5 | Build the accessible Stage Director dashboard | ToDo |
| 6 | Configure VS Code MCP integration and document the demo | ToDo |
| 7 | Verify acceptance criteria and live-demo behavior | ToDo |

## Tasks and implementation prompts

### Task 1 — Scaffold the .NET 10 console project and MCP stdio host

**Dependencies:** None  
**Status:** ToDo  
**Target files:** `src/MCP-Demos.03-MCPServer/StageDirector.csproj`, `Program.cs`

**Prompt:**

> Create the single C# Console Application for Demo 03 under `src/MCP-Demos.03-MCPServer/`. Target `net10.0`, enable nullable reference types, implicit usings, analyzers, and meaningful warnings. Pin the official `ModelContextProtocol` SDK package version after checking its current documentation and compatibility. Bootstrap the MCP server with stdio transport and assembly tool discovery, using the SDK's supported hosting APIs. Configure all diagnostics/logging to stderr; stdout must remain exclusively for MCP protocol messages. Keep this as the only `.csproj` and application assembly for Demo 03. Do not add ASP.NET Core, a second web project, cloud services, databases, or a Node.js runtime. Establish clean cancellation/shutdown handling and leave clear integration points for one shared `StageService` and the local HTTP server. Build the project and resolve any SDK API differences using the pinned version's documentation.

### Task 2 — Implement the scene domain and thread-safe in-memory service

**Dependencies:** Task 1  
**Status:** ToDo  
**Target files:** `Domain/StageScene.cs`, `Domain/StageState.cs`, `Domain/StageSceneCatalog.cs`, `Services/StageService.cs`

**Prompt:**

> Implement the Stage Director domain and application service. Define only the canonical scenes `welcome`, `focus`, `energy`, and `celebration`, with the exact predefined titles, subtitles, palettes, intensity, effects, and celebration duration from `docs/descriptions/demo03.md`. Model snapshots as immutable values with scene, monotonically increasing revision, presentation fields, UTC activation timestamp, effect duration, and available scene identifiers. Initialize to `welcome` at revision 1. Implement a single thread-safe `StageService` instance that returns consistent snapshots and atomically activates or resets scenes. Increment revision and update the timestamp only for effective changes; repeated activation of the current scene or repeated reset must return `changed: false` without replaying effects. Validate identifiers server-side and never accept arbitrary display content or external input for preset values. Add no persistence, filesystem access, device control, or external dependencies.

### Task 3 — Expose the three constrained MCP tools

**Dependencies:** Task 2  
**Status:** ToDo  
**Target files:** `Mcp/StageTools.cs`, `Program.cs`

**Prompt:**

> Add the MCP tool adapter for the shared `StageService`, exposing exactly `get_stage_status`, `activate_scene`, and `reset_stage` with stable snake_case names. Follow the pinned official MCP C# SDK APIs and project instructions. Provide the exact purpose-oriented descriptions and the strict parameter contracts from `docs/descriptions/demo03.md`: status and reset take no parameters; activation requires one `scene` string constrained to the four canonical identifiers, with no additional properties. Return JSON-serializable snapshots and activation/reset results including success, changed, previous/current scene, revision, and stage. Invalid scenes must produce an MCP tool error (not a success-shaped response), identify the allowed values without leaking stack traces or host details, and leave state unchanged. Keep tool methods thin, use the injected singleton service, and ensure cancellation is supported where applicable. Do not add generic execution, filesystem, network, or hardware tools.

### Task 4 — Add the same-process, read-only localhost HTTP server

**Dependencies:** Tasks 1 and 2  
**Status:** ToDo  
**Target files:** `Http/LocalStageHttpServer.cs`, `Program.cs`, `StageDirector.csproj`

**Prompt:**

> Implement the minimal HTTP server in the same Stage Director process using `System.Net.HttpListener` (or another standard .NET API only if documented compatibility requires it), without ASP.NET Core or the MCP HTTP transport. Bind exclusively to `http://127.0.0.1:{port}/`; default to port 5187 and validate `STAGE_DIRECTOR_PORT` as an integer from 1024 through 65535 before startup. Serve only the explicit GET allowlist `/`, `/styles.css`, `/stage.js`, and `/api/stage`; return 404/405 otherwise and never expose a state-changing route, arbitrary file serving, directory listing, or CORS. Embed the three static assets in this same project. `/api/stage` must serialize the same immutable snapshot returned by `get_stage_status`, with `Cache-Control: no-store`; set `X-Content-Type-Options: nosniff` and a restrictive CSP compatible with the local assets. Bound request handling with timeouts and isolate client disconnects so they cannot stop MCP. On port/listener startup failure, report a clear diagnostic to stderr and fail startup rather than silently running a partial demo. Wire listener startup and cancellation into `Program.cs`, share the single `StageService`, and release the port on process shutdown.

### Task 5 — Build the accessible Stage Director dashboard

**Dependencies:** Task 4  
**Status:** ToDo  
**Target files:** `Web/index.html`, `Web/styles.css`, `Web/stage.js`

**Prompt:**

> Create the self-contained Stage Director presentation page using only local HTML, CSS, and JavaScript included in the C# project. Design a responsive 16:9-projection-friendly virtual stage with a generic `STAGE DIRECTOR` heading, large scene title/subtitle, scene indicator, and connection status; include no controls for changing scenes, logos, remote assets, CDN calls, audio, or inline script. Poll same-origin `GET /api/stage` every 500 ms with `cache: 'no-store'`, never overlap requests, and render only when `revision` changes. Implement the four catalog-driven visual treatments, including approximately four seconds of celebration confetti once per new revision, without rapid strobing. Respect `prefers-reduced-motion`. On fetch/JSON failure, show `DISCONNECTED` / `Stage Director unreachable`, keep the last rendered scene visibly stale, do not replay effects, and avoid unhandled exceptions; recover by synchronizing the next valid snapshot. Never use untrusted values as HTML.

### Task 6 — Configure VS Code MCP integration and document the demo

**Dependencies:** Tasks 1, 3, 4, and 5  
**Status:** ToDo  
**Target files:** `src/MCP-Demos.03-MCPServer/.vscode/mcp.json`, `src/MCP-Demos.03-MCPServer/README.md`

**Prompt:**

> Add the reference GitHub Copilot in VS Code setup and a concise presenter-oriented README for Demo 03. Before writing the MCP configuration, check current official VS Code documentation and select one configuration schema supported by the intended workspace workflow; do not combine `servers` and `mcpServers` formats. Provide a clear local stdio configuration for the already-built .NET 10 executable/DLL without relying on `dotnet run`, and explain how to build, connect MCP first, then open `http://127.0.0.1:5187/`, configure an optional valid port, and stop the process cleanly. Document the three tools and four scenes, the MCP/stdio versus browser/HTTP boundary, safe limitations, troubleshooting (including port conflict and disconnected dashboard), and brief natural-language demo prompts. Keep the README factual and aligned with the implementation; do not imply cloud connectivity, hardware control, or persistence.

### Task 7 — Verify acceptance criteria and live-demo behavior

**Dependencies:** Tasks 3, 4, 5, and 6  
**Status:** ToDo  
**Target files:** Demo 03 project and its verification documentation/scripts only if needed

**Prompt:**

> Verify Demo 03 against `docs/descriptions/demo03.md` acceptance criteria without adding a second Demo 03 `.csproj`, application process, or runtime dependency. Build the single project, then exercise the server using a compliant stdio MCP client and local HTTP requests. Check exact discovery and invocation of all three tools; initial state; matching MCP and `/api/stage` snapshots; each scene transition; invalid-scene rejection without state mutation; repeated activation/reset idempotency; monotonically increasing revisions under rapid calls; the HTTP GET allowlist and absence of write methods; loopback-only binding; cache/security headers; disconnected/stale UI and single-shot celebration effect; stdout purity; invalid/occupied port diagnostics; and clean listener shutdown/port release. Run the opening, change-of-pace, and finale narratives. Fix any failures and record the actual build/smoke-test commands and any remaining environment-dependent limitation in the README.

## Dependency order

- Task 1 is the foundation.
- Task 2 depends on Task 1.
- Tasks 3 and 4 both depend on Task 2 and can proceed independently after it.
- Task 5 depends on Task 4's HTTP contract.
- Task 6 integrates the completed server and dashboard for the presenter.
- Task 7 validates the integrated result.

## Notes and constraints

- Keep all scene state in memory and make the browser interface read-only.
- Prefer embedded static assets so a published single assembly remains self-contained.
- Keep stdout free of all non-MCP output; diagnostics belong on stderr.
- Use the repository's C# and MCP server instructions and verify APIs against the selected SDK version.
- Do not create a plan or test project inside the repository that would violate the single-project constraint.
