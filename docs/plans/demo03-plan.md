# PizzaOps phased implementation plan

## 1. Purpose and definition of success

Build a reliable, conference-ready C#/.NET 10 demonstration of GitHub Copilot in VS Code controlling a simulated pizzeria through a real Azure Functions MCP server. A separate, read-only Blazor Web App on App Service displays the same authoritative Azure Table Storage state.

The release must demonstrate this complete story:

1. An authenticated operator reset produces exactly three ovens: Oven 01 at 320 C, Oven 02 at 450 C / CRITICAL, and Oven 03 at 300 C.
2. Copilot discovers exactly `get_ovens_status`, `get_pizza_recipe`, `set_oven_temperature`, and `bake_test_pizza`.
3. Copilot diagnoses Oven 02, reads the Margherita recipe, and changes Oven 02 to 320 C / HEALTHY.
4. A Margherita test bake returns PASS and produces a visible event.
5. An authenticated 600 C attempt reaches C# domain validation, returns `TEMPERATURE_OUT_OF_RANGE`, produces a rejection event when event storage is healthy, and leaves the oven entity unchanged.
6. The audience can follow the actions on the dashboard, while the presenter explains that MCP supplies discovery/invocation, Copilot requests actions, and the server enforces policy.
7. Reset and repeat the story without rebuilding or editing data in the portal.

This is an implementation plan, not evidence that any application has been built, tested, or deployed. All implementation tasks initially have status **ToDo**. No repository files or Azure resources were changed during planning.

## 2. Inputs, repository baseline, and agreed decisions

### Repository baseline

The functional source is [demo03.md](C:/Sviluppo/MCP-Demos/docs/descriptions/demo03.md). It is currently an untracked, user-provided document; preserve it and do not overwrite it as part of implementation.

The repository already contains:

- A root [azure.yaml](C:/Sviluppo/MCP-Demos/azure.yaml) using Bicep from `infra`, with no deployable service entries yet.
- A subscription-scoped [main.bicep](C:/Sviluppo/MCP-Demos/infra/main.bicep) creating `rg-mcp-demos-<environment>` and deploying Demo 02 Foundry resources through [foundry.bicep](C:/Sviluppo/MCP-Demos/infra/modules/foundry.bicep).
- A [main.bicepparam](C:/Sviluppo/MCP-Demos/infra/main.bicepparam) reading AZD environment variables, an existing [abbreviations.json](C:/Sviluppo/MCP-Demos/infra/abbreviations.json), and [infrastructure documentation](C:/Sviluppo/MCP-Demos/infra/README.md).
- A fail-fast Demo 02 postprovision hook, [initialize-demo02-secrets.ps1](C:/Sviluppo/MCP-Demos/scripts/azd/initialize-demo02-secrets.ps1), and its [offline regression tests](C:/Sviluppo/MCP-Demos/scripts/azd/tests/Test-Demo02Secrets.ps1).
- Existing .NET 10 demo projects, but no discovered solution-wide .NET test setup, central package management, SDK pin, or GitHub Actions workflow.

Do not move, upgrade, or otherwise refactor the existing demos. In particular, the existing [MCP-Demos.Core.csproj](C:/Sviluppo/MCP-Demos/src/MCP-Demos.Core/MCP-Demos.Core.csproj) references an MCP SDK; the new PizzaOps domain must remain independent rather than inherit that dependency.

### Confirmed scope and behavior

| Topic | Decision |
| --- | --- |
| Hosting | Functions v4, .NET 10 isolated worker, official Functions MCP extension, Linux Flex Consumption; Blazor Web App on Linux App Service. No classic Linux Consumption hosting. |
| Azure integration | Extend the existing root Bicep/AZD project and shared resource group. Preserve Demo 02 resource identities, module behavior, outputs, and secret hook. |
| MVP resources | Add one Function App and its plan, one Web App and its plan, one shared storage account, two application tables, one Application Insights resource, and one Log Analytics workspace. Host/deployment blobs are platform necessities, not new application services. |
| Existing Foundry | Preserve Demo 02 Foundry resources. PizzaOps neither consumes them nor adds another model deployment. Copilot is the only agent. |
| Storage identity | Azure managed identities; Functions can write application tables, dashboard can only read. Operator reset has separate, explicit data-plane authorization. |
| MCP authentication | The Functions MCP extension system key, sent in `x-functions-key`. This authenticates possession of a shared secret, not a named human or a per-user role. |
| Native Copilot secret delivery | A process-scoped environment variable, securely supplied to a dedicated presenter VS Code instance. No literal key in configuration, command history, process arguments, logs, or source control. |
| Event consistency | Keep separate oven/event tables. Oven state is authoritative; events are best-effort. An event failure after an oven commit yields an explicit warning and telemetry, not a false rollback claim or a retry of the mutation. |
| Reset | A fixed kitchen partition, with tool calls paused and in-flight calls drained. Clear only this kitchen's events, restore the three known ovens, write RESET, and verify before resuming. No generation-switching system or public reset API. |
| Tests | Include focused unit, integration, contract, and end-to-end tests. The explicit user request takes precedence over the specification's contradictory "No tests for the first version" sentence. |
| Optional features | No extra MCP tools, Resources, Prompts, public editor/admin UI, real devices, asynchronous baking, outbox, OAuth, gateway, or additional Azure data/event services in the MVP. |

### Compatibility facts and open gates

The current official references confirm the Functions extension package, isolated-worker model, `McpToolTrigger`/`McpToolProperty`, explicit required-property metadata, Streamable HTTP endpoint `/runtime/webhooks/mcp`, and the `mcp_extension` system key.

Important differences from older examples:

- Current native Copilot configuration uses `mcpServers` in its native user configuration or workspace-root `.mcp.json`. The older editor-managed `servers` plus `${input:...}` format is not interchangeable with it.
- Native HTTP headers support `${VARIABLE_NAME}` references; the current harness does not bridge VS Code secret-input prompts or SecretStorage into those headers.
- MCP 2026-07-28 changes protocol lifecycle behavior. The user accepts an older Functions protocol revision for now; verify and record the actual version used by the pinned extension, compliant test client, Inspector, and presenter Copilot harness. Do not invent handshake compatibility or implement JSON-RPC yourself.
- Flex Consumption runtime/deployment configuration differs from classic Functions hosting. Derive settings from current Flex guidance and schemas, not a copied classic-plan app-settings list.

Exact SDK, Functions build SDK/worker, MCP extension, Core Tools, Inspector, test client, and Copilot versions remain implementation checkpoints. Prefer stable releases; document any unavoidable preview dependency and stop for approval if it changes the agreed architecture. The older MCP revision is an explicitly accepted temporary deviation, not a silent downgrade.

## 3. Architecture and implementation boundaries

### Planned layout

Use `src\MCP-Demos03-PizzaOps` as specified, without renaming sibling demos. Normalize the inconsistent dashboard names in the description to **PizzaOps.Dashboard** and place the admin utility alongside the other PizzaOps projects, not in a second `tools` tree.

```text
src\MCP-Demos03-PizzaOps\
  PizzaOps.slnx
  global.json
  Directory.Build.props
  Directory.Packages.props
  .editorconfig
  README.md
  PizzaOps.Core\
    Domain\
    Application\
    Contracts\
  PizzaOps.Storage\
  PizzaOps.Functions\
  PizzaOps.Dashboard\
  PizzaOps.DemoAdmin\
  scripts\
    Start-Local.ps1
    Test-Local.ps1
    Start-Presenter.ps1

infra\
  main.bicep                         extend
  main.bicepparam                    extend
  abbreviations.json                reuse
  modules\pizzaops.bicep             new cohesive resource-group module
  README.md                         extend

azure.yaml                          add exactly two deployable services
```

These are proposed paths, not files that already exist. Keep SDK/package/analyzer changes scoped to PizzaOps; a root central-package file would inadvertently change existing projects.

### Dependencies and responsibilities

- **Core:** domain models, immutable recipes, pure safety/bake rules, application result contracts, read/write ports, application orchestration. No Azure SDK types, Functions attributes, MCP objects, Blazor components, or static mutable kitchen state.
- **Storage:** `Azure.Data.Tables`, credential/configuration handling, mapping, bounded queries, conditional writes, event appends. Map Azure ETags to an opaque version token at the Core boundary. Never decide safe temperatures here.
- **Functions:** composition root plus four thin official MCP-trigger functions. All tools call the application service. Do not introduce an ASP.NET MCP server, stdio process, generic proxy, or custom JSON-RPC implementation.
- **Dashboard:** composition root plus a read-only kitchen query path, conventional GET API, and Blazor component. Register only the read capability it needs; use Table Reader RBAC as the actual write barrier. Share backend health/snapshot projection rather than duplicating rules in Razor.
- **DemoAdmin:** separately authorized C# reset/status commands and verification. No MCP exposure and no dashboard dependency.

Use built-in DI, nullable reference types, asynchronous I/O, cancellation, typed validated options, `TimeProvider` where time affects results, and structured logging. Add only small, purpose-specific interfaces; no generic repository framework, CQRS bus, or extra service layer without a concrete need.

### Domain and response invariants

- Commanded temperature is an integer in **200-350 inclusive**. Persisted malfunction data such as 450 is representable; command validation must not prevent reading or resetting it.
- Health is derived in Core: inside the operating range is HEALTHY; outside is CRITICAL. Missing, malformed, or incomplete kitchen data is an operational failure, not a fabricated healthy kitchen.
- Recipes are immutable: Margherita 320 +/-15, Marinara 300 +/-15, Diavola 310 +/-15. Normalize recipe input by trimming and invariant case; reject unsupported names. Trim and canonicalize oven IDs, then require an existing known oven.
- Missing, null, fractional, overflow, wrong-type, or extra mutation fields are rejected; unknown ovens are never created. Optional `reason` is trimmed, limited to 120 characters, and never rendered as HTML. Overlength input returns `INVALID_ARGUMENT`.
- Tool content has a stable snake_case envelope: `success`, `code`, `message`, `data`, `correlation_id`; UTC timestamps are ISO 8601. Verify whether the extension returns structured content or serialized JSON text and test that exact supported shape.
- Business rejection is `success: false`. A completed BURNT/UNDERCOOKED test is `success: true`, `code: TEST_COMPLETED`, with the bake result in `data`. Transport/host failures remain distinguishable from both.
- Add a small optional `warnings` field and an `event_recorded` indicator on event-producing outcomes. Preserve the primary result if only event append fails. Do not make an already-successful state change look safe to retry.
- `NO_CHANGE` preserves the existing oven entity/version and creates no duplicate temperature-change event. Repeated test bakes can create separate test events.

### Persistence, concurrency, and reset

- One account, application tables `PizzaOpsOvens` and `PizzaOpsEvents`, fixed configured partition `main-kitchen` in each environment's own storage account.
- Oven row keys are `oven-01`, `oven-02`, `oven-03`; store temperature, label, UTC update time, and last action. Derive health rather than persisting it.
- Event row keys use fixed-width reverse UTC ticks plus a collision-resistant suffix so Table Storage's ascending key order yields newest-first records. For equal times, use the row key as a deterministic tie-breaker.
- Filter by partition, enumerate no more than the newest 20 records, and verify that pagination does not turn `maxPerPage` into an accidental unbounded scan.
- Use the observed ETag for each normal mutation; no wildcard/unconditional upsert on the tool path. A 412 produces `CONCURRENT_UPDATE`, with no silent lost update and no optimistic success event.
- Bound storage calls and transient-read retries. Do not blindly replay writes. A transport failure after a write may have an unknown outcome: surface `STORAGE_OUTCOME_UNKNOWN`, require a status refresh, and never assert `state_changed: false` without evidence.
- Reset is an explicit operator maintenance window, not atomic with live tools. Fail closed on errors and keep the demo out of service until verification succeeds. Re-running reset must converge after partial failure.
- Reset removes only rows belonging to this PizzaOps kitchen, restores exactly the canonical oven set, and leaves exactly one current RESET event. Never delete a storage account, whole shared table, or another partition. Event retention for this short-lived MVP is reset-driven; the operator resets between runs rather than adding a cleanup service.
- A snapshot is a best-effort read across two tables, not a cross-table transaction. Surface event-feed degradation separately from authoritative oven data; never synthesize a missing event or a PASS.

### Dashboard and operational behavior

- Read-only `GET /api/kitchen/state`, `GET /health/live`, and bounded `GET /health/ready`. No mutation endpoints. Return `Cache-Control: no-store`.
- Interactive Server Blazor refreshes through the same read service as the API, not through loopback HTTP or MCP. ASP.NET Core's built-in Blazor circuit transport is sufficient; do not provision Azure SignalR.
- Poll every one second without overlapping requests. Stop/cancel on disposal and pause when hidden; refresh immediately on return. A minimal local visibility interop helper is acceptable if necessary, with no external JavaScript library or business logic.
- First load has a loading/error state, not sample oven data. Later failures retain the last successful snapshot with a prominent STALE / CONNECTION LOST indicator. Event-only failure visibly marks the feed degraded.
- Stage acceptance target, not a delivery estimate: a committed operation becomes visible within three polling intervals on the intended stage setup. Record observed latency rather than assuming a poll setting proves it.
- Large 16:9-friendly oven cards, textual health labels, latest 20 events, freshness and connection indicators, safe summaries, reduced-motion support, no remote fonts/images/CDNs.
- Application Insights correlation spans tool invocation, service operation, storage, and emitted event; use separate cloud role names for Functions and Dashboard. Event rows carry the originating correlation ID; subsequent dashboard polls have their own request traces.
- Logs must distinguish rejected policy, concurrency conflict, storage outage, unknown write outcome, and event-append failure. Never log keys, authorization headers, unbounded arguments, or raw user prompts. Sample/filter noisy polling telemetry without hiding demo failures.

## 4. Phases, tasks, and status tracking

The table below is the manual implementation tracker. Use only **ToDo**, **Done**, or **Blocked**. Mark Done only after the task's acceptance evidence exists. Record the reason and missing prerequisite when Blocked. Planning/research performed for this document does not mark implementation tasks Done.

| Task | Title | Status |
| --- | --- | --- |
| 01 | Establish the .NET 10 solution and validation baseline | Done |
| 02 | Implement domain rules and stable application contracts | Done |
| 03 | Implement Table Storage and shared read projection | Done |
| 04 | Implement authenticated deterministic reset and status | Done |
| 05 | Prove real MCP discovery and the two read tools | Blocked |
| 06 | Deliver the ETag-protected temperature repair and rejection | Done |
| 07 | Deliver the deterministic test-bake tool | Done |
| 08 | Expose the read-only dashboard API and health checks | Done |
| 09 | Build the live, accessible Blazor dashboard | ToDo |
| 10 | Complete telemetry and local reliability verification | ToDo |
| 11 | Extend Bicep with minimal PizzaOps resources and RBAC | ToDo |
| 12 | Integrate AZD packaging and reproducible deployment automation | ToDo |
| 13 | Verify the Azure deployment and presenter Copilot profile | ToDo |
| 14 | Produce and rehearse the ten-minute presenter runbook | ToDo |

Task 05 remains blocked: local legacy-protocol tests succeeded with the C#
client, Inspector and Copilot CLI, and the user accepted the older revision
for now. The intended VS Code presenter configuration has not been verified; see
[`compatibility.md`](../../src/MCP-Demos03-PizzaOps/docs/compatibility.md).

### Phase map and gates

| Phase | Tasks | Exit gate |
| --- | --- | --- |
| A - Foundation and deterministic behavior | 01-02 | Scoped .NET 10 build, version choices, domain tests and contracts established. |
| B - Real shared state | 03-04 | Azurite-backed reads/ETags and repeatable reset work; no runtime mock state. |
| C - MCP vertical slices | 05-07 | Genuine tool discovery and all four tools work against persisted state. Stop on host/schema compatibility failures before UI polish or deployment. |
| D - Audience view | 08-09 | API and dashboard read the same state and report failures honestly. |
| E - Local reliability | 10 | Full story, failure cases, telemetry, and repeatability pass locally. |
| F - Azure reproducibility | 11-13 | IaC and packaging validated, authorized deployment verified, real MI/RBAC and Copilot evidence captured. |
| G - Conference readiness | 14 | Presenter completes repeat rehearsals, reset, and fallback with no undocumented changes. |

Recommended execution is the numbered order. True dependencies are listed below: for example Task 08 can follow Task 03 independently of the write tools, and Task 11 can begin after Task 05 proves the chosen Functions integration. Shared root infrastructure/workflow edits should be serialized.

### How to use the implementation prompts

Attach this plan and `docs\descriptions\demo03.md` to each implementation conversation, then copy the relevant prompt. Common requirements for every prompt:

- Implement only that task and its tightly coupled documentation/tests. Read existing work and preserve other demos and user edits.
- Do not add services, tools, architectural patterns, or implicit cloud actions beyond this plan.
- Check dependencies before editing. If genuinely blocked, record Blocked and the reason rather than inventing a passing fallback.
- Run the smallest checks covering the change. Report commands and evidence, then update this plan's tracker to Done only when its acceptance gate passes.
- For cloud-changing tasks, first confirm the environment, subscription, tenant, region, exact scope, and cost-bearing choices. Never reveal secrets while requesting approval.

### Task 01 - Establish the .NET 10 solution and validation baseline

**MVP critical:** Yes. **Dependencies:** None.

**Deliverables and locations:** Scoped solution, five production projects, two focused test projects, scoped build/package configuration, initial README and compatibility record under `src\MCP-Demos03-PizzaOps`.

**Work:**

- Scaffold with supported .NET/Functions tools; use the current official Functions project SDK/worker template compatible with .NET 10 and the MCP extension. Pin an actually available compatible SDK/package set, not versions guessed from old tutorials.
- Keep Core independent, project references acyclic, and Dashboard/Admin independent of Functions.
- Scope central package management and analyzers to the demo. Pick one mainstream .NET test framework, record its runner/platform and exact commands, and use it consistently.

**Acceptance:**

- All production projects target `net10.0` and build; the dependency graph contains no transport/storage dependency in Core.
- Builds from the scoped directory actually honor its SDK pin, including when packaging is later invoked from root AZD.
- The existing demos keep their package resolution and project files unchanged.
- Documentation states prerequisites, scoped build commands, and that no cloud operation is automatic.

**Implementation prompt:**

> Implement Task 01 of the attached PizzaOps plan. Scaffold the scoped .NET 10 solution at src\MCP-Demos03-PizzaOps with Core, Storage, Functions, Dashboard, DemoAdmin, and the two test projects. Verify the current official Functions isolated-worker/MCP template and pin compatible SDK/package versions. Keep package management, analyzers, and SDK selection scoped so the existing demos are unaffected. Add the initial no-cloud CI validation workflow and compatibility/prerequisite documentation. Build the projects, verify dependency boundaries and SDK selection, and stop before implementing the kitchen behavior. When you complete the task, update the tasks table in the plan.

### Task 02 - Implement domain rules and stable application contracts

**MVP critical:** Yes. **Dependencies:** 01.

**Deliverables and locations:** `PizzaOps.Core\Domain`, `Application`, `Contracts`.

**Work:**

- Implement oven/recipe/event models, pure health/temperature/bake rules, canonical recipe catalog, result codes, and small persistence/read-service interfaces.
- Distinguish loaded malfunction state from valid commands; define opaque version tokens without leaking Azure ETag types.
- Fix normalization, argument validation, reason-length behavior, error envelopes, event warnings, and bake execution/result semantics.
- Keep policy values in one place rather than mutable deployment settings that can drift between apps. Inject time only where deterministic testing requires it.

**Acceptance:**

- No Core dependency on Azure, MCP, Functions, or Blazor exists.

**Implementation prompt:**

> Implement Task 02 of the PizzaOps plan, limited to Core and its unit tests. Create the deterministic oven policies, immutable three-recipe catalog, result/contracts and minimal storage/read interfaces. Permit incident state at 450 C while enforcing commanded integers from 200 through 350, and keep bake results distinct from execution failures. Specify normalization, unknown IDs, reason validation, event-warning fields and opaque concurrency tokens. Test the meaningful range/tolerance boundaries and invalid inputs. Do not add persistence, HTTP endpoints, tools, or configurable safety limits. When you complete the task, update the tasks table in the plan.

### Task 03 - Implement Table Storage and shared read projection

**MVP critical:** Yes. **Dependencies:** 02.

**Deliverables and locations:** `PizzaOps.Storage`; shared application read projection in Core; documented local endpoint/options.

**Work:**

- Add typed Table clients, mappings, production managed-identity authentication and explicit Azurite-only local configuration.
- Implement partition-scoped read, conditional oven update, event append/query, and narrow operator maintenance primitives. Validate startup configuration and avoid `CreateIfNotExists` on the dashboard path.
- Implement the common server-side health/snapshot projection and deterministic ordering; report missing/corrupt/incomplete kitchen data.
- Carry ETags through the persistence boundary, map 412 conflicts, bound retries, and preserve ambiguous-write semantics.
- Implement newest-first event keys and bounded latest-20 query; never scan all historical rows to obtain recent events.

**Acceptance:**

- Connection strings cannot silently override Azure managed identity; no application account key is needed in cloud configuration.

**Implementation prompt:**

> Implement Task 03 of the PizzaOps plan using Azure.Data.Tables and Azure.Identity. Build the purpose-specific persistence adapter, opaque ETag mapping, partition-scoped latest-20 event query, and shared backend read projection. Use managed identity in Azure and explicitly configured Azurite locally, with validated options and no runtime mock fallback. Add real emulator tests for conditional-update conflicts, pagination/order, partition isolation, corrupt/missing state and failure mapping. Keep domain policy in Core and do not create tables from the dashboard's reader path. When you complete the task, update the tasks table in the plan.

### Task 04 - Implement authenticated deterministic reset and status

**MVP critical:** Yes. **Dependencies:** 03.

**Deliverables and locations:** `PizzaOps.DemoAdmin`; reset/status instructions in the demo README.

**Work:**

- Implement C# `reset --environment <name>` and `status --environment <name>`, resolving an explicit endpoint, table names, and kitchen partition from validated configuration.
- For Azure, require an authorized operator identity; for local development, require an explicit emulator profile. Confirm nonsecret target scope and never silently select a different environment.
- Require quiescence acknowledgement for reset. Clear only the configured kitchen's old events, restore exactly three canonical ovens (including 450), and append one RESET event after successful restoration.
- Verify ovens and event feed before reporting readiness. A repeat run repairs partial setup; no automatic reset on application startup or normal deployment.

**Acceptance:**

- Reset twice yields the same domain state, exactly three ovens, and one RESET event each time. UTC timestamps and ETags may change.
- Failure midway is nonzero and visibly not ready; rerun converges. Other partitions/tables are unchanged.
- A read-only identity cannot reset in Azure; this is a later live RBAC check, not something Azurite can prove.
- Status is read-only. Output contains readiness and dashboard URL when configured, never credentials.

**Implementation prompt:**

> Implement Task 04 of the PizzaOps plan in PizzaOps.DemoAdmin. Add explicit-environment reset and status commands, separate authorized Azure/operator and local-emulator configuration, scope confirmation, and reset quiescence acknowledgement. Restore only this kitchen to ovens 320/450/300, remove its old events, and verify one RESET event before returning success. Make partial failures explicit and repeat reset convergent. Test repeatability, failure recovery and partition isolation. Do not expose reset as MCP, HTTP, a dashboard control, or an automatic deploy/startup action. When you complete the task, update the tasks table in the plan.

### Task 05 - Prove real MCP discovery and the two read tools

**MVP critical:** Yes. **Dependencies:** 03, 04.

**Deliverables and locations:** `PizzaOps.Functions\Program.cs`, `host.json`, read-tool triggers; compatibility evidence.

**Work:**

- Register the real application/storage services through DI and implement `get_ovens_status` and `get_pizza_recipe` using official triggers.
- Explicitly define snake_case argument names, required/optional metadata, stable descriptions, and the empty status input.
- Use the supported extension response mapping; prove real discovery and invocation through an official/compliant MCP client and Inspector, not direct C# calls alone.
- Keep deployed webhook authorization at System. Local development behavior must not be mistaken for proof of Azure authentication.

**Acceptance:**

- The real endpoint advertises only the two implemented PizzaOps read tools at this intermediate slice; no template hello-world, reset, admin, or generic tools remain.
- Status reads the seeded storage state; recipe returns exact target/tolerance values. Missing recipe/type errors are observable through the actual wire format.
- Generated schemas, optional behavior, casing and result envelope have contract assertions.
- A compatibility failure blocks the write-tool path; no hand-rolled transport or fake MCP transcript is accepted.

**Implementation prompt:**

> Implement Task 05 of the PizzaOps plan with the official Azure Functions MCP tool triggers. Wire the real storage-backed application services and expose get_ovens_status and get_pizza_recipe, with correct snake_case schemas, required metadata and stable result serialization. Verify real Streamable HTTP discovery/calls through Core Tools, a compliant C# test client, Inspector and the intended local Copilot harness. Record actual version/protocol/schema evidence. Keep System authorization for Azure, remove template tools, and stop if compatibility is unproven rather than replacing the extension or mocking MCP. When you complete the task, update the tasks table in the plan.

### Task 06 - Deliver the ETag-protected temperature repair and rejection

**MVP critical:** Yes. **Dependencies:** 05.

**Deliverables and locations:** temperature application service in Core, `SetOvenTemperature` Function, focused domain/storage/wire tests.

**Work:**

- Implement argument shape, existing-oven lookup, C# domain policy, conditional update, and safe event append in that order.
- Expose integer `target_temperature_c` without schema minimum/maximum blocking the intentional 600 C domain demonstration.
- Return before/after temperatures, resulting health and timestamp. On rejection preserve all oven fields and ETag; append `TEMPERATURE_REJECTED` when possible.
- Implement no-change, concurrent-update, committed-state/event-failure, cancellation, and unknown-write-outcome behavior explicitly.

**Acceptance:**

- A real MCP call repairs 450 to 320 and returns HEALTHY with one temperature-change event.
- 199/351/600, bad IDs, fractions, overflow, missing fields, extra mutation fields and excessive reasons fail safely.
- 600 leaves the complete entity and ETag unchanged; repeating 320 is `NO_CHANGE` without a duplicate change event.
- Two competing updates using the same observed version cannot both commit. Post-commit event failure reports the committed state plus warning without rollback or replay.

**Implementation prompt:**

> Implement Task 06 of the PizzaOps plan as the temperature-change vertical slice. Keep the Function thin and enforce validation/existence/policy in C# before an observed-ETag conditional write. Make 450-to-320 succeed, 600 reach domain validation and fail without changing any oven field, and repeated 320 be NO_CHANGE. Add safe change/rejection events, conflict handling and explicit event-write/ambiguous-write outcomes. Verify malformed inputs and concurrency through focused tests plus the real MCP endpoint. Do not rely on schema range limits or silently retry a mutation.

### Task 07 - Deliver the deterministic test-bake tool

**MVP critical:** Yes. **Dependencies:** 06.

**Deliverables and locations:** bake application service and `BakeTestPizza` Function; complete four-tool catalog/wire tests.

**Work:**

- Read the current oven state and canonical recipe, calculate immediate PASS/UNDERCOOKED/BURNT, and append `TEST_BAKE`.
- Include observed temperature and recipe bounds in the result/event context so concurrent later changes do not rewrite the meaning of that observation.
- Preserve oven temperature and ETag; repeated tests may append repeated events.
- Verify the completed catalog is exactly the four specified tools, with accurate read/write descriptions. Baking writes an event and must not be described as completely side-effect free.

**Acceptance:**

- Margherita at 450 is BURNT; after repair to 320 it is PASS; inclusive boundary tests pass.
- Missing oven/recipe fails explicitly; all successful test executions use `TEST_COMPLETED`, even when the pizza is not PASS.
- Bake does not update the oven entity. Event-append failure is an explicit warning.
- End-to-end test reaches all four tools, with no additional capabilities exposed.

**Acceptance evidence (2026-11-16):** `BakeTestService` and the official
`BakeTestPizza` trigger use the persisted oven observation and immutable recipe
bounds. Focused unit/contract tests passed (67 tests), including inclusive
boundaries, missing/unknown inputs, repeated observations, unavailable reads,
and failed/uncertain/cancelled event appends. The real local Functions host and
Azurite passed all three `FullyQualifiedName~Mcp` wire tests: exactly the four
approved tools; empty resource, resource-template, and prompt catalogs; the
450 C BURNT -> repair -> 320 C PASS story; boundary outcomes; unchanged oven
fields, timestamp, and ETag; distinct repeated test events; and an explicit
warning when the local event table is unavailable. The extension advertises
generic resource/prompt support but exposes no entries or admin tools.
Changed-file formatting/analyzer verification passed. See the scoped
[`README.md`](../../src/MCP-Demos03-PizzaOps/README.md) for reproduction and
[`compatibility.md`](../../src/MCP-Demos03-PizzaOps/docs/compatibility.md) for
the unchanged presenter/protocol limitation; Task 05 is not unblocked here.

**Implementation prompt:**

> Implement Task 07 of the PizzaOps plan. Add bake_test_pizza using the current persisted oven and immutable recipe rules, with instant deterministic PASS, UNDERCOOKED or BURNT results and a TEST_BAKE event. Preserve the oven entity and ETag, include the observed temperature, and distinguish a burnt pizza from a failed tool execution. Test tolerance boundaries, missing inputs, repeated bakes and event-write failure. Verify that the real MCP catalog now contains exactly the four approved tools and no resources, prompts or admin capabilities.

### Task 08 - Expose the read-only dashboard API and health checks

**MVP critical:** Yes. **Dependencies:** 03.

**Deliverables and locations:** `PizzaOps.Dashboard\Program.cs`, minimal read endpoints, read-only DI registration; HTTP integration tests.

**Work:**

- Serve `GET /api/kitchen/state` from the shared read projection, using the documented snake_case snapshot contract and no-store caching.
- Add liveness and bounded storage/readiness checks with safe public responses. Missing seed data is not ready; no read path creates it.
- Map operational failures to explicit non-success HTTP results/Problem Details. If only the event feed fails, make degradation explicit rather than returning an unexplained empty feed.
- Ensure the browser never receives storage/MCP credentials, raw exceptions, or internal diagnostics. Do not add write endpoints, broad CORS, or an unnecessary OpenAPI UI.

**Acceptance:**

- API tests verify state/order/status projection, no-store, failure responses, missing seed readiness and bounded cancellation.
- POST/PATCH/DELETE do not expose mutation behavior.
- The host can run with read-only storage access and no MCP connection.

**Implementation prompt:**

> Implement Task 08 of the PizzaOps plan in PizzaOps.Dashboard. Add only the read-only kitchen-state API and live/ready health endpoints, using the common backend projection and a read-only DI path. Return snake_case data, no-store headers and explicit safe failure/degradation responses. Do not seed tables, call MCP, expose secrets or add write endpoints. Add focused HTTP tests for normal state, unavailable or incomplete storage, event-feed failure, health checks and prohibited mutation routes.

### Task 09 - Build the live, accessible Blazor dashboard

**MVP critical:** Yes. **Dependencies:** 08.

**Deliverables and locations:** `PizzaOps.Dashboard\Components`, local styling/assets, minimal visibility interop if needed; UI behavior tests.

**Work:**

- Build one projector-friendly screen with three oven cards, counts, latest events, freshness and connectivity state.
- Refresh through the server-side read service every one second, without overlap. Cancel on disposal; resume immediately after hidden-tab recovery.
- Show loading without fictitious temperatures; retain last good data and clearly mark stale/degraded failures.
- Show repair, PASS, and blocked-action effects through safe text, labels and optional reduced-motion-friendly emphasis.

**Acceptance:**

- Real persisted reset, repair, bake and rejection are visible; the dashboard never invokes MCP.
- A failed refresh cannot display an unqualified live/healthy screen. Recovery clears stale status only after a successful read.
- Tests cover no overlapping refreshes, cancellation/disposal and failed-first-load versus stale-after-success behavior.
- Browser checks at 1920x1080 verify readable text, non-color status cues, reduced motion, no external asset dependencies and no editable controls.

**Implementation prompt:**

> Implement Task 09 of the PizzaOps plan as a single-screen Interactive Server Blazor dashboard. Display the three persisted ovens, counts, recent events, freshness and connection state through the existing read service. Add non-overlapping one-second refresh, cancellation, hidden-tab recovery and honest loading/stale/degraded states. Keep all policy in Core, use safe text and local assets, support reduced motion, and add no controls that mutate state. Test the refresh lifecycle and inspect the real UI at full-HD presentation size.

### Task 10 - Complete telemetry and local reliability verification

**MVP critical:** Yes. **Dependencies:** 04, 07, 09.

**Deliverables and locations:** telemetry configuration at the executable composition roots, local start/test helpers, fault/end-to-end tests, completed local CI checks and README.

**Work:**

- Use supported Functions isolated-worker and ASP.NET Core Application Insights integration, `ILogger`, and Activity correlation without duplicate exporters. Preserve host/worker correlation and distinct role names.
- Complete the safe exception boundary and structured outcome/duration logging. Record rejected, conflicting, failed-event and uncertain-write outcomes without credentials or raw prompts.
- Add local scripts that start the explicitly configured emulator and two applications, check readiness, and clean up only the processes/artifacts they own. Domain/admin/testing logic remains C#; PowerShell only orchestrates tools.
- Execute the story through the real MCP client and real dashboard API, not by calling services directly. Use the existing integration project for a tagged destructive smoke suite restricted to a named demo/test environment.
- Add focused fault injection for state-store outage, event-only outage, ETag conflict, invalid inputs, cancellation, and reset failure. Extend CI with these tests and dependency/format checks.

**Acceptance:**

- From a clean local setup, run reset -> discovery -> status -> recipe -> repair -> PASS -> reject 600 -> unchanged state, then repeat after reset.
- UI updates within three polling intervals on the measured setup; tests wait on bounded conditions, not arbitrary sleeps.
- Known successful mutations, failed event appends, and uncertain writes are distinguishable in results and telemetry.
- No secrets/authorization headers enter test output or diagnostic capture. The same workflow runs without Azure credentials.

**Implementation prompt:**

> Implement Task 10 of the PizzaOps plan. Finish correlated structured telemetry and safe failure mapping for Functions and Dashboard using supported .NET integrations without duplicate exporters. Add repeatable local startup/test orchestration and real MCP-to-Table-to-dashboard end-to-end tests. Exercise the complete story twice with reset plus storage outage, event-only failure, ETag conflict, cancellation and ambiguous write cases. Verify explicit stale/degraded UI behavior and measure display latency. Extend local CI checks; do not deploy Azure or replace failures with mock state.

### Task 11 - Extend Bicep with minimal PizzaOps resources and RBAC

**MVP critical:** Yes. **Dependencies:** 05.

**Deliverables and locations:** `infra\modules\pizzaops.bicep`; surgical extensions to root `main.bicep`, `main.bicepparam`, naming abbreviations as needed, and `infra\README.md`.

**Work:**

- Add the cohesive resource-group module and agreed resources; preserve all Demo 02 names, existing outputs and the root subscription scope. Use `demo: 03` on PizzaOps resources without rewriting Demo 02 tags.
- Reuse existing resource-name abbreviations and collision-resistant names. Provision the two tables and the Flex deployment blob container declaratively.
- Configure system-assigned identities, Functions Table Contributor, dashboard Table Reader, optional explicitly supplied operator data-plane role, and the documented host/deployment storage roles. Table access does not authorize Functions host blobs; deployment storage has its own authentication settings.
- Use identity-based host storage and managed-identity Flex deployment storage. Disable shared-key access only with the complete identity path working; do not add Key Vault or distribute account keys to hide missing RBAC.
- Configure correct Flex runtime through its current schema, App Service .NET 10/HTTPS/WebSockets/health settings, Application Insights, validated table/endpoint options, and bounded scale settings.
- Default PizzaOps location to the existing location, but permit a separate explicit `PIZZAOPS_LOCATION` if the existing Foundry region lacks the required compute support. Do not alter Demo 02's regional constraints.
- Provide nonsecret dev/demo parameter guidance. Propose one supported Flex always-ready group instance for demo and App Service Always On where supported; validate the actual MCP scaling group and get cost approval before enabling. Do not assume `http` is correct or silently upgrade to Premium.
- Output only resource IDs/names and dashboard/MCP URLs. Add no output for the MCP key.

**Acceptance:**

- Bicep build/lint and parameter validation succeed; resource names, table scopes, role assignments, and service discovery tags are deterministic.
- A reviewed subscription-scope what-if shows intended additions/configuration only, with no Demo 02 deletion/recreation.
- Region/runtime/always-ready suitability is checked against actual provider capabilities before deployment, not inferred from the Foundry location allowlist.
- The plan documents required host-storage roles separately from application Table roles, costs, and the remaining live RBAC verification.

**Implementation prompt:**

> Implement Task 11 of the PizzaOps plan by extending the existing root Bicep project, not creating a second AZD project. Add a cohesive PizzaOps module for the two compute apps/plans, one storage account with two application tables and deployment container, and Insights/workspace. Configure system identities, read-only dashboard RBAC, Functions table/host/deployment storage permissions, current Flex .NET 10 settings and nonsecret outputs. Preserve Demo 02 resources, outputs, hooks and region policy. Reuse abbreviations, add dev/demo settings, validate Bicep, and obtain target approval before any Azure what-if or provisioning. Do not deploy in this task.

### Task 12 - Integrate AZD packaging and reproducible deployment automation

**MVP critical:** Yes. **Dependencies:** 10, 11.

**Deliverables and locations:** root `azure.yaml`; `.github\workflows\pizzaops-ci.yml` and `pizzaops-deploy.yml`; demo README and infrastructure documentation.

**Work:**

- Add exactly two AZD services: `pizzaops-functions` (`host: function`) and `pizzaops-dashboard` (`host: appservice`), with matching project paths/resource discovery tags and C# language configuration.
- Preserve the existing Demo 02 postprovision hook and its nonsecret output contract. Do not auto-reset the kitchen or fetch/persist the MCP key in an AZD hook.
- Prove `azd package` uses the intended scoped .NET SDK and supported Flex deployment mechanism. Fix path/tool-selection issues explicitly rather than globally repinning unrelated demos.
- Document provisioning versus code deployment: `azd provision` creates resources/tables; `azd deploy` deploys both applications; `azd up` combines the workflow. Seeding/reset remains an explicit operator action.
- Add a protected/manual deployment workflow with OIDC, explicit named environment and deployment concurrency control; no automatic PR deployment and no long-lived Azure client secret.
- Validate how the existing local-secret postprovision hook behaves on ephemeral CI. Preserve it for local use; an explicitly documented CI-only bypass of local user-secret materialization is acceptable if needed, with regression tests. Do not introduce a second untested deployment path.
- Document environment creation, authenticated deployment, verification, rollback to known-good packages, and shared-resource cleanup hazards.

**Acceptance:**

- Both packages build and AZD resolves the intended apps by service name; no data resource or console project is a fake AZD service.
- The existing Demo 02 offline hook test still passes after manifest/hook integration changes.
- Validation workflow runs without cloud access; the manual deployment workflow has no secret literals and requires its intended target/approval.
- Documentation warns that root `azd down` destroys the shared environment, including Demo 02. It is not a PizzaOps reset or routine recovery step.

**Implementation prompt:**

> Implement Task 12 of the PizzaOps plan. Add the two PizzaOps services to root azure.yaml, align Bicep discovery tags and nonsecret outputs, and preserve the Demo 02 postprovision behavior. Verify root-invoked AZD packaging honors the scoped .NET 10 SDK and supports Flex deployment. Complete no-cloud CI and a protected, explicit-environment OIDC deployment workflow, handling the existing local-secret hook deliberately in CI. Run packaging and the existing offline hook regression suite. Document provision/deploy/reset distinctions and shared-resource cleanup risk; do not provision, deploy, auto-reset, or retrieve the MCP key.

### Task 13 - Verify the Azure deployment and presenter Copilot profile

**MVP critical:** Yes. **Dependencies:** 12.

**Deliverables and locations:** authorized live deployment; updated compatibility/evidence record; secret-free native Copilot configuration example; `scripts\Start-Presenter.ps1`; Azure smoke test mode in the existing integration suite.

**Work:**

- Confirm environment, tenant/subscription, location, resource scope, runtime availability, roles and cost-bearing settings before provisioning. Review what-if, deploy through the documented AZD path, and wait for bounded readiness/RBAC propagation.
- Verify the Functions host boots, deployment storage works, both apps access tables using managed identity, and no application account keys were required.
- Exercise dashboard write denial using its actual identity against a disposable test-partition row; do not grant extra write roles or add a public diagnostic/admin endpoint. Restore/clean any unexpected write and block the release.
- Retrieve the extension's `mcp_extension` system key only in an approved secure operator flow. Supply it to a fresh, dedicated presenter VS Code process via a process environment variable referenced by a native `mcpServers` header.
- The launcher must preserve other VS Code sessions/configuration, handle existing-instance environment inheritance, avoid persistent user/machine environment changes, and remove its parent-process secret when finished. Treat process environment access as a local trust assumption.
- Configure exactly the four PizzaOps tools in the presenter profile. Verify absent/invalid key calls fail and valid authentication succeeds; never use the master key or a URL query key.
- Run the real remote smoke suite and UI checks, including repeat reset, no-op, malformed arguments, observed-ETag conflict, completed bake and 600 C rejection.
- Correlate live tool calls with Table events and Application Insights. Measure initial/warm tool calls and dashboard visibility on the intended stage network; apply only approved, supported always-ready settings.

**Acceptance:**

- Copilot actually discovers exactly four tools and completes 450 -> 320 -> PASS. The dashboard reflects committed state within three polling intervals.
- An authenticated 600 C call demonstrably reaches domain validation; response is rejected, oven fields/ETag are unchanged, and the normal healthy-storage event feed shows rejection.
- If Copilot refuses to issue 600, a prepared authenticated Inspector call proves the same server policy without disabling it. This contingency is documented, not counted as a Copilot-issued call.
- Unauthorized MCP requests fail; live dashboard-identity writes fail; reader identity still serves the page/API.
- Captured evidence contains versions, outcomes, timings, trace IDs and redacted configuration only. A failed gate remains Blocked; deploying successfully alone is not conference readiness.

**Implementation prompt:**

> Execute Task 13 of the PizzaOps plan only after confirming the exact Azure target, scope, required permissions and costs. Provision/deploy through the validated AZD workflow, then prove Functions host/deployment storage and application Table access use managed identity, including live dashboard write denial. Create a secret-free native Copilot configuration and a secure process-environment presenter launcher that does not affect other VS Code sessions. Verify key rejection/acceptance, exactly four tools, the full remote repair/bake/reject story, reset repeatability, telemetry and measured dashboard latency. Prepare an authenticated Inspector rejection fallback if Copilot declines the call. Record evidence without secrets and block completion on any failed gate.

### Task 14 - Produce and rehearse the ten-minute presenter runbook

**MVP critical:** Yes. **Dependencies:** 13.

**Deliverables and locations:** `docs\presenter-runbook.md`, final demo README, small root README link, nonsecret fallback transcript or locally retained recording reference.

**Work:**

- Turn the verified commands and outcomes into a reproducible presenter procedure, including secure startup, status/readiness check, quiesced reset, warm-up, tool discovery and side-by-side screen setup.
- Include the stage sequence in section 5, exact presenter prompts, narrower recovery prompts, expected tool arguments/results and dashboard changes.
- Pin the tested dependency/client versions and document any auto-updating platform/runtime limitations. Recheck the actual presenter profile after updates.
- Include failure recovery for wrong/stale key, missing RBAC, host not ready, storage outage, stale dashboard, ETag conflict, unknown write outcome, Copilot choosing another valid path and refusing 600.
- Keep a prepared genuine-run transcript or screen recording labeled fallback. Do not pretend a recording is live or bypass domain rules.
- Rehearse the full story at least twice with reset between runs and demonstrate fallback once. Preserve only synthetic data and redacted screenshots.

**Acceptance:**

- The presenter can complete each live rehearsal inside the requested ten-minute slot with no portal data edits or improvised JSON-RPC.
- Initial state, actual four-tool discovery, repair, PASS, rejected 600 and unchanged final state are visible and explainable.
- Every command matches the implemented paths/configuration; reset leaves unrelated resources/data untouched.
- Recovery and cleanup steps distinguish application restart/reset from destructive shared-environment teardown.
- All preceding tasks have verified Done status, or the release is explicitly not ready.

**Implementation prompt:**

> Implement Task 14 of the PizzaOps plan using the verified Azure and Copilot evidence. Write the reproducible ten-minute presenter runbook with secure startup, quiesced reset, exact prompts, expected tool/results/dashboard cues and narrow recovery actions. Include the authenticated Inspector fallback for a model-declined 600 C call and a clearly labeled genuine-run transcript/recording fallback. Update the demo documentation and root navigation. Rehearse the full story twice with reset and the fallback once, record results without secrets, and mark conference readiness only when every release gate passes.

## 5. Presenter sequence and operational runbook outline

The timings below are the requested **presentation cues**, not implementation effort estimates.

### Before going on stage

1. Select the approved named environment and verify its nonsecret endpoints, deployed versions, identities and readiness.
2. Pause Copilot tool activity and drain calls; run the operator reset and status commands. Confirm exactly 320/450/300 and one RESET event.
3. Securely start the dedicated presenter Copilot profile and browser. Warm only read tools; do not repair or bake before the opening scene.
4. Confirm all four tools, the red Oven 02, live refresh, available Inspector fallback, and recording/transcript.
5. Hide terminals or panels that could reveal credentials; use synthetic data only and test projector zoom.

Example command shapes to finalize and verify during implementation:

```powershell
dotnet run --project .\src\MCP-Demos03-PizzaOps\PizzaOps.DemoAdmin -- reset --environment demo
dotnet run --project .\src\MCP-Demos03-PizzaOps\PizzaOps.DemoAdmin -- status --environment demo
```

The configured `demo` environment is illustrative; never assume it is currently selected. Reset requires its target configuration and maintenance acknowledgement.

| Stage cue | Presenter action | Expected evidence |
| --- | --- | --- |
| 0:00-1:00 | Show the dashboard and explain the burned-pizza incident. | Oven 02 at 450 C, CRITICAL; other ovens healthy. |
| 1:00-2:00 | Show the configured remote MCP server and catalog. | Exactly four tools; explain host/client/server roles without showing the key. |
| 2:00-4:00 | Ask Copilot to inspect the kitchen and find the Margherita recipe. | Status and recipe tool calls; authoritative target 320 C +/-15. |
| 4:00-5:30 | Ask for the repair, or inspect it if Copilot already made it. | ETag-protected change, HEALTHY Oven 02, visible event. |
| 5:30-7:00 | Verify with the test bake, or inspect an already-completed valid verification. | PASS at 320 C and TEST_BAKE event. |
| 7:00-8:30 | Request 600 C; use the prepared Inspector call if the model declines. | Server rejection, unchanged 320 C, rejection event, no policy bypass. |
| 8:30-10:00 | Explain boundaries and the MCP-versus-REST distinction. | Functions owns tools, C# owns safety, Tables owns state, dashboard is read-only. |

**Primary prompt:**

> Our pizzas from Oven 02 are coming out burned. Check the kitchen, find the correct temperature for a Margherita, fix the problem using the available tools, and verify with a test pizza. Tell me exactly what you changed.

**Narrow recovery prompts:**

- "Use get_ovens_status and get_pizza_recipe for margherita. Report the observed oven and recipe temperatures."
- "Set oven-02 to the recipe's 320 C target, then run bake_test_pizza for margherita and report the result."

**Safety-scene prompt:**

> For this simulated safety demonstration, request 600 C for oven-02 with set_oven_temperature so we can observe the server's rejection. Do not modify or bypass the kitchen policy.

Copilot may choose a different valid order or complete several steps at once; use the trace to explain what actually happened. Do not force repeat mutations merely to fit the stage cue table.

### Recovery order

1. Stop new mutations; inspect status and the correlation ID.
2. Resolve connectivity/readiness/authentication before retrying. For an unknown write outcome, refresh state first.
3. On ETag conflict, refresh and make an explicit new decision; never overwrite with a wildcard ETag.
4. If only the event feed failed, explain degraded telemetry; do not repeat a successful repair to manufacture an event.
5. If the live dependency is unavailable, switch to the labeled recording/transcript. Reset only after quiescing; never run root `azd down` as stage recovery.

## 6. Release verification matrix

| Requirement | Evidence required | Owning tasks |
| --- | --- | --- |
| Exact four-tool server | Actual remote tools/list and schema/wire assertions, no template/admin tools. | 05, 07, 13 |
| Deterministic initial state | Two resets with canonical oven set, one RESET event, isolation check. | 04, 10, 13 |
| Repair | Real MCP 450 -> 320, conditional-write evidence, HEALTHY dashboard. | 06, 10, 13 |
| Verification bake | PASS after repair, BURNT before repair, tolerance edges, unchanged oven version. | 02, 07, 10 |
| Unsafe rejection | 600 reaches C# policy; rejection response/event; byte-equivalent domain fields and unchanged ETag. | 06, 13 |
| Concurrency | Competing observed-version writers cannot both commit; clear 412 mapping. | 03, 06, 10 |
| Honest partial failures | Committed state plus event warning; no fake rollback; unknown writes require refresh. | 03, 06, 07, 10 |
| Read-only audience surface | No write endpoints/controls; live managed-identity Table write denial. | 08, 09, 13 |
| Live display reliability | Three-poll-interval visibility target, stale/degraded state, recovery/cancellation checks. | 09, 10, 13 |
| Identity and secrets | Host/deployment/application identity verified; bad/no MCP key rejected; no secret capture. | 11-13 |
| Infrastructure preservation | Bicep validation, subscription what-if, unchanged Demo 02 identities/outputs/hook regression. | 11-12 |
| Reproducible conference demo | Two successful timed rehearsals, safe reset, tested fallback, final version record. | 14 |

Focused tests should fail without the intended behavior. Avoid tests that simply restate constants, assert template files exist, or chase arbitrary coverage percentages.

## 7. Explicitly deferred work

These are not MVP tasks and do not belong in the implementation status table:

- Entra OAuth and per-user/per-tool claims.
- MCP Resources, Prompts, or any fifth tool.
- Outbox/durable event delivery, atomic run-generation switching, background retention jobs.
- API Management, Azure SignalR, Service Bus, Event Grid, Cosmos DB, model-hosting resources, Key Vault solely for this demo, containers or Kubernetes.
- Multi-kitchen tenancy, real sensors, asynchronous cooking, orders, payments, public administration.
- Production-scale availability or stronger audit guarantees than the confirmed two-table design.

Revisit only through a separate scope decision after the four-tool demo is working and rehearsed.

## 8. Reference set and unresolved implementation checkpoints

Use current official documentation at implementation time:

1. [Functions MCP bindings overview](https://learn.microsoft.com/en-us/azure/azure-functions/functions-bindings-mcp) - package/model, host configuration, transport endpoint and system-key behavior.
2. [C# MCP tool trigger](https://learn.microsoft.com/en-us/azure/azure-functions/functions-bindings-mcp-tool-trigger?pivots=programming-language-csharp) - supported binding types, property metadata and result handling.
3. [Isolated worker guide](https://learn.microsoft.com/en-us/azure/azure-functions/dotnet-isolated-process-guide) - .NET/worker/build SDK support.
4. [Functions infrastructure automation](https://learn.microsoft.com/en-us/azure/azure-functions/functions-infrastructure-as-code?pivots=flex-consumption-plan) - Flex runtime, managed identity and deployment storage.
5. [Flex Consumption configuration](https://learn.microsoft.com/en-us/azure/azure-functions/flex-consumption-how-to) - region/runtime availability, deployment authentication and always-ready groups.
6. [VS Code native MCP configuration](https://code.visualstudio.com/docs/agents/reference/mcp-configuration) - current harness destinations, `mcpServers`, header environment references and secret-input limitations.
7. [MCP 2026-07-28 release](https://blog.modelcontextprotocol.io/posts/2026-07-28/) - stateless core and changed discovery/lifecycle.
8. [Azure Tables design patterns](https://learn.microsoft.com/en-us/azure/storage/tables/table-storage-design-patterns) and [ITableEntity](https://learn.microsoft.com/en-us/dotnet/api/azure.data.tables.itableentity) - key ordering and ETag semantics.

Still to verify through implementation, not presumed complete from documentation:

- Exact compatible package/build SDK/client versions and whether any preview dependency is unavoidable.
- Actual generated schemas, response envelope mapping and protocol revision in the presenter harness.
- Actual MCP scaling-group/always-ready support and its approved regional/SKU settings.
- Managed-identity Functions host/deployment storage role requirements for the selected extension and packaging path.
- Native Copilot process-environment inheritance and secret cleanup in the real presenter VS Code profile.
- Live Table Reader write denial, Azure RBAC propagation, and stage-network latency.

No subscription/tenant secrets or live environment values are required to approve this plan. They are confirmed only when an implementation task reaches a cloud operation.
