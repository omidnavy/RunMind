# Remaining implementation backlog

This file preserves the full feature wishlist discussed for RunMind. All unchecked items remain in scope; there are no priority levels or implementation phases. Implemented items are recorded in `FEATURE-MATRIX.md` for 1.7.0.

## Core agent completeness

- [x] JSON Schema validation (`validate_json_schema`; bounded keyword coverage and local `$ref`, with unsupported advanced keywords reported as warnings)
- [x] Regex tester (`regex_test`) with execution-safety constraints (isolated worker, hard timeout, and bounded inputs)
- [x] PDF text extraction (`read_pdf`; best-effort selectable text, no OCR or encrypted/object-stream PDF support)
- [x] Image understanding from workspace files (`read_image`; requires a vision-capable model)
- [ ] More complete connection diagnostics and error classification (`diagnose_connection` now checks model discovery and classifies common failures; endpoint-specific and generation diagnostics remain)
- [ ] Retry/recovery controls and idempotency-aware tool replays
- [ ] Backups/undo for file edits and stronger transactional writes (file edit/delete backup and restore are implemented; broader rollback coverage remains)
- [ ] Robust direct-download metadata, content validation, and resumable large downloads

## Web research and knowledge

- [ ] Multi-query topic investigation orchestrator (`research_topic`)
- [ ] Website crawling with depth/domain limits (`crawl_website`)
- [ ] Link validation (`check_links`)
- [ ] Page-to-Markdown conversion (`page_to_markdown`)
- [ ] Webpage comparison (`compare_webpages`)
- [ ] Searchable sitemap traversal and website change tracking
- [ ] Persistent local knowledge indexing and full-text search
- [ ] Unified knowledge search across local files and MCP resources
- [ ] Source manager with exact evidence snippets, citation verification, source trust, allow/block lists, and deduplication
- [ ] Domain prioritization and restrictions in Deep Research
- [ ] Preserve page excerpts, publication dates, and retrieval timestamps
- [ ] More robust alternative Reader/proxy provider adapters and endpoint-specific telemetry

## Workspace and project tools

- [x] ZIP archive inspection (`inspect_archive`; standard ZIP only, ZIP64 and multi-disk archives remain unsupported)
- [x] ZIP extraction (`extract_archive`; approval-gated, CRC-checked, traversal-safe; TAR extraction remains pending)
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

- [ ] True streaming tool-call pipeline
- [ ] Pause/resume with durable, transactional checkpoints and recovery
- [ ] Parallel tool execution with dependency scheduling
- [ ] Checkpoint review, retry, and roll-back controls
- [ ] Token/context monitoring and adaptive context management
- [ ] Search/download/time budgets and live budget adjustment
- [ ] Sub-agent workflows
- [ ] Source manager and citation verification
- [ ] Task templates
- [ ] Scheduled tasks (subject to user-approved execution model)
- [ ] Conversation branching
- [ ] Tool usage analytics
- [ ] Plan priorities, dependency graph, and drag/drop editing
- [ ] Research domain allowlist/blocklist and configurable source trust

## Research controls

- Research depth presets (Standard, Deep, Extensive), max pages, max tool calls, and optional time budgets.
- User-editable plan while running, step reordering, adding/removing questions, scope changes, source/domain preferences, pause/resume/cancel, and report export.
- Cross-checking, disagreement/conflict reporting, explicit uncertainty, methodology, limitations, and linked source appendix.
- Public web, MCP servers, local files, knowledge index, and user-provided sources as research inputs.

## Workspace and data capabilities

- File discovery/info, folder creation, rename/move/copy, file/folder comparison, project overview, archive inspect/extract, checksum, duplicate finding, file edit/delete/search, PDF and image reading, clipboard read/write.
- Web extraction: link extraction, crawling, sitemap traversal, tables, broken-link checks, webpage comparison, Markdown conversion, RSS reading, metadata extraction, research-topic orchestration.
- Data: CSV analysis/merge/filter/deduplicate/compare, JSON/schema validation and transforms, regex tests, statistics.
- Outputs: DOCX, XLSX, PDF, presentation, Markdown/HTML reports, charts, diagrams, document conversion.
- Media: resize, convert, crop, metadata, QR, thumbnails, galleries.
- Agent runtime: true streaming, pause/resume, retry, parallel execution, checkpoints, token/context monitoring, budgets, subagents, source manager, task templates, scheduled tasks, branching, analytics.
