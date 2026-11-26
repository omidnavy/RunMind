<h1 align="center">RunMind</h1>

<p align="center">
  <strong>One workspace for your models, your projects, and your Windows machine.</strong><br>
  A local-first AI workspace that brings chat, research, developer tools, and Windows system intelligence together.
</p>

<p align="center">
  <sub>Windows 11 x64&nbsp; · &nbsp;.NET 10&nbsp; · &nbsp;C# and JavaScript&nbsp; · &nbsp;MIT License</sub>
</p>

<p align="center">
  <img src="screenshots/Screenshot-1.png" alt="RunMind Runner showing a local model result over the Windows desktop" width="960">
</p>

RunMind is more than a chat window. Connect a local or OpenAI-compatible model, give the Agent a selected project workspace, and use one desktop environment to understand code, investigate problems, run approved development workflows, research with sources, create deliverables, and inspect Windows system health.

RunMind is **local-first, not offline-only**: local model servers can keep inference on your machine, while hosted providers, web search, and connected MCP servers may receive the data sent to them.

<p align="center">
  <a href="docs/getting-started/BUILD-AND-SETUP.md"><strong>Build and setup</strong></a>
  &nbsp; · &nbsp;
  <a href="docs/README.md"><strong>Documentation</strong></a>
  &nbsp; · &nbsp;
  <a href="docs/product/FEATURE-MATRIX.md"><strong>Feature matrix</strong></a>
  &nbsp; · &nbsp;
  <a href="CHANGELOG.md"><strong>Changelog</strong></a>
</p>

## What RunMind brings together

- **Project intelligence** — understand project structure, architecture, dependencies, tests, and diagnostics. Change Impact Explorer analyzes Git changes, symbol and dependency relationships, assets, candidate tests, and available coverage evidence.
- **A tool-using Agent** — plan multi-step work across approved workspace files, Git, structured data, developer tools, native Windows capabilities, Skills, and connected MCP servers. Checkpoints make interrupted work reviewable; side effects are not blindly replayed.
- **Deep Research** — create an editable research plan, collect sources and evidence, assess claims, pause or resume, and export a Markdown report.
- **Developer workflows** — Node.js debugging and profiling, Playwright browser checks, API/backend testing, development-server management, SQLite inspection, and container workflows.
- **Windows System Manager** — inspect applications, processes, services, events, crashes, network, storage, security posture, scheduled tasks, and supported application adapters.
- **Live Resource and Process Intelligence** — start bounded CPU, memory, GPU, network, and process-I/O monitoring; inspect live processes and use guarded close, terminate, and restart actions for eligible processes.
- **Useful deliverables** — create Markdown/HTML reports, Word and Excel files, PDFs, SVG charts and diagrams, and image outputs.

The capabilities are evidence-led and deliberately bounded. Static analysis cannot discover every runtime dependency, telemetry depends on Windows providers and drivers, and generated artifacts still need factual and domain review.

## Choose how to open RunMind

| Host | Full Agent | Best for |
|---|:---:|---|
| **Standalone** | Yes | The complete native Windows desktop workspace. No PowerToys required. |
| **PowerToys Run** | Yes | Launch RunMind from search and request an inline model answer with `llm <prompt>`. |
| **Command Palette** | Yes | Keyboard-first access to inline answers and the RunMind chat experience inside the Command Palette extension. |
| **RunMind Runner** | No | A separate lightweight launcher for apps, files, folders, URLs, calculator results, and inline model answers. |

The first three hosts share the Full Agent experience. **Runner is intentionally a restricted Quick Host**, not another Full Agent: it does not provide project/workspace access, file attachments, developer tools, MCP, Skills, or System Manager. Use its handoff to open the real RunMind workspace when a task needs those capabilities.

## Work with the model stack you choose

| Provider or integration | How RunMind uses it |
|---|---|
| **Ollama** | Local models through the native API, commonly `http://localhost:11434`. |
| **LM Studio** | Local models through its native or OpenAI-compatible server, commonly `http://localhost:1234/v1`. |
| **OpenAI-compatible endpoints** | Additional hosted or self-hosted model endpoints configured by the user. |
| **SearXNG** | Optional web search and multi-query research. |
| **MCP** | External tools, resources, templates, and prompts over Streamable HTTP. STDIO transport is not included. |

Vision, tool calling, context size, and other model features depend on the selected model and provider. Remote services receive the prompts, queries, or tool arguments sent to them; connect only services you trust.

## See RunMind in action

<p align="center">
  <img src="screenshots/Screenshot-2.png" alt="RunMind full chat workspace running as a Windows desktop application" width="960">
</p>

<p align="center">
  <sub>RunMind's full workspace and the lightweight Runner are separate hosts built around the same product.</sub>
</p>

## Security and privacy are part of the design

- **Workspace boundaries:** the Agent works within the selected workspace. File changes use the applicable approval rules and recovery protections.
- **Native action checks:** system-changing operations use native permission checks and confirmation. System Manager is disabled until enabled.
- **No false sandbox promise:** approved PowerShell and native process commands run with the permissions of your Windows account. RunMind does not silently elevate and is not a security sandbox.
- **Untrusted integrations:** MCP responses, Skills, repository content, and adapter metadata cannot grant themselves permissions. Skill scripts are not automatically executed.
- **Credential caveat:** credential protection differs between hosts. Full Chat and shared PowerToys Run/Command Palette credential stores are not all DPAPI-protected. Review the [Security and Privacy guide](docs/security/SECURITY-AND-PRIVACY.md) before storing sensitive credentials.

## Get started

### Requirements

- Windows 11 x64, build 22621 or later. ARM64 is not currently packaged.
- .NET 10 SDK to build; .NET 10 Desktop Runtime for the framework-dependent Standalone and Runner publications.
- Microsoft Edge WebView2 Runtime.
- Node.js 24+ for the automated JavaScript regression suite and Node-based developer integrations.
- Git for Windows on `PATH` for Git-backed project tools.
- Ollama, LM Studio, or another OpenAI-compatible model server for model features.
- Microsoft PowerToys only for the PowerToys Run plugin and Command Palette extension.

### Build the source

From the repository root on Windows, run:

```bat
Build-Release-x64.bat
```

The script builds the four x64 hosts, publishes Standalone and Runner, and packages/signs the Command Palette MSIX. It does **not** install the hosts. Install or update each optional host separately using its corresponding installer script.

For prerequisites, output paths, installation steps, OCR/PDF asset preparation, model-server CORS setup, and validation commands, see the [Build and Setup guide](docs/getting-started/BUILD-AND-SETUP.md).

> **Source status:** This repository snapshot is labelled 3.4.0, but host/package version metadata is not yet uniform and the current source has not completed native Windows release certification. See the [Build and Validation Status](docs/project/BUILD-STATUS.md) before treating it as a certified release.

## Documentation

- [Documentation index](docs/README.md) — maintained guides grouped by topic.
- [Feature matrix](docs/product/FEATURE-MATRIX.md) — host capabilities and explicit implementation boundaries.
- [Advanced Intelligence & Extensibility](docs/product/ADVANCED-INTELLIGENCE-AND-EXTENSIBILITY.md) — Change Impact Explorer, Development Session Timeline, System Intelligence, orchestration, MCP, Skills, adapters, and Runner plugins.
- [Developer workflows](docs/developer/DEVELOPMENT-INTELLIGENCE.md) — build/test intelligence and project development workflows.
- [System Manager](docs/system/SYSTEM-MANAGER-OPERATIONS.md) — Windows diagnostics, operations, and safeguards.
- [Build status](docs/project/BUILD-STATUS.md) and [roadmap](ROADMAP.md) — verification state and remaining priorities.
- [Release notes](docs/releases/3.4.0-RELEASE-NOTES.md) and [changelog](CHANGELOG.md) — current release summary and product history.
- [Historical archive](docs/archive/README.md) — milestone audits, acceptance checklists, and prior validation reports.

## License

RunMind is released under the [MIT License](LICENSE.txt).
