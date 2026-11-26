# RunMind 3.1.0 — P4 Windows acceptance checklist

Run after extracting the 3.1.0 source on Windows with Node.js 24+ and .NET 10. Repeat interactive checks in Standalone, PowerToys Run and PowerToys Command Palette.

## Build and version

- [ ] Run `.\Validate-Phase1-Windows.ps1 -Configuration Release -Platform x64` from the repository root in PowerShell.
- [ ] Confirm all three hosts compile/package with version `3.1.0` and Command Palette Appx identity `3.1.0.0`.
- [ ] Verify `p4-logic.js`, updated Agent scripts and Workflow UI are present in all three host outputs.
- [ ] Run `node --test --test-concurrency=1` using the installed Node.js 24 runtime.

## MCP discovery

- [ ] Connect a test MCP server that exposes tools, resources, resource templates and prompts.
- [ ] Confirm all advertised lists are discovered, paginated and counted correctly.
- [ ] Simulate a list method error and verify `discoveryComplete=false` with an explanatory error.
- [ ] Simulate repeated pagination cursor and more than 2,000 items; verify discovery is marked truncated without looping.
- [ ] Search across all four metadata categories; confirm search does not read resource bodies or execute remote tools.
- [ ] Read an advertised resource and expand a resource template only after fresh approval.
- [ ] Verify unadvertised templates, missing/extra parameters and control characters are rejected.
- [ ] Confirm remote MCP tool calls and prompt retrieval request fresh approval.

## Orchestration and summarization

- [ ] Issue a task with at least two independent read-only operations and verify bounded parallel execution.
- [ ] Include an edit/side-effect operation between reads; verify it creates a serialized barrier and the later reads wait for it.
- [ ] Repeat an identical tool call and verify duplicates are not dispatched concurrently.
- [ ] Set concurrency to 1, 2 and 4 and verify the operation graph reports actual grouping.
- [ ] Inspect summarized outputs containing a fake token, large body, nested values and large arrays; verify secrets/bodies are redacted/omitted and results remain readable.

## Checkpoints and recovery

- [ ] Start a multi-step Agent task and confirm a checkpoint appears in Workflow.
- [ ] Confirm plans, progress and operation IDs are recorded without raw bodies or credentials.
- [ ] Close the WebView during a deliberately safe in-flight test read; reopen and verify active checkpoint and operation become interrupted/unknown.
- [ ] Confirm no operation is automatically replayed.
- [ ] Click Review & resume in the same workspace and verify a fresh Agent turn is started through normal approvals.
- [ ] Attempt resume with a different workspace and confirm it is blocked.
- [ ] Verify non-idempotent operations require current-state inspection and fresh approval before a retry.
- [ ] Confirm a new conversation loads its own checkpoint/timeline state without exposing the previous chat's task history.

## Skills and project context

- [ ] Use a fixture workspace with AGENTS.md, package.json, `.agents/skills/<name>/SKILL.md` and an indexed JS/TS project.
- [ ] Verify bounded instruction/package/index context appears in the Agent prompt.
- [ ] Verify relevant Skill metadata is ranked and loaded Skill names are reported.
- [ ] Verify Skill content is loaded only through the explicit Skill tool.
- [ ] Include malicious instruction text in AGENTS.md/SKILL.md and verify it cannot bypass native approval or RunMind security instructions.

## Change Impact Explorer

- [ ] Change a JS/TS module with direct and transitive importers and verify both sets are reported.
- [ ] Verify JS/TS test candidates and confidence labels.
- [ ] Change an SVG referenced from HTML/CSS/SVG and verify the asset consumers appear.
- [ ] Supply unknown and unsupported-language paths and verify they are explicitly classified.
- [ ] Confirm it does not run Git or tests automatically.

## Development Session Timeline

- [ ] Run a multi-tool task and verify checkpoint, tool operation, dependency group and sub-agent events appear in chronological order in the Workflow panel.
- [ ] Call `development_session_timeline` and verify it matches the same local evidence.
- [ ] Confirm timeline output remains chat/workspace-profile scoped and reports unknown outcomes.

## Artifact Verification

- [ ] Verify known JSON, XML and text artifacts; confirm receipt contains exact-byte SHA-256 and structural results.
- [ ] Supply matching expected SHA-256 and confirm match.
- [ ] Supply a different expected SHA-256 and confirm verification fails.
- [ ] Verify unsupported/malformed formats report a failed structural check without claiming success.
- [ ] Confirm the receipt explicitly states that digest equality is not provenance/signature validation.

## Acceptance record

- Host: ____________________  Windows build: ____________________
- Node.js: __________________  .NET SDK: _________________________
- Test transcript: ________________________________________________
- Passed / failed items and evidence: _____________________________
- Date: _____________________  Reviewer: _________________________
