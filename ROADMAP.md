# RunMind roadmap

## Current release: 2.3.0

RunMind 2.3.0 adds explicit persistent project indexing and reusable developer workflows to the shared Agent experience. The index is scoped to a Workspace Context and stored locally in the existing IndexedDB database; it incrementally reuses unchanged files, indexes bounded text sources and manifests, and records supported imports and symbol declarations, and derives dependency/framework hints from common manifests. Project search, overview, and static validation tools expose that index to the Agent; static validation does not execute build or test commands. Indexing is opt-in and bounded, and excludes common generated/dependency/cache directories, credential/secret paths, binary files, and oversized files. Reusable developer workflows cover project understanding, feature implementation, bug investigation, change review, and release preparation; workspace-specific custom workflows are also supported. They run through the existing Agent runtime, tool contract, approvals, task history, Skills, workspace tools, and Git operations. All three hosts retain the same shared ChatWindow and native bridge architecture. The index is a local lightweight code/project index, not a full language-server or compiler-grade semantic index; symbol/import extraction is heuristic and language-specific; unchanged-file reuse relies on size and modification-time metadata.

## Planned release sequence

- **2.2.0 — PowerToys Command Palette Extension:** provide a first-class Command Palette host that shares the canonical RunMind chat UI and native runtime, supports a branded inline fallback with streaming responses and follow-up turns, and opens chat with empty or generated context. A native extension settings page configures the shared Ollama/LM Studio model and generation settings used by PowerToys Run and Command Palette.
- **2.4.0 — Standalone RunMind Launcher:** a dedicated standalone launcher experience built on the same Agent Core and native host capabilities.

These are planned scopes, not shipped features or release-date commitments. The detailed backlog below is retained as historical candidate work from the 1.10.x roadmap and should be re-evaluated against the 2.x plan before implementation.

### 2.2.0 implementation boundary

The Command Palette extension is an independent MSIX/WinRT COM host. It launches its own chat process from the packaged Command Palette executable and hosts the canonical shared ChatWindow, ArtifactTools, and native bridge. It does not launch or depend on the Standalone executable. Free-text fallback input is handled by a native Command Palette content page, which calls the selected local provider directly and streams the answer in the palette; follow-up questions stay in the same page. The native extension settings page configures a provider/model/generation settings file shared with PowerToys Run; it does not launch the chat window. The Command Palette package's COM-host app entry is hidden from Start/Search; the MSIX remains installed to register the extension. The packaged extension requires deployment/reload through Command Palette.

---

## 1.10.34 completed

- Improved the default size and viewport placement of toolbar flyouts. All flyouts can be temporarily moved and resized while open; their geometry resets on close and is never persisted.

## 1.10.33 completed

- Hardened transcript rendering, CSV analysis, workspace search budgets, IndexedDB commit handling, attachment lookup, and post-approval workspace file revalidation without adding dependencies or changing the streaming rendering model.

## 1.10.32 completed

- Added shared, persisted System/Light/Dark appearance preferences with five curated palettes per mode. The original Classic Light and Warm Graphite palettes remain defaults; palette choices are independent, apply immediately, and synchronize across standalone and PowerToys chat windows, including native title bars.

- [x] Give the branch-comparison AI action a consistent action label and a theme-native control style.
## 1.10.30 completed

- [x] Match LM Studio title generation to the normal reasoning-off request settings, retain the optional Nemotron template preference, and retry requests only when the server explicitly rejects an optional field.
- [x] Extract text from standard string or structured OpenAI-compatible responses and report a diagnostic when the model returns no assistant text.
- [x] Make the AI Branch Comparison composer single-line when empty, automatically grow to five lines, and scroll internally after reaching the five-line cap.

## 1.10.29 completed

- [x] Fix the remaining native-response title update case where WebView2 initialization replaces the original control with a temporary-profile fallback; generated titles now target the active hosted control.
- [x] Ignore delayed title/navigation events from replaced controls and confirm the page accepted the generated title before updating the native window title.
- [x] Retry transient title-application failures and record diagnostics without disguising a failure as a successful fallback-title update.

## 1.10.28 completed

- [x] Expand the AI Branch Comparison composer to at least five visible lines and auto-grow it before scrolling; keep the change isolated from the main chat composer.

## 1.10.27 completed

- [x] Fix the PowerToys native-response title race by waiting until chat/WebView2 initialization completes before generating and applying the LLM title.
- [x] Avoid title-generation work if the window closes or WebView2 initialization fails.

## 1.10.26 completed

- [x] Keep the System prompt and original comparison dossier hidden while pre-filling the editable Initial assessment instruction in the AI discussion composer.
- [x] Do not generate an assessment automatically when the modal opens; send the edited instruction only after the user presses Send.
- [x] Record the submitted instruction as the first user message and preserve follow-up, copy, and Markdown export behavior.

## 1.10.25 completed

- [x] Make branch comparison take over the complete area below the application header, including the transcript, composer, and chat toolbar; preserve a clear **Back to chat** path.
- [x] Make **AI Branch Comparison** a centered modal containing a streaming follow-up discussion with an independent model selector and a **Re-examine branches** action.
- [x] Bind each discussion to its immutable branch-comparison snapshot and preserve it when the modal closes/reopens or the user returns to chat and reopens the same comparison.
- [x] Add per-message copy, copy-all Markdown, and complete `.md` export of the original dossier, initial assessment, and ordered follow-up messages.
- [x] Keep comparison chat isolated from branch histories and Agent/Deep Research tools; retain all discussion context and expose an approximate token estimate.

## 1.10.23 completed

- [x] Contain the branch preview within a bounded comparison area with independent scrolling.
- [x] Present the AI assessment in a fixed, internally scrollable modal so it cannot displace the chat composer.
- [x] Stop an active assessment when the modal is closed or the comparison context changes.

## 1.10.22 completed

- [x] Add model-assisted comparison to the existing branch comparison view, with a predefined neutral assessment template and a streamed, cancellable result.
- [x] Default the independent comparison-model selector to the active chat model while allowing another discovered model to be selected without changing the main chat model.
- [x] Limit comparison input to each branch's latest assistant response and preceding user request; keep the result outside branch histories and conversation messages.

## 1.10.21 completed

- [x] Treat public-host DNS responses containing only non-public addresses as recoverable Native Reader failures, never as permission to connect to those addresses.
- [x] Keep Reader failover inside the application: Native → configured Custom Reader → Jina, or Native → Jina when no custom gateway is configured.
- [x] Preserve hard stops for explicit private/local URL targets and unsafe redirects.

## 1.10.20 completed

- [x] Separate OpenAI-compatible protocol identity from optional LM Studio metadata capability.
- [x] Preserve user-defined connection names throughout message display and conversation serialization.

Current 1.10.34 improves flyout sizing, viewport placement, and temporary drag/resize controls. Current 1.10.33 hardens transcript rendering, CSV analysis, bounded workspace search, IndexedDB durability, attachment resolution, and post-approval file checks. Current 1.10.32 adds curated, independently remembered light/dark palettes and shared appearance preferences across both hosts. Current 1.10.31 refines the **Compare with AI** workspace action and styles it with RunMind theme tokens. Current 1.10.34 provides larger, movable, and resizable flyouts with temporary per-open geometry. Current 1.10.29 fixes the remaining PowerToys native-response title application path when WebView2 replaces its control; title generation now runs after chat/WebView2 initialization and is skipped if initialization fails. Current 1.10.26 presents branch comparison as a full lower-workspace view with side-by-side answer panels and a centered AI Branch Comparison discussion modal. Its initial assessment instruction is pre-filled and editable, and no model request is made until the user presses Send. The modal retains snapshot-bound follow-up history, per-message copy, and Markdown export; 1.10.22 introduced model-assisted comparison. Current 1.10.21 corrects Native Reader handling for public hostnames whose local DNS answers contain only non-public addresses, allowing the application-level remote fallback chain to run without connecting to those addresses.
# Remaining implementation backlog

This file preserves the full feature wishlist discussed for RunMind. This backlog is updated to reflect RunMind 1.10.34. Checked items describe the implemented bounded capability; unchecked items remain future work. See `FEATURE-MATRIX.md` for current behavior and limits.

Current 1.10.29 fixes the remaining PowerToys native-response title application path when WebView2 replaces its control, confirms the page accepted the title, and then synchronizes the native window title. Current 1.10.26 provides a dedicated full lower-workspace branch comparison view and a centered AI Branch Comparison discussion modal. The initial assessment instruction is pre-filled and editable, but only runs after the user presses Send; the hidden system prompt and branch dossier remain application-managed. The 1.10.25 release added persistent discussion and Markdown export. Current 1.10.21 keeps Reader failover inside the application and treats DNS answers containing only non-public addresses for an otherwise public hostname as a recoverable Native Reader failure. Current 1.10.20 preserves connection aliases in message identity and conversation serialization. Current 1.10.18 prioritizes the configured custom Reader gateway before Jina when native retrieval fails, adds the optional model-specific Nemotron 3.5 Bolt E4B conversational-mode setting, and makes full branch-workspace saving the default with an active-branch-only escape hatch. Current 1.10.17 adds native-first public webpage reading in both desktop hosts, with the configured remote Reader retained as fallback. Current 1.10.16 preserves literal underscores and asterisks embedded in identifiers during Markdown rendering. Current 1.10.15 restores the requested `Branch N` labels for generic child branches and migrates saved auto-generated `Alternative N` labels back to `Branch N`. Current 1.10.14 refines the editable PowerToys QuickAI starter prompt. Current 1.10.13 adds a Copy action to text-bearing user prompts in the chat transcript; original prompt text remains copyable while a response is streaming. Current 1.10.12 adds the editable pre-filled QuickAI system prompt in PowerToys settings and a shared native work-area-aware startup placement helper for both PowerToys and standalone windows; the default height is reduced by 24 DIPs. Current 1.10.x UX refinements: Deep Research is a mutually exclusive toolbar flyout (not a side drawer), and Always-approved tools can be removed individually from Agent settings.
RunMind 1.10.5 also opens that flyout when Deep Research is enabled from the composer and replaces the blue-toned dark palette with Warm Graphite.
RunMind 1.10.7 replaces the insufficient horizontal-only flyout adjustment with shared viewport-aware placement for all toolbar and Conversations panels, including vertical overflow and narrow-window handling.
Current 1.10.11 prevents completed inline responses from overwriting newer PowerToys Run input by returning completed LLM results through PowerToys’ delayed query phase; the plugin no longer rewrites search-box text with `ChangeQuery`. Current 1.10.10 adds a native multiline PowerToys Run custom system prompt editor (PowerToys 0.80.0+); current 1.10.9 fixes flyout scroll restoration across all toolbar menus. Current 1.10.8 settings additions: independent PowerToys inline system instructions and sampling defaults; multiline chat-app custom instructions with preview/persistence; prompt composition across chat, Agent, Deep Research, and sub-agents; and consistent generation parameters in tool-calling paths.

## RunMind 1.9.0 — implemented scope

- One Deep Research drawer with Research, Review, and Recovery views.
- Model-assisted claim assessments and qualitative source assessments, with locally checked exact-quote presence and explicit limits.
- Interactive claim review: filters, source passages, user Accept/Reject decisions, and targeted follow-up investigation.
- Source disagreement and uncertainty instructions, plus hostname/source-dependency caveats.
- Per-task checkpoint history, confirmed restore, exact-operation result reuse, and acknowledgement for ambiguous interrupted operations.
- Selected-model discovery, generation, and harmless tool-call diagnostics.
- Approximate context telemetry and active-limit-aware compaction of older research tool messages.
- Seven research templates and expanded Markdown export.


## RunMind 1.10.0 — implemented scope

- Shared bounded tool-batch scheduler for Agent Mode and Deep Research; parallel read-only allowlist, configurable 1–4 concurrency, deterministic result order, model-turn dependency layers, and persisted operation intent.
- Workflow view with recent execution layers, operation input/output inspection, retry for safe read-only failures, interruption visibility, drag/drop preferred start order for future independent read-only groups, and local performance analytics.
- Bounded one-level sub-agent delegation for Researcher, Coder, Security Analyst, Reviewer, and Summarizer roles; read-only tool subset, maximum four child model/tool turns, normalized structured outputs, and Deep Research parent-budget/domain enforcement.
- Conversation branching from completed messages, independent local branch histories, rename/delete/switch, return to Original, and side-by-side latest-answer comparison. Local snapshots are bounded and omit binary attachment bytes; `.runmind` remains the full conversation archive.
- 1.10.19: `.runmind` save scope offers All branches by default or Active branch only. Full saves preserve complete branch histories, active selection, names/IDs, and fork-origin metadata where known; older single-branch files remain compatible.
- Local analytics for tool calls, success/failure, duration, provider/model request latency, and provider-reported token usage when available.

## RunMind 1.10.1 — hotfix

- [x] Fix Deep Research context-metric assignment when an active provider/model context limit is available (`estimatedPromptTokens: estimatedTokens`).

## RunMind 1.10.2 — branch UI hotfix

- [x] Keep conversation branch controls collapsed until explicitly opened; provide a compact toggle and close button.
- [x] Fix transcript-frame flex direction so branch management and comparison occupy full-width rows instead of a side-by-side half-screen pane.
- [x] Ensure the HTML `hidden` attribute is respected by the branch manager despite its flex display rule.

## RunMind 1.10.3 — branch title and spacing hotfix

- [x] Keep the parent conversation title stable when switching branches; branch labels remain local to the branch manager.
- [x] Display the immutable storage branch `main` as **Original**, migrating existing local state without changing its ID.
- [x] Suggest child-branch labels from the latest user prompt at the fork point; use numbered `Branch N` labels for generic prompts and disambiguate duplicates.
- [x] Hide the redundant **Branches** toggle while the manager is open so the manager sits directly below the application header.

## Core agent completeness

- [x] JSON Schema validation (`validate_json_schema`; bounded keyword coverage and local `$ref`, with unsupported advanced keywords reported as warnings)
- [x] Regex tester (`regex_test`) with execution-safety constraints (isolated worker, hard timeout, and bounded inputs)
- [x] Attachment-aware Agent file tools for text/JSON, PDF, and ZIP files; PDF/ZIP uploads are signature-checked and bounded
- [x] PDF text extraction and OCR (`read_pdf`; PDF.js extraction plus per-page Tesseract fallback, English/Persian bundled and optional cached language packs; encrypted PDFs remain unsupported)
- [x] Image understanding from workspace files (`read_image`; requires a vision-capable model)
- [x] More complete connection diagnostics and error classification (selected-model discovery, small generation probe, and harmless tool-call probe in Recovery; LM Studio Native REST remains not tested and custom endpoints require chat-completions compatibility)
- [x] Retry/recovery controls and bounded idempotency-aware tool result reuse (exact tool name + canonical arguments within an uncommitted batch; ambiguous attempts require acknowledgement; no exactly-once guarantee)
- [ ] Backups/undo for file edits and stronger transactional writes (file edit/delete backup and restore are implemented; broader rollback coverage remains)
- [ ] Robust direct-download metadata, content validation, and resumable large downloads

## Web research and knowledge

- [x] Multi-query topic investigation orchestrator (`research_topic`; up to six SearXNG queries, bounded results, URL deduplication; requires configured SearXNG)
- [x] Website crawling with depth/domain limits (`crawl_website`; same-origin, maximum 20 pages and depth 3)
- [x] Link validation (`check_links`; up to 30 HTTP HEAD checks; CORS/unsupported HEAD is unverified)
- [x] Page-to-Markdown conversion (`page_to_markdown`; direct HTML conversion with Reader fallback)
- [x] Webpage comparison (`compare_webpages`; exact trimmed-line comparison, not semantic comparison)
- [ ] Searchable sitemap traversal and website change tracking
- [ ] Persistent local knowledge indexing and full-text search
- [ ] Unified knowledge search across local files and MCP resources
- [x] Research source register with metadata, excerpts, normalized-URL deduplication, domain allow/block/preferred rules, exact-quote presence checks, model-assisted claim assessments, and qualitative source assessments and a model-generated source-dependency note (not deterministic entailment, reputation, factual verification, or independence proof)
- [x] Domain prioritization and allow/block restrictions in Deep Research (preferred domains rank first; blocked domains take precedence)
- [x] Preserve bounded page excerpts, author/publication/modified metadata when available, retrieval route, and retrieval timestamps
- [ ] More robust alternative Reader/proxy provider adapters and endpoint-specific telemetry
- [x] Model-assisted semantic claim assessment and explainable qualitative source assessment (local exact-quote presence check only; not deterministic entailment, reputation, factual verification, or independence proof)

## Workspace and project tools

- [x] ZIP archive inspection (`inspect_archive`; workspace and attached ZIPs, standard ZIP only, ZIP64 and multi-disk archives remain unsupported)
- [x] ZIP extraction (`extract_archive`; workspace or attached ZIP, approval-gated, CRC-checked, traversal-safe; TAR extraction remains pending)
- [ ] More complete project overviews and semantic/code search
- [ ] Folder content comparison and robust content-level duplicate detection
- [ ] Clipboard access (`clipboard_read`, `clipboard_write`) with explicit permissions
- [ ] Additional file-search modes and result filters
- [ ] Rich document conversion with metadata preservation

## Data analysis and documents

- [x] JSON Schema validation (`validate_json_schema`)
- [ ] Advanced JSON transforms
- [x] CSV dataset joins (`join_datasets`; inner/left/full) and heuristic schema inference (`infer_dataset_schema`)
- [ ] Dataset validation rules and broader CSV/TSV processing
- [ ] DOCX creation (`create_docx`)
- [ ] XLSX creation (`create_xlsx`)
- [ ] PDF creation (`create_pdf`)
- [ ] Presentation creation (`create_presentation`)
- [ ] Markdown report generation enhancements (`create_markdown_report`)
- [ ] Advanced chart generation (`create_chart`)
- [ ] Editable diagram generation (`create_diagram`)
- [ ] Document conversion (`convert_document`)
- [ ] HTML report generation enhancements (`create_html_report`)

## Image and media tools

- [ ] QR code generation (`generate_qr_code`)
- [ ] Image galleries (`create_image_gallery`)
- [ ] Rich metadata/EXIF inspection and thumbnails for more media types
- [ ] More image transformations with EXIF orientation/color-profile handling

## Advanced MCP support

- [ ] Resource templates
- [ ] Resource subscriptions and updates
- [ ] Completion support
- [ ] Elicitation support
- [ ] Progress notifications
- [ ] Robust cancellation propagation
- [ ] Structured tool output rendering
- [ ] OAuth authorization flow
- [ ] MCP Tasks protocol support
- [ ] Full Streamable HTTP/SSE streaming and reconnect semantics
- [ ] Unified searchable MCP knowledge/resource index

## Advanced agent and Deep Research runtime

- [x] Streaming tool-call visibility for Ollama NDJSON and OpenAI-compatible SSE, including partial function names/arguments and live progress (calls execute after complete argument parsing)
- [x] Pause/resume with bounded IndexedDB checkpoints, heartbeat, and same-profile recovery (tool-batch checkpoints; no exactly-once guarantee or cross-profile synchronization)
- [x] Shared bounded parallel tool execution with dependency layers and read-only allowlist (model tool-call batches are layers; arbitrary in-batch dependency inference is not attempted)
- [x] Safe workflow cancellation through the shared active AbortSignal (queued operations are marked cancelled and later model-turn layers are not dispatched; external side effects already accepted cannot be undone)
- [x] Checkpoint history, review summaries, confirmed restore/rollback, and resume/retry controls (up to eight bounded snapshots per task in the current WebView2 profile)
- [x] Approximate token/context monitoring and adaptive compaction of older research tool results (only when an active context limit is known; estimate is serialized characters ÷ 4)
- [x] Search/page/tool/time budgets and live adjustment for Deep Research (time limit is a soft boundary; resumable downloads remain future work)
- [x] One-level specialized sub-agent workflows with bounded read-only tool access and structured result normalization
- [x] Source register with metadata, deduplication, exact quote presence checks, and a research evidence list
- [x] Research Review view with claim filters, model verdicts, quoted source passages, local exact-quote match state, user accept/reject decisions, and targeted follow-up investigation
- [x] Per-task checkpoint history and tool-operation recovery ledger with safe acknowledgement for ambiguous interrupted calls
- [x] Model-assisted semantic claim assessment and explainable qualitative source-quality assessment (quote presence is checked locally; verdicts do not prove entailment, source authority, or factual truth)
- [x] Research task templates (General, Technical, Product/Technology Comparison, Security, Academic, Website, Troubleshooting)
- [ ] Scheduled tasks (subject to user-approved execution model)
- [x] Conversation branching with independent local histories, rename/delete/switch, and side-by-side latest-answer comparison (bounded branch snapshots omit binary attachment bytes)
- [x] Local tool/provider usage analytics (provider token counts only when reported)
- [x] Workflow dependency-layer visualization and drag/drop preferred start order for future independent read-only tool groups
- [x] Research preferred/allow/block domain rules

## Research controls

- Research depth presets (Standard, Deep, Extensive), max pages, max tool calls, and optional time budgets.
- User-editable plan while running, step reordering, adding/removing questions, scope changes, source/domain preferences, pause/resume/cancel, and report export.
- Cross-checking, disagreement/conflict reporting, explicit uncertainty, methodology, limitations, and linked source appendix.
- Public web, enabled MCP resources/tools, user-supplied URLs, and selected workspace files as research inputs when the user grants folder access. Persistent knowledge indexing remains future work.

## Workspace and data capabilities

- File discovery/info, folder creation, rename/move/copy, file/folder comparison, project overview, archive inspect/extract, checksum, duplicate finding, file edit/delete/search, PDF and image reading, clipboard read/write.
- Web extraction: link extraction, crawling, sitemap traversal, tables, broken-link checks, webpage comparison, Markdown conversion, RSS reading, metadata extraction, research-topic orchestration.
- Data: CSV analysis/merge/filter/deduplicate/compare, JSON/schema validation and transforms, regex tests, statistics.
- Outputs: DOCX, XLSX, PDF, presentation, Markdown/HTML reports, charts, diagrams, document conversion.
- Media: resize, convert, crop, metadata, QR, thumbnails, galleries.
- Agent runtime: true streaming, pause/resume, retry, parallel execution, checkpoints, token/context monitoring, budgets, subagents, source manager, task templates, scheduled tasks, branching, analytics.


## RunMind 1.10.15 — generated branch-label correction

- [x] Use `Branch 1`, `Branch 2`, etc. for generic child branches while keeping descriptive prompt-based names for meaningful fork points.
- [x] Migrate generated `Alternative N` labels back to `Branch N` while preserving descriptive branch names.

## RunMind 1.10.14 — QuickAI prompt refinement

- [x] Use the concise two-sentence QuickAI text as the pre-filled PowerToys Run prompt.
- [x] Migrate only the exact previous built-in starter prompt; preserve user-customized and explicitly empty values.
- [x] 1.10.14 briefly migrated generated `Branch N` labels to `Alternative N`; 1.10.15 restores `Branch N` and migrates those saved generated labels back.

## RunMind 1.10.13 — copy sent prompts

- [x] Add a Copy action to each text-bearing user prompt, alongside existing branch/edit actions.
- [x] Copy the original prompt text exactly, preserving line breaks and Markdown syntax rather than copying rendered HTML.
- [x] Allow prompt copying during active response generation without changing message, branch, or generation state.
- [x] Retain existing assistant-response copying and the previous edit/branch availability rules.
