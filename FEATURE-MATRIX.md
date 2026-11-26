# RunMind capability matrix

This matrix is a current product map, not a release history. “Implemented in source” describes code present in this repository; it does not mean that native Windows runtime acceptance has passed for the current checkout. See [Build status](BUILD-STATUS.md) and the feature-specific Windows checklists.

## Hosts

| Host | Full Agent | Project/workspace tools | System Manager | Notes |
|---|---:|---:|---:|---|
| Standalone | Yes | Yes | Yes | Complete native desktop workspace. |
| PowerToys Run | Yes | Yes | Yes | Full Agent hosted in PowerToys Run. |
| Command Palette | Yes | Yes | Yes | Full Agent hosted by the Command Palette extension/MSIX. |
| Runner | No | No | No | Restricted Quick Host: app/file/URL search, calculator and inline model provider. |

The three Full Agent hosts share the ChatWindow/Agent implementation and native bridge. Runner is deliberately a separate lightweight host, not a reduced copy of the Full Agent.

## Product capabilities

| Area | Implemented behavior | Important boundaries |
|---|---|---|
| Chat and providers | Ollama, LM Studio and configured OpenAI-compatible providers; conversations, attachments and model settings | Provider capabilities differ; credentials are not uniformly DPAPI-protected. |
| Deep Research | Editable plan, source collection, evidence notes/claim assessments, pause/resume and Markdown export | Research output must retain source attribution; generated synthesis is not independent proof. |
| Workspace | Selected workspace, bounded file operations, Git workflows, backups/recovery and project context | Workspace boundaries and approval rules apply; shell/native execution is not sandboxed. |
| Project understanding | Project index, architecture/diagnostic summaries, Mission Control and release readiness | Static indexes can be incomplete or stale; show coverage and uncertainty. |
| Change Impact Explorer | Git-aware changed paths, symbol/dependency/asset impact, test candidates, coverage evidence, risk and confidence reporting | Static analysis cannot discover every runtime-generated/reflection/plugin dependency; candidate tests are not proof of coverage. |
| Development intelligence | Node debugger, runtime profiling, Playwright workflows, API testing, development-server management, SQLite and container tools | Requires compatible local runtimes/dependencies; actions follow approval and size/time bounds. |
| Agent orchestration | Bounded parallel read-only work, serialized mutation/approval barriers, checkpoints and review-before-resume | Unknown/in-flight outcomes are not automatically replayed. |
| MCP and Skills | Streamable HTTP MCP tools/resources/templates/prompts; installed Skills and project context | STDIO MCP is not included. MCP content and Skills are untrusted data/guidance and cannot grant permissions. |
| System Manager | Applications/processes, Windows services, event/crash/health, network, storage, security posture, tasks, adapters and containers | Capabilities are individually enabled; native checks and confirmations remain authoritative. |
| Live Resource Intelligence | Explicit bounded CPU/memory/GPU/network/process-I/O sampling, charts and CSV export | In-memory session only; GPU counters depend on drivers; no per-process GPU attribution. |
| Process Intelligence | Live process list, on-demand inspection, file/signature/owner/security/parent/child details, reveal/copy, graceful close, force terminate and guarded restart | UI actions only. Service-hosted/protected processes are blocked. Restart cannot reproduce original token, current directory, inherited handles or application state. |
| Artifacts | Markdown/HTML, Word, Excel, PDF, SVG charts/diagrams, images and QR outputs | Structural checks do not prove every calculation, fact or visual is correct. |
| Runner unified search | Apps, files/folders, URLs, calculator, inline model with keyword/global activation and plugin weights | Quick Host only; no workspace, Full Agent, MCP, Skills, developer tools or System Manager. |

## Security and privacy posture

- Workspace file operations are constrained to the selected workspace and use applicable approval/backup protections.
- Approved native and PowerShell commands run as the current Windows account; this is not a security sandbox and does not silently elevate.
- System Manager is disabled until enabled and each native capability is checked again at dispatch.
- Full Chat and shared inline host credential stores are not fully protected by DPAPI. See [Security and privacy](docs/SECURITY-AND-PRIVACY.md).
- Live Resource telemetry is explicitly started, bounded and in-memory; it is not an always-on monitoring service.

## Detailed guides

See the [README](README.md) for the product overview and [docs/](docs/README.md) for the maintained documentation index.
