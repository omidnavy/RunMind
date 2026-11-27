# Changelog

## 3.5.0 — Agent Toolsets and host-specific GitHub release archives

- Add editable Agent Toolset profiles, group-level preferences, individual tool overrides, and a live inventory view of tool configuration and exposure.
- Preserve System Manager's existing native master/capability settings and workspace access conditions as authoritative availability gates.
- Apply one shared tool-eligibility policy to model-facing tool definitions and execution dispatch; disabled tools are not exposed and cannot be invoked through the Agent executor.
- Extend `list_available_tools` with provider-neutral toolset reporting, per-tool execution counts and last outcomes while preserving its existing pagination and exact exposed-tools list. Accepted-but-unverified application handoffs remain distinct from failures.
- Keep provider-specific tool-call schemas and streaming behavior inside the existing adapters.
- Extend `Build-Release-x64.bat` to create `standalone-3.5.0.zip`, `runner-3.5.0.zip`, `powertoysrun-3.5.0.zip`, and `commandpalette-3.5.0.zip` under `Release\GitHub-Releases`.
- Include the corresponding installers and required deployment scripts in the PowerToys Run, Command Palette, and Runner release archives.
- Align all four host versions, PowerToys Run metadata, Runner installer metadata, MCP client identification, and Command Palette MSIX identity at `3.5.0` (`3.5.0.0` for MSIX).

This file records product-level milestones, not every implementation patch. Detailed historical entries are retained in [the archived changelog](docs/archive/historical-changelog/CHANGELOG-DETAILED-HISTORY.md).

## 3.4.1 — Release metadata alignment and repository front page

- Unified the four host project versions at `3.4.1`, the PowerToys Run plugin and Runner installer metadata at `3.4.1`, and the Command Palette MSIX project/package identity at `3.4.1.0`.
- Updated the MCP client identification version to `3.4.1` so remote MCP servers receive the current client version.
- Expanded the host-version regression check to cover all four projects, plugin manifest, MSIX identity, MCP client identification and Runner uninstall display metadata.
- Rebuilt the GitHub README as the repository front page, restoring all bundled screenshots with clear historical-capture labels and adding practical product, host, integration, security, build and documentation guidance.
- No new application feature or behavioral change is intended in this release; native Windows build and acceptance remain required before binary-release certification.

## 3.4.0 — Runner and intelligence expansion

- Added the optional Runner Quick Host with unified launcher search, inline model answers, configurable activation/ranking, dynamic sizing and Full Chat handoff.
- Expanded Change Impact Explorer with Git-aware changesets, symbol/dependency/asset impact, test candidates, coverage evidence and confidence-aware risk reporting.
- Added Live Resource Intelligence with bounded CPU, memory, GPU, network and process-I/O monitoring.
- Added Process Intelligence: on-demand process inspection and confirmed close/terminate/restart actions with process identity and service/protected-process safeguards.
- Refreshed repository front-page documentation and consolidated current build/security guidance.

## 3.3.x — Developer intelligence and stabilization

- Added Node Inspector debugging/profiling, Playwright browser workflows, API/backend testing and development-server management.
- Hardened shared WebView/native bridge boundaries, bounded diagnostic process output and improved WebView2 startup coordination.
- Identified credential-at-rest protection and Windows certification as open release gates; portable tests are not production sign-off.

## 3.2.x — System Manager health intelligence

- Added event/crash correlation, finite resource sampling, storage/reliability reporting, security posture and targeted network diagnostics.
- Expanded application/service/process and adapter management with native permission checks and confirmations.

## 3.1.x — Agent capabilities

- Added bounded orchestration, task checkpoints/recovery review, MCP resource discovery and expanded Skills/project context.
- Added artifact verification and evidence-aware Agent workflows.

## 3.0.x — Developer infrastructure

- Added Node.js debugger/profiling foundations, Playwright integrations, API testing, development-server lifecycle management, SQLite and container workflows.

## 2.8.x — Project intelligence

- Added project understanding/diagnostics, dependency and test impact analysis, and Project Mission Control.

## 2.7.x — Workspace foundation

- Established unified workspace selection and bounded native file operations shared by the Full Agent hosts.
