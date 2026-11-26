# Change Impact Explorer v2 — RunMind 3.4.0

**Status:** Implemented in the shared 3.4.0 source tree; portable regression-tested. Windows/.NET 10 acceptance remains a target-machine gate.

Change Impact Explorer v2 upgrades the existing shared project-index and Git-review pipeline. It does not introduce a second dependency engine, a separate Git implementation in JavaScript, or a test runner. Standalone, PowerToys Run, PowerToys Command Palette and the built-in developer workflow use the same Agent tool contract.

## 1. What the analyzer answers

Given a selected workspace and a concrete set of changes, the analyzer builds an evidence-backed answer to:

- Which modules directly and transitively depend on changed modules?
- Which exported symbols, declarations, callers or import bindings may be affected?
- Which HTML, CSS, SVG, media, font, script, preload and module-relative assets consume changed files or assets?
- Which indexed tests are likely relevant, and how strong is the evidence?
- Which removed or renamed module paths still appear in importer declarations?
- Are there dynamic dependency patterns or unresolved imports that make the graph incomplete?
- Do available coverage reports show changed source lines with no observed execution?
- What is the estimated blast radius and which evidence or gaps drive the risk/confidence assessment?

The analyzer is read-only. It does not edit files, stage or commit Git changes, fetch branches, execute builds, or run tests.

## 2. Invocation

### Default: inspect the current Git working tree

Use the Agent tool `project_change_impact` without `changed_paths` (or select `source: "git-working-tree"`). The operation first requests fresh `gitRead` approval. After approval, the native host collects branch/status, staged and unstaged diffs, rename/delete metadata, untracked path names and whitespace checks. RunMind refreshes the selected project index so untracked source files and current file contents can be analyzed.

Example natural-language request:

> Run Change Impact Explorer on the current working tree. Explain direct and transitive module impact, changed symbols, affected assets and tests, risk and confidence. Do not modify files or run tests.

### Optional: compare against an existing local base ref

Use `source: "git-branch"` and `base_ref: "main"` (or another existing local branch/tag/ref). The native bridge resolves the merge base and computes `base_ref...HEAD`, then combines that branch diff with current staged/unstaged worktree changes and untracked indexed paths. This is a local read-only operation: it does not fetch or alter refs. If the ref is missing or there is no merge base, the operation returns an explicit error rather than silently falling back.

### Explicit path mode

Use `source: "explicit"` with workspace-relative `changed_paths` when the caller already has an authoritative path list. Optional `changed_symbols` can narrow/augment symbol analysis. Explicit mode does not launch Git or request Git-read approval; it only uses the selected workspace's current index.

`changed_paths` accepts up to 1,000 paths. The tool response arrays are separately bounded by `max_results` (1–500, default 200).

## 3. Evidence pipeline

### 3.1 Git changeset normalization

The unified-diff parser handles ordinary add/modify/delete/rename headers, old/new path names, binary markers, hunk line ranges, added lines and removed lines. Staged, unstaged and optional branch-comparison diffs are merged by path. Untracked files are absent from Git's unified diff; their workspace-indexed contents are treated as whole-file additions so declarations are not silently omitted.

The parser retains old and new paths for rename detection. A deleted or renamed-away path can be absent from the current index by design; it is distinguished from an indexed unsupported source file and is also checked against importer declarations.

### 3.2 Automatic symbol inference

Added/modified unified-diff hunk ranges are mapped to declaration ranges collected from the current AST for JS/TS/JSX/TSX. Supported declarations include functions, classes, interfaces, type aliases, enums, variables, methods, method signatures and properties. Removed declaration names are reported separately using conservative declaration-signature matching against removed diff lines.

The analyzer then examines:

- TypeScript compiler-checker references, including their target declaration, source path, name and read/write classification where available.
- Resolved import/export bindings to identify module-level consumers even when a precise changed symbol could not be inferred.
- Explicit caller-supplied symbols, which are marked as user-supplied evidence.

Exact symbol matches and file-level potential consumers are labeled separately. A file-level dependency is not presented as proof that every symbol in that file is affected.

### 3.3 Dependency graph and removed modules

The analyzer traverses the reverse local-import graph to identify direct and transitive importers. The graph includes supported local Node-compatible relative paths and workspace package exports/imports where the target is present in the index. For deleted/renamed paths it checks indexed relative import bindings against the missing path and common extension/index candidates, returning unresolved importer evidence.

### 3.4 Asset dependency graph

The asset scanner resolves bounded local references from:

- HTML `src`, `poster`, `data`, `href`, `xlink:href` and `srcset` attributes on relevant media, script, link, iframe, track, image, use, object and embed elements.
- HTML inline `style` attributes and embedded `<style>` blocks.
- CSS `url(...)` and `@import` references, including font/image/media resources.
- SVG local `href` and `xlink:href` references.
- JS/TS `new URL("...", import.meta.url)` patterns.

Supported local asset suffixes include common image, SVG, icon, font, CSS/JS/TS/JSON/WASM, audio and video formats. Remote/data/blob/fragment references are not treated as local workspace edges. Original bounded HTML/CSS/SVG source is retained for path extraction so path casing is not lost through lower-cased search text.

A changed image/font/media file can be a known asset change even when it is not a text-indexed file. RunMind resolves it against the asset graph, identifies the owning HTML/CSS/SVG/JS/TS files, and feeds those owners into impact traversal and test candidate selection. Such a path is exposed as `unindexedAssetChanges`, not incorrectly classified as an unknown file.

The reference collector is bounded to 2,500 local edges. Reaching that cap is surfaced in completeness diagnostics, risk factors and confidence drivers rather than being hidden.

### 3.5 Dynamic dependency signals

The JS/TS parser records non-literal dynamic `import()` and `require()` expressions, `require.context(...)`, and `import.meta.glob` / `globEager` patterns. These signals are joined to changed and impacted files and included in the confidence/risk explanation.

A signal means “this module has a dynamic dependency construction site,” not that every runtime target has been resolved. Literal dynamic imports remain ordinary statically resolvable import edges when the local target is indexed.

### 3.6 Test selection and coverage evidence

Test candidates are selected from indexed JS/TS test files using:

1. Direct imports of changed modules or direct asset-owner modules.
2. Transitive reverse-import relationships.
3. Changed-file-is-itself-a-test evidence.
4. Filename similarity, explicitly labelled low confidence.

Recommendations include evidence paths and confidence labels. They are suggestions only; no test command is executed.

Optional coverage evidence is read from bounded Istanbul `coverage-final.json`, `.nyc_output/out.json` and LCOV `lcov.info` reports discovered in the selected workspace. Changed added-line ranges are compared with line hit counts. Removed-only hunk lines are excluded because their old line numbers do not map to the current source file. The report distinguishes measured, covered, uncovered and unmeasured changed lines. Uncovered measured lines add a risk factor for validation attention.

Standard Istanbul/LCOV line coverage does **not** identify which individual test executed a line. RunMind therefore does not claim test-to-line attribution or use coverage data to eliminate a test. Per-test trace ingestion is a separate future evidence source, not inferred from aggregate coverage.

## 4. Report contract

The result has `schemaVersion: 2` and includes:

| Field | Meaning |
|---|---|
| `changedPaths` | Normalized changed path inventory from the selected evidence source |
| `matchedChangedPaths` | Paths present in the current project index |
| `analyzedCodeChanges` | Indexed JS/TS changed files with language-aware analysis |
| `assetChanges` / `unindexedAssetChanges` | Indexed HTML/CSS/SVG files and non-indexed asset paths resolved through local references |
| `unknownChangedPaths` / `unanalysedChangedPaths` | Paths not recognized by the index/asset graph, or indexed files outside the language-aware scope |
| `changedSymbols` / `removedSymbolCandidates` | AST-inferred or explicitly supplied symbols; removal candidates remain heuristic |
| `symbolConsumers` | Checker/import-binding evidence, with exact-symbol vs file-level-potential confidence |
| `directDependents` / `transitiveDependents` | Reverse import graph traversal results with distance |
| `assetConsumers` | Indexed owners containing references to the changed local asset/file |
| `removedModuleConsumers` | Importers that still point at a removed/renamed relative module path |
| `dynamicDependencySignals` / `unresolvedImports` | Explicit completeness warnings from changed or impacted modules |
| `tests` / `testImpact` | Candidate tests with evidence, confidence and optional coverage context |
| `blastRadius` | Counts and percentage of indexed JS/TS files in the impact set |
| `risk` | Transparent weighted exposure factors; **not** a defect probability |
| `confidence` | Structural evidence coverage heuristic, drivers and index/compiler/parse/asset/coverage diagnostics; **not** a statistically calibrated probability |
| `limitations` | Static-analysis and execution boundaries retained in every response |

Risk is the capped sum of the listed weighted factors (maximum 100): entry-point exposure, coupling, direct/transitive dependents, symbol consumers, removed-module consumers, dynamic dependency uncertainty, truncated index/asset graphs and changed lines without observed coverage. It is a validation-priority heuristic, not a prediction that a change will fail. Confidence starts from full evidence coverage and subtracts bounded penalties for index truncation, compiler availability/coverage, parser errors, unmatched paths, dynamic signals, unresolved imports and a capped asset graph. The exact drivers are returned in the report. Confidence describes observed evidence coverage, not correctness. Neither score should be represented as a quality gate or as proof that an unlisted file is unaffected. Missing coverage reports are reported as unavailable/unmeasured; absence of a report is not mislabeled as an uncovered line.

## 5. Completeness, bounds and safety

The analyzer is designed to be thorough without turning into an unbounded scanner:

- Project index: up to 10,000 files, 50,000 directory entries, depth 10, 256 KiB per file, 24 MiB aggregate source text; generated/dependency/secret/backup directories remain excluded.
- Coverage reports: 1 MiB per report, 4 MiB aggregate coverage data.
- TypeScript checker: up to 500 source files / 12 MiB, 10,000 resolved bindings and 20,000 cross-file references.
- Local import graph: 5,000 edges, 5,000 resolved bindings and 5,000 unresolved bindings.
- Asset reference scan: 2,500 local edges; limit is reported.
- Changed paths: 1,000; impacted module traversal: 5,000 visited nodes; test impact traversal: 3,000 nodes; test recommendations: 300.
- Tool response arrays are separately capped to the requested `max_results` (maximum 500).
- Git reading requires fresh `gitRead` approval. Branch refs must already exist locally. No fetch, stage, checkout, commit, reset, cleanup or other Git mutation occurs.
- No test/build execution or source mutation is performed by the analyzer.

The cache schema is versioned as project-index v7. A prior v6 index is not silently treated as v7; it must be rebuilt. HTML/CSS/SVG source retention is included in the v7 cache so case-sensitive local asset paths are analyzed from original text.

## 6. What it deliberately cannot prove

No bounded static analyzer can know every runtime dependency. Remaining unprovable cases include runtime-computed module names without enumerated literal targets, dependency injection containers whose registrations are generated externally, reflection, dynamically assembled filesystem paths, plugin registries populated at runtime, server-side configuration fetched remotely, bundler/custom-loader semantics outside indexed source, generated code that is not present in the workspace, native module dependencies, unindexed external packages, and behavior dependent on environment/data/feature flags.

The analyzer lists recognizable dynamic sites and unresolved bindings, identifies index/compiler/asset caps, and lowers confidence when evidence is incomplete. It does not claim those lists are exhaustive. Stronger future evidence can come from bundler metadata, per-test traces, runtime import maps, build manifests or execution telemetry; those sources must be ingested explicitly and retain their provenance.

## 7. Windows acceptance required

Portable tests validate parser, analyzer, schema and source contracts. They do not compile the C# bridge or prove WebView2/native Git behavior. On Windows 11 with Node.js 24+ and .NET 10:

1. Run `Validate-RunMind-Windows.ps1`.
2. Exercise the analyzer in Standalone, PowerToys Run and Command Palette using a workspace with staged, unstaged, untracked, renamed and deleted files.
3. Verify fresh Git-read approval, working-tree discovery, untracked files, rename/delete behavior, optional existing local `base_ref`, invalid-ref handling, and no Git mutation.
4. Exercise a fixture with JS/TS modules, method and function edits, deleted/renamed modules, dynamic imports, HTML/CSS/SVG assets, image/font/media assets, tests and Istanbul/LCOV reports.
5. Confirm unknowns, caps, confidence drivers and unmeasured coverage are visible.

Do not mark the Windows feature accepted until these target-machine checks are recorded.
