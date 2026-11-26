# RunMind

**A local-first AI workspace for Windows, project work, and system operations.**

RunMind brings model-powered chat, project-aware Agent workflows, research, developer tools, and Windows System Manager into one Windows desktop product. It can work with local model servers or configured remote providers, and it keeps native operations behind explicit permission checks and confirmation steps.

> **Source status:** This repository is the source snapshot referred to as **3.4.0**. It is not a certified binary release. Native Windows build, installation, and acceptance checks remain release gates; see [Build and Validation Status](./docs/project/BUILD-STATUS.md).
>
> **Version metadata is not uniform:** Standalone, PowerToys Run, and Command Palette project/package metadata remain at **3.3.1** (Command Palette MSIX identity **3.3.1.0**), while Runner is **3.4.0**. Do not infer the installed version of every host from the repository snapshot label.

[Build and setup](./docs/getting-started/BUILD-AND-SETUP.md) · [Documentation index](./docs/README.md) · [Feature matrix](./docs/product/FEATURE-MATRIX.md) · [Changelog](./CHANGELOG.md) · [Roadmap](./ROADMAP.md)

**Target platform:** Windows 11 x64, build 22621 or later · **Framework:** .NET 10 · **Languages:** C# and JavaScript · **License:** MIT

## What RunMind does

### Project-aware Agent

Select a local project workspace and use the Agent for bounded file operations, Git review, project indexing, architecture and diagnostic summaries, and approved developer workflows. **Change Impact Explorer** analyzes Git changes, indexed symbols and reverse dependencies, local asset references, candidate tests, and available coverage evidence. The analysis is heuristic and evidence-limited: it does not discover every runtime dependency, run the candidate tests, or prove that a change is safe.

Agent workflows can use approved workspace tools, developer integrations, native Windows capabilities, installed Skills, and connected MCP servers. Task checkpoints and the Development Session Timeline preserve bounded local progress evidence. Interrupted side effects are not automatically replayed; review and resume are explicit.

### Deep Research

Deep Research can create and revise a research plan, search through a configured SearXNG instance, fetch source pages, record evidence excerpts, make model-assisted claim/source assessments, pause or resume a task, and export a Markdown report. Exact-quote matching checks whether an excerpt appears in retrieved text; it does not independently prove a claim or the reliability of a source.

### Developer workflows

Available integrations include Node.js debugging and runtime profiling, Playwright browser workflows, API/OpenAPI testing, development-server management, and SQLite inspection. These features depend on the relevant local runtime, project dependencies, browser, and permissions. Playwright runs use a fresh browser context and do not persist login state.

### Windows System Manager

System Manager provides permission-controlled access to supported Windows application/startup, process, service, scheduled-task, event/crash, network, storage-health, security-posture, application-adapter, and container operations. Container operations support Docker and Podman when a compatible runtime is installed and available on `PATH`.

**Live Resource Intelligence** is an explicitly started, bounded session for CPU, memory, GPU, network, and process-I/O readings. Samples remain in memory for that session and can be exported by the user; this is not an always-on monitoring service. GPU readings depend on the counters exposed by the installed driver. **Process Intelligence** adds on-demand process inspection and separately confirmed close, terminate, and guarded restart actions for eligible processes. Service-hosted and protected processes are blocked from process-level control.

### Files and deliverables

The Agent can create Markdown/HTML files, structured Word DOCX documents, Excel XLSX workbooks, text-oriented PDFs, SVG charts and diagrams, Mermaid diagram source, SVG QR codes, HTML image galleries, and WebP thumbnails from workspace images. These are bounded generators, not full Microsoft Office editors or a general-purpose image-generation system. PDF output is text-focused and does not render embedded images or complex page layouts.

## Choose a RunMind host

| Host | Full Agent | Workspace and developer tools | System Manager | Intended use |
|---|:---:|:---:|:---:|---|
| **Standalone** | Yes | Yes | Yes | The full native Windows desktop workspace. |
| **PowerToys Run** | Yes | Yes | Yes | Launch RunMind from PowerToys Run; the plugin also supports inline model answers with `llm <prompt>`. |
| **Command Palette** | Yes | Yes | Yes | Inline model answers, a persistent RunMind Dock conversation, and the Command Palette package’s own full RunMind chat window. |
| **RunMind Runner** | No | No | No | A separate lightweight launcher for apps, files/folders, URLs, calculator expressions, and inline model answers. |

The first three hosts use the shared ChatWindow/Agent implementation and native bridge. Runner is deliberately a **restricted Quick Host**, not another Full Agent. It does not expose workspace access, file attachments, developer tools, MCP, Skills, or System Manager. Its handoff opens the actual Standalone application for full workspace tasks. Runner's current launcher registry is first-party-only; it is not a public third-party plugin loader or SDK.

## Models and integrations

| Provider or integration | Current support |
|---|---|
| **Ollama** | Local models through the Ollama API; the default local URL is `http://localhost:11434`. |
| **LM Studio** | Local models through OpenAI-compatible chat completions or LM Studio Native REST v1; the default base URL is `http://localhost:1234/v1`. |
| **OpenAI-compatible endpoints** | Additional local or hosted endpoints configured by the user. |
| **SearXNG** | Optional web discovery for normal Agent tools and multi-query Deep Research. Configure an instance URL to enable search. |
| **MCP** | Remote tools, resources, resource templates, and prompts over Streamable HTTP. STDIO transport is not implemented. Remote operations require approval. |

Vision, tool calling, reasoning features, context length, and other model behaviors depend on the selected model, server, and API mode. Local inference does not make the entire product offline-only: configured search services, remote model providers, MCP servers, websites, and browser workflows may receive the prompts, queries, page requests, or tool arguments sent to them.

## Security and privacy

- Workspace file operations are scoped to the selected workspace and apply the relevant validation, approval, and recovery rules. This does not sandbox arbitrary native execution.
- System Manager is disabled by default. Its native operations check the enabled permission/capability again when dispatched; state-changing actions use operation-specific confirmation.
- Approved PowerShell and native process commands run with the permissions of the current Windows account. RunMind does not silently elevate and is **not** a security sandbox.
- MCP responses, Skills, repository instructions, and adapter metadata are untrusted content. They cannot grant permissions or override native policy. Skill scripts are not automatically executed.
- **Credentials are not uniformly protected at rest.** Full Chat provider/Jina/Reader/MCP secrets and the shared PowerToys Run/Command Palette inline credentials are stored in local WebView2 or per-user JSON settings without DPAPI encryption by RunMind. Runner inline credentials use Windows DPAPI CurrentUser protection. Review [Security and Privacy](./docs/security/SECURITY-AND-PRIVACY.md) before storing sensitive credentials.

## Build and run

### Requirements

- Windows 11 x64, build 22621 or later. The provided release pipeline targets x64; ARM64 is not packaged.
- .NET 10 SDK and Windows x64 targeting/runtime packs.
- .NET 10 Desktop Runtime for the framework-dependent Standalone and Runner publications.
- Microsoft Edge WebView2 Runtime.
- Node.js 24 or later for the JavaScript regression suite and Node-based developer integrations.
- Git for Windows on `PATH` for Git-backed project tools.
- Microsoft PowerToys for the PowerToys Run plugin and Command Palette extension only.
- Ollama, LM Studio, or another supported model endpoint for model features. SearXNG is optional and required for web search discovery.

### Build the four hosts

From the repository root on Windows, run:

```bat
Build-Release-x64.bat
```

The script restores and builds all four x64 host projects, publishes the framework-dependent Standalone and Runner applications, and packages/signs the Command Palette MSIX using the local development certificate (creating it if a valid one is not already present). It does **not** install the hosts. The first build prepares the PDF.js/Tesseract runtime assets and downloads their packages and language data, so it requires internet access; the downloads are not yet fully pinned and hash-verified for reproducible, offline builds.

After a successful build:

- Run Standalone from `Release\Standalone\win-x64\publish\RunMind.exe`.
- Install PowerToys Run with `Install-RunMind-PowerToys-Run.bat`.
- Install the Command Palette MSIX with `Install-RunMind-CommandPalette.bat`.
- Install Runner for the current user with `Install-RunMind-Runner.ps1`.

For exact prerequisites, outputs, installation behavior, OCR asset preparation, model-server CORS setup, and validation commands, follow the [Build and Setup guide](./docs/getting-started/BUILD-AND-SETUP.md).

## Documentation

- [Documentation index](./docs/README.md) — maintained documentation grouped by topic.
- [Feature matrix](./docs/product/FEATURE-MATRIX.md) — host capabilities and implementation boundaries.
- [Agent capabilities](./docs/product/AGENT-CAPABILITIES.md) — MCP, orchestration, checkpoints, Skills, Change Impact Explorer, and artifact verification.
- [Advanced intelligence and extensibility](./docs/product/ADVANCED-INTELLIGENCE-AND-EXTENSIBILITY.md) — System Intelligence, Development Session Timeline, orchestration, MCP, Skills, adapters, and Runner plugins.
- [Developer workflows](./docs/developer/DEVELOPMENT-INTELLIGENCE.md) — Git review, build/test intelligence, dependencies, and environment checks.
- [System Manager operations](./docs/system/SYSTEM-MANAGER-OPERATIONS.md) — Windows operations, scheduled tasks, and container controls.
- [Live Resource Intelligence](./docs/LIVE-RESOURCE-INTELLIGENCE.md) and [Process Intelligence](./docs/system/PROCESS-INTELLIGENCE.md) — live monitoring and process inspection/actions.
- [Security and privacy](./docs/security/SECURITY-AND-PRIVACY.md) — credential storage, data flows, execution boundaries, and untrusted content.
- [Build and validation status](./docs/project/BUILD-STATUS.md) and [roadmap](./ROADMAP.md) — current verification state and remaining priorities.
- [3.4.0 source release notes](./docs/releases/3.4.0-RELEASE-NOTES.md) and [changelog](./CHANGELOG.md) — release-line summary and product history.
- [Historical archive](./docs/archive/README.md) — milestone audits, acceptance checklists, and archived validation reports.

> **Screenshot note:** The images currently bundled in `screenshots/` were captured under the earlier **LocalLLM** branding. They are retained as historical development captures and are not presented here as current RunMind UI screenshots.

## License

RunMind is provided under the [MIT License](./LICENSE.txt).
