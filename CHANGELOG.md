# Changelog

## [1.10.6] — Toolbar flyout viewport positioning hotfix

### Fixed
- Fixed toolbar flyouts opening beyond the right edge of the application window. Every standard toolbar panel now measures its trigger and rendered width, then shifts horizontally just enough to remain within the visible WebView viewport with a consistent margin.
- Applied the same positioning rule to all standard toolbar flyouts rather than special-casing only the MCP menu, so other right-side menus remain usable as the toolbar layout changes.
- Recalculates open flyout positions when the window is resized.
- Updated plugin, standalone, and MCP client version identifiers to `1.10.6`.

### Validation
- JavaScript syntax, flyout positioning bounds, version alignment, and ZIP integrity checked. Live Windows/WebView2 rendering still requires validation on Windows.

## [1.10.5] — Deep Research mode and warm dark theme hotfix

### Fixed
- Fixed event bubbling from the composer **Deep Research** toggle: enabling research mode now opens the Research workspace flyout and the same click is no longer misinterpreted as an outside click that immediately closes it. Other toolbar flyouts still close as expected.
- Replaced the blue/indigo dark palette with **Warm Graphite**: neutral charcoal surfaces, warm ivory text, subdued amber highlights, warm user-message surfaces, and softer neutral borders. Light and System-Light palettes remain unchanged.
- Updated plugin, standalone, and MCP client version identifiers to `1.10.5`.

### Notes
- Warm Graphite reduces blue-toned interface colors; it is not a hardware/display blue-light filter and does not change monitor brightness or white-point settings.

### Validation
- Browser regression checks cover composer-toggle opening, exclusive flyout behavior, outside-click closing, active/ARIA state, and the warm dark palette tokens. JavaScript syntax, version alignment, and ZIP integrity are checked. Windows/.NET 10 compilation and live WebView2 visual validation still require Windows.

## [1.10.4] — Toolbar workspace and approval management hotfix

### Fixed
- Converted the Deep Research workspace from a standalone right-side drawer into a standard toolbar flyout anchored directly below its toolbar trigger, matching the placement and sizing behavior of the other toolbar sections.
- Made the Deep Research toolbar trigger use the same open/active visual state as other toolbar menus, with `aria-expanded` kept synchronized for mouse, keyboard, and programmatic open/close actions.
- Added shared mutual exclusion for toolbar and conversation flyouts: opening any section closes the previously open section, and opening Deep Research closes other toolbar sections. Outside-click and Escape handling now cover every top-level flyout, including menus previously omitted from the hard-coded lists.
- Added per-tool **Remove** actions to the Always-approved tools list. Removing one approval updates the persisted permission set immediately; **Clear approvals** remains available.
- Updated plugin, standalone, and MCP client version identifiers to `1.10.4`.

### Validation
- Static checks cover the toolbar-details structure, anchored flyout CSS, active state and `aria-expanded` synchronization, exclusive menu handling, per-tool permission removal, and version alignment.
- JavaScript syntax, HTML ID uniqueness, metadata parsing, and ZIP integrity are checked in the release package. Windows/.NET 10 compilation and live WebView2 interaction still require validation on Windows.

## [1.10.3] — Branch title and spacing hotfix

### Fixed
- Kept the conversation title independent from branch selection. Switching to a branch no longer appends the branch label to, or overwrites, the parent conversation title.
- Renamed the immutable `main` branch's display label from `Main` to `Original`, including migration of existing per-window branch state. The stable internal branch ID remains `main`.
- Generated new branch labels from the user prompt nearest the fork point instead of the first prompt in the conversation. Short greetings and generic prompts receive neutral labels such as `Alternative 1`; duplicate labels receive a numeric suffix.
- Removed the redundant **Branches** toggle row while the branch manager is open. The manager now sits directly below the header and uses its own close control, eliminating the extra vertical gap. The collapsed toggle row is also more compact.

### Limits
- Branch labels are deterministic prompt-derived suggestions, not AI-generated summaries. Users can rename non-original branches at any time.
- Branch names are labels within the current conversation; the parent conversation keeps one stable title across all branches.


## [1.10.2] — Conversation branch layout hotfix

### Fixed
- Fixed the branch manager's `hidden` state being overridden by `.conversation-branchbar { display:flex }`. With only the Main branch, the supposedly hidden manager was still rendered because the author CSS won over the browser's default hidden rule.
- Fixed the transcript frame's default horizontal flex direction. The branch manager and comparison view now occupy a full-width row above the transcript instead of becoming a side-by-side column that squeezed the conversation to roughly half the window.
- Added a compact **Branches** toggle that appears only when multiple branches exist. Branch management is collapsed by default, opens after the user explicitly creates a branch, and has an explicit close button. The comparison panel retains its own Close control.
- Updated plugin, standalone, and MCP client version identifiers to `1.10.2`.

### Validation
- Static regression checks confirm hidden branch controls are force-hidden, the transcript frame uses a vertical layout, branch management starts collapsed, create-branch opens it, and both branch manager and comparison views have close controls.
- JavaScript syntax, HTML ID uniqueness, metadata alignment, and ZIP integrity checks pass. Windows/.NET 10 and live WebView2 rendering still require validation on Windows.

## [1.10.1] — Context metrics hotfix

### Fixed
- Fixed a `ReferenceError` in `compactResearchConversation` when the selected provider exposes an active context limit. The context metric now stores `estimatedPromptTokens: estimatedTokens` instead of referencing an undeclared `estimatedPromptTokens` variable. This prevented Deep Research from sending its first chat request for affected LM Studio models.
- Updated plugin, standalone, and MCP client version identifiers to `1.10.1`.

### Validation
- Added a regression check for the active-context-limit branch and confirmed it records the estimated prompt token count without throwing.
- JavaScript syntax, metadata alignment, and ZIP integrity checks pass. Windows/.NET 10 and live LM Studio/WebView2 integration still require validation on Windows.

## [1.10.0] — Agent orchestration and workflow intelligence

### Added
- Added one shared bounded tool-batch scheduler for Agent Mode, Deep Research, and sub-agents. Independent operations in a model tool-call layer can run concurrently when their tool is on the explicit read-only allowlist, up to a user-configurable limit of four workers. Result messages are returned to the model in the original call order. Mutating tools, approval-gated reads, MCP calls, duplicate identical calls, and delegated-agent calls remain serialized.
- Added workflow operation records with dependency-layer IDs, sanitized inputs, bounded output previews, lifecycle states, duration, retry controls for safe read-only Agent operations, and durable local persistence before execution. Interrupted in-flight read-only operations require an explicit user confirmation before retry. The shared Stop control aborts the active run, marks undispatched queued operations cancelled, and prevents later model-turn layers from being sent.
- Added read-only tool start-priority preferences to Workflow. Dragging a parallel read-only operation card onto another changes the preferred start order for future independent groups. Existing executions, provider result ordering, and dependency layers are not reordered. Priorities are stored in the current WebView profile.
- Expanded bounded one-level sub-agent workflows for Researcher, Coder, Security Analyst, Reviewer, and Summarizer roles. Child agents receive only the approved read-only tool subset, cannot delegate further, and return a normalized structured result (summary, findings, sources, uncertainties, and limitations). Deep Research child retrieval consumes the parent task budgets and domain policy.
- Added inspectable sub-agent assignments/results, including bounded tool-call counts and elapsed time.
- Added conversation branching from completed messages, independent in-window histories, branch selector, rename/delete, return-to-Main, and latest-answer side-by-side comparison. Local snapshots are bounded to eight branches and the latest 16 messages per branch; binary attachment bytes are not duplicated into branch snapshots.
- Expanded local Workflow analytics with per-tool call counts, success rate, average execution duration, provider/model request counts, request success/failure, average latency, and provider-reported token usage when available.

### Changed
- Agent Mode and Deep Research use the same tool-batch scheduler. Each model tool-call response is treated as a dependency layer; the next model turn waits for every result in that layer. Dependencies between calls generated together are not inferred because the model has not seen any of those results yet. Cancellation is propagated through the active AbortSignal; an adapter or external service may take time to stop or may not be able to undo a side effect already accepted.
- Added the **Workflow** view alongside the existing Research, Review, and Recovery views in the same workspace drawer. It shows recent dependency layers, operation status, inputs/outputs, sub-agent work, and local analytics.
- Persisted operation intent before a tool starts. On reload, operations left in `running` state become `interrupted`; pending undispatched operations become `not_started`. RunMind does not silently replay them.
- Provider request telemetry now records failed requests as well as successful ones. Token counts remain provider-reported only; providers that omit usage metadata are shown as not reporting tokens.
- Updated plugin, standalone, and MCP client version identifiers to `1.10.0`.

### Limits and behavior
- Parallel execution is deliberately limited to a reviewed allowlist of known read-only built-in tools. It is not arbitrary dependency inference, arbitrary code concurrency, or unrestricted MCP parallelism. MCP tools remain serialized because the current approval UI and route metadata do not provide a trusted concurrent read-only authorization path.
- Priority drag-and-drop changes the start preference of tool types in future independent read-only groups only. It cannot reorder operations already running or override serialized side-effect/approval barriers. The model's result transcript remains in original tool-call order.
- Sub-agents use the selected model/provider, at most four model/tool turns per delegation, one delegation level, and a read-only child-tool subset. They are not separate models, isolated processes, or autonomous background workers. Structured JSON is normalized when returned; if a model ignores the JSON instruction, its text is retained as an unstructured summary.
- Agent workflow operation history is locally persisted and bounded; a crash during an external operation can leave its outcome ambiguous. Only allowlisted read-only operations have an in-app retry action. This is not an exactly-once guarantee for arbitrary external services. Deep Research additionally retains its existing per-task operation ledger and checkpoint recovery.
- Conversation branches are scoped to the WebView profile/window instance. Local branch snapshots keep at most eight branches and 16 recent messages per branch, trim long text, and omit binary attachment bytes. Use a `.runmind` save for a complete conversation archive. Research checkpoints remain attached to their original research task and are not cloned into branches. Branch comparison is a side-by-side comparison of the latest assistant answers, not a semantic diff.
- Workflow analytics remain local to the current WebView profile and are best-effort. Tool duration is measured by RunMind; provider latency is client-observed. Token usage appears only when a provider reports usage metadata. Clearing workflow history also clears its local analytics, but does not clear conversation branches or research checkpoints.

### Validation
- Static JavaScript syntax, shared scheduler behavior, bounded concurrency, deterministic output order, safe-tool gating, priority ordering, branch snapshot limits, metadata/version alignment, and overlay ZIP integrity are checked in this environment.
- A Windows/.NET 10 build and live WebView2/PowerToys/provider/MCP integration tests still require validation on Windows.

## [1.9.0] — Research intelligence and agent reliability

### Added
- Added an integrated Research / Review / Recovery tab set inside the existing Deep Research drawer. No second panel or parallel research UI was introduced.
- Added claim-level model assessments with supported, partially supported, contradicted, and insufficient-evidence verdicts; explanations; uncertainty notes; and 1–8 exact source passages classified as supporting, contradicting, or contextual. The application checks normalized exact-quote presence against the bounded source text it stored.
- Added a Review view with claim filters, source passage links, hostname-independence cautions, user Accept/Reject decisions, and targeted “Investigate further” actions that continue the same research task.
- Added qualitative source assessments covering primary/secondary/reference type, observable strengths and limitations, author/date visibility, and freshness. No numeric trust score is generated.
- Added checkpoint history in IndexedDB (up to eight snapshots per research task), checkpoint summaries, restore/rollback confirmation, and a Recovery view.
- Added a tool-operation ledger that records intent before a tool executes and saves bounded model results. Exact matching completed operations in an uncommitted batch can reuse their saved result; ambiguous interrupted operations require explicit user acknowledgement before retry.
- Added selected-model diagnostics for discovery, a small text-generation probe, and a harmless function-calling probe. The diagnostic function is never executed.
- Added research context telemetry with an approximate token estimate, active context-limit source, utilization, and compaction counts.
- Added adaptive compaction of older research tool messages at safe checkpoints when an active context limit is known and utilization exceeds the threshold. Full bounded source text remains in the research source register.
- Added seven reusable research templates: General, Technical Investigation, Product & Technology Comparison, Security Assessment, Academic Research, Website Investigation, and Troubleshooting Investigation.
- Expanded Markdown report export with claim assessments, user review decisions, evidence passages, qualitative source assessments, and tool-operation recovery state.

### Changed
- The existing Deep Research workspace now has Research, Review, and Recovery views. Existing source preferences, plan editor, budgets, sources, evidence, activity, pause/resume/cancel, copy, and export remain in the Research view.
- Research instructions ask the model to assess material claims and important sources, cross-check disagreements, disclose uncertainty, and explain possible source dependencies. The UI explicitly distinguishes model assessments from local exact-quote checks.
- Checkpoint history is scoped by research task ID rather than WebView instance ID. The existing primary checkpoint and session fallback remain intact.
- Updated standalone, PowerToys plugin, plugin manifest, and MCP client version identifiers to `1.9.0`.

### Limits and behavior
- Semantic claim verdicts and source-quality explanations are model-assisted assessments. RunMind does not independently prove logical entailment, source authority, factual truth, or editorial independence. Exact quote matching only confirms text presence in the bounded stored excerpt.
- Hostname counts are a visual caution, not an independence test. Different hostnames do not prove independent reporting; common underlying sources can exist across domains.
- Tool-operation keys use the tool name and canonicalized arguments. They protect identical operations within an uncommitted batch, not semantically equivalent actions with different arguments. If a process stops after a side effect but before its result is committed, RunMind labels the operation ambiguous and asks before retrying. Exactly-once execution cannot be guaranteed for arbitrary local or MCP tools.
- Checkpoint history retains at most eight snapshots for the current task. Restoring replaces the current task state after confirmation; newer snapshots remain in history. Recovery storage is WebView-profile local, so PowerToys and standalone histories do not synchronize.
- Context-token counts are estimates based on serialized conversation length divided by four, not provider tokenization. Adaptive compaction is limited to older tool messages and runs only when RunMind knows an active context limit; it does not guarantee that every model prompt will fit.
- Context compaction and operation history are research-task features. Parallel tool scheduling, subagents, conversation branching, scheduled tasks, persistent knowledge indexing, resumable downloads, and advanced MCP protocol features remain outside 1.9.0.
- The LM Studio Native REST mode does not receive the generation/tool-call diagnostics in this release; those probes require a supported chat-completions-compatible route.

### Validation
- Static validation covers inline JavaScript syntax, unique HTML IDs, required element references, project XML, plugin JSON, version alignment, checkpoint/operation-ledger invariants, and overlay archive integrity.
- Windows/.NET 10 compilation and live WebView2 IndexedDB, SearXNG, Reader, provider, local workspace, and MCP side-effect recovery tests still require execution on Windows.

## [1.8.0] — Deep Research evolution and Agent runtime

### Added
- Extended the existing Deep Research workspace with a live operation indicator, source collection details, an evidence register, and a timestamped activity feed. Existing depth presets, page/tool budgets, plan editing, pause/resume/cancel, task history, copy actions, and report export remain.
- Added bounded multi-query discovery with `research_topic` (up to six queries and eight results per query, deduplicated by normalized URL). Search is available only when SearXNG is configured.
- Added `page_to_markdown`, converting directly retrieved HTML into readable Markdown and using the configured Reader fallback when direct retrieval is blocked.
- Added bounded same-origin breadth-first crawling with `crawl_website` (up to 20 pages and depth 3), HTTP link reachability checks with `check_links` (up to 30 links), and exact line-based `compare_webpages`.
- Added research source metadata: URL/final URL, title, domain, author, publication/modified dates when available, retrieval route/time, excerpt, read state, and fetch failures. Sources are deduplicated by normalized URL.
- Added preferred, allowed, and blocked domain controls for Deep Research. Blocked domains take precedence; an allowlist restricts retrieval only when populated; preferred domains affect discovery ordering.
- Added `research_record_evidence`, which records a claim and exact quotation and reports whether the quoted text occurs in the stored source excerpt.
- Added streaming tool-call progress for Ollama NDJSON and OpenAI-compatible SSE responses, including partial tool names and argument previews. Non-streaming JSON responses remain supported.
- Added IndexedDB-backed research checkpoints with bounded snapshots, periodic heartbeat persistence, stale/paused/interrupted task recovery in another window using the same WebView2 profile, and a session-storage fallback if durable storage is unavailable.
- Expanded Markdown research report export with task/model/budget metadata, sources, evidence register, and activity history.

### Changed
- Research now records live status while the model plans, streams tool calls, executes retrieval, and synthesizes findings. Large research tool results are compacted as valid JSON before being appended to the model transcript; full fetched text remains in the source register rather than being cut mid-JSON. Multi-query discovery preserves results across subquestions when compacting.
- Direct HTML retrieval produces Markdown with headings, paragraphs, links, lists, blockquotes, tables, and code blocks; title/author/publication/modified metadata is extracted where available, including supported JSON-LD metadata.
- Domain policy and page/tool/time budgets are enforced by the Deep Research retrieval paths. Time limits are a soft boundary: an in-flight operation is allowed to finish before the task stops retrieving and synthesizes the collected material.
- Updated standalone, PowerToys plugin, plugin manifest, and MCP client version identifiers to `1.8.0`.

### Limits and behavior
- `research_topic` requires a configured SearXNG endpoint. Search snippets are discovery leads and should be opened before use as evidence.
- Direct webpage retrieval depends on CORS and site behavior. If enabled, Jina Reader or a custom Reader gateway may receive the public URL and page content needed for extraction. Reader fallback is not used for private/local or credential-bearing URLs.
- `crawl_website` stays on the starting origin, with a maximum depth of 3 and 20 pages. It is not a whole-web crawler or scheduler.
- `check_links` uses bounded HTTP HEAD requests. CORS/network failures and servers that reject HEAD are reported as unverified, not broken. Private/local destinations are skipped.
- `compare_webpages` compares trimmed exact text lines; it does not infer semantic equivalence or explain meaning-level changes.
- Evidence verification checks exact normalized quote presence in the bounded text stored for a source. It does not independently prove that a source is authoritative or that the quote supports the associated claim. Citation correctness remains model-assisted, not semantically verified by the application.
- Domain preferences apply to Deep Research, not ordinary chat. Preferred domains are ranking hints; they do not prove source quality.
- Pause and plan edits take effect at safe complete tool-call-batch checkpoints. If a process closes during an in-flight batch, the last committed transcript is recovered; operations may need review and are not guaranteed exactly-once. Checkpoints do not synchronize between separate PowerToys and standalone WebView2 profiles.
- Research operations are executed serially. Parallel tool scheduling, generalized idempotent replay/rollback, resumable downloads, subagents, scheduled tasks, conversation branching, persistent knowledge indexing, and advanced MCP protocol features remain outside 1.8.0.

### Validation
- Static checks cover inline JavaScript syntax, Ollama NDJSON and OpenAI-compatible SSE/JSON tool-call parsing, project XML, plugin JSON, version consistency, and overlay archive integrity.
- A Windows/.NET 10 build, WebView2 IndexedDB recovery behavior, live SearXNG/Reader/provider tests, and end-to-end research runs still require validation on Windows.

## [1.7.4] — Transcript text selection polish

### Changed
- Kept the empty composer placeholder (`Message the selected local model…`) out of text selection while leaving text entered by the user selectable.
- Restored text selection for message attribution (author, timestamp, provider, and model) and generation statistics (time to first token, elapsed time, token count, and tokens per second). Application controls and action buttons remain excluded from Ctrl+A selections.
- Synchronized standalone, PowerToys plugin, and plugin manifest version metadata to `1.7.4`.

### Validation
- Static checks cover JavaScript syntax, CSS selection rules, project XML, plugin manifest JSON, release-version consistency, and overlay ZIP integrity. WebView2 selection behavior and a Windows/.NET 10 build still need verification on Windows.

## [1.7.3] — Responsiveness and interface polish

### Changed
- Added an optional Ollama CPU thread cap for PowerToys Run inline requests. Leave the setting blank or use `0` for Ollama's automatic thread selection; enter `1`–`128` to set the maximum worker-thread count. No automatic CPU policy is applied. The setting does not affect standalone chat or LM Studio.
- Throttled live chat Markdown rendering to at most one update every 90 ms. The final transcript is still rendered immediately when generation ends; the limit reduces repeated parsing of an ever-growing response.
- Made application controls, status labels, message metadata, action buttons, and other interface chrome non-selectable. User and assistant message content remains selectable for normal copying.

### Validation
- Static checks in this environment cover JavaScript syntax, project XML, plugin manifest JSON, the optional CPU-thread setting path, the non-selectable chrome selectors, the 90 ms render scheduler, and ZIP integrity.
- A Windows/.NET 10 build, live Ollama comparison with automatic versus capped CPU threads, PowerToys Run responsiveness under load, and WebView2 selection/rendering behavior still need verification on Windows.

## [1.7.2] — PDF.js and offline multilingual OCR

### Added
- Replaced the hand-written PDF stream/text scanner with PDF.js for page-aware text extraction, standard font resources, and CMap support.
- Added automatic per-page OCR fallback using Tesseract.js when extracted text is absent or fails basic text-quality checks. The `read_pdf` tool preserves page order and reports which pages used PDF.js or OCR, selected OCR languages, and pages with low or unavailable confidence.
- Added the **PDF OCR languages** toolbar menu. English (`eng`) and Persian (`fas`, locale `fa_IR`) are the initial bundled language choices; users can select multiple languages for a document.
- Added individually downloadable offline language packs for French, Spanish, Arabic, Simplified/Traditional Chinese, Russian, Finnish, German, Italian, Portuguese, Japanese, Korean, Turkish, and Urdu. Successfully downloaded packs are cached in the current WebView2 profile.
- Added `Prepare-RunMind-OCR.ps1` to retrieve pinned PDF.js `6.5.438`, Tesseract.js `7.0.0`, matching `tesseract.js-core` `7.0.0`, and the English/Persian `tessdata_fast` files on first build. The build script skips downloads after the required local resources are present.

### Changed
- Updated the Agent tool description and system guidance to describe automatic OCR fallback and report OCR confidence limitations rather than claiming PDF OCR is unsupported.
- Updated both desktop project files to copy local PDF.js, Tesseract.js, core, and language model resources into their build outputs.
- Synchronized standalone, PowerToys plugin, and plugin manifest version metadata to `1.7.2`.

### Limitations
- OCR runs only on pages whose extracted text is missing or suspicious; language identification is not automatic. Select the expected language(s) in the OCR menu before reading a mixed-language or non-English scan.
- The first build needs internet access to download pinned third-party runtimes and English/Persian traineddata. After preparation, app runtime and later builds can work offline. Optional language packs are downloaded on demand and cached separately in the PowerToys and standalone WebView2 profiles.
- Encrypted PDFs are not supported. Handwriting, low-resolution scans, unusual scripts/fonts, tables, columns, and reading order can still produce OCR errors. Review low-confidence pages against the original.
- PDF input is limited to 25 MB and 500 pages; extracted text is capped at 90,000 characters per tool result. PDF rendering is bounded to 12 million pixels per page for OCR.

### Validation
- JavaScript syntax, project XML, plugin JSON, build-script structure, and overlay archive integrity are checked in this environment. The PowerShell bootstrap cannot be executed here because this environment has no Windows PowerShell/tar.exe and outbound package downloads are unavailable.
- A Windows/.NET 10 build, PDF.js worker/CMap loading, real English/Persian PDF corpus, scanned-page OCR, optional language download/cache, and WebView2 end-to-end tests still need to be run on Windows.

## [1.7.1] — Conversation attachment support for Agent file tools

### Fixed
- Fixed Agent file tools treating attached filenames as workspace paths. Text-based readers and JSON validation now resolve exact filenames from conversation attachments before falling back to the selected workspace.
- Added PDF and ZIP attachments with MIME/signature checks and bounded per-file/combined sizes. `read_pdf` and `inspect_archive` can read attached files directly; `extract_archive` can use an attached ZIP while still requiring a selected workspace and explicit extraction approval.
- Updated conversation prompt context and tool descriptions so models can identify attached PDF/ZIP filenames and pass the exact filename to the corresponding tool.
- Preserved supported PDF/ZIP attachment bytes when saving and reopening `.runmind` conversations, with validation of their MIME types, signatures, and size limits.

### Changed
- Synchronized standalone, PowerToys plugin, plugin manifest, and MCP client version metadata to `1.7.1`.

### Limitations
- PDF extraction remains best-effort selectable-text extraction; OCR, encrypted PDFs, and unsupported PDF structures/encodings remain unsupported.
- ZIP64 and multi-disk ZIP archives remain unsupported. Extraction continues to require a selected workspace, explicit approval, a new destination folder, and CRC-32 verification.

### Validation
- JavaScript syntax validation passed. Targeted checks passed for PDF/ZIP upload and signature checks, text/PDF/ZIP attachment resolution, PDF text extraction, ZIP inspection/deflate decompression/CRC-32 verification, missing-file handling, workspace fallback, and PDF/ZIP payload validation when reopening `.runmind` sessions. Windows/.NET build and WebView2 end-to-end attachment tests must be run on Windows.

## [1.7.0] — Expanded Agent data and workspace tools

### Added
- Added `validate_json_schema` for workspace JSON data with local JSON Pointer `$ref`, type checks, object/array constraints, string/number bounds, common formats, and logical schema composition (`allOf`, `anyOf`, `oneOf`, `not`, and conditional schemas). Validation returns bounded, path-specific errors. Schema regular expressions run in the isolated worker with a hard timeout. Unsupported advanced keywords are reported as warnings rather than silently claimed as validated.
- Added `read_pdf` for best-effort extraction of selectable PDF text from unencrypted PDFs, including plain, Flate-compressed, and ASCII85/Flate streams where supported. Extraction is bounded to 25 MB per source file and 80 MB of decoded streams.
- Added `inspect_archive` and approval-gated `extract_archive` for standard ZIP files. Extraction rejects path traversal, symbolic links, encrypted entries, duplicate/case-colliding paths, unsupported compression, and oversized archives; every extracted file is checked against its ZIP CRC-32 before it is written. Extraction uses a new destination folder and removes partial output after a failure.
- Added `diagnose_connection` to classify common model endpoint failures and report connection/model-discovery status without sending a generation request or exposing API key values.
- Added CSV schema inference (`infer_dataset_schema`) and key-based CSV joins (`join_datasets`) supporting inner, left, and full joins, with approval and no-overwrite behavior.

### Changed
- Updated Agent tool descriptions and documentation for the new data, PDF, ZIP, and connection-diagnostic capabilities.
- Synchronized standalone, PowerToys plugin, plugin manifest, and MCP client version metadata to `1.7.0`.

### Limitations
- PDF extraction is not a complete PDF renderer: scanned-page OCR, encrypted PDFs, ZIP/object streams, custom font CMaps, and some complex encodings are not supported. Extracted text should be checked against the original for layout-sensitive documents.
- Archive support in this release is standard ZIP only; ZIP64, multi-disk ZIPs, TAR, and other archive formats are not included.
- JSON Schema validation is a bounded in-app implementation, not a claim of full conformance to every draft/keyword. Local `$ref` is supported; dynamic/unevaluated and selected content keywords are reported as unimplemented warnings.
- CSV inference is heuristic (up to 10,000 rows); joins are capped at 100,000 output rows. Empty join keys do not match.
- Connection diagnosis checks model discovery only. It cannot distinguish every network failure from a WebView2 CORS rejection and does not test generation/tool-call capability.

### Validation
- JavaScript syntax validation passed with Node.js 22.
- Targeted helper tests passed for valid/invalid JSON Schema checks, local `$ref`, basic PDF text extraction, deflated ZIP reading and CRC verification, CSV type inference, and full CSV joins.
- Full Windows/.NET build, WebView2 integration, real-world PDF corpus testing, and destructive ZIP-extraction UI testing remain to be run on Windows.

## [1.6.13] — Agent safety and multimodal workspace tools

### Added
- Added `regex_test`, which evaluates JavaScript regular expressions in a disposable Web Worker with a 750 ms hard timeout, bounded pattern/input sizes, and bounded match output.
- Added `read_image` for workspace PNG, JPEG, WebP, and GIF files. The image is attached to the next model turn for visual analysis; a vision-capable model is required.
- Added automatic workspace backups for file edits and deletions. Backups are stored under `.runmind-backups` and can be restored through the approved `restore_file_backup` tool.

### Changed
- File edit/delete approval cards now explain that a recovery backup is created before the operation.
- Restoring over an existing file first saves its current contents as another recovery point.
- Synchronized standalone, PowerToys plugin, plugin manifest, and MCP client version metadata to `1.6.13`.

### Validation
- The shared JavaScript application passes `node --check`.
- The complete Windows/.NET build, WebView2 host integration, live regex timeout behavior, and vision-provider compatibility still require testing on Windows with suitable local model servers.

All notable changes to RunMind are documented here, with the newest entries first. Historical product and file names are retained where they describe behavior at the time of that release.

## [1.6.12] — PowerToys plugin build fix

### Fixed
- Fixed the PowerToys plugin compilation error in `Main.CreateSettingPanel`. A WPF `Grid` derives from `Panel`, not `Control`, and cannot be returned from the `ISettingProvider` method.
- Restored the standard PowerToys pattern used by Microsoft's own plugins: `CreateSettingPanel` throws `NotImplementedException` because RunMind provides its settings through `AdditionalOptions` and does not implement a custom WPF settings panel.

### Changed
- Kept the standalone and PowerToys projects on `net10.0-windows10.0.22621.0`. The third-party linter's hardcoded `net9.0` check is not a reason to downgrade the projects or their .NET 10 PowerToys references.
- Synchronized project, plugin, and MCP client version metadata to `1.6.12`.

### Validation
- Static source checks passed. A full Windows/.NET build and live host tests still need to be run on a Windows development machine.

## [1.6.11] — Conversation UX and reliability

### Changed
- Replaced the ambiguous **Export** toolbar entry with **Conversations**, separating resumable Save/Open actions from one-way export formats.
- Enforced the `.runmind` extension and RunMind conversation JSON format. Empty conversations are rejected consistently by the page and native Save As host.
- Removed live-app message action buttons from standalone HTML transcript exports; code-copy controls remain available.
- Made native conversation saves safer by writing to a temporary file beside the destination and replacing the destination only after the complete JSON file has been written.
- Corrected build and setup documentation, added a static validation report, fixed a broken documentation link, removed obsolete branding screenshots from the README, and corrected the MIT copyright placeholder.
- Updated the unused PowerToys settings-panel hook. Its implementation was corrected in `1.6.12` to use the standard no-custom-panel pattern.

### Fixed
- Cleared a cached WebView2 bootstrapper after a failed launch or installation so an incomplete or invalid cached download does not block later startup attempts.

### Validation
- Static JavaScript, HTML, metadata, XML/XAML, and archive checks were performed. A Windows/.NET compile and live host tests remain necessary.

## [1.6.10] — Build warning cleanup and WebView2 clarification

### Fixed
- Enabled nullable annotations explicitly in the shared native conversation Save As helper. This removes compiler warning `CS8632` when the helper is compiled by the PowerToys project without enabling nullable analysis across the entire plugin project.

### Documentation
- Clarified that the WebView2 Evergreen bootstrapper checks for an installed runtime and downloads/runs Microsoft's installer only when the runtime is missing.
- Clarified that first launch can take longer on a machine without WebView2; subsequent launches reuse the installed shared runtime.

## [1.6.9] — WebView2 provisioning and conversation format

### Added
- Added automatic WebView2 Evergreen Runtime provisioning to both standalone and PowerToys hosts. RunMind checks for an existing runtime and, only when it is missing, downloads Microsoft's official bootstrapper and silently installs it for the current Windows user. An internet connection is needed on first launch only when WebView2 is not already installed.

### Changed
- Removed support for `.llmchat`, legacy LocalLLM JSON, and other portable JSON files as resumable conversations. The native conversation picker and readers now accept only `.runmind` files containing `format: "runmind"`.
- Both hosts use the same WebView2 runtime check and `.runmind` conversation validation.

## [1.6.8] — Native conversation Save As

### Added
- Added a native Windows Save As dialog to both desktop hosts. The current conversation title is pre-filled, `.runmind` is the default extension, and the chosen filename is written back to the conversation metadata and in-app/native window title. Browser-only previews retain a download fallback.
- Centralized the native save-dialog bridge in one shared helper linked into both WPF projects. The bridge accepts only validated messages from the local chat page; WebView2 host objects remain disabled.

### Retained
- Carried forward the non-blocking PowerToys delayed-query implementation, RunMind project/folder/assembly naming, conversation-title behavior, and open-in-current-window/open-in-new-window choices from `1.6.7`.

## [1.6.7] — RunMind project identity and non-blocking PowerToys queries

### Changed
- Renamed the source tree and build identities to `RunMind.Standalone`, `Community.PowerToys.Run.Plugin.RunMind`, `RunMind.sln`, `RunMind.exe`, and `Community.PowerToys.Run.Plugin.RunMind.dll`.
- Kept the PowerToys plugin GUID and action keyword stable. Existing `%LOCALAPPDATA%\LocalLLM` browser profiles and storage keys remain unchanged to preserve user data.
- New conversation files use the `.runmind` extension and JSON format marker `format: "runmind"`. At this release, opening also accepted `.llmchat` and legacy LocalLLM JSON; that compatibility was removed in `1.6.9`.

### Added
- Added **Open in this window** and **Open in new window** choices when opening a saved conversation. The new-window option leaves the current chat untouched and opens the selected conversation in a separate window in both standalone and PowerToys hosts.
- Added a narrowly scoped WebView2 message for opening saved conversations in new windows. Messages are accepted only from the app's local chat document and are checked for format, version, message count, and size. WebView2 host objects remain disabled.

### Fixed
- Removed synchronous `GetAwaiter().GetResult()` calls from the delayed PowerToys query callback. The callback starts an asynchronous worker and returns immediately; model discovery and generation run outside the PowerToys search callback, and results are refreshed after completion.

## [1.6.6] — RunMind branding and standalone conversation titles

### Changed
- Renamed the visible product to **RunMind**. The standalone executable became `RunMind.exe`, and the PowerToys plugin display name became RunMind.
- Retained the existing plugin ID/DLL name, internal namespaces/bootstrap API, action keyword, WebView2 data directories, and `.llmchat` file-format identifier at this stage to preserve compatibility. The legacy conversation format was later removed in `1.6.9`.

### Added
- Enabled the shared browser-side conversation-title generator for standalone chats. A prompt-derived fallback appears when the first message is sent; after the first successful assistant response, it is replaced by a model-generated title using the active provider/model and that connection's configured authorization headers.
- Updated the in-app heading, browser document title, and native Windows caption when the conversation title changes.
- Kept title generation independent for each standalone and PowerToys-launched window.

## [1.6.5] — Native title-bar theme

### Changed
- Made the standalone WPF title bar follow the Windows app appearance preference (`AppsUseLightTheme`) using the native DWM immersive dark-mode attribute.
- Re-applied the title-bar appearance when Windows broadcasts `WM_SETTINGCHANGE` or `WM_THEMECHANGED`, so an already-open window updates when Windows switches between light and dark modes.
- Retained the standard Windows title bar and controls; no custom window chrome or taskbar grouping changes were introduced.

## [1.6.4] — Window icons and multiple standalone instances

### Changed
- Set the LocalLLM ICO on both PowerToys-hosted chat window types so the title bar and taskbar display the application icon.
- Removed standalone single-instance activation so each launch opens a separate chat window in its own process.

## [1.6.3] — Branding, PowerToys query handling, and CORS diagnostics

### Added
- Integrated the supplied branding exports: colorful `Icon.ico` and `Icon.png`, raster sizes, light/dark monochrome PNG variants, and the theme-aware `Icon.svg`.
- Added an explicit WebView2 favicon reference and deployed the ICO beside the virtual-hosted chat page, avoiding the implicit missing `/favicon.ico` request.
- Added actionable model-discovery diagnostics for browser network/CORS failures.

### Changed
- Allowed one-character prompts in global PowerToys mode instead of suppressing them before delayed execution.
- Documented that WebView2 fetches originate from `https://localhost` and provider servers must allow that origin. Rewriting the provider hostname to `127.0.0.1` avoids the virtual-host collision but does not bypass server-side CORS.

## [1.6.1] — PowerToys two-stage query workflow

### Changed
- Finalized the global two-stage PowerToys Run workflow and explicit `llm` action keyword. Standalone launch remains available through normal application search.
- Tightened stale-query protection and cancellation ownership across fast queries, delayed execution, and pending-result hand-off.
- Hardened WPF dispatcher checks and WebView2 initialization/disposal diagnostics for the PowerToys host; retained separate WebView2 profiles for standalone and PowerToys.
- Updated build, setup, feature, implementation, and validation documentation for the `1.6.1` source package.

### Validation
- JavaScript and metadata checks passed. A Windows/.NET build and live PowerToys/WebView2 tests remained necessary.

## [1.6.0] — Global two-stage PowerToys Run results

### Added
- Added a global two-stage PowerToys Run result: an immediate `Ask LocalLLM` placeholder followed by a completed response result.
- Added query-operation cancellation. New fast queries cancel previous requests; delayed execution uses linked cancellation tokens for model discovery, response headers, and streamed response bodies, and suppresses stale results.

### Changed
- Enabled global plugin matching while retaining the `llm` action keyword. `llm` by itself no longer opens a chat window; the standalone executable is launched through normal application search.
- Selecting a pending result hands the request to the chat window and cancels the PowerToys request first. Selecting a completed result opens the chat with the existing prompt and answer.
- Marshalled PowerToys window creation, WebView2 initialization entry points, clipboard actions, and teardown to the WPF dispatcher. Added safe disposal and stage/HRESULT diagnostics.
- Separated PowerToys and standalone WebView2 user-data folders, added a standalone single-instance activation signal (removed in `1.6.4`), and integrated the supplied ICO/PNG/SVG branding assets.
- Kept the shared `ChatWindow.html` implementation and existing provider, Agent, Deep Research, workspace, and MCP code intact.

## [1.5.1] — Agent and Deep Research reliability

### Added
- Added immediate PowerToys Run live-chat hand-off as the default inline behavior: selecting the result opens the shared chat and starts the prompt automatically. The previous blocking inline behavior remains configurable. In-place token mutation of PowerToys results is not supported by the plugin result contract.
- Added task-scoped snapshots of model connection, generation settings, and tool definitions for Agent and Deep Research runs.
- Added collision-resistant MCP tool names and safe expired-session recovery for idempotent reads/lists.

### Changed
- Bounded direct page/document and Agent tool-turn response bodies.
- Improved Deep Research source provenance with discovered/read distinction, retrieval route, final URL, and failed-fetch records.
- Tightened LM Studio base URL validation and cancelled conversation-title generation when the owning window closes.
- Retained the one-product/two-host architecture without an installer, startup registration, tray, or service.

## [1.5.0] — Standalone Windows chat application

### Added
- Added a directly launched Windows desktop host using WebView2 and the existing shared chat application.
- Added explicit WebView2 disposal and window-lifetime checks to the PowerToys chat host.
- Added bounded MCP response reads, request deadlines, and parsing for multiple SSE data events.
- Applied bounded response reads to direct webpage and Reader retrieval.

### Changed
- Kept the PowerToys Run plugin as the second entry point to the same product.
- At this release, both hosts shared a WebView2 profile and therefore application settings and local browser storage. They were separated in `1.6.0`.
- Pressing Enter on the `llm` action keyword without a prompt opens a clean chat window.
- Removed global provider/base-URL mutation during model discovery; each discovery request uses its own connection snapshot and credentials.
- Serialized concurrent model refresh operations.
- Did not add an installer, startup registration, tray, service, Electron, C# Agent bridge, or MCP process bridge.

## [1.4.0-preview.7] — Persistent connections, workspace access, and title generation

### Added
- Added conversation-title generation when the user explicitly opens the standalone Chat window. The request uses the initial PowerToys Run provider/model and combines the initial prompt and response. The native caption starts as `LocalLLM`; a generated title updates the native caption and in-app heading. A prompt-derived fallback is used on failure. Normal inline PowerToys Run invocations do not trigger title generation.
- Added a unique native-generated window ID to the WebView bootstrap. Recent Agent Tasks and Deep Research checkpoints are isolated using this ID before history/checkpoints are loaded, rather than relying on `window.name` as the primary namespace.
- Added persistent model-server API-key authentication per connection, including a configurable authentication-header name. Saved keys are used for discovery, metadata, chat, and tool-call requests.
- Added persistent Jina Reader API key and custom gateway token/endpoint configuration, per-provider connection tests, and explicit credential clearing.
- Added persistent MCP server URLs, enabled state, and bearer tokens. Enabled saved servers attempt to reconnect at chat startup; individual saved tokens can be cleared.
- Added persistent workspace directory handles in IndexedDB, permission rechecks on new chats, reconnection of existing handles, and notifications to other open windows after selection, reconnection, change, or clear.

### Changed
- Kept direct website fetch as the first retrieval strategy. Jina Reader (`https://r.jina.ai`) is the second fallback; an optional user-configured Reader-compatible gateway is the third. No Cloudflare dependency is included.
- Improved Agent and Deep Research evidence instructions to follow links to version-matched manuals, release notes, and source-tree build instructions rather than treating landing/index pages as substantive documentation or inventing unverified build details.
- Kept Deep Research drawer visibility separate from Deep Research execution mode. Opening the drawer does not enable research; enabling the composer mode opens it.
- Cleaned Agent settings text and clarified persistent credential state.

### Validation
- Embedded JavaScript syntax, HTML IDs/selectors, project metadata, static storage/title/tool-flow checks, and ZIP integrity were checked. A .NET compile and live Windows/WebView2 test were not possible in that environment.
- Fixed two C# compile errors in the conversation-title WebView2 update path by awaiting the WPF `DispatcherOperation<Task<string>>` and then awaiting the returned WebView2 script task. No behavior or public API changes were made beyond restoring compilation of the preview.7 title-update implementation.

## [1.4.0-preview.6] — Reader endpoint and gateway configuration

### Fixed
- Corrected the Jina Reader endpoint and request shape. The default is `https://r.jina.ai`; requests use the documented `GET https://r.jina.ai/{target-url}` format instead of the unavailable EU hostname and `?url=` query parameter.

### Added
- Added a configurable Reader base URL, optional Jina Bearer API key, and a connection-test button in Agent settings. Anonymous Reader access remains supported; an API key raises the documented rate limits.
- Added an optional custom Reader-compatible HTTP gateway with its own optional Bearer token and connection test. It is attempted only after Jina fails.
- Added response handling for either a Jina JSON envelope (`data.content`) or readable plain-text/Markdown responses. Retrieval diagnostics identify the provider and HTTP status/detail where available.

### Changed
- Kept Jina and custom gateway tokens in per-window `sessionStorage`; they are not written to `localStorage`, exported conversation files, or task history.
- Updated Reader documentation and settings text. Public URLs may be sent to the selected Reader provider; private/local URLs, embedded credentials, and likely secret-bearing query parameters are blocked from remote forwarding.
- Kept `Main.cs` unchanged. The native window caption was still created by C# from the initial model name; dynamically changing/removing it required a C# host change and was not included in this preview.

### Validation
- Embedded JavaScript syntax, DOM ID uniqueness/references, Reader URL construction and mock response parsing, project metadata, and ZIP integrity were checked. Live CORS behavior and the .NET build still required Windows testing.

## [1.4.0-preview.5] — Agent task isolation and Reader fallback

### Fixed
- Fixed Recent Agent Tasks leaking between independent chat windows. Task-history storage now uses a versioned key scoped to the unique top-level chat-window ID. The old shared v1 history is not migrated because its entries cannot be reliably attributed to one window.
- Replaced the GitHub-specific API branch in `fetch_webpage` with one generic page-reader path. GitHub and HAProxy are ordinary URL targets, not special workflows.

### Added
- Added a configurable EU Reader fallback for `fetch_webpage`. Direct browser fetch remains first; if browser CORS, anti-bot, or HTTP access restrictions block it, the app can send a public URL to Jina AI's EU Reader and return extracted page text.

### Changed
- Enabled the Reader fallback by default, with an option to disable it in Agent settings. The UI discloses that public URLs are sent to the remote reader. Local/private IPs, localhost, credential-bearing URLs, and URLs with likely secret-bearing query parameters are not forwarded.
- Updated Recent Agent Tasks help text to explain that each chat window has a separate task list. Clearing history clears only the current window's list.
- Improved Deep Research activity output to show fetched character count and retrieval route. Errors preserve both direct-fetch and Reader-fallback failures, and research instructions tell the model to try an alternate official page rather than abandoning a task after one failed read.

### Validation
- Embedded JavaScript, fetch-fallback mocks, private-URL blocking, opt-out behavior, HTML IDs/selectors, and ZIP integrity were checked. Live WebView2/CORS behavior and .NET compilation still required Windows testing. `Main.cs` remained unchanged.

## [1.4.0-preview.4] — Deep Research drawer and task-history isolation

### Added
- Replaced the full-width research section above the transcript with an optional right-side overlay drawer.
- Added a header toggle to open/reopen the drawer, a visible close control, and Escape-to-close behavior.

### Changed
- Separated Deep Research mode from drawer visibility. Closing the drawer does not stop research, delete its checkpoint, or disable the mode. Switching to ordinary chat/Agent Mode hides the drawer while preserving saved research results.
- Scoped research checkpoint keys with a unique per-window identifier stored in `window.name`, while retaining `sessionStorage` for reload/resume in that window. A new chat window does not read another window's checkpoint, even when WebView2 shares a browser profile.
- Cleaned Recent Agent Tasks so only conversations with evidence of an actual tool invocation are retained. Ordinary chat and greetings are excluded from the old history list. The history popup is height-limited and scrollable.
- Kept C# unchanged.

### Validation
- Embedded JavaScript passed `node --check`. Actual .NET compilation and live WebView2 behavior still required Windows testing.

## [1.4.0-preview.3] — General Agent workflow and cross-window isolation

### Added
- Added compatibility parsing for a model that emits a complete function-style tool call as text (for example, `researchupdateplan(...)`) instead of native structured `tool_calls`. Only a response consisting of a recognized tool invocation is promoted to an action.
- Added generic file-intent preflight and refreshed the tool registry after folder-access changes so file tools become available immediately after the user selects a folder.
- Added one general completion-audit turn for sequential multi-step Agent requests after tool execution, discouraging the model from stopping after the first convenient action.

### Changed
- Removed the request-specific GitHub URL → HTML → read-back → summary shortcut. Agent Mode now relies on its general tool loop to plan and execute user-requested multi-step tasks; GitHub is not a hardcoded workflow.
- Stored Deep Research checkpoints in per-WebView `sessionStorage` instead of shared profile `localStorage`, preventing cross-window leakage. Opening a different conversation clears the current workspace association.
- Made Agent Mode and Deep Research mutually exclusive. Enabling one turns off the other; Deep Research retains access to the shared agent and enabled MCP tools.
- Moved the clearly labelled Close workspace control into the visible research toolbar.
- Canonicalized tool-name aliases in native tool-call responses, including aliases missing underscores.
- Kept `Main.cs` unchanged.

### Validation
- JavaScript syntax, parser smoke tests, static HTML checks, and ZIP integrity checks passed. Live Windows/WebView2 behavior and .NET compilation remained unverified.

## [1.4.0-preview.2] — Research controls and tool approvals

### Added
- Added Copy plan and Copy results controls for live research work and partial/final reports.
- Added persistent, explicitly opted-in per-tool **Always allow this tool** approvals, with a revocation list and clear-all control in Agent settings.

### Changed
- Improved GitHub repository fetching to return README text, repository metadata, and a bounded root-file listing in the readable content field.
- Strengthened Agent instructions to complete dependent multi-step requests rather than stopping after an initial fetch.
- Clarified that native WebView2/Windows file-access prompts are browser-controlled and cannot be restyled by the HTML application.
- Kept `Main.cs` unchanged.

### Fixed
- Fixed the Deep Research workspace visibility bug: Close/Escape hides the panel without cancelling or deleting the active/saved research task.

### Validation
- JavaScript syntax and static HTML checks passed. Windows/WebView2 and .NET runtime tests remained unverified.

## [1.4.0-preview.1] — Deep Research and expanded Agent tools

### Added
- Added standalone Deep Research mode alongside ordinary chat and Agent Mode.
- Added editable research plans with checkpoint-applied revisions, page/tool budgets, source collection, and Markdown report export.
- Added pause/resume/cancel lifecycle controls and `localStorage` checkpoints.
- Added direct file downloads, bounded web extraction (links, metadata, tables, sitemap, RSS/Atom), and GitHub repository README retrieval.
- Expanded selected-folder tools for editing, deleting, copying, moving, renaming, metadata, checksums, comparisons, project overview, and duplicate detection.
- Added CSV analysis/transforms/comparison and JSON validation/formatting tools.
- Added Markdown/HTML report creation, SVG chart/flow-diagram generation, and browser-canvas image operations.

### Changed
- Preserved the existing C# source; only JavaScript, version metadata, and documentation were updated.

### Validation
- This remained a preview. Full .NET compilation and live WebView2/provider/MCP testing were not possible in the build environment.

## [1.3.0] — Agent, connections, MCP, and workspace foundation

### Starting point and preservation
- Based on the user's `1.2.3` source and the `1.3.0-preview.4` development snapshot.
- Preserved the compact PowerToys Run UI, inline provider settings, standalone WebView2 chat, exports, context manager, and normal streaming chat path.
- Kept `Main.cs` unchanged. No C# MCP bridge, native process launcher, or STDIO transport was added.
- Kept `RequestTimeoutSeconds` behavior for inline PowerToys Run results unchanged; standalone chat remained governed by user cancellation and provider completion/errors.

### Added
- Added inline approval cards for Agent/MCP tool execution, replacing browser-native confirmation dialogs. Cards show the requested tool, server, path/URL, and arguments; potentially mutating actions receive a warning treatment.
- Added **Allow once**, **Deny**, and (for read-only Agent tools) **Allow for this task** actions. Task-scoped read approvals reset when a new Agent run starts. MCP tool calls/resources/prompts and file creation remain explicit per-call approvals.
- Made Stop/cancel dismiss a pending approval.
- Added a browser-side connection manager for multiple Ollama and OpenAI-compatible endpoints. Enabled servers are discovered sequentially and models are merged into one dropdown, grouped by alias and endpoint host. Connections can be added, enabled, disabled, and removed; the two built-in defaults can be disabled but not removed.
- Made the selected connection determine the endpoint used for discovery, metadata, ordinary streaming chat, and Agent tool-call requests. Endpoints and aliases are stored in WebView `localStorage`; conversation exports record the selected connection ID so duplicate model names can be restored to the same configured server when available.
- Added `update_plan` for concise Agent Mode plans, with pending/in-progress/completed statuses rendered in the transcript.
- Added local recent task history (up to 20 snapshots) for Agent Mode and MCP-enabled chats. History can reopen a snapshot or be cleared. Snapshots are stored in the WebView profile; attachment bytes are excluded.
- Retained the per-run maximum tool-step setting as the tool-call limit. Read-only approvals can be allowed once or for the current run; the automatic read-only setting remains available. Tool errors are returned to the model as tool results so it can attempt recovery within the configured step limit.
- Added a browser-only Streamable HTTP MCP client: initialize/initialized, tool discovery/calls, resource listing/reading, prompt listing/retrieval, pagination, session IDs, protocol-version headers, optional bearer authentication, disconnect, enable/disable, remove, and reconnect.
- Added MCP access in Agent Mode and optionally in ordinary chat. MCP tool/resource/prompt actions use the inline approval card.
- Added explicit-URL routing to `fetch_webpage`; public GitHub repository URLs use GitHub's public API to retrieve metadata and README.
- Added `web_search`, exposed only when a SearXNG URL is configured.
- Added selected-folder file tools to list, read UTF-8 text, and create new files without overwriting existing files.

### Changed
- Kept a user-selected folder available across prompts and Agent Mode toggles for the current WebView session. The user can clear it in Agent settings. The model instructions tell the model not to ask for the folder again when file tools are already available.
- Kept folder access session-scoped in this release: it is not persisted across WebView restarts, and file tools remain unavailable until a folder is selected.
- Kept MCP bearer tokens session-only. Server URLs are saved locally; HTTP/HTTPS only. STDIO is unsupported because no native process bridge was added.
- Kept MCP resources discoverable/readable; this build does not create a persistent vector index or automatically embed/index all remote resources.

### Limitations at this release
- Model-server API-key authentication was not implemented. OpenAI-compatible endpoints requiring bearer/API-key authentication did not work in this build. MCP bearer authentication was supported but session-only.
- Remote model and MCP endpoints had to allow WebView2 CORS/network access. Some browser-only MCP implementations and authentication flows might not work.
- LM Studio Native REST mode did not support Agent tool calls; OpenAI-compatible mode was required.
- Persistent knowledge-base embedding/indexing, MCP STDIO, file edit/delete, task retry UI, and a secure credential vault were not included.
- JavaScript syntax, HTML IDs/selectors, and ZIP integrity were checked. A full .NET build and live WebView2/provider/MCP tests could not be run because the .NET SDK and configured live test servers were unavailable.

### Web search validation
- `web_search` is included in the model's Agent tool definitions only when Agent settings contain a syntactically valid HTTP(S) SearXNG instance URL.
- Saving a non-empty malformed URL is rejected with an explanatory toast; an empty URL saves normally and explicitly leaves web search disabled.
- Tool execution independently rechecks the normalized configured endpoint and fails closed if the setting is missing or invalid, protecting against stale tool calls or settings changed after tool discovery.
- The configured URL is normalized by removing a trailing slash; credentials embedded in URLs are rejected.
- `fetch_webpage` remains available independently, so direct URL fetching does not require a search engine.

## [1.2.3] — Regenerate thinking-toggle behavior

### Fixed
- Regenerate no longer restores the previous assistant message's `thinkRequested` value. It preserves the current thinking toggle, so enabling thinking before regenerating with a different model/provider is respected.

### Unchanged
- Standalone provider/model selection behavior, `Main.cs`, and the solution were unchanged. Only `ChatWindow.html` and version metadata in the project and plugin manifests changed.

## [1.2.2] — Inline PowerToys provider selection

### Added
- Added a **PowerToys Run LLM provider** setting with **Ollama** and **LM Studio** choices.

### Changed
- The selected provider controls inline PowerToys Run model discovery and generation.
- Ollama uses the configured `/api/tags` and `/api/generate` endpoints, retaining its `keep_alive` and `think` request options.
- LM Studio uses the configured OpenAI-compatible base URL, `GET /v1/models` for model discovery, and streaming `POST /v1/chat/completions` for generation.
- The default-model field is shared; its description refers to the model name/ID from the selected provider.
- The standalone WebView2 chat retains its own provider selector and behavior; the PowerToys Run setting does not force the chat window to use the same provider.
- `RequestTimeoutSeconds` applies only to inline PowerToys Run generation for either provider. `KeepAliveSeconds` and `Thinking` are Ollama-specific and are not sent to LM Studio.

## [1.2.1] — Streaming, context management, and reliability

### Fixed
- Fixed the live thinking disclosure interaction. Streaming updates now patch the existing assistant message in place and preserve the `<details>` node, allowing the user to expand/collapse thinking while generation is active.
- Made the LM Studio API mode setting provider-specific. It is hidden for Ollama selections and shown only when an LM Studio model is selected; the saved preference is retained when switching providers.

### Added
- Added an explicit `[hidden]` style so provider-specific setting fields are reliably removed from the CSS grid.
- Added a dedicated **Context** toolbar panel to replace context controls that were difficult to find inside Generation Settings.
- Added estimated input-token usage and a utilization bar based on a manual per-request context override or detected active/model context metadata.
- Added separate counts for included, summarized, and unsummarized/omitted messages.
- Added an optional hard recent-turn cap, an optional stricter estimated history-token budget, and provider-aware model context length.
- Added a reviewable/editable summary, explicit save/clear actions, and a preview of the selected context. The preview is limited to the most recent 30 included messages for UI responsiveness; this does not change the request.

### Changed
- Summary and history-token-budget settings are preserved in portable conversation files. Recent-turn and token-budget preferences are also remembered locally.
- Context estimates are explicitly approximate. They use serialized text length and do not precisely represent model tokenization, image costs, system prompt overhead, provider formatting, or reserved output tokens.
- Changing context selection invalidates LM Studio native response continuation IDs to prevent a later request from continuing with a stale server-side context chain.
- Scoped the PowerToys Run request-timeout setting to inline PowerToys Run generation. It is not included in the WebView2 bootstrap or applied to standalone chat. Chat generation uses the user-triggered Stop cancellation signal; discovery and metadata lookups retain short independent deadlines.
- Added context detection through Ollama `/api/ps` for active loaded context and `/api/show` for model maximum metadata. LM Studio uses `/api/v1/models` for loaded instance context and model maximum, with `/api/v0/models` as a compatibility fallback. Metadata failures do not block chat generation.
- Distinguished manual request overrides, active loaded context, and model maximum (not currently loaded) in the Context UI.
- Removed the implicit 12-user-turn cap. Blank now means no hard turn cap; old persisted default-12 preferences are migrated to no cap. When context capacity is detected, an adaptive history budget retains space for output/provider overhead. An optional user-set history-token cap can make that budget stricter.
- Updated Ollama and both LM Studio stream readers to cancel unfinished response bodies and release reader locks on errors, Stop, and early completion.
- Normalized LM Studio object-shaped capability metadata (vision, tool-use, reasoning) into model-info capability labels.

### Validation
- Embedded JavaScript syntax, HTML ID uniqueness, and static assertions for timeout separation, context metadata, and history defaults were checked.
- Context-plan tests covered full history, recent-turn limiting, token-budget trimming, newest-turn preservation, and summary/omission boundaries.
- Stream readers were exercised with mocked completion/error streams to verify cancellation and reader-lock cleanup. WebView2/provider runtime behavior and C# compilation still required testing on Windows.

### Historical follow-up notes (release version not recorded)
The original changelog recorded the following reliability follow-ups without assigning them to a specific release. The dated 2026-10-30 thinking-disclosure and LM Studio setting fixes are already consolidated under this version above, so they are not repeated here.
- Scoped the PowerToys Run request timeout to inline generation only. Standalone chat uses user-triggered Stop cancellation, while discovery and metadata lookups retain independent deadlines.
- Added provider-aware context detection and UI labels for manual override, active loaded context, and model maximum.
- Removed the implicit 12-user-turn cap and migrated the old persisted default of 12 to no hard cap.
- Updated Ollama and LM Studio stream readers to cancel unfinished response bodies and release reader locks on errors, Stop, and early completion.
- Normalized LM Studio object-shaped capability metadata for vision, tool use, and reasoning.

## [1.2.0] — Streaming, provider, context, and PowerToys result improvements

### Changed
- Batched streaming content updates with `requestAnimationFrame` and patched only the active message; completed transcript messages remain in place.
- Changed the one-second metrics refresh to update only the metrics element, avoiding transcript rebuilds and Markdown reparsing.
- Handled thinking disclosure state through one delegated listener instead of reattaching listeners after each full render.
- Made preferences reject `null`, arrays, and non-object JSON values.
- Added a 15-second connection-establishment ceiling, a separate short model-discovery deadline, and a longer generation deadline. The PowerToys Run Ollama model list is cached for five seconds. WebView2 request deadlines remain active while response bodies are consumed, including streamed reads.
- Improved Ollama NDJSON and LM Studio SSE parsers to report malformed records/events and fail clearly when a stream is unusable.
- Added time-to-first-token metrics alongside elapsed time, token count, and throughput.

### Added
- Added a small provider registry with normalized callbacks for chat model discovery and generation.
- Added opt-in LM Studio native REST v1 support for native reasoning events, context-length configuration, server response IDs, and stateful continuation.
- Added conversation context summarization. Older turns can be summarized on demand; the summary is included in subsequent requests and exports, while original messages are retained.

### Changed — provider and context behavior
- LM Studio OpenAI-compatible requests retry only for recognized optional-parameter validation errors. Rejected capabilities are cached per endpoint/model for the current WebView session.
- The OpenAI-compatible LM Studio adapter remains available for broad compatibility.
- If an LM Studio native conversation is imported, edited, summarized, or exceeds the configured history window, the next request starts a fresh native thread seeded with the managed local transcript.
- The full conversation remains local and available for export/editing. Requests can be limited to a configurable number of recent user turns (default 12 at this release). Older turns can be summarized on demand; the summary is included in subsequent requests and exports without deleting the original messages.
- Context estimates are labelled approximate and are based on text length rather than actual tokenizer counts. Editing/regenerating invalidates a potentially stale summary.

### Changed — PowerToys Run results
- Preserved the compact multi-result presentation.
- Updated section splitting to preserve all characters exactly (concatenating sections reproduces the original response) and avoid splitting UTF-16 surrogate pairs.
- Kept the context-menu copy action copying the complete response rather than only the selected section.

### Validation and limitations at this release
- Checked imported attachment count, declared/decoded image size, allowed image MIME types, and per-message aggregate size.
- JavaScript endpoint normalization rejects invalid schemes and embedded credentials.
- C# model discovery has its own 8-second timeout; C# generation keeps the configured timeout through stream reading.
- PowerToys Run's plugin query contract is synchronous and exposes no documented in-place result mutation callback. Inline generation therefore returns completed results; live token-by-token launcher result updates were intentionally not implemented.
- The original 1.2.0 archive did not include PowerToys reference assemblies under `lib\x64`, and the editing environment did not have the .NET SDK. The C# project could not be compiled there. Embedded JavaScript passed `node --check`, but WebView2/provider integration was not tested.

## [1.1.0] — WebView2 chat window

### Added
- Replaced the response window's legacy WPF `WebBrowser` renderer with WebView2.
- Seeded each response window with its original prompt and initial Ollama response. Request-error windows also retain the prompt when available.
- Added per-window in-memory conversation history. At this release, closing the window ended the session unless the user exported it.
- Added concurrent Ollama and LM Studio model discovery at chat startup. A single model selector groups models by provider and includes a refresh button; models from the available endpoint remain usable if the other endpoint is unavailable.
- Added the `LMStudioBaseUrl` PowerToys setting (default `http://localhost:1234/v1`). At this release it was used for chat model discovery and follow-up turns; inline PowerToys Run queries remained on the configured Ollama `/api/generate` endpoint.
- Added Ollama follow-up streaming through `/api/chat` and LM Studio follow-up streaming through the OpenAI-compatible `/v1/chat/completions` API.
- Added System, Light, and Dark themes. CSS color, font, radius, and shadow variables were centralized in the `:root` rules in `Main.Chat.cs` for developer customization.
- Added exports for standalone HTML, Open WebUI import JSON, portable conversation JSON, OpenAI-compatible request JSON, Markdown, CSV, TXT, JSONL, and Print/Save as PDF. CSV uses UTF-8 with BOM and guards against common spreadsheet formula-injection prefixes.

### Export behavior
- Standalone HTML exports contain the rendered transcript, embedded CSS, and a small local code-copy helper. They do not contain the WebView2 messaging bridge or Ollama/LM Studio endpoint logic and do not connect to the plugin after export.
- Open WebUI export produces a JSON array containing a conversation `chat` object with a linear message tree (`parentId`, `childrenIds`, and `currentId`). Import it through Open WebUI's **Settings → Data Controls → Import Chats**. The format followed the documented import shape but was not tested against a live Open WebUI instance in the original environment.

### Build requirements and validation at this release
- Required Windows and the .NET SDK targeted by the included project file, Microsoft Edge WebView2 Runtime, and NuGet packages including `Microsoft.Web.WebView2`.
- The original source archive omitted the PowerToys common-library DLLs expected under `lib\x64`; those assemblies had to be supplied separately for the PowerToys build.
- The editing environment did not have the .NET SDK or the omitted PowerToys common libraries, so a full Windows build/runtime test could not be performed. Embedded JavaScript syntax and project XML were checked.

## [1.0.3.1] — Initial Ollama PowerToys Run plugin

### Dependencies
- Uses `Markdig` to render Markdown and `HtmlAgilityPack` to sanitize generated HTML. Keep both package references in the project file.
- The PowerToys reference assemblies and target framework must align with the PowerToys version installed on the Windows machine.

### Behavior and security
- Sends the user's query unchanged as Ollama's `prompt`; no hidden prompt or temperature is added.
- Keeps the configured default model unchanged when a one-off model override is used.
- Supports `> model-name > your prompt` for per-query model selection.
- Makes `keep_alive` and request timeout configurable in PowerToys Run settings.
- Uses Ollama `/api/tags` to distinguish a missing model from an endpoint/network failure.
- Parses newline-delimited `/api/generate` streaming JSON and reports API errors instead of displaying them as model output.
- Provides a clean, responsive, theme-aware HTML response page with browser scrolling, selectable text, clickable HTTP/HTTPS/mailto links, a Copy Markdown button, and per-code-block copy buttons.
- Disables model-supplied raw HTML, strips images and active elements, restricts link schemes, and uses a restrictive Content Security Policy. The response page makes no external requests on its own.
- Keeps the PowerToys result subtitle short; Enter opens the full response, and the result context menu copies the complete Markdown response.
