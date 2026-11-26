# RunMind 3.4.1 — Advanced Intelligence & Extensibility

This guide describes five capabilities introduced in the 3.4.0 feature line and carried forward in the current 3.4.1 source: Change Impact Explorer, Development Session Timeline, System Intelligence, Advanced Agent Orchestration, and the RunMind extension model. They are not separate applications or parallel agent engines. They reuse the shared Agent Core, native host bridge, workspace identity, project index, approval policy, tool ledger, and existing local persistence.

The word *complete* in this guide means that the shipped contract is implemented, exposed, bounded, and tested. It does not mean unrestricted machine access, arbitrary third-party code execution, continuous surveillance, or guaranteed inference where the underlying evidence is unavailable.

## 1. Change Impact Explorer v2

Change Impact Explorer is upgraded to Git-aware, symbol-level static impact analysis. It can inspect staged/unstaged/untracked working-tree changes after fresh `gitRead` approval, optionally compare an existing local `base_ref` with `HEAD`, infer changed symbols from diff hunks, map checker references and reverse import dependencies, inspect HTML/CSS/SVG/media/font assets, find importers of deleted/renamed modules, surface dynamic dependency patterns, select candidate tests, and consume Istanbul/LCOV changed-line coverage evidence.

Its report includes blast radius, transparent risk factors and evidence-coverage confidence. These are heuristics rather than probabilities. Aggregate line coverage cannot identify individual tests; dynamic runtime behavior cannot be exhaustively inferred by static analysis. The analyzer is read-only and never executes tests or modifies Git.

See [`CHANGE-IMPACT-EXPLORER-V2.md`](CHANGE-IMPACT-EXPLORER-V2.md) for invocation, report semantics, evidence sources, bounds, safety and the target-Windows acceptance checklist.

## 2. Development Session Timeline

### What it records

The Development Session Timeline reconstructs RunMind-recorded work in chronological order. Its evidence includes:

- Agent task checkpoints and task status transitions.
- Tool operations, bounded summaries and operation outcomes.
- Dependency groups/execution layers and predecessor relationships.
- Delegated sub-agent activity where recorded.
- Interruptions and operations whose external outcome is uncertain.

### Entry points

- Workflow UI: **Development Session Timeline**.
- Agent tool: `development_session_timeline`.
- Recovery: the explicit Resume action for a compatible checkpoint.

Example request: “Show the latest Development Session Timeline. Highlight failed, interrupted and outcome-unknown operations, and tell me which ones need inspection before retry.”

### Runtime log correlation is complementary

`runtime_log_correlation` is a different capability. It can create an ephemeral timeline from exact RunMind-owned development-server sessions and Node Inspector sessions, plus explicitly supplied Playwright/API event slices. It extracts request, trace, correlation and Journey IDs. It does not scan arbitrary project logs or retain a persistent log warehouse.

The Development Session Timeline records RunMind's own Agent workflow; runtime correlation relates selected runtime evidence. Neither infers causality from event order or matching identifiers alone.

### Persistence and limits

Checkpoint and timeline records are bounded, redacted local metadata associated with a chat/WebView profile and workspace identity. They are not a full transcript archive, do not synchronize automatically across hosts/devices, and do not recreate external process state. The timeline contains only events RunMind actually recorded; it cannot reconstruct activity that was never captured.

## 3. System Intelligence

### What it combines

The on-demand Windows health report consolidates only the System Manager categories that are enabled and individually available:

- Windows service state/configuration, declared dependencies, startup configuration and recovery/trigger settings.
- Application crashes, Windows Application/System events and recurring service/application/network event signatures.
- Finite CPU, system memory, process memory and supported physical-disk I/O samples.
- Bounded TCP/UDP connection ownership and possible listener-conflict candidates.
- Storage health and coverage warnings when the underlying Windows provider exposes the relevant data.
- Optional comparison with the preceding user-requested health snapshot.

### Entry points

- Agent tool: `system_manager_health_report`.
- Related read-only tools: `system_manager_read_event_log`, `system_manager_investigate_crashes`, `system_manager_resource_monitor`, `system_manager_storage_health`, `system_manager_targeted_network_diagnostics`, and service inspection/dependency actions.
- Settings: System Manager master switch, group/individual capabilities, and the separate optional health-history setting.

Example request: “Generate a Windows health report for the last 7 days. Include available service, crash, event, resource, network and storage evidence. Separate confirmed observations from possible correlations and list unavailable data.”

### Bounds and safety

- The report runs on demand; it does not install a background monitor or daemon.
- Service start/stop/restart remain separate operations requiring fresh native confirmation and post-action state verification.
- DNS/route/TCP checks are user-directed; the TCP check targets one explicit endpoint and requires confirmation. No scans or destination sweeps are performed.
- Dependency traversal is capped at 5 levels / 50 service nodes. Network inventory is bounded. Resource sampling is finite.
- Optional history is disabled by default and stores only bounded summary data (up to 30 snapshots / 512 KB), not raw Event Log messages or raw process/network inventories.
- Event overlap and five-minute correlation windows show temporal association, not root cause. Missing providers/counters are reported as unavailable, never silently treated as healthy zero values.

## 4. Advanced Agent Orchestration

### Bounded multi-tool planning

The shared Agent Core creates deterministic execution groups. Independent calls on an explicit read-only allow-list can run in parallel with a maximum of four workers. Duplicate calls, writes, approval-gated actions, MCP operations and delegated work are serialized as barriers. Dependency layers and operation records remain available for review.

This is deliberate conservative orchestration: RunMind does not infer that two arbitrary tools are safe to run concurrently merely because they appear unrelated in a model-generated plan.

### Checkpoints and recovery

A task checkpoint stores bounded/redacted task, plan, progress, operation links and status. If a WebView closes or an operation is interrupted, the outcome is marked uncertain rather than assumed successful or failed. Resume is explicit and bound to the same workspace identity. The Agent is instructed to inspect current state before retrying a non-idempotent action and to obtain fresh approval where required. The system never automatically replays side effects.

### Tool-result context

Tool results are summarized with depth, node, property and string limits; common credentials are redacted; raw HTML, bodies and binary/base64 payloads are omitted from model context and local previews. Truncation is explicit so an incomplete summary is not presented as a complete raw result.

### MCP discovery

MCP catalogue discovery covers server metadata/capabilities, tools, resources, resource templates and prompts. Pagination is bounded to 20 pages / 2,000 entries per catalogue, repeated cursors are detected, and discovery errors/truncation affect completeness reporting. Template expansion is explicit. Discovery does not imply permission to read remote resources or invoke tools/prompts: those operations remain separately approval-gated.

## 5. Ecosystem and Extensibility

RunMind has several intentionally different extension planes. They share the same safety principle—extension metadata can describe capabilities, but native policy decides what may execute.

### MCP servers — external tool/resource integration

Use MCP when an external service should expose tools, resources, templates or prompts to the Full Agent. RunMind discovers metadata, reports completeness, and requests approval for remote operations. MCP metadata and returned content are untrusted input; they cannot grant permissions or override native policy.

### Agent Skills — reusable knowledge and procedures

RunMind supports bundled, global and authorized project Skills. Skills can be discovered without loading their instructions, then loaded explicitly when relevant. Installation paths include a local Skill folder, direct HTTPS `SKILL.md`, supported GitHub/GitLab repository-folder URLs, or pasted `SKILL.md` content; installation includes preview/confirmation. Skill content is untrusted guidance, not authorization. Skill scripts are never automatically executed. Full repository/archive resource import is not part of the current URL installer; local folder imports can include the Skill's resource files.

### System Manager application adapters — declarative native integrations

Built-in adapters can be edited through user overrides; custom adapters can be installed, listed and removed as declarative JSON. The supported custom operations are `open-file`, `open-folder` and `open-url`; argument templates are limited to `{target}` and `{line}`. Adapter identity, permission checks, confirmation prompts and result verification remain native-owned. Scripts, shell commands, environment expansion, arbitrary executable paths and arbitrary code are not supported.

Adapter manifests live under `%LOCALAPPDATA%\RunMind\SystemManager\Adapters`; built-in overrides live under its `Overrides` subfolder. Resetting a built-in adapter removes its override and restores the shipped default.

### RunMind Runner launcher plugins — quick-launch integration

The native WPF Runner uses an internal first-party `IRunnerLauncherPlugin` registry for Applications, Files & Folders, URLs, Calculator and RunMind inline answers. Plugins return bounded ranked results through one contract; global mode can apply configured per-plugin ranking weights. A plugin failure is isolated so remaining plugins can still return results.

The Runner registry is currently first-party-only. It does not dynamically load third-party DLLs or scripts. This is an intentional security boundary, not an undocumented promise of a public binary-plugin SDK. The Runner remains a restricted quick host and does not acquire Full Agent, workspace, MCP, Skills, developer or System Manager access.

### Which extension plane should be used?

| Need | Use | Why |
|---|---|---|
| Connect an external service with callable operations | MCP | Native approval remains the execution boundary |
| Add domain knowledge, workflows or reusable instructions | Agent Skills | Content is explicitly discovered/loaded and never grants permission |
| Teach System Manager how to open files/folders/URLs in an application | Declarative application adapter | Narrow argument templates; no script or shell execution |
| Add another built-in quick-launch provider to Runner | `IRunnerLauncherPlugin` first-party contract | Native bounded ranking/action dispatch; no runtime code loading |

## 6. Shared-host behavior

The Agent, developer and System Manager features use shared source and native bridge deployments across Standalone, PowerToys Run and PowerToys Command Palette. The optional Runner is different by design: it is a restricted launcher that hands Full Agent conversations to the real Standalone application. It must not be described as exposing the Full Agent capability set itself.

## 7. Validation status and certification

Portable regression results and target-Windows acceptance are recorded in the current [Build status](../project/BUILD-STATUS.md). Historical test counts in earlier release reports are snapshots and do not certify the current source tree. The Windows release pipeline is `Build-Release-x64.bat`; complete the current [P7 Windows acceptance checklist](../P7-WINDOWS-ACCEPTANCE.md) and [Process Intelligence acceptance checklist](../system/PROCESS-INTELLIGENCE-WINDOWS-ACCEPTANCE.md) on the target machine.

## 8. Explicit follow-up candidates—not shipped capabilities

These remain separate future work and must not be implied by the current implementation:

- A public third-party Runner plugin SDK/marketplace or runtime DLL/script loader.
- Full remote Skill repository/archive resource import with bounded dependency handling.
- A central native conversation/session database shared across Standalone and the quick hosts.
- Continuous System Manager monitoring, alerting or automatic remediation.
- A full log warehouse or general-purpose distributed tracing backend.
- Automatic replay of interrupted side-effecting Agent operations.
- Causal root-cause proof from temporal correlation alone.

The current architecture intentionally prefers bounded capabilities and reuse of the shared Agent Core over a second plugin runtime, background service or unrestricted execution layer.
