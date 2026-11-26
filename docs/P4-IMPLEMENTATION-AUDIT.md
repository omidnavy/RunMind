# RunMind 3.2.0 — P4 implementation audit

## Scope

This audit maps every P4 request to shipped source behavior. P4 reuses the shared Agent Core, workspace, project index, approval UI, workflow operation ledger, local conversation storage and artifact reader. It does not introduce a second agent executor or persistent event database.

| Requested capability | Implementation | Evidence / boundary |
|---|---|---|
| Better MCP resource discovery | `agent/mcp.js` | Server info/capabilities; tools, resources, resource templates and prompts; 20-page/2,000-item limits; repeated-cursor protection; metadata search; exact URI-template expansion; error/truncation-aware discovery completeness. Resource reads and remote calls require fresh approval. |
| More robust multi-tool orchestration | `agent/p4-logic.js`, `agent/workflow.js` | Deterministic groups; 1–4 worker limit; independent read-only parallel groups; serialized state-changing/approval/MCP/delegated barriers; duplicate-call serialization; dependency layer and predecessor IDs; visible execution graph. |
| Task checkpoints and recovery | `agent/workflow.js`, `agent/runtime.js` | Per-chat bounded checkpoint records; sanitized request/plan/progress; operation links; closed WebView marks unfinished operations unknown/interrupted; explicit Resume control; same-workspace guard; prompt instructs inspect-before-retry and fresh approval. No automatic operation replay. |
| Better tool-result summarization | `agent/p4-logic.js`, `agent/workflow.js`, `agent/runtime.js` | Depth, node, array and object caps; common-secret redaction; body/HTML/base64 omission; valid JSON truncation envelope; summarized model context and operation previews. |
| Improved Skills/project context | `agent/workspace.js`, `agent/runtime.js`, `core.js` | Bounded AGENTS.md/project instruction/package/index context; relevance-ranked Skill metadata and enabled Skill names; explicit Skill loading remains user/tool-controlled. Project instructions and Skill content are untrusted guidance, not authorization. |
| Change Impact Explorer | `agent/project-index.js`, `agent/development-intelligence.js`, `agent/tools.js`, `providers.js`, `AgentHostBridge.cs`, `agent/developer-workflows.js` | v2 adds Git working-tree discovery, optional local base-ref comparison, automatic AST symbol inference, checker references, expanded HTML/CSS/SVG/media/font asset graph, dynamic dependency signals, removed-module importer candidates, Istanbul/LCOV line evidence, risk/blast-radius and completeness reporting. See `docs/CHANGE-IMPACT-EXPLORER-V2.md`. |
| Development Session Timeline | `agent/workflow.js`, `agent/tools.js`, `providers.js`, `Shared/ChatWindow.html` | Chronological local checkpoint, tool operation/dependency and sub-agent events in Workflow UI and read-only Agent tool. Bounded response; no arbitrary log ingestion or causal claim. |
| Artifact Verification | `agent/artifacts.js`, `agent/tools.js`, `providers.js` | Exact file-byte SHA-256, structural format checks, optional expected digest comparison and bounded receipt. Digest match is not provenance/authenticity proof. |

## Shared-host deployment

- Standalone, PowerToys Run and PowerToys Command Palette project versions are `3.2.0`.
- Command Palette Appx identity is `3.2.0.0`.
- `p4-logic.js` and all Agent HTML/JS/CSS assets are included by the shared ChatWindow content deployment in all hosts.
- All P4 Agent definitions and dispatch routes live in shared provider/tool files.
- No new npm package/runtime dependency was added.

## Safety review

- MCP metadata is untrusted; tool/resource reads and remote MCP execution remain approval-gated.
- Only declared independent read-only calls can run concurrently. Duplicate calls are serialized. Operations using approvals, side effects, MCP or delegated agents are barriers.
- The task checkpoint persists only bounded/redacted metadata, never raw bodies. If operation status is uncertain after interruption, the Agent must inspect current state before retrying. Resume is user-initiated and workspace-bound.
- Project instructions and Skill descriptions/content are not trusted policy or permission grants.
- Change impact remains static, bounded and tied to the selected workspace's current index. Git-derived paths require fresh `gitRead` approval; branch comparisons use existing local refs and never fetch or mutate Git.
- Timeline data is local to the chat and contains only events RunMind recorded.
- SHA-256 comparison verifies equality to a supplied digest only; the digest must come from an independently trusted channel to provide meaningful integrity evidence.

## Known limits

- MCP catalogue discovery reflects server-advertised metadata; it cannot guarantee server implementation quality or freshness beyond the last successful discovery.
- Tool parallelism is conservative and based on a maintained allow-list, not speculative dependency inference. It does not parallelize MCP calls or approval-gated operations.
- Checkpoints are localStorage/WebView-profile records, bounded to 20 per chat. They are not synchronized between hosts or devices and do not restore a full model transcript.
- Runtime-computed imports, reflection, dependency injection, generated loaders, external runtime configuration and unindexed dependencies cannot be exhaustively resolved statically. Recognizable dynamic import/require, `import.meta.glob` and `require.context` sites are surfaced; analyzer caps are reported.
- The timeline is not an OpenTelemetry backend, a log warehouse or a causal tracing engine.
- Artifact Verification does not perform Authenticode/code-signature validation, SBOM comparison, certificate trust validation or remote provenance attestation.
- This Linux environment cannot compile .NET 10 Windows targets or run WebView2/PowerToys. Windows acceptance remains pending.


## Change Impact Explorer v2 source enhancement — 2026-11-24

The existing P4 Change Impact Explorer is upgraded in the 3.4.0 source tree rather than replaced. The detailed contract and acceptance steps are in `docs/CHANGE-IMPACT-EXPLORER-V2.md`.

- Default invocation now collects the selected Git working tree after fresh `gitRead` approval; it handles staged, unstaged, rename/delete and untracked paths. An optional existing local `base_ref` adds `<base>...HEAD` plus current worktree changes without fetching.
- Diff hunks and untracked file contents are mapped to JS/TS AST declaration ranges. TypeScript checker references and resolved import bindings identify symbol consumers; removed declaration names are reported as conservative candidates.
- Asset references cover HTML media/script/link/iframe/track/embed tags, `srcset`, inline/embedded CSS, CSS `url()`/`@import`, SVG local references and JS/TS `new URL(..., import.meta.url)`. Unindexed image/font/media files can still resolve to indexed owners.
- Dynamic import/require, `require.context` and `import.meta.glob` signals are surfaced. Optional Istanbul/LCOV changed-line evidence distinguishes covered, uncovered and unmeasured lines without claiming individual-test attribution.
- Transparent blast-radius, weighted risk factors and evidence-coverage confidence expose index/compiler/parser/asset/dynamic/coverage uncertainty. Scores are explicitly not probabilities.
- Project-index v7 retains original bounded HTML/CSS/SVG text for case-preserving asset resolution. The current index v6 is rebuilt rather than being interpreted as v7.

Validation: full portable Node regression suite **307/307 passed** under Node.js 22.16.0, including nine focused Change Impact Explorer v2 tests. Windows Release x64/.NET 10 compilation and interactive Git acceptance remain pending; run the documented Windows validation checklist before certifying.
