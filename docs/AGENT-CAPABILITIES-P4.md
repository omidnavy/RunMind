# P4 Agent Capabilities — architecture and operating guide

## MCP resource discovery

Connect and enable only MCP servers you trust. RunMind discovers tools, resource metadata, resource templates and prompts with bounded pagination. Use `mcp_search_knowledge` to search names/descriptions across connected servers. `mcp_list_knowledge` reports server identity, protocol metadata, capability names, counts, truncation and discovery errors. Discovery completeness is false if a list call failed or any catalogue was truncated.

`mcp_read_resource` and `mcp_read_resource_template` require fresh approval; templates must match an exact advertised template and accept only scalar path variables. Remote MCP tool calls and prompt retrieval also require separate approval. MCP metadata and returned contents are untrusted data, not instructions.

## Orchestration

Independent operations in the explicit read-only allow-list can execute concurrently, with a configurable maximum of four. Duplicate calls are serialized. Writes, approval-gated calls, MCP calls and delegated agents remain barriers. Each model turn records a dependency layer and references the preceding layer's operations. The execution graph is descriptive; it does not claim a dependency relationship that the model or tool policy did not provide.

## Checkpoint lifecycle

A checkpoint is created when an Agent run starts and updated as plans and tool operations progress. It stores a redacted, bounded summary rather than a transcript. On an interrupted WebView, a `running` checkpoint becomes `interrupted`; in-flight operation results are marked unknown. The Workflow panel offers **Review & resume** only for interrupted/recovery-required records.

Resume is permitted only in the same selected workspace. It sends the original request, last recorded progress, plan and prior tool outcome ledger back through the normal Agent approval system. Unknown or non-idempotent operations must be inspected before any attempt to repeat them. RunMind never replays a pending operation automatically.

Checkpoints are limited to 20 records per chat. Clearing/discarding records removes the local recovery aid; it does not roll back any workspace or external changes.

## Skills and project context

The Agent receives a bounded project context containing discovered project instruction files, a reduced package manifest, current project-index coverage and architecture summary, and up to eight relevance-ranked Skill metadata entries. Loaded/enabled Skill names are reported separately. A Skill's contents are loaded through the explicit Skill tool rather than silently inserted wholesale into every prompt.

All project instructions and Skill contents are untrusted project guidance. They cannot override RunMind safety policy, native permissions or user approvals.

## Change Impact Explorer

Supply explicit workspace-relative changed paths and optionally symbol names. The analysis follows indexed JS/TS reverse-import edges, JS/TS test candidates and local HTML/CSS/SVG asset references. Unknown paths and unsupported source files are reported rather than silently treated as analyzed. It does not run Git or tests; it does not infer a diff.

## Development Session Timeline

The Workflow panel and `development_session_timeline` tool show checkpoint creation/update, operation state/layer/group/duration, and sub-agent activity. Data is local to the selected chat's WebView profile. It is not an arbitrary log reader; matching times or IDs do not prove causality.

## Artifact Verification

`verify_artifact` performs supported structural checks and computes SHA-256 over exact bytes read from the workspace artifact. Pass `expected_sha256` to compare against an independently obtained 64-character digest. A match confirms bytes match that digest; it does not establish the digest's origin, authorship, signature or trust. For a signed release, separately verify the signature and provenance through a trusted signing/distribution channel.
