# RunMind roadmap

This roadmap describes current priorities. Completed milestones and implementation snapshots live in [the archive](docs/archive/README.md), not in this file.

## Current release line: 3.5.0

RunMind has four access points: Standalone, PowerToys Run, Command Palette and Runner. The first three host the Full Agent. Runner is a separate restricted Quick Host. Current major capabilities include Project Intelligence, Change Impact Explorer v2, Deep Research, Agent orchestration/checkpoints, Developer Infrastructure, System Manager, Live Resource Intelligence, Process Intelligence, MCP/Skills and unified Runner search.

## Priority 0 — release hardening

1. **Windows certification:** compile and package the current source for all four hosts; install/update each optional host; verify WebView2, PowerToys, Command Palette activation, Runner handoff, System Manager, GPU counters and Process Intelligence actions against the current acceptance checklists.
2. **Credential protection at rest:** move Full Chat provider/Jina/Reader/MCP credentials and shared PowerToys Run/Command Palette inline credentials behind a native DPAPI CurrentUser vault. Include transactional migration, rollback, multiple-host contention, backup/restore and failure-injection tests. Do not delete old credential values until retrieval from the new store has been verified.
3. **Dependency assurance:** restore on Windows, inspect NuGet direct/transitive dependencies, run vulnerability and license checks, generate an SBOM, and retain the reports outside the source tree.
4. **Reproducible OCR resources:** pin the exact English/Persian trained-data commits and checksums, and verify the fixed-version npm tarballs against committed SHA-512 integrity values.

## Priority 1 — shared Quick Agentic Mode

A bounded tool-using Quick Agent mode remains a separate future milestone for PowerToys Run, Command Palette and Runner. It is not the same as today's inline LLM answer. The three hosts should share one policy/tool implementation; host-specific shortcuts must not create three different agents.

## Priority 2 — deeper intelligence

- Change Impact Explorer: broaden framework-specific/runtime dependency evidence and integrate verified test execution results while preserving explicit uncertainty.
- System Intelligence: correlate resource, service and event evidence over opt-in sessions without introducing an always-on service by default.
- Development Session Timeline: improve evidence navigation and recovery visibility without automatically replaying uncertain side effects.
- Advanced orchestration: improve long-task observability, cancellation and resumability while retaining approval barriers.

## Priority 3 — ecosystem

- Strengthen the extension contract and compatibility/versioning guidance.
- Improve MCP server setup, diagnostics and security visibility.
- Keep Skills and declarative adapters clearly separated from executable plugins; neither should be able to grant permissions.

## Scope discipline

The code-edit/refactor tooling scope is deliberately limited to JavaScript, TypeScript, JSX/TSX, HTML and CSS. SVG is a first-class vector asset, not a programming language handled by the code refactoring engine. Prefer strengthening an existing shared capability over adding another parallel subsystem. New background services, persistent telemetry stores, privileged brokers, runtime dependencies or plugin execution models require a concrete user need, a threat review and a measurable benefit before implementation.
