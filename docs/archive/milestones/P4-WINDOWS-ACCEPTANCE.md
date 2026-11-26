> **Archive:** Historical development record. This file describes the source state at the time it was written; use the current README, build guides, feature matrix, and feature-specific documentation for present behavior.

# RunMind 3.4.0 — P4 Change Impact Explorer v2 Windows acceptance checklist

> **Historical checklist:** This checklist targets the 3.4.0 feature implementation and its original version metadata. It is not a 3.4.1 acceptance result. Use the current [P7 Windows acceptance checklist](../../P7-WINDOWS-ACCEPTANCE.md), [Process Intelligence checklist](../../system/PROCESS-INTELLIGENCE-WINDOWS-ACCEPTANCE.md), and [build status](../../project/BUILD-STATUS.md) for current release work.

Run against a 3.4.0 source tree on Windows with Node.js 24+ and .NET 10. Repeat interactive checks in Standalone, PowerToys Run and PowerToys Command Palette.

## Build and version

- [ ] Run `.\Validate-Phase1-Windows.ps1 -Configuration Release -Platform x64` from the repository root in PowerShell.
- [ ] Confirm all three hosts compile/package with version `3.4.0` and Command Palette Appx identity `3.4.0.0`.
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

## Change Impact Explorer v2

- [ ] Invoke `project_change_impact` without `changed_paths`; verify fresh `gitRead` approval precedes snapshot collection and index refresh.
- [ ] Decline Git-read approval; verify no Git snapshot or index refresh occurs and the operation reports cancellation.
- [ ] Create a fixture with staged, unstaged, untracked, renamed and deleted JS/TS files. Verify old/new paths, untracked full-file declaration inference, changed hunk ranges and removed-module importer candidates.
- [ ] Change a function, method, property, class and type declaration; verify AST line-range mapping infers symbols without manually supplying `changed_symbols`.
- [ ] Add direct/transitive importers and aliased/named imports; verify TypeScript checker references and resolved import bindings distinguish exact symbol consumers from file-level potential consumers.
- [ ] Select `source=git-branch` with an existing local `base_ref`; verify the merge-base comparison is combined with staged/unstaged/untracked changes and no fetch or Git mutation occurs.
- [ ] Supply a missing/invalid local ref and a ref without a merge base; verify explicit errors and no fallback to an unrelated comparison.
- [ ] Change assets referenced through HTML `src`, `poster`, `srcset`, script/link/iframe/track/embed, CSS `url()`/`@import`, inline/embedded styles, SVG `href`/`xlink:href` and JS/TS `new URL(..., import.meta.url)`; verify owner/consumer edges.
- [ ] Change an image, font or media file that is not text-indexed but is referenced by a known HTML/CSS/SVG/JS/TS file; verify it appears under `unindexedAssetChanges`, not `unknownChangedPaths`, and owning modules contribute test candidates.
- [ ] Introduce literal and non-literal `import()`, variable `require()`, `require.context()` and `import.meta.glob`; verify statically resolvable literal targets are graph edges and dynamic constructions appear as uncertainty signals.
- [ ] Provide Istanbul `coverage-final.json`, `.nyc_output/out.json` and/or LCOV `lcov.info`; verify changed added lines are marked measured/covered/uncovered. Confirm removed-only lines are excluded from current-source coverage.
- [ ] Verify aggregate coverage never claims individual test-to-line attribution; recommendations combine import/asset evidence and lower-confidence filename heuristics.
- [ ] Trigger or simulate index/compiler/asset graph caps and parser/unresolved/dynamic evidence; verify `confidence.coverage`, drivers, risk factors and limitations disclose the gaps.
- [ ] Confirm risk and confidence descriptions state that they are heuristics, not probabilities. Confirm no files are edited and no tests/builds are run.
- [ ] Repeat the working-tree and local-base-ref scenarios in Standalone, PowerToys Run and PowerToys Command Palette.

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
