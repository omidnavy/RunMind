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
| Change Impact Explorer | `agent/project-index.js`, `agent/tools.js`, `providers.js`, `agent/developer-workflows.js` | Explicit JS/TS changed paths/symbols; reverse direct/transitive import dependencies; test candidates; HTML/CSS/SVG asset consumers; unknown/unanalysed paths. No implicit Git diff or test execution. |
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
- Change impact is static, bounded and tied to the selected workspace's current index.
- Timeline data is local to the chat and contains only events RunMind recorded.
- SHA-256 comparison verifies equality to a supplied digest only; the digest must come from an independently trusted channel to provide meaningful integrity evidence.

## Known limits

- MCP catalogue discovery reflects server-advertised metadata; it cannot guarantee server implementation quality or freshness beyond the last successful discovery.
- Tool parallelism is conservative and based on a maintained allow-list, not speculative dependency inference. It does not parallelize MCP calls or approval-gated operations.
- Checkpoints are localStorage/WebView-profile records, bounded to 20 per chat. They are not synchronized between hosts or devices and do not restore a full model transcript.
- Dynamic module loading, runtime-generated dependencies and non-indexed files can be missing from Change Impact results.
- The timeline is not an OpenTelemetry backend, a log warehouse or a causal tracing engine.
- Artifact Verification does not perform Authenticode/code-signature validation, SBOM comparison, certificate trust validation or remote provenance attestation.
- This Linux environment cannot compile .NET 10 Windows targets or run WebView2/PowerToys. Windows acceptance remains pending.
