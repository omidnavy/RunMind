# RunMind

**A local-first AI workspace for Windows.** RunMind connects local language models and OpenAI-compatible providers to a chat application that can research the web, work with files, analyze data, create documents and visual assets, and coordinate multi-step tasks—with user approval for operations that change your workspace.

![Screenshot](screenshots/Screenshot-1.png)

RunMind is available through three Windows entry points:

- **Standalone Windows app** — a full chat window that can be launched independently, with support for multiple windows.
- **PowerToys Run plugin** — start a RunMind conversation directly from PowerToys Run using `llm <prompt>` or search for RunMind. The retained inline query can be opened in multiple independent chat windows, including after the first window takes over an in-progress answer.
- **PowerToys Command Palette extension** — an independent host with a branded **RunMind** inline fallback, **Open chat with empty context**, and an extension settings page for the shared provider/model configuration. The fallback streams local model answers directly into a Command Palette conversation page and supports follow-up turns. It starts its own chat process from the packaged Command Palette executable and hosts the canonical shared chat UI; it does not launch or require the Standalone executable. Inline settings are shared with PowerToys Run. The helper package is hidden from Start and Windows Search because it is a COM host, not a separately launchable app. `Build-Release-x64.bat` builds all three Release x64 hosts and creates/signs the Command Palette MSIX without installing it. `Install-RunMind-PowerToys-Run.bat` separately installs the already-built PowerToys Run plugin after checking that PowerToys is closed; `Install-RunMind-CommandPalette.bat` separately installs or uninstalls the already-built Command Palette package.

## What you can do with RunMind

- Chat with local models hosted by Ollama or LM Studio, or connect additional OpenAI-compatible endpoints.
- Switch between configured providers and models, including custom API keys and authentication headers.
- Ask questions, work through code and technical problems, inspect images and documents, and use model tool-calling capabilities.
- Use **Agent Mode** for multi-step tasks that combine web research, workspace files, data processing, artifact creation, Git operations, approved PowerShell/native execution, on-demand Agent Skills, a Global/Project Skills Manager, and organized Agent Settings for Agent, Project, Skills, and Security, plus a dedicated Windows System Manager flyout. Tall flyouts remain anchored to their toolbar buttons.
- Use **Deep Research** for structured investigations with editable plans, source collection, evidence notes, checkpoints, and report export.
- Connect external **Model Context Protocol (MCP)** servers over Streamable HTTP to make their tools, resources, and prompts available to RunMind.
- Create and manage conversation branches, compare branch responses with AI, and continue a separate discussion about a comparison without changing the original branches.
- Start a fresh conversation in the current or a separate window, and save or reopen conversations in RunMind's `.runmind` format.
- Choose from curated light and dark palettes, use Windows System appearance, and move or resize open toolbar flyouts.
- Themes are discovered at runtime from `Shared/ChatWindow/theme/`. Add one `<id>.css` file with a `RunMind-Theme` metadata comment declaring its `id`, `name`, and supported `modes` (`light`, `dark`, or both), plus the matching palette rules. Remove the file to remove that theme. No HTML or JavaScript theme registry or build-time catalog is needed. Malformed themes are skipped independently. The normal build/deployment copies the complete theme folder to all three hosts.

Example theme header:

```css
/* RunMind-Theme: {"id":"ocean","name":"Ocean","modes":["light","dark"]} */

:root[data-theme="light"][data-color-theme="ocean"] {
  color-scheme: light;
  /* Define the light palette tokens. */
}

:root[data-theme="dark"][data-color-theme="ocean"] {
  color-scheme: dark;
  /* Define the dark palette tokens. */
}
```

For a single-mode theme, list only that mode and provide only its matching palette rule. The native bridge discovers the name and modes from the file itself.

## Built-in capabilities and tool catalog

RunMind exposes tools to Agent Mode and, where applicable, Deep Research. Tools that access local files require a user-selected workspace folder. Mutating operations—such as creating, editing, moving, or deleting files—require approval and follow the application's no-overwrite and backup safeguards. Some web-search tools appear only when the corresponding search service is configured; MCP tools depend on connected servers.

### Planning, reasoning support, and diagnostics

| Tool | What it does |
|---|---|
| `current_datetime` | Returns the computer's current local date and time. |
| `update_plan` | Creates or updates a concise task plan with pending, active, and completed steps. |
| `calculate` | Evaluates basic arithmetic expressions, including parentheses, percentages, and exponentiation. |
| `regex_test` | Tests JavaScript regular expressions in an isolated worker with a hard timeout. |
| `diagnose_connection` | Checks the selected model connection, model discovery, selected-model availability, and likely configuration or CORS issues. |
| `delegate_subagent` | Delegates a bounded, read-only subtask to a Researcher, Coder, Security Analyst, Reviewer, or Summarizer sub-agent. |

### Web research and website inspection

| Tool | What it does |
|---|---|
| `fetch_webpage` | Reads the exact supplied URL using the native Reader and configured Reader fallbacks. |
| `page_to_markdown` | Retrieves a webpage as clean Markdown with available page metadata. |
| `web_search` | Searches the web through a configured SearXNG endpoint. |
| `research_topic` | Runs a bounded multi-query web investigation and deduplicates results by URL. |
| `extract_links` | Extracts HTTP/HTTPS links from a webpage. |
| `extract_metadata` | Reads page title, description, canonical URL, Open Graph data, and selected metadata. |
| `extract_tables` | Extracts bounded HTML tables into headers and row data. |
| `read_sitemap` | Recursively traverses same-host sitemap indexes, deduplicates URLs, and searches URL and sitemap metadata. |
| `read_rss_feed` | Reads recent items from public RSS and Atom feeds. |
| `check_links` | Checks the reachability of a bounded set of links; CORS failures are reported as unverified rather than broken. |
| `compare_webpages` | Compares readable text from two webpages and reports shared and differing passages. |
| `crawl_website` | Performs a bounded, same-origin breadth-first crawl of a website. |

### Workspace and file operations

| Tool | What it does |
|---|---|
| `request_folder_access` | Requests that the user select a local workspace folder before file operations. |
| `list_files` | Lists files and folders in the selected workspace. |
| `search_files` | Searches workspace filenames and, optionally, bounded UTF-8 file contents. |
| `read_file` | Reads a UTF-8 text file. |
| `create_file` | Creates a new text file without overwriting an existing file. |
| `download_webpage` | Downloads webpage content through the Reader pipeline and saves it locally. |
| `download_file` | Downloads original response bytes. Uses browser fetch for smaller CORS-accessible files or the native streaming/resumable downloader for larger or browser-restricted downloads. |
| `file_info` | Returns file size, MIME type, last-modified time, and name. |
| `edit_file` | Replaces a text file after approval and creates a recovery backup. |
| `restore_file_backup` | Restores a previous backup after approval. |
| `delete_file` | Deletes a file after approval and creates a recovery backup. |
| `create_folder` | Creates a workspace folder after approval. |
| `copy_file` | Copies a file to a new path after approval. |
| `move_file` | Moves a file to a new path after approval. |
| `rename_file` | Renames a file after approval. |
| `checksum_file` | Calculates a file's SHA-256 checksum. |
| `compare_files` | Compares two UTF-8 text files and identifies the first differing line. |
| `project_overview` | Builds a bounded overview of a project workspace. |
| `find_duplicates` | Finds files with identical SHA-256 content within configured scan limits. |
| `compare_folders` | Compares two folders by relative file paths and sizes. |

### Git integration, Project Profiles, and Agent Skills

| Tool / capability | What it does |
|---|---|
| `git_clone` | Clones an HTTP/HTTPS Git repository into a new folder using Git for Windows and the native folder picker. Supports self-hosted Gogs/Gitea endpoints. |
| `git_select_repository` | Grants native Git access to one existing normal working tree for the active conversation. |
| `git_status`, `git_diff`, `git_log`, `git_show`, `git_branches`, `git_remotes` | Read working-tree status, diffs, history, commit summaries, branches, and configured remotes. |
| `git_stage`, `git_unstage`, `git_commit`, `git_create_branch`, `git_switch_branch`, `git_merge`, `git_revert`, `git_cherry_pick`, `git_stash` | Perform common local Git operations subject to the active conversation's approval policy. |
| `git_fetch`, `git_pull`, `git_push` | Perform remote operations with an explicitly named remote and branch. |
| `git_reset_hard`, `git_clean`, `git_restore`, `git_delete_branch` | Perform destructive operations; these always require a fresh approval. |
| `list_agent_skills` | Discover `SKILL.md` metadata in global and selected-project Skill folders. |
| `load_agent_skill` | Load the selected Skill instructions and explicitly requested references on demand. Scripts are not automatically executed. |
| Branch comparison | Compare each branch’s latest completed response to its own latest user prompt; different branch lengths are supported. |
| Project Profiles | Save and reuse project instructions, provider/model connection, safe Agent settings, Skill names, MCP references, and conversation permission policy across both desktop hosts. |
| Agent Skills Manager | Browse Global and Current Project Skills, inspect descriptions, install a local Skill folder, and remove a selected installation after confirmation. |

Git requires Git for Windows on `PATH`. This release supports normal repositories with a local `.git` directory, not linked worktrees. A native Git folder selection is separate from the browser workspace grant. Project Profiles are shared through `%LOCALAPPDATA%\RunMind\ProjectProfiles`; browser folder handles remain in IndexedDB in the current WebView profile and must be reconnected when switching hosts/devices. Credential fields and filesystem handles are not copied into profiles or portable conversations; do not place secrets in freeform project instructions.

Global Skills are stored under `%LOCALAPPDATA%\RunMind\Skills`; project Skills live under `.agents\skills` inside the selected browser workspace. Use **Agent Settings → Agent Skills** to manage either scope. Manual installation remains supported: copy a Skill folder containing `SKILL.md` to `%LOCALAPPDATA%\RunMind\Skills\<skill-name>\` for global use, or `.agents\skills\<skill-name>\` for project use. The required `SKILL.md` metadata fields are `name` and `description`. Skills load on demand and cannot grant themselves permissions; scripts are never executed automatically.

The Skills Manager also accepts direct HTTPS `SKILL.md` URLs and GitHub/GitLab repository folder URLs. RunMind previews metadata and content and requires confirmation before installing. A chat message that explicitly asks to install a Skill from a URL is recognized, and valid `SKILL.md` content pasted into chat is detected automatically; both are offered for preview/installation; declining the offer sends the text as a normal chat message. URL and pasted-content imports support Global and Current Project scopes, but currently import `SKILL.md` only rather than recursively fetching linked resources. Skills remain untrusted instructions; scripts and commands are never executed by the installer.

### Developer diagnostics and quality workflows

RunMind includes reusable workflows for project builds and tests, static analysis/lint, Playwright test-runner checks, API smoke tests, dependency security audits, feature implementation, and issue diagnosis. They inspect the project before choosing commands and use the existing native PowerShell execution path with explicit approval. Playwright support invokes a project's already-installed Playwright CLI; it is not an embedded interactive browser automation runtime. API smoke tests are workflow-guided PowerShell requests rather than a dedicated API client. `project_validate` performs static manifest/test-file discovery only and does not claim to run builds or tests.

The native process tools are `select_native_workspace`, `powershell_execute`, and `run_native_command`. They require a separately selected native execution workspace and fresh approval for each process invocation. Processes run with the current Windows user's permissions and are not sandboxed.

### Windows System Manager (optional)

Open System Manager from its own toolbar flyout, separate from Agent Settings. System Manager is disabled by default. When enabled, users can independently enable Application Management, Process Management, and System Diagnostics, then toggle individual capabilities within each group. The settings are shared across Standalone, PowerToys Run, and Command Palette. The native bridge re-checks the master switch, group, and capability for every operation; disabled System Manager tools are not offered to the Agent.

- **Application Management:** discover applications from Start Menu shortcuts, registered App Paths, and PATH; keep a local catalog with five-minute freshness and on-demand refresh; launch catalogued applications; open existing files/folders and HTTPS URLs.
- **Application adapters:** built-in adapters for VS Code, Windows Terminal, Firefox, Notepad++, and Blender, plus user-defined declarative JSON adapters. Adapters use argument arrays only—no scripts, shell commands, or arbitrary executable paths. See `docs/SYSTEM-MANAGER-ADAPTERS.md`.
- **Process Management:** list and inspect processes. Termination is disabled by default and always requires a native Windows confirmation; RunMind verifies PID, process name and start time, checks executable path when available, and refuses selected critical Windows processes.
- **System Diagnostics:** read OS/runtime details, PATH entries, Windows service status, network adapters, active TCP connections and listeners, depending on individually enabled capabilities. Diagnostics do not change system configuration.
- **Permissions:** Restricted, Balanced and Custom profiles. Launch/open actions require native confirmation unless the user explicitly allows them in Balanced or Custom. Process termination and adapter installation always require confirmation.

System Manager Agent tools (only exposed when the master switch and relevant capabilities are enabled) include `system_manager_find_applications`, `system_manager_launch_application`, `system_manager_open_target`, `system_manager_list_adapters`, `system_manager_install_adapter`, `system_manager_application_action`, `system_manager_list_processes`, `system_manager_terminate_process`, and `system_manager_diagnostics`. Native launch/open/process actions re-check settings and confirmation policy independently of the Agent tool definitions.

System Manager does not provide administrator elevation, service control, registry editing, system configuration changes, or arbitrary GUI automation. It does not depend on PowerShell being enabled.

Unsaved conversations and Deep Research checkpoints use per-window temporary sessions in IndexedDB, with the session identifier in `sessionStorage`. A new window never inherits another window's conversation or research checkpoint. Normal window close removes temporary conversation data; abandoned sessions are cleaned up on a later startup after a 24-hour grace period. Workspace folder handles remain in their separate IndexedDB store. Save a `.runmind` file explicitly to retain a conversation.

### Documents, archives, and structured data

| Tool | What it does |
|---|---|
| `read_pdf` | Extracts PDF text and can OCR pages with missing or suspicious text using the languages selected in PDF OCR settings. |
| `inspect_archive` | Inspects ZIP entries, sizes, compression, encryption, and safety-limit violations without extracting the archive. |
| `extract_archive` | Extracts a supported ZIP archive into a new workspace folder after approval, with path-traversal and archive-safety checks. |
| `validate_json` | Parses a JSON file and reports its top-level type and basic structure. |
| `validate_json_schema` | Validates JSON data against an inline or file-based JSON Schema, including local `$ref` and common schema constraints. |
| `transform_json` | Pretty-prints, minifies, sorts keys, picks or omits fields, renames keys, flattens or unflattens objects, filters arrays, and maps values. |
| `infer_dataset_schema` | Infers CSV column types, nullability, uniqueness, and sample values. |
| `analyze_csv` | Reports CSV columns, row count, sample rows, and numeric summaries. |
| `generate_statistics` | Calculates descriptive numeric statistics for CSV columns. |
| `validate_dataset` | Validates CSV/TSV required and unique columns and applies per-column rules such as numeric ranges, membership, email, and URL checks. |
| `filter_dataset` | Filters CSV rows by column values and writes a new dataset. |
| `deduplicate_dataset` | Removes duplicate CSV rows or duplicates based on selected columns. |
| `merge_csv` | Appends CSV files with matching headers. |
| `join_datasets` | Joins two CSV datasets using selected keys and an inner, left, or full join. |
| `compare_datasets` | Compares CSV headers and row contents. |

### Reports, documents, charts, and diagrams

| Tool | What it does |
|---|---|
| `create_markdown_report` | Creates a Markdown report in the workspace after approval. |
| `create_html_report` | Creates a standalone HTML report with safe Markdown rendering, optional summary, and table of contents. |
| `create_docx` | Creates a Microsoft Word document from a supported Markdown subset, including headings, lists, emphasis, code, and simple tables. |
| `create_xlsx` | Creates multi-sheet Excel workbooks with typed cells, styled headers, frozen header rows, and filters. Text values are stored as text, not formulas. |
| `create_pdf` | Creates a paginated, text-focused PDF from Markdown content. |
| `create_chart` | Creates editable SVG bar, line, pie, and doughnut charts, including multiple data series. |
| `create_diagram` | Creates flow diagrams as SVG and editable Mermaid source. |
| `verify_artifact` | Checks the basic structure of generated or existing DOCX, XLSX, PDF, SVG, HTML, Mermaid, Markdown, JSON, CSV, and TSV artifacts. |

### Images and media

| Tool | What it does |
|---|---|
| `image_metadata` | Reads image dimensions, MIME type, and file size. |
| `read_image` | Attaches a workspace image to the next model turn for visual analysis by a vision-capable model. |
| `resize_image` | Resizes an image to fit within specified maximum dimensions. |
| `convert_image` | Converts an image to PNG, JPEG, or WebP. |
| `crop_image` | Crops an image using pixel coordinates. |
| `generate_thumbnail` | Creates a WebP thumbnail up to 256 × 256 pixels. |
| `generate_qr_code` | Generates an offline SVG QR code from text or a URL. |
| `create_image_gallery` | Creates a responsive standalone HTML gallery that references selected workspace images. |

### Connected MCP tools

When an MCP Streamable HTTP server is connected, RunMind can discover and call that server's tools. It can also list available knowledge resources, read a selected resource, and retrieve named prompts. These are supplied by the connected server, not bundled as fixed RunMind tools.

## Example workflows

RunMind's tools can be combined in one Agent task. For example:

- **Research to report:** search for a topic, open and read relevant pages, extract tables, compare sources, then create a Markdown, HTML, DOCX, or PDF report.
- **Dataset cleanup:** inspect a CSV, infer its schema, validate required columns and rules, filter or deduplicate rows, join another dataset, and export the result.
- **Project review:** request workspace access, map the project structure, search for relevant files, compare or checksum files, and prepare a findings report. File changes remain approval-gated.
- **Document production:** turn structured information into a Word document or Excel workbook, create charts or a flow diagram, then verify the generated artifacts' structure.
- **Image preparation:** inspect image metadata, resize, crop, or convert images, generate thumbnails or QR codes, and build an HTML gallery.
- **Website inspection:** crawl a bounded same-origin section, traverse its sitemap, inspect metadata and links, and compare pages.

## Models and integrations

### Model providers

- **Ollama** — default local API address: `http://localhost:11434`.
- **LM Studio** — use its local server with the OpenAI-compatible API, commonly `http://localhost:1234/v1`.
- **PowerToys Run authentication** — the plugin settings expose separate optional API key and custom auth-header fields for Ollama and LM Studio. API keys use Bearer authorization; a custom header uses `Header-Name: value` and overrides the Bearer value when its name is `Authorization`.
- **Other providers** — add an OpenAI-compatible endpoint, with optional API key and custom authentication headers.

PowerToys Run API keys and custom auth headers are stored in the per-user RunMind settings JSON without DPAPI encryption, and the PowerToys Run text fields are not masked. Use only credentials appropriate for local storage on that Windows account.

RunMind supports multiple configured connections. Model availability and capabilities such as vision and tool calling depend on the selected model and provider.

### Web search and Reader services

- **SearXNG** provides web search and multi-query research when configured.
- **Native Reader** is used by desktop hosts for webpage retrieval. A configured custom Reader-compatible gateway can be used as a fallback, followed by Jina Reader for public URLs.
- Reader retrieval and ordinary web search are separate: opening a supplied URL uses the Reader path; discovery uses the configured search service.

### Model Context Protocol

Connect MCP servers using **Streamable HTTP**. RunMind can discover server tools, resources, and prompts and make supported capabilities available to the agent. STDIO transport is not included.

## Modes and conversation features

### Chat

Use RunMind as a regular streaming chat interface with model/provider selection, conversation branches, attachments, and explicitly saved `.runmind` conversations.

### Agent Mode

Agent Mode can plan and execute multi-step tasks using built-in tools, selected workspace files, configured web services, connected MCP servers, native Git operations, and Agent Skills loaded on demand. Project Profiles provide reusable project instructions and safe settings shared by Standalone and PowerToys Run. Each conversation retains its own Workspace Context and permission policy; applying a profile does not silently transfer folder handles or native Git grants. Independent read-only operations can run in parallel where safe; mutating operations are approval-gated. Specialized sub-agents are bounded, read-only, and cannot delegate further.

### Deep Research

Deep Research supports editable research plans, bounded search and page budgets, source collection, evidence and claim assessments, source notes, pause/resume/cancel controls, checkpoints, and Markdown report export. Research claims and model assessments remain distinguishable from application-checked quotation presence; the application does not claim to independently establish the truth of a model's verdict.

### Branches and AI Branch Comparison

Create and switch between conversation branches, rename or delete branches, and compare branch content in two modes. **Single response** (default) compares one question/answer turn from each branch, initially selecting each branch's latest turn; users can independently choose any answered turn in Branch A and Branch B. **Whole branch** compares the complete ordered histories, including prompts, answers, per-message provider/model attribution, available text attachments, and recorded model thinking. **Compare with AI** opens a separate discussion over an immutable snapshot of the selected mode and content. It supports follow-up questions, model switching, re-examination, copying messages, and Markdown export. Large whole-branch dossiers may exceed the selected model's context limit; RunMind displays an approximate estimate rather than silently dropping turns. Comparison does not modify either branch or change the main chat model.

## Workspace safety and limits

- A browser workspace folder is selected by the user before local file operations are offered. Native Git repository selection is a separate, conversation-scoped authorization.
- File paths are workspace-relative; absolute paths and traversal outside the selected folder are rejected.
- Creating, editing, deleting, copying, moving, renaming, extracting, and generating files uses explicit approval where applicable.
- New-file operations do not overwrite existing files. Editing and deletion create recovery backups; restore operations are also approval-gated.
- File searches, web crawling, dataset operations, archive extraction, and artifact generation use explicit size, count, or depth limits to keep work bounded.
- Artifact verification checks file structure and basic readability. It does not guarantee visual layout, formula calculation, or semantic correctness.

Some generation formats have intentionally focused scope: DOCX supports a bounded Markdown subset; XLSX has worksheet, row, column, and cell limits; PDF is text-focused and uses built-in fonts; QR payloads are limited to 106 UTF-8 bytes; image galleries reference existing workspace images rather than copying them. See the [Feature Matrix](FEATURE-MATRIX.md) for exact limits and implementation details.

## Requirements

- Windows 11 x64 (build 22621 or later). An ARM64 build is not currently provided.
- Microsoft PowerToys is required for the PowerToys Run plugin and Command Palette extension; the standalone app does not require PowerToys.
- .NET 10 Desktop Runtime (x64) for framework-dependent standalone builds.
- The .NET 10 SDK to build the Command Palette extension. Its pinned Windows SDK/MSIX build tools are restored through NuGet. The first local MSIX installation requests administrator approval to trust the development signing certificate.
- Ollama, LM Studio, or another supported model server.
- Git for Windows on `PATH` for native Git tools.
- The Microsoft Edge WebView2 Runtime. RunMind checks for it at startup and, if missing, downloads Microsoft's Evergreen bootstrapper and installs the runtime for the current Windows user. An internet connection is needed for that one-time setup.

## Build from source

Run `Build-Release-x64.bat` from the repository root to prepare runtime resources, clean/build all three hosts in Release x64 mode, and package/sign the Command Palette MSIX. It does not install anything. Use `Install-RunMind-PowerToys-Run.bat` to install the already-built PowerToys Run plugin; it checks that PowerToys is closed and mirrors the complete plugin output to the per-user plugin directory. Use `Install-RunMind-CommandPalette.bat` to install the prebuilt MSIX, or `Install-RunMind-CommandPalette.bat uninstall` to remove it. Both installers are separate from the build and do not rebuild the projects. The release build environment requires the .NET 10 SDK. PowerToys reference assemblies are included under `lib\x64`:

- `PowerToys.Common.UI.dll`
- `PowerToys.ManagedCommon.dll`
- `PowerToys.Settings.UI.Lib.dll`
- `Wox.Infrastructure.dll`
- `Wox.Plugin.dll`

See [BUILD-AND-SETUP.md](BUILD-AND-SETUP.md) for detailed build and deployment instructions.

## Install and launch

### PowerToys Run plugin

1. Build all three hosts with `Build-Release-x64.bat`.
2. Exit PowerToys completely from its system-tray menu.
3. Run `Install-RunMind-PowerToys-Run.bat`. It verifies that PowerToys is closed, copies the complete Release x64 plugin output into the correct per-user Plugins folder, removes stale deployment files, and checks the required resources.
4. Start PowerToys again and use `llm <prompt>` or search for RunMind.

### Standalone app

Launch `RunMind.exe` from the standalone build output. Each launch opens an independent chat window; multiple standalone windows can run alongside PowerToys-hosted chats.

### PowerToys Command Palette extension

The extension is installed as a signed MSIX package by `Install-RunMind-CommandPalette.bat`. The installer also attempts to set the direct **RunMind** alias to **Open chat with empty context**; if Command Palette was open, close it and rerun `Configure-RunMind-CommandPalette-Alias.ps1`. The extension About section links to [omid.navy](https://omid.navy). On first installation, Windows requests administrator approval to trust the public certificate whose publisher matches the package. This trust step does not enable Developer Mode; Windows 10 version 2004 and later and Windows 11 enable sideloading by default unless a policy disables it. After installation, run Command Palette's **Reload Command Palette extensions** command; fully restart PowerToys if the extension list does not refresh. The extension provides a branded **RunMind** fallback and **Open chat with empty context**. Inline model configuration is available from Command Palette's Installed Extensions settings for RunMind; there is deliberately no searchable **Configure RunMind** command.

Command Palette starts a chat process from its own packaged executable and hosts the canonical `Shared/ChatWindow.html`, `Shared/ArtifactTools.js`, `Shared/ChatWindow/styles/`, `Shared/ChatWindow/theme/`, `Shared/ChatWindow/scripts/`, and shared native bridge directly. It does not require the Standalone executable or a configured path to `RunMind.exe`. The branded **RunMind** fallback accepts a plain natural-language query or a `RunMind <question>` prefix. Selecting the fallback opens a Command Palette content page; model inference starts only after selection, streams the local model's answer, and accepts follow-up questions. The page's **Open chat with context** action transfers the complete ordered inline transcript—including every completed user/assistant turn—into the Command Palette-owned chat window. If generation is still running, it transfers the current question without presenting a partial answer as complete. The destination restores the transcript as its real conversation state, so follow-up requests retain the prior context. The package does not create a Start-menu or Windows Search entry because its executable is a COM host, not a user-launchable app.

## Connect your local model server

The WebView2 chat page uses the origin `https://localhost`. The model server must allow requests from that origin:

- For Ollama, include `https://localhost` in `OLLAMA_ORIGINS` and restart Ollama.
- For LM Studio, enable CORS in the local server settings.
- For another OpenAI-compatible provider, configure its CORS policy to allow the RunMind page origin.

## Conversations, settings, and privacy notes

RunMind saves resumable conversations as `.runmind` files. When opening one, choose **Open in this window** to replace the current conversation or **Open in new window** to keep the current chat and open another window. The saved filename becomes the conversation title.

Standalone and PowerToys use separate WebView2 profiles. Their chat settings, credentials, Agent task history, and Deep Research checkpoints are therefore separate. PowerToys Run and Command Palette inline-answer configuration is separate from full chat preferences, but both inline hosts read and write the same `%LOCALAPPDATA%\RunMind\Shared\inline-model-settings.json` file. API keys and MCP tokens are stored in the local WebView2 profile's `localStorage`; they are not encrypted by RunMind. Protect access to your Windows account and profile accordingly.

## Documentation

- [Build and setup](BUILD-AND-SETUP.md) — build prerequisites, release packaging, and deployment details.
- [Feature matrix](FEATURE-MATRIX.md) — detailed capability status, behavior, and limits.
- [Changelog](CHANGELOG.md) — release-by-release changes and fixes.
- [Roadmap](ROADMAP.md) — planned work and project direction.

---

![Screenshot](screenshots/Screenshot-3.png)
![Screenshot](screenshots/Screenshot-2.png)


## About RunMind

RunMind for Command Palette includes an About section in its extension settings. Website: [https://omid.navy](https://omid.navy).


### System Manager (2.6.1)

Windows System Manager has a dedicated toolbar flyout and remains disabled by default. Application discovery uses ranked name/alias matching across registered desktop applications, Start Menu shortcuts, bounded common install locations, PATH and packaged Windows Apps. Launches use stable IDs from current discovery, reuse and activate existing desktop windows by default, and distinguish verified foreground activation from process startup or unverified application handoff. Ask/reuse/new-window/new-process behavior and persistent per-app preferences are supported with application-specific limits. Graceful close requests always require native confirmation and never force-kill processes. Packaged-app activation is supported but does not claim visible-window or navigation verification. The flyout also includes an Application Adapters manager: inspect built-in/custom adapters, install JSON-only declarative adapters, and remove custom manifests with native confirmation. The first-party RunMind adapter can launch RunMind with a supplied prompt through its existing `--prompt` argument; it reuses the normal discovery, launch, and permission path. Native action results distinguish process startup, foreground activation, new-window requests, existing-instance handoff, and unverified navigation. The Agent policy is merged into the single leading system message for compatibility with stricter model chat templates.

Agent introspection now includes `list_available_tools` for the exact tools exposed in the current session. Global Skills can be discovered and loaded without selecting a project workspace; project Skills remain workspace-scoped. The current source bundle does not include a separate built-in/Bionic skill payload, so the inventory reports only installed Global and authorized Project skills.


### Agent System Management Skills (2.6.2)

RunMind bundles three on-demand skills: `system-management`, `container-management`, and `runmind-management`. They are discoverable through `list_agent_skills` and loadable through `load_agent_skill`. Skills provide operational guidance only; native tools remain responsible for validation, permissions, confirmations, execution, and result verification. Adapter inventory includes descriptive operation metadata.
