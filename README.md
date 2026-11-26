<h1 align="center">RunMind</h1>

<p align="center">
  <strong>Understand your projects. Take control of your systems.</strong><br>
  A local-first AI workspace for Windows — bringing chat, research, developer intelligence, and system management together.
</p>

<p align="center">
  <sub>Windows 11 x64&nbsp; · &nbsp;.NET 10&nbsp; · &nbsp;C# / JavaScript&nbsp; · &nbsp;MIT License</sub>
</p>

<p align="center">
  <img src="screenshots/README-Workspace.png" alt="RunMind chat workspace" width="920">
</p>

RunMind is built for more than asking a model a question. It gives local and OpenAI-compatible models a practical workspace: understand a codebase, investigate a problem, work with files and data, run approved development tools, research a topic, or inspect the health of a Windows machine.

Use the full workspace when a task needs context and tools. Use one of the lightweight launchers when you just need to find something or ask a quick question. Your model connection, workspace access, and permissions stay under your control.

## One workspace. Four ways to open it.

| Host | Best for | What it provides |
|---|---|---|
| **Standalone** | The complete RunMind experience | A native Windows desktop app with the full chat, Agent, Deep Research, project tools, and System Manager. No PowerToys required. |
| **PowerToys Run** | Fast access from search | Search for RunMind or enter `llm <prompt>` for an inline model answer, then continue in the full workspace. |
| **Command Palette** | A keyboard-first workflow | Inline model answers and a RunMind chat experience hosted inside the Command Palette extension. |
| **RunMind Runner** | A separate, lightweight launcher | Quick search for applications, files, folders and URLs, a calculator, and an inline Ollama/LM Studio answer using `llm <prompt>`. It does not require PowerToys. |

**Know the boundary:** Runner is intentionally a Quick Host, not another Full Agent. It does not provide project/workspace access, file attachments, developer tools, MCP, Skills, or System Manager. Open the full RunMind chat when a task needs those capabilities.

## What you can do

### Work with real projects

RunMind can map and explain a project, inspect its structure, diagnose issues, and build a picture of its architecture and development workflow. Project intelligence includes:

- **Project Understanding, Diagnostics, and Mission Control** for architecture, scripts, dependencies, tests, and project health.
- **Change Impact Explorer** for Git-aware changed-file and symbol impact, reverse dependencies, asset references, candidate tests, coverage evidence, and a confidence-aware blast-radius report.
- **Developer workflows** for Node.js debugging and profiling, Playwright browser checks, API testing, development-server lifecycle, SQLite inspection, and container workflows.
- **Development Session Timeline** to review recorded Agent operations, checkpoints, interruptions, and outcomes before deciding what to resume.

The analysis is evidence-led: static analysis and runtime observations have limits, and RunMind reports uncertainty rather than pretending every dependency or cause is known.

### Let the Agent do the work

Agent Mode turns a request into a multi-step plan and uses the tools that are available in the current session. It can combine web research, selected workspace files, Git, structured data processing, document creation, developer tools, native Windows capabilities, Agent Skills, and connected MCP servers.

Independent read-only work can be orchestrated in parallel. Changes and other side effects remain controlled by the relevant approval and confirmation rules. Checkpoints help explain what happened when a task is interrupted; RunMind does not blindly replay unfinished side effects.

### Research with a trail of evidence

Deep Research builds an editable plan, gathers sources, keeps evidence notes and claim assessments, supports pause and resume, and exports a Markdown report. Source collection, quoted material, and model-generated assessments are kept distinguishable; a model's conclusion is not presented as independently proven fact.

### Understand and manage Windows

The optional **System Manager** brings application and process management together with Windows diagnostics. Depending on the capabilities you enable, it can inspect services, events, crashes, network connections, storage, security posture, applications, and processes. Actions that change system state use separate native permission checks and confirmation.

**Live Resource Intelligence** adds an explicitly started, bounded monitor with CPU, memory, GPU utilization, dedicated/shared GPU-memory, network, and process-I/O charts. Its integrated **Process Intelligence** view lets you filter and rank live processes, inspect executable/command-line, owner, security, parent/child and signature details, open the executable location, copy details, close gracefully, force-terminate or restart eligible processes. Service-hosted and protected processes are guarded; restart is limited to reconstructable same-user processes and cannot restore their original token or working directory. Per-process GPU attribution and the live working directory are not available from the supported Windows sources. Monitoring continues when the flyout is closed and ends when stopped, when its session expires, or when the RunMind window closes. Samples remain in memory for the session; there is no always-on monitoring service or persistent raw telemetry store. GPU readings depend on the counters exposed by the installed Windows graphics driver.

### Create useful deliverables

The Agent can work with structured data and create Markdown/HTML reports, Word documents, Excel workbooks, PDFs, editable SVG charts, and diagrams. It can also inspect, resize, crop, and convert images, prepare thumbnails, generate QR codes, and build image galleries. These tools have documented size and format limits; artifact verification checks structure, not the correctness of every calculation or visual detail.

## Models and integrations

RunMind is designed to work especially well with local model servers, while allowing other OpenAI-compatible endpoints when configured.

| Provider or service | Use |
|---|---|
| **Ollama** | Local models through the native API (`http://localhost:11434`). |
| **LM Studio** | Local models through its native or OpenAI-compatible server (commonly `http://localhost:1234/v1`). |
| **OpenAI-compatible endpoints** | Additional hosted or self-hosted model endpoints, with configurable authentication. |
| **SearXNG** | Optional web search and multi-query research. |
| **MCP (Streamable HTTP)** | Connect external tools, resources, and prompts to the Full Agent. STDIO transport is not included. |

Model features such as vision, tool calling, and context size depend on the selected model and provider. Web search, remote endpoints, and connected MCP servers may send data outside your machine; configure only services you trust.

## Control is part of the design

RunMind is a desktop application with real capabilities, not a sandbox. Its safety boundaries are explicit:

- **Workspace-scoped files:** select the folder the Agent may work in. File operations stay within that workspace; applicable changes require approval, and edits/deletions create recovery backups.
- **Native actions:** process, service, application, and other system-changing actions are independently checked and may require a Windows confirmation. System Manager is disabled until you enable it.
- **Current-user execution:** approved PowerShell and native process commands run with the permissions of your Windows account. They are not isolated in a security sandbox, and RunMind does not provide administrator elevation or arbitrary GUI automation.
- **Untrusted extensions:** MCP responses, Skills, and adapter metadata cannot grant themselves permissions. Skill scripts are not automatically executed.
- **Local data:** conversations and settings are stored in the relevant local host/WebView profile. Credential protection differs between hosts; some settings are not DPAPI-encrypted. Protect your Windows account and do not commit local credentials or settings.

## Get started

### Requirements

- **Windows 11 x64**, build 22621 or later. ARM64 is not currently provided.
- **.NET 10 SDK** to build the source. The published Standalone and Runner hosts are framework-dependent and require the **.NET 10 Desktop Runtime**.
- **Microsoft Edge WebView2 Runtime**. RunMind checks for it at startup and can install the Evergreen runtime for the current user if it is missing.
- **Ollama, LM Studio, or another OpenAI-compatible model server** to use model features.
- **Git for Windows** on `PATH` for native Git workflows.
- **Microsoft PowerToys** only for the PowerToys Run plugin and Command Palette extension.
- **Node.js 24+** for the automated JavaScript regression suite and Node-based development integrations.

### Build the Windows hosts

From the repository root, run:

```bat
Build-Release-x64.bat
```

This builds the four x64 hosts, publishes Standalone and Runner, and packages/signs the Command Palette MSIX. It does **not** install them. On the first build, the script prepares the PDF/OCR runtime assets; the npm tarball URLs use fixed versions but are not verified against committed SHA-512 integrity values; English/Persian language data is downloaded from an upstream moving branch without content checksums. That preparation requires an internet connection.

The Standalone executable is published to:

```text
Release\Standalone\win-x64\publish\RunMind.exe
```

Install optional hosts separately:

- **PowerToys Run:** close PowerToys completely, then run `Install-RunMind-PowerToys-Run.bat` and restart PowerToys.
- **Command Palette:** run `Install-RunMind-CommandPalette.bat`.
- **Runner:** run `Install-RunMind-Runner.ps1` for the current user.

See [Build and setup](BUILD-AND-SETUP.md) for deployment details and troubleshooting. The repository is a source project; the build script creates the host outputs locally.

### Connect a local model

1. Start Ollama or LM Studio and make sure a model is available.
2. Open RunMind and select/configure the provider and model in its model settings.
3. Allow the RunMind WebView origin `https://localhost` in the model server's CORS settings. For Ollama, include it in `OLLAMA_ORIGINS`; for LM Studio, enable CORS in the local server settings.

For other OpenAI-compatible services, configure the endpoint's CORS policy to allow `https://localhost` and provide the required authentication settings in RunMind.

## Explore the project

| Guide | What it covers |
|---|---|
| [Feature Matrix](FEATURE-MATRIX.md) | Current capability map, host boundaries and implementation limits. |
| [Documentation index](docs/README.md) | Maintained feature and operations guides. |
| [Security and Privacy](docs/SECURITY-AND-PRIVACY.md) | Credential storage, execution boundaries, untrusted content and data flows. |
| [Advanced Intelligence & Extensibility](docs/ADVANCED-INTELLIGENCE-AND-EXTENSIBILITY.md) | Change Impact Explorer, Development Session Timeline, System Intelligence, Agent orchestration, MCP, Skills, adapters, and Runner boundaries. |
| [Project Understanding & Diagnostics](docs/PROJECT-UNDERSTANDING-AND-DIAGNOSTICS.md) | Project indexing, architecture understanding, diagnostics, and Mission Control. |
| [Development Intelligence](docs/DEVELOPMENT-INTELLIGENCE.md) | Developer tools and evidence-driven workflows. |
| [Live Resource Intelligence](docs/LIVE-RESOURCE-INTELLIGENCE.md) | Live monitoring behavior, metrics, bounds, and telemetry limitations. |
| [Process Intelligence](docs/PROCESS-INTELLIGENCE.md) | Process details, permissions, actions, restart constraints, and safeguards. |
| [Process Intelligence Windows acceptance](docs/PROCESS-INTELLIGENCE-WINDOWS-ACCEPTANCE.md) | Target-machine validation for process inspection and actions. |
| [System Manager health intelligence](docs/SYSTEM-MANAGER-HEALTH-INTELLIGENCE.md) | Windows health reports, event correlation, services, network, and storage. |
| [Runner implementation](docs/RUNNER-IMPLEMENTATION.md) | Runner architecture, plugins, and the Quick Host boundary. |
| [Build and setup](BUILD-AND-SETUP.md) | Build prerequisites, packaging, and host deployment. |
| [Build status](BUILD-STATUS.md) | Current source checks, open release gates and Windows validation status. |
| [Windows validation script](Validate-RunMind-Windows.ps1) | Runs portable tests, the complete Release x64 build and output checks. |
| [3.4.0 release notes](3.4.0-RELEASE-NOTES.md) · [Changelog](CHANGELOG.md) · [Roadmap](ROADMAP.md) | Release history and project direction — kept out of this front page. |

## Contributing and license

RunMind is released under the [MIT License](LICENSE.txt). The source is organized around shared chat/Agent logic and native Windows hosts, so changes to shared behavior should be validated across the hosts that consume it. Start with the relevant guide and `BUILD-STATUS.md`; portable JavaScript tests do not replace a Windows x64 build and host-level acceptance.

---

<p align="center">
  <strong>RunMind</strong><br>
  <sub>One workspace for your models, your projects, and your Windows machine.</sub>
</p>
