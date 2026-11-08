## RunMind 2.1.1 — Conversation lifecycle and Agent Skills Manager

RunMind Standalone and the PowerToys Run plugin use the same `ChatWindow.html` Agent runtime. The runtime owns tool selection, permission checks, approval prompts, Workspace Context, and Agent Skills. The shared `Shared/AgentHostBridge.cs` adapter is limited to named Windows-native capabilities: shared Project Profile storage, Git process execution, global Skills storage/management, and resumable downloads. It does not expose a general command shell.

### Overlay installation

This release ZIP is a changed-files-only overlay. Extract it over the **complete RunMind 2.1.0 source tree**, including the 2.1.0 changes you tested, preserving all existing files and directories. Do not replace the source tree with the overlay ZIP. The supplied source archive does not contain every build-time resource referenced by the project; retain/provide the original `lib`, `Assets`, `Vendor`, `OCR`, and `screenshots` directories from the full project checkout. The existing `Shared/NativePageReader.cs` remains unchanged and its integration in both hosts is retained.

### Build and requirements

- Build on Windows with the .NET 10 SDK.
- Retain the PowerToys reference assemblies under `lib\x64` for the PowerToys plugin project.
- Retain the linked `Assets`, `Vendor`, and `OCR` resources used by the two host projects.
- Install Git for Windows and ensure `git.exe` is available on `PATH` to use native Git tools.
- Build both projects using the repository's `Build-Release-x64.bat` script. The plugin also requires the matching PowerToys environment for deployment and runtime testing.

### Project Profiles and Workspace Context

Project Profiles are shared between Standalone and PowerToys Run in `%LOCALAPPDATA%\RunMind\ProjectProfiles`. Profiles capture project instructions, provider/model and connection ID, selected Agent settings, enabled Skill names, MCP server IDs, a workspace folder-name hint, and permission policy. Credential fields and browser folder handles are not copied; do not put secrets in freeform instructions.

Folder handles are browser capabilities stored in IndexedDB within the current WebView profile and keyed by Workspace Context ID. They are not shared across WebView profiles or exported. When moving to the other host or another device, use the folder reconnect/choose action. A Project Profile's folder name is only a hint; applying it never silently selects that folder. Applying a profile creates a new context and requires browser folder access and native Git repository selection to be granted again.

Saved conversations use `.runmind` format version 2 and continue to import version 1. MCP server IDs are references, not credentials; a server reconnects only when a matching locally trusted configuration exists and the user explicitly reconnects it.

### Git tools

Install Git for Windows and configure credentials through Windows Git Credential Manager when the remote requires authentication. Git clone supports HTTP/HTTPS URLs, including self-hosted Gogs/Gitea installations. The native folder picker asks where to clone or selects an existing repository. The selected native repository is scoped to the active conversation context; switching to another context requires selecting/cloning again.

Supported operations include status, diff, log, show, local branches, remotes, stage/unstage, commit, create/switch branch, merge, revert, cherry-pick, stash, fetch, pull, push, hard reset, clean, restore, and branch deletion. Fetch/pull/push require an explicit remote and branch. Git worktrees and linked `.git` directories are not supported in 2.1.0. There is no general terminal or arbitrary process-execution tool. Mutating operations use the shared approval policy; commits and destructive operations require fresh approval.

A cloned repository is authorized for native Git. To use browser-based file tools against it, separately call `request_folder_access` and choose the cloned directory in the WebView's folder picker. Browser folder grants and native Git selection are different capabilities.

### Agent Skills and Skills Manager

RunMind uses the open Agent Skills folder format. Global Skills live under `%LOCALAPPDATA%\RunMind\Skills\<skill-name>\SKILL.md`; project Skills live under `<workspace>\.agents\skills\<skill-name>\SKILL.md`. The required frontmatter fields are `name` and `description`.

Use **Agent Settings → Agent Skills** to switch between **Global skills** and **Current project skills**, refresh the installed list, inspect each Skill description, install a local Skill folder, or remove the selected installation after confirmation. Project operations require the current workspace folder to be authorized. The global and project scopes are independent: removing one does not remove a same-named Skill installed in the other scope. Installation does not overwrite an existing Skill of the same name.

Manual installation remains supported: place a Skill folder containing `SKILL.md` in `%LOCALAPPDATA%\RunMind\Skills\<skill-name>\` for global use, or in `<workspace>\.agents\skills\<skill-name>\` for project use. `list_agent_skills` discovers metadata; `load_agent_skill` loads the selected `SKILL.md` and up to 10 requested references on demand. `SKILL.md` and each reference are limited to 128 KB. Scripts/assets may be included in a Skill folder, but scripts are never executed automatically and Skills cannot grant tool permissions.

### Conversation storage and saved conversations

Unsaved conversation branches are temporary and scoped to the current chat window. `sessionStorage` holds the window's session identifier; IndexedDB stores the full-size temporary branch archive so it can be restored after a reload. A separate window gets a separate session. Starting a new conversation retires the previous temporary session. Normal window close removes the session archive, Deep Research checkpoints/history, and temporary Agent activity; if the process crashes or is terminated before cleanup runs, the next startup expires orphaned sessions after a 24-hour grace period. Research checkpoints are restored only by their owning window session; a new chat never claims another window's paused task.

Local Storage is not used for conversation branch transcripts. It remains available for application preferences and other persistent settings. IndexedDB is also still used for browser workspace folder handles; the temporary conversation cleanup does not delete workspace grants. Legacy 2.1.0 Local Storage branch snapshots are removed on first 2.1.1 startup. Legacy IndexedDB branch archives and unowned research checkpoints receive a 24-hour grace period; legacy research-history rows that have no owning-session identifier are removed during cleanup. Save a conversation explicitly as a `.runmind` file when it should be retained, reopened later, or moved between windows/hosts. Version 1 and version 2 `.runmind` files remain supported.

### Reliable downloads

`download_file` saves the original HTTP/HTTPS response bytes. With a selected browser workspace, RunMind first attempts browser fetch for files up to 10 MB; CORS, network restrictions, or size limits may trigger the native fallback. Without browser folder access, the desktop host uses the native Save dialog. Native downloads stream in 1 MB blocks to a `.runmind.part` file, support Range resume when a matching ETag or Last-Modified validator is available, calculate SHA-256, optionally compare an expected digest, show progress, support cancellation, and finalize without overwriting an existing file. The native downloader limits transfers to 4 GB / two hours and blocks private, loopback, link-local, and reserved network destinations. It does not use cookies or the system proxy. Downloads are never extracted or executed automatically.

### Permission defaults

Git reads default to **Allow**; local Git writes, remote Git operations, and downloads default to **Ask**. The policy can be changed for the active conversation and saved into a Project Profile. Destructive Git actions are always **Always ask** and cannot be relaxed. Native Git folder selection asks separately. Existing file-tool approvals and the NativePageReader bridge remain in place.

### Validation boundary

The overlay is validated against the supplied 2.0.9 baseline at source level (JavaScript syntax, XML/JSON, static integration invariants, exact file-diff reconstruction, and ZIP integrity). A Windows/.NET compile and live WebView2/PowerToys tests must still be performed in the target Windows environment; they are not represented as completed by this package.

## RunMind 2.0.9 — configurable PowerToys Run timeouts

PowerToys Run now exposes separate **Model discovery timeout** and **Provider connection timeout** settings. Their defaults remain 8 and 15 seconds, matching the previous hard-coded values. Both accept 1–300 seconds; missing or invalid saved values fall back to the defaults. Model discovery applies to the provider model-list request and response-body read. Provider connection applies to the initial generation request through its first response headers. The existing **Request timeout** remains the overall inline generation-request limit. These settings affect the PowerToys Run plugin only, not standalone chat.

This overlay contains changed files only. Extract it over the complete RunMind 2.0.8 source tree, retaining excluded resources such as `lib`, `assets`, `Vendor`, `OCR`, and `screenshots`, then build on Windows with `Build-Release-x64.bat` using the .NET 10 SDK and the PowerToys reference assemblies.
