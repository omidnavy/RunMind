# Remaining implementation backlog

This file preserves the full feature wishlist discussed for RunMind. This backlog is updated to reflect RunMind 1.10.10. Checked items describe the implemented bounded capability; unchecked items remain future work. See `FEATURE-MATRIX.md` for current behavior and limits.

Current 1.10.x UX refinements: Deep Research is a mutually exclusive toolbar flyout (not a side drawer), and Always-approved tools can be removed individually from Agent settings.
RunMind 1.10.5 also opens that flyout when Deep Research is enabled from the composer and replaces the blue-toned dark palette with Warm Graphite.
RunMind 1.10.7 replaces the insufficient horizontal-only flyout adjustment with shared viewport-aware placement for all toolbar and Conversations panels, including vertical overflow and narrow-window handling.
Current 1.10.10 adds a native multiline PowerToys Run custom system prompt editor (PowerToys 0.80.0+); current 1.10.9 fixes flyout scroll restoration across all toolbar menus. Current 1.10.8 settings additions: independent PowerToys inline system instructions and sampling defaults; multiline chat-app custom instructions with preview/persistence; prompt composition across chat, Agent, Deep Research, and sub-agents; and consistent generation parameters in tool-calling paths.

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
- [x] Suggest child-branch labels from the latest user prompt at the fork point; use numbered alternatives for generic prompts and disambiguate duplicates.
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
