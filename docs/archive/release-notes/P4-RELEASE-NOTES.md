> **Archive:** Historical development record. This file describes the source state at the time it was written; use the current README, build guides, feature matrix, and feature-specific documentation for present behavior.

# RunMind 3.1.0 — P4 Agent Capabilities

RunMind 3.1.0 retains the complete P0 reliability foundation, P1 Project Intelligence/Mission Control, P2 System Manager Intelligence and P3 Developer Infrastructure. P4 strengthens the shared Agent Core across Standalone, PowerToys Run and PowerToys Command Palette.

## Included capabilities

1. **MCP resource discovery:** protocol/server metadata, paginated tools/resources/resource templates/prompts, repeated-cursor and size limits, metadata search, safe expansion of exact advertised URI templates, accurate incomplete/error reporting, and separate approval before reading resource contents or executing remote tools.
2. **Multi-tool orchestration:** deterministic dependency layers, bounded parallel execution for explicitly classified independent read-only operations, serialized barriers for writes/approvals/MCP/delegation, duplicate-call serialization, dependency IDs, operation history and visible execution graph. Concurrency is configurable from one to four.
3. **Task checkpoints and recovery:** bounded, redacted per-chat checkpoints, task plan/progress, linked tool-operation IDs, interrupted/unknown outcomes, explicit review-and-resume, same-workspace requirement and fresh approvals. Uncertain non-idempotent operations are inspected before any retry; no automatic replay occurs.
4. **Tool-result summarization:** bounded structured summaries, secret redaction, depth/item/property caps, omission of raw bodies/HTML/binary/base64 payloads, JSON-safe truncation and summarized Agent context plus local operation history.
5. **Skills and project context:** bounded discovery of project instructions and package metadata, current index coverage/architecture context, relevance-ranked Skill metadata and loaded Skill names. Skills are not silently auto-executed; project instructions and Skill content remain untrusted guidance and never grant tool permissions.
6. **Change Impact Explorer (original P4):** explicit changed workspace-relative paths/symbols; direct/transitive JS/TS import dependents, test candidates, HTML/CSS/SVG asset consumers, and unknown/unanalysed paths. The 3.4.0 source enhancement is documented below and in `docs/CHANGE-IMPACT-EXPLORER-V2.md`.
7. **Development Session Timeline:** chronological local view of checkpoint, tool-operation/dependency-layer and sub-agent evidence, available both in the Workflow panel and as a read-only Agent tool.
8. **Artifact Verification:** structural checks and SHA-256 over the exact workspace artifact bytes, optional independently supplied expected digest comparison, and bounded verification receipt. A digest match does not prove provenance, signer identity or trustworthiness.

## Boundaries

- MCP catalogue discovery reads advertised metadata only. Resource body reads, prompt retrieval, and remote MCP tool calls remain separately approved. Pagination and catalogue sizes are bounded.
- Only explicitly independent read-only calls may share a parallel group. Writes, approvals, MCP operations and delegation remain serialized barriers. The approval UI is not opened concurrently.
- Checkpoints are local to the current chat/WebView profile and bounded to the latest 20 records. They do not persist raw conversation transcripts, credentials or full tool payloads. Resume is explicit and requires the same workspace identity.
- Project source analysis remains JavaScript, TypeScript, JSX/TSX, HTML and CSS. SVG is an indexed vector asset, not a programming language. Other source files remain generic text.
- Change impact is static and index-dependent; dynamic imports and runtime-generated relationships may be missed.
- Session timelines contain recorded RunMind events only. They do not ingest arbitrary logs or prove causal relationships.
- SHA-256 verifies byte identity only. It is not a digital signature or provenance proof.
- No new runtime package dependency is introduced by P4.

## Validation boundary

Portable test and syntax results are recorded in `BUILD-STATUS.md`. Windows/.NET compilation and live acceptance across all three Windows hosts remain a target-machine gate; do not label this release Windows-certified until `Validate-Phase1-Windows.ps1` and the P0–P4 Windows checklists pass on Windows with .NET 10 and Node.js 24+.


## Change Impact Explorer v2 enhancement — 2026-11-24

The 3.4.0 shared source upgrades the P4 analyzer with Git working-tree and optional local base-ref intake (fresh `gitRead` approval), AST-inferred changed symbols, TypeScript checker consumers, deleted/renamed module importer candidates, expanded HTML/CSS/SVG/media/font asset edges, dynamic dependency signals, Istanbul/LCOV changed-line coverage, test recommendations, blast-radius and transparent evidence-based risk/confidence reports. It remains read-only and does not execute tests.

See `docs/CHANGE-IMPACT-EXPLORER-V2.md` for exact behavior and limits. Static analysis cannot prove all runtime dependencies; risk/confidence are heuristics, not probabilities. Windows acceptance remains pending.
