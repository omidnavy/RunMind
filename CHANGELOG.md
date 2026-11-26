# Changelog

This is the **single maintained changelog** for RunMind. It records release-level product milestones, not every implementation patch. For current capabilities and operational details, use the [repository README](README.md) and the [documentation index](docs/README.md). Historical implementation notes are preserved separately in the [development history archive](docs/archive/development-history/DEVELOPMENT-HISTORY.md); that archive is not a second current changelog.

## 3.4.1 — Release metadata alignment and repository documentation

- Aligned all four Windows host project versions at `3.4.1`, the PowerToys Run plugin and Runner installer display metadata at `3.4.1`, and the Command Palette MSIX project/package identity at `3.4.1.0`.
- Updated MCP initialization client identification to report `RunMind` version `3.4.1`.
- Extended the host-version regression check to cover the four host projects, plugin manifest, MSIX identity, MCP client identification, and Runner uninstall display metadata.
- Reworked the root README into the GitHub repository front page, restored the bundled screenshots with accurate historical-capture labels, and improved product, host, integration, security, build, and documentation guidance.
- This is a source/documentation and release-metadata update. It does not introduce a new application feature or intentionally change runtime behavior. Windows build, packaging, installation, and native acceptance remain release gates.

See the [3.4.1 release notes](docs/releases/3.4.1-RELEASE-NOTES.md) and [current build status](docs/project/BUILD-STATUS.md).

## 3.4.0 — Runner and intelligence expansion

- Added the optional Runner Quick Host with unified launcher search, inline model answers, configurable activation and result weighting, dynamic window sizing, and handoff to the full Standalone workspace.
- Expanded Change Impact Explorer with Git-aware change collection, symbol/dependency/asset impact analysis, evidence-ranked test candidates, and confidence-aware risk reporting.
- Added Live Resource Intelligence with bounded CPU, memory, GPU, network, and process-I/O monitoring, in-memory charts, and CSV export.
- Added Process Intelligence with on-demand process inspection and separately confirmed close, force-terminate, and guarded restart actions, including process-identity checks and service/protected-process safeguards.
- Consolidated product, setup, security, and build documentation for the 3.4.0 feature line.

See the [3.4.0 release notes](docs/releases/3.4.0-RELEASE-NOTES.md) for feature scope and limitations at that release point.

## 3.3.x — Developer intelligence and stabilization

- Added Node.js debugging and profiling, Playwright browser workflows, API/backend testing, and development-server management.
- Hardened shared WebView/native bridge boundaries, bounded diagnostic process output, and improved WebView2 startup coordination.
- Identified credential-at-rest protection and Windows certification as release gates; portable tests are not production sign-off.

## 3.2.x — System Manager health intelligence

- Added event/crash correlation, finite resource sampling, storage/reliability reporting, security posture, and targeted network diagnostics.
- Expanded application, service, process, and adapter management with native permission checks and confirmations.

## 3.1.x — Agent capabilities

- Added bounded orchestration, task checkpoints and recovery review, MCP resource discovery, and expanded Skills/project context.
- Added artifact verification and evidence-aware Agent workflows.

## 3.0.x — Developer infrastructure

- Added Node.js debugger/profiling foundations, Playwright integrations, API testing, development-server lifecycle management, SQLite, and container workflows.

## 2.8.x — Project intelligence

- Added project understanding/diagnostics, dependency and test impact analysis, and Project Mission Control.

## 2.7.x — Workspace foundation

- Established unified workspace selection and bounded native file operations shared by the Full Agent hosts.

## Historical records

Entries before the summarized milestones above remain available in the [development history archive](docs/archive/development-history/DEVELOPMENT-HISTORY.md). Their original version labels, test counts, and validation statements are historical records and must not be interpreted as current 3.4.1 verification.
