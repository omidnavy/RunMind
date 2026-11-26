# Project Understanding and Project Diagnostics

These are separate developer capabilities built on the same persistent project index.

## Project Understanding

Project Understanding is the codebase intelligence layer. It maps architecture, entry points, modules, dependency direction, exports, symbols, references, calls, and important execution paths. It uses the persistent index, JavaScript AST analysis, TypeScript compiler/checker analysis where supported, module-resolution evidence, and source inspection. It is intended to be reused by implementation, debugging, code review, and impact analysis.

It does not run build commands, install dependencies, or change project files. Import-graph heuristics are discovery hints; important relationships must be checked in source.

## Project Diagnostics

Project Diagnostics is the operational health/readiness layer. It checks manifest validity, dependency and lockfile consistency, configured toolchains, project scripts, build/test/lint configuration, test discovery, environment requirements, and—when explicitly approved—installed runtime/tool versions and actual build/test/audit results.

Static observations, live environment probes, and executed commands must be reported separately. The workflow is read-only by default. It does not install, upgrade, repair, format, or modify source/configuration files.

## Relationship to Build & Test and Debugging

Project Diagnostics can identify which build/test commands are configured and whether prerequisites appear ready. Build & Test executes selected project commands only after displaying the command and receiving fresh approval. Debugging investigates runtime behavior using the relevant runtime debugger, logs, and reproduction steps.

All three consume Project Understanding rather than duplicating its index or symbol graph.

## Current workspace limitation

The browser file workspace and native execution workspace are still separate in this release because browser directory handles do not expose a canonical Windows path. Static Project Understanding and Project Diagnostics complete using the selected browser workspace. Live environment checks or native command execution require the native workspace and fresh per-command approval. Do not select the same folder twice just to finish static diagnostics.
