# RunMind 3.1.0 — P4 Agent Capabilities Build Status

## Current source bundle

This source tree contains P0 Foundation & Reliability, P1 Project Intelligence/Mission Control, P2 System Manager Intelligence, P3 Developer Infrastructure and P4 Agent Capabilities for all three Windows hosts: RunMind Standalone, PowerToys Run and PowerToys Command Palette. All three share the same Agent Core, native bridge, canonical workspace, project index, approval model and capability enforcement.

P4 includes enhanced MCP resource discovery, bounded multi-tool orchestration, per-chat checkpoints and explicit recovery, bounded/redacted tool-result summaries, project/Skills context, Change Impact Explorer, Development Session Timeline and exact-byte artifact SHA-256 verification receipts. No new package/runtime dependency was added.

Project-specific language-aware analysis remains JavaScript, TypeScript, JSX/TSX, HTML and CSS. SVG is a first-class vector asset with bounded local-reference analysis, not a programming language. Other source files remain generic text for reading/search only.

## Validation actually performed

- Portable test runtime: **Node.js v22.16.0**. The intended target remains **Node.js 24+** and .NET 10.
- Full portable suite: **224/224 passed** with `node --test --test-concurrency=1` after P4 integration, version synchronization, MCP discovery-completeness correction and Timeline UI integration.
- P4 focused suite: **10/10 passed**.
- JavaScript/CJS/MJS syntax: **51/51 passed** with `node --check`; repository JSON: **4/4 parsed**; project/property/XAML/AppxManifest XML-family files: **7/7 parsed**. ZIP integrity, extracted-bundle test results and final SHA-256 are recorded after release packaging.
- Standalone, PowerToys Run and Command Palette source package versions are **3.1.0**; Command Palette Appx identity is **3.1.0.0**.
- C# compilation and live Windows runtime behavior were not run in this environment because the .NET SDK, C# compiler, Windows PowerShell and WebView2/PowerToys hosts are unavailable. MCP server integration, Windows-native acceptance and real project/container/runtime operations require target-machine validation.

## P4 behavior and boundaries

- **MCP discovery:** protocol/server metadata and paginated tools/resources/resource templates/prompts, with 20-page/2,000-item bounds, repeated-cursor detection, metadata search and accurate error/truncation reporting. Metadata search does not fetch resource contents or execute tools. Resource reads, template reads, prompt retrieval and remote tools require fresh approval.
- **Orchestration:** only explicitly classified independent read-only operations are parallelized, with 1–4 workers. Duplicate calls are serialized. Writes, approvals, MCP and delegated operations are barriers. Execution graph records dependency layers and operation links. No speculative dependency inference or parallel approval dialogs.
- **Checkpoints:** maximum 20 bounded/redacted records per chat. Checkpoints store task/plan/progress and operation IDs, not raw conversations or request/response bodies. Interrupted operations are marked unknown; resume is explicit and requires the same workspace. No automatic operation replay or external rollback.
- **Tool-result summaries:** bounded depth/node/property/array budgets, common credential redaction, valid JSON truncation and omission of raw bodies/HTML/binary/base64 fields. Summaries are not full-fidelity data exports.
- **Skills/project context:** bounded AGENTS.md/project instruction/package/project-index context and relevance-ranked Skill metadata. Skills remain explicitly loaded guidance; untrusted files and Skills cannot grant permissions or override RunMind policy.
- **Change Impact Explorer:** explicit paths/symbols, static JS/TS reverse dependencies/test candidates and HTML/CSS/SVG asset consumers. Unknown/unanalysed paths are disclosed. Dynamic/runtime relationships may be missed. No implicit Git diff or test execution.
- **Development Session Timeline:** chronological local checkpoint, tool operation/dependency and sub-agent events shown in Workflow UI and through a read-only Agent tool. It is chat-profile scoped and is not a persistent log warehouse or causal tracing system.
- **Artifact Verification:** exact-byte SHA-256 plus supported structural checks and optional comparison to an independently supplied expected digest. A matching digest does not establish provenance, signer identity or trust.

## Windows certification remains pending

Run on the target Windows machine with Node.js 24+ and .NET 10:

```powershell
.\Validate-Phase1-Windows.ps1 -Configuration Release -Platform x64
```

Then complete `docs/P0-WINDOWS-ACCEPTANCE.md`, `docs/P1-WINDOWS-ACCEPTANCE.md`, `docs/P2-WINDOWS-ACCEPTANCE.md`, `docs/P3-WINDOWS-ACCEPTANCE.md` and `docs/P4-WINDOWS-ACCEPTANCE.md` in Standalone, PowerToys Run and PowerToys Command Palette. Validate the real MCP servers and Windows tools you intend to use. Do not describe the bundle as Windows-certified until all applicable builds and acceptance gates pass.
